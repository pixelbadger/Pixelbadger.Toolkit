using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class AudioCommand
{
    // Comfortably inside System.CommandLine's 2 s process termination timeout.
    private static readonly TimeSpan ReleaseAfterCancel = TimeSpan.FromSeconds(1);

    public static Command Create()
    {
        var command = new Command("audio", "Record Ogg Opus audio, insert or overwrite sections of an existing file by packet splicing, and play MIDI files with a SoundFont");

        command.Add(CreateRecordCommand());
        command.Add(CreateInsertCommand());
        command.Add(CreateOverwriteCommand());
        command.Add(CreatePlayMidiCommand());

        return command;
    }

    /// <summary>
    /// Runs the action on a foreground thread. After Ctrl+C, System.CommandLine abandons the action once its termination
    /// timeout (2 s) elapses and the process would exit mid-write; a foreground thread keeps the process alive until the
    /// file is fully written, and then restores the real exit code.
    /// </summary>
    /// <remarks>
    /// System.CommandLine completes a forced-termination source on every Ctrl+C whose wait times out, and throws (killing the
    /// process and losing the take) when a second press does so too. So after a cancellation the action's task is released
    /// early, well inside the timeout, so no press ever waits that long; the thread carries on and sets the final exit code.
    /// Presses after System.CommandLine has stood down are swallowed until the work is done.
    /// </remarks>
    private static Task<int> RunDetachedAsync(CancellationToken cancellationToken, Func<Task> work)
    {
        // In-process hosts (the MCP server) capture output: they must keep their own exit code and shut down freely.
        var hostsProcess = !ToolkitConsole.IsCaptured;
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var finished = false;
        ConsoleCancelEventHandler? swallow = null;

        var thread = new Thread(() =>
        {
            int code;
            try
            {
                work().GetAwaiter().GetResult();
                code = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                code = WriteCancelled();
            }
            catch (Exception ex)
            {
                code = WriteError(ex);
            }

            if (hostsProcess && cancellationToken.IsCancellationRequested)
                Environment.ExitCode = code;
            lock (gate)
            {
                finished = true;
                if (swallow is not null)
                    Console.CancelKeyPress -= swallow;
            }

            completion.TrySetResult(code);
        }) { IsBackground = !hostsProcess, Name = "audio" };

        if (hostsProcess)
        {
            cancellationToken.Register(() =>
            {
                lock (gate)
                {
                    if (finished || swallow is not null)
                        return;
                    swallow = (_, e) => e.Cancel = true;
                    Console.CancelKeyPress += swallow;
                }

                _ = Task.Delay(ReleaseAfterCancel).ContinueWith(_ => completion.TrySetResult(130), TaskScheduler.Default);
            });
        }

        thread.Start();
        return completion.Task;
    }

    private static int WriteError(Exception ex)
    {
        WidenRedirectedOutput();
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        return 1;
    }

    /// <summary>A cancelled fixed-length take is abandoned: nothing was written. 130 is the conventional exit code for a Ctrl+C.</summary>
    private static int WriteCancelled()
    {
        WidenRedirectedOutput();
        AnsiConsole.WriteLine("Cancelled; no changes were written");
        return 130;
    }

    // Redirected output has no real terminal width; stop Spectre wrapping the message mid-sentence.
    private static void WidenRedirectedOutput()
    {
        if (!ToolkitConsole.IsCaptured && Console.IsOutputRedirected && AnsiConsole.Profile.Width < 4096)
            AnsiConsole.Profile.Width = 4096;
    }

    private static AudioInputCollector NewCollector(TextWriter output) =>
        new(new ConsoleTerminalInput(), output, ffmpegLocator: AudioSeams.FfmpegLocator);

    private static Option<string?> DurationOption(string description) =>
        new("--duration") { Description = description };

    private static Option<string?> FromWavOption(string description) =>
        new("--from-wav") { Description = description };

    private static Option<int> BitrateOption(string description) =>
        new("--bitrate") { Description = description, DefaultValueFactory = _ => 64000 };

    private static Option<string?> DeviceOption() =>
        new("--device") { Description = "Capture device name passed to ffmpeg (default: pulse 'default' on Linux, avfoundation ':0' on macOS; required on Windows)" };

    private static Option<string?> CaptureFormatOption() =>
        new("--capture-format") { Description = "ffmpeg input format used for capture (default: pulse on Linux, avfoundation on macOS, dshow on Windows)" };

    private static Command CreateRecordCommand()
    {
        var command = new Command("record", "Record audio (live capture via ffmpeg, or a WAV file) to a new Ogg Opus file");

        var outFileOption = new Option<string>("--out-file") { Description = "Path of the .opus file to write (an existing file is overwritten)", Required = true };
        var durationOption = DurationOption("Length to capture or keep from the WAV, e.g. 10s, 1:30 or 2.5 (default: until Enter or Ctrl+C)");
        var fromWavOption = FromWavOption("Read audio from this WAV file instead of capturing (no ffmpeg needed)");
        var channelsOption = new Option<int>("--channels") { Description = "Channel count of the new file: 1 (mono) or 2 (stereo)", DefaultValueFactory = _ => 1 };
        var bitrateOption = BitrateOption("Opus bitrate in bits per second (6000 to 510000)");
        var deviceOption = DeviceOption();
        var captureFormatOption = CaptureFormatOption();

        command.Add(outFileOption);
        command.Add(durationOption);
        command.Add(fromWavOption);
        command.Add(channelsOption);
        command.Add(bitrateOption);
        command.Add(deviceOption);
        command.Add(captureFormatOption);

        command.SetAction((parseResult, cancellationToken) => RunDetachedAsync(cancellationToken, async () =>
        {
            var output = ToolkitConsole.Out;
            var component = new AudioRecordComponent(new OpusCodecFactory(), NewCollector(output), output);
            await component.RunAsync(new AudioRecordOptions(
                parseResult.GetValue(outFileOption)!,
                parseResult.GetValue(durationOption),
                parseResult.GetValue(fromWavOption),
                parseResult.GetValue(channelsOption),
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(deviceOption),
                parseResult.GetValue(captureFormatOption)), cancellationToken);
        }));

        return command;
    }

    private static Command CreateInsertCommand()
    {
        var command = new Command("insert", "Insert new audio into an existing Ogg Opus file at a time, editing the file in place");
        var (fileOption, atOption, durationOption, fromWavOption, bitrateOption, deviceOption, captureFormatOption) = AddEditOptions(command, "insert");

        command.SetAction((parseResult, cancellationToken) => RunDetachedAsync(cancellationToken, async () =>
        {
            var output = ToolkitConsole.Out;
            var component = new AudioInsertComponent(new OpusCodecFactory(), NewCollector(output), output);
            await component.RunAsync(new AudioEditOptions(
                parseResult.GetValue(fileOption)!,
                parseResult.GetValue(atOption)!,
                parseResult.GetValue(durationOption),
                parseResult.GetValue(fromWavOption),
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(deviceOption),
                parseResult.GetValue(captureFormatOption)), cancellationToken);
        }));

        return command;
    }

    private static Command CreateOverwriteCommand()
    {
        var command = new Command("overwrite", "Replace audio in an existing Ogg Opus file starting at a time, editing the file in place");
        var (fileOption, atOption, durationOption, fromWavOption, bitrateOption, deviceOption, captureFormatOption) = AddEditOptions(command, "overwrite");

        command.SetAction((parseResult, cancellationToken) => RunDetachedAsync(cancellationToken, async () =>
        {
            var output = ToolkitConsole.Out;
            var component = new AudioOverwriteComponent(new OpusCodecFactory(), NewCollector(output), output);
            await component.RunAsync(new AudioEditOptions(
                parseResult.GetValue(fileOption)!,
                parseResult.GetValue(atOption)!,
                parseResult.GetValue(durationOption),
                parseResult.GetValue(fromWavOption),
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(deviceOption),
                parseResult.GetValue(captureFormatOption)), cancellationToken);
        }));

        return command;
    }

    private static Command CreatePlayMidiCommand()
    {
        var command = new Command("play-midi", "Play a MIDI file live through the system audio output using a SoundFont (SF2) synthesiser");

        var fileOption = new Option<string>("--file") { Description = "MIDI file (.mid) to play", Required = true };
        var soundFontOption = new Option<string>("--soundfont") { Description = "SoundFont (.sf2) whose instruments synthesise the MIDI file", Required = true };
        var outputFormatOption = new Option<string?>("--output-format") { Description = "ffmpeg output format used for playback (default: pulse on Linux, audiotoolbox on macOS, sdl2 on Windows)" };
        var deviceOption = new Option<string?>("--device") { Description = "Output device passed to ffmpeg (default: the system default output; pulse sink name on Linux, audiotoolbox device index on macOS; ignored by sdl2)" };

        command.Add(fileOption);
        command.Add(soundFontOption);
        command.Add(outputFormatOption);
        command.Add(deviceOption);

        // The component handles Ctrl+C itself (prints "Stopped at m:ss", returns 130), so this does not use RunDetachedAsync.
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var component = new MidiPlayComponent(
                    new MeltySynthSourceFactory(),
                    new FfmpegPlaybackSinkFactory(AudioSeams.FfmpegLocator),
                    MidiPlaybackViewFactory.Create(ToolkitConsole.Out));
                return await component.RunAsync(new MidiPlayOptions(
                    parseResult.GetValue(fileOption)!,
                    parseResult.GetValue(soundFontOption)!,
                    parseResult.GetValue(outputFormatOption),
                    parseResult.GetValue(deviceOption)), cancellationToken);
            }
            catch (Exception ex)
            {
                return WriteError(ex);
            }
        });

        return command;
    }

    private static (Option<string> File, Option<string> At, Option<string?> Duration, Option<string?> FromWav, Option<int> Bitrate, Option<string?> Device, Option<string?> CaptureFormat)
        AddEditOptions(Command command, string verb)
    {
        var fileOption = new Option<string>("--file") { Description = "Existing Ogg Opus file to edit in place", Required = true };
        var atOption = new Option<string>("--at") { Description = $"Time in the file to {verb} at, from 0 to the file's duration: [[hh:]mm:]ss[.fff], seconds, or a number with an ms, s or m suffix", Required = true };
        var durationOption = DurationOption("Length to capture or keep from the WAV (default: until Enter or Ctrl+C)");
        var fromWavOption = FromWavOption("Read the new audio from this WAV file instead of capturing (no ffmpeg needed)");
        var bitrateOption = BitrateOption("Opus bitrate for the new audio in bits per second (6000 to 510000)");
        var deviceOption = DeviceOption();
        var captureFormatOption = CaptureFormatOption();

        command.Add(fileOption);
        command.Add(atOption);
        command.Add(durationOption);
        command.Add(fromWavOption);
        command.Add(bitrateOption);
        command.Add(deviceOption);
        command.Add(captureFormatOption);

        return (fileOption, atOption, durationOption, fromWavOption, bitrateOption, deviceOption, captureFormatOption);
    }
}
