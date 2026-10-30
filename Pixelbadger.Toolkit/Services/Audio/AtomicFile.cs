namespace Pixelbadger.Toolkit.Services.Audio;

public static class AtomicFile
{
    /// <summary>
    /// Writes to a temp file next to <paramref name="path"/> (or next to its final target when it is a symlink) then
    /// renames it over; the original is untouched on failure, a symlink stays a symlink and the file mode is preserved.
    /// </summary>
    public static void Replace(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);
        var full = ResolveFinalTarget(Path.GetFullPath(path));
        var directory = Path.GetDirectoryName(full)!;
        var temp = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }

            CopyPermissions(full, temp);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static string ResolveFinalTarget(string full)
    {
        try
        {
            var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
            return target is null ? full : Path.GetFullPath(target.FullName);
        }
        catch (IOException)
        {
            return full;
        }
    }

    private static void CopyPermissions(string original, string temp)
    {
        if (!File.Exists(original)) return;
        if (OperatingSystem.IsWindows())
            File.SetAttributes(temp, File.GetAttributes(original));
        else
            File.SetUnixFileMode(temp, File.GetUnixFileMode(original));
    }
}
