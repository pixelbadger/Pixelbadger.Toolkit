namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class AudioCaptureException : Exception
{
    public AudioCaptureException()
    {
    }

    public AudioCaptureException(string message) : base(message)
    {
    }

    public AudioCaptureException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
