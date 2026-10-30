namespace Pixelbadger.Toolkit.Services.Audio;

public sealed class AudioPlaybackException : Exception
{
    public AudioPlaybackException(string message) : base(message)
    {
    }

    public AudioPlaybackException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
