using System.Reflection;
using System.Runtime.InteropServices;
using Pixelbadger.Toolkit.Opus.Native;

namespace Pixelbadger.Toolkit.Opus;

public static class OpusLibrary
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static IntPtr _handle;

    public static string VersionString
    {
        get
        {
            EnsureAvailable();
            return Marshal.PtrToStringUTF8(OpusNative.opus_get_version_string()) ?? string.Empty;
        }
    }

    public static void EnsureAvailable()
    {
        if (_initialized && _handle != IntPtr.Zero)
            return;

        lock (Gate)
        {
            if (!_initialized)
            {
                NativeLibrary.SetDllImportResolver(typeof(OpusLibrary).Assembly, Resolve);
                _handle = TryLoadAny(PlatformCandidates());
                _initialized = true;
            }

            if (_handle == IntPtr.Zero)
                throw new OpusLibraryNotFoundException();
        }
    }

    internal static void EnsureAvailable(IEnumerable<string> candidateNames)
    {
        var handle = TryLoadAny(candidateNames);
        if (handle == IntPtr.Zero)
            throw new OpusLibraryNotFoundException();
        NativeLibrary.Free(handle);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => libraryName == OpusNative.LibraryName ? _handle : IntPtr.Zero;

    private static IntPtr TryLoadAny(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            try
            {
                if (NativeLibrary.TryLoad(name, out var handle))
                    return handle;
            }
            catch (ArgumentException)
            {
            }
        }

        return IntPtr.Zero;
    }

    private static string[] PlatformCandidates()
    {
        if (OperatingSystem.IsWindows())
            return ["opus.dll", "libopus-0.dll"];
        if (OperatingSystem.IsMacOS())
            return ["libopus.0.dylib", "libopus.dylib", "/opt/homebrew/lib/libopus.0.dylib", "/usr/local/lib/libopus.0.dylib"];
        return ["libopus.so.0", "libopus.so"];
    }
}
