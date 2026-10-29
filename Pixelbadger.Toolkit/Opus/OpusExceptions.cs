namespace Pixelbadger.Toolkit.Opus;

public class OpusException : Exception
{
    public OpusException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}

public sealed class OpusLibraryNotFoundException : Exception
{
    internal const string DefaultMessage =
        "The native Opus library (libopus) was not found. Install it: Debian/Ubuntu 'apt install libopus0', macOS 'brew install opus', Windows: put opus.dll on PATH.";

    public OpusLibraryNotFoundException() : base(DefaultMessage)
    {
    }
}
