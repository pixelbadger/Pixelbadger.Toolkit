using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pixelbadger.Toolkit.Services;

/// <summary>Per-game persistent store rooted at root/stem-sha8/. Holds run.lock until disposed.</summary>
public sealed partial class IfSaveStore : IIfSaveStore
{
    public const int MaxSlots = 20;
    public const int MaxNotesLength = 8000;
    public const int MaxArchives = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly FileStream _lock;
    private readonly object _gate = new();
    private bool _disposed;

    private IfSaveStore(string gameDirectory, FileStream lockStream)
    {
        GameDirectory = gameDirectory;
        _lock = lockStream;
    }

    public string GameDirectory { get; }
    public string AutosavePath => Path.Combine(GameDirectory, "autosave.qzl");
    public bool HasAutosave => File.Exists(AutosavePath);

    private string SlotsDirectory => Path.Combine(GameDirectory, "slots");
    private string TranscriptPath => Path.Combine(GameDirectory, "transcript.jsonl");
    private string NotesPath => Path.Combine(GameDirectory, "notes.md");
    private string ArchiveRoot => Path.Combine(GameDirectory, "archive");

    internal static string DefaultRootDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pbtk", "if");

    public static IfSaveStore Open(string gameFile, string? rootDirectory = null)
    {
        var root = rootDirectory ?? DefaultRootDirectory;
        byte[] hash;
        using (var stream = File.OpenRead(gameFile))
            hash = SHA256.HashData(stream);
        var sha8 = Convert.ToHexString(hash).ToLowerInvariant()[..8];
        var directory = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(gameFile)}-{sha8}");
        Directory.CreateDirectory(directory);

        FileStream lockStream;
        try
        {
            lockStream = new FileStream(Path.Combine(directory, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            throw new IfStoreException("Another llm if run is using this game's saves.");
        }

        return new IfSaveStore(directory, lockStream);
    }

    public string CreateTempSavePath()
    {
        // Short name: dfrotz truncates what is typed at its save prompt to the screen width.
        string path;
        do
        {
            path = Path.Combine(GameDirectory, $"t{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}.qzl");
        }
        while (File.Exists(path));
        return path;
    }

    private string CreateSiblingTempPath(string target) => target + $".{Guid.NewGuid():N}.tmp";

    public void CommitAutosave(string tempPath)
    {
        lock (_gate) File.Move(tempPath, AutosavePath, overwrite: true);
    }

    public void DiscardTemp(string tempPath)
    {
        lock (_gate)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
        }
    }

    public IReadOnlyList<string> ListSlots()
    {
        if (!Directory.Exists(SlotsDirectory)) return [];
        return Directory.EnumerateFiles(SlotsDirectory, "*.qzl")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public void SaveSlot(string name)
    {
        lock (_gate)
        {
            ValidateSlotName(name);
            if (!HasAutosave) throw new IfStoreException("No game to save yet.");
            var path = SlotPath(name);
            if (!File.Exists(path) && ListSlots().Count >= MaxSlots)
                throw new IfStoreException($"Slot limit ({MaxSlots}) reached. Overwrite an existing slot or delete one.");
            Directory.CreateDirectory(SlotsDirectory);
            ReplaceWithCopy(AutosavePath, path);
        }
    }

    public void RestoreSlot(string name)
    {
        lock (_gate)
        {
            ValidateSlotName(name);
            var path = SlotPath(name);
            if (!File.Exists(path)) throw new IfStoreException($"No slot named '{name}'.");
            ReplaceWithCopy(path, AutosavePath);
        }
    }

    public string ReadNotes()
    {
        lock (_gate) return File.Exists(NotesPath) ? File.ReadAllText(NotesPath) : string.Empty;
    }

    public void WriteNotes(string notes)
    {
        if (notes.Length > MaxNotesLength)
            throw new IfStoreException($"Notes are longer than {MaxNotesLength} characters.");
        lock (_gate)
        {
            var temp = CreateSiblingTempPath(NotesPath);
            try
            {
                File.WriteAllText(temp, notes);
                File.Move(temp, NotesPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    public void AppendEvent(IfTranscriptEvent transcriptEvent)
    {
        var json = JsonSerializer.Serialize(new EventDto(
            transcriptEvent.Ts.ToString("o", CultureInfo.InvariantCulture),
            transcriptEvent.Actor, transcriptEvent.Kind, transcriptEvent.Text, transcriptEvent.Output), JsonOptions);
        lock (_gate)
        {
            using var stream = new FileStream(TranscriptPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var prefix = EndsWithoutNewline(TranscriptPath) ? "\n" : string.Empty;
            var bytes = new UTF8Encoding(false).GetBytes(prefix + json + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
    }

    public IReadOnlyList<IfTranscriptEvent> ReadEvents()
    {
        string[] lines;
        lock (_gate)
        {
            if (!File.Exists(TranscriptPath)) return [];
            using var stream = new FileStream(TranscriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            lines = reader.ReadToEnd().Split('\n');
        }

        var events = new List<IfTranscriptEvent>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var dto = JsonSerializer.Deserialize<EventDto>(line, JsonOptions);
                if (dto?.Ts == null || dto.Actor == null || dto.Kind == null || dto.Text == null) continue;
                if (!DateTimeOffset.TryParse(dto.Ts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)) continue;
                events.Add(new IfTranscriptEvent(ts, dto.Actor, dto.Kind, dto.Text, dto.Output));
            }
            catch (JsonException)
            {
                // Torn or malformed line; skip.
            }
        }
        return events;
    }

    public void ArchiveCurrent()
    {
        lock (_gate)
        {
            if (!File.Exists(AutosavePath) && !File.Exists(TranscriptPath) && !File.Exists(NotesPath))
                return;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = Path.Combine(ArchiveRoot, stamp);
            for (var i = 2; Directory.Exists(target); i++)
                target = Path.Combine(ArchiveRoot, $"{stamp}-{i}");
            Directory.CreateDirectory(target);

            foreach (var file in new[] { AutosavePath, TranscriptPath, NotesPath })
            {
                if (File.Exists(file))
                    File.Move(file, Path.Combine(target, Path.GetFileName(file)));
            }

            var archives = Directory.EnumerateDirectories(ArchiveRoot)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var old in archives.Skip(MaxArchives))
                old.Delete(recursive: true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }

    private static bool EndsWithoutNewline(string path)
    {
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (reader.Length == 0) return false;
        reader.Seek(-1, SeekOrigin.End);
        return reader.ReadByte() != '\n';
    }

    private void ReplaceWithCopy(string source, string target)
    {
        var temp = CreateSiblingTempPath(target);
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private string SlotPath(string name) => Path.Combine(SlotsDirectory, name + ".qzl");

    private static void ValidateSlotName(string name)
    {
        if (name == null || !SlotNameRegex().IsMatch(name))
            throw new IfStoreException($"Invalid slot name '{name}'. Use 1-32 letters, digits, '-' or '_'.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,32}\z")]
    private static partial Regex SlotNameRegex();

    private sealed record EventDto(
        [property: JsonPropertyName("ts")] string? Ts,
        [property: JsonPropertyName("actor")] string? Actor,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("output")] string? Output);
}
