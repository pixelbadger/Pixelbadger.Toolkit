using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Components;

public sealed record AudioEditOptions(string File, string At, string? Duration, string? FromWav, int Bitrate, string? Device, string? CaptureFormat);

public sealed class AudioInsertComponent
{
    private readonly IOpusCodecFactory _factory;
    private readonly IAudioInputCollector _collector;
    private readonly TextWriter _output;

    public AudioInsertComponent(IOpusCodecFactory factory, IAudioInputCollector collector, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(output);
        _factory = factory;
        _collector = collector;
        _output = output;
    }

    /// <summary>Inserts new audio into an Ogg Opus file in place. Throws with the user-facing message on any failure.</summary>
    public async Task RunAsync(AudioEditOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        AudioCommandSupport.ValidateBitrate(options.Bitrate);
        var at = AudioTimeParser.Parse(options.At);
        var duration = AudioCommandSupport.ParseOptionalTime(options.Duration);

        var (file, stamp) = AudioCommandSupport.ReadOpusFile(options.File);
        var requested = AudioCommandSupport.ToSamples(at);
        if (requested > file.DurationSamples)
            throw new InvalidOperationException(
                $"Time {AudioCommandSupport.Seconds(requested)}s is beyond the end of '{options.File}' ({AudioCommandSupport.Seconds(file.DurationSamples)}s)");

        AudioCommandSupport.EnsureOpusAvailable();
        var channels = file.Head.ChannelCount;
        var request = new AudioInputRequest(channels, duration, options.FromWav, options.Device, options.CaptureFormat);
        _collector.Validate(request);

        var pcm = await _collector.CollectAsync(request, cancellationToken);
        AudioCommandSupport.RequireAudio(pcm);

        var encoder = new OpusPcmEncoder(_factory);
        var settings = new OpusEncodeSettings(channels, options.Bitrate);
        var result = OpusSpliceEditor.Insert(file, requested,
            request => encoder.Encode(OpusPcmEncoder.PrependSilence(pcm, request.FrontPadSamples, channels), settings, request.ExactSamples));
        AudioCommandSupport.WriteOpusFile(options.File, result.File, request, cancellationToken, stamp);

        _output.WriteLine(
            $"Inserted {AudioCommandSupport.Seconds(pcm.Length / channels)}s at {AudioCommandSupport.Seconds(result.SnappedSample)}s " +
            $"(requested {AudioCommandSupport.Seconds(requested)}s) into '{options.File}': {AudioCommandSupport.Seconds(result.File.DurationSamples)}s");
    }
}
