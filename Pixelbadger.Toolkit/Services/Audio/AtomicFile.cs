namespace Pixelbadger.Toolkit.Services.Audio;

public static class AtomicFile
{
    /// <summary>Writes to a temp file next to <paramref name="path"/> then renames it over; the original is untouched on failure.</summary>
    public static void Replace(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        var temp = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
