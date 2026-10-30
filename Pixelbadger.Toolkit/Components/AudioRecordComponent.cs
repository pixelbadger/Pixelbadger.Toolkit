using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Components;

public sealed record AudioRecordOptions(string OutFile, string? Duration, string? FromWav, int Channels, int Bitrate, string? Device, string? CaptureFormat);

public sealed class AudioRecordComponent
{
    private readonly IOpusCodecFactory _factory;
    private readonly IAudioInputCollector _collector;
    private readonly TextWriter _output;

    public AudioRecordComponent(IOpusCodecFactory factory, IAudioInputCollector collector, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(output);
        _factory = factory;
        _collector = collector;
        _output = output;
    }

    /// <summary>Records a new Ogg Opus file. Throws with the user-facing message on any failure.</summary>
    public async Task RunAsync(AudioRecordOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        AudioCommandSupport.ValidateChannels(options.Channels);
        AudioCommandSupport.ValidateBitrate(options.Bitrate);
        var duration = AudioCommandSupport.ParseOptionalTime(options.Duration);

        OpusLibrary.EnsureAvailable();
        var request = new AudioInputRequest(options.Channels, duration, options.FromWav, options.Device, options.CaptureFormat);
        _collector.Validate(request);

        var pcm = await _collector.CollectAsync(request, cancellationToken);
        AudioCommandSupport.RequireAudio(pcm);

        var encoded = new OpusPcmEncoder(_factory).Encode(pcm, new OpusEncodeSettings(options.Channels, options.Bitrate));
        var file = OpusSpliceEditor.CreateRecording(encoded, options.Channels);
        AudioCommandSupport.WriteOpusFile(options.OutFile, file);

        _output.WriteLine($"Recorded '{options.OutFile}': {AudioCommandSupport.Seconds(file.DurationSamples)}s");
    }
}
