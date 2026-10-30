using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Opus;
using Pixelbadger.Toolkit.Services.Audio;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class AudioCommand
{
    public static Command Create()
    {
        var command = new Command("audio", "Record Ogg Opus audio, and insert or overwrite sections of an existing file by packet splicing");

        command.Add(CreateRecordCommand());
        command.Add(CreateInsertCommand());
        command.Add(CreateOverwriteCommand());

        return command;
    }

    /// <summary>
    /// Runs the action on a foreground thread. After Ctrl+C, System.CommandLine abandons the action once its termination
    /// timeout (2 s) elapses and the process would exit mid-write; a foreground thread keeps the process alive until the
    /// file is fully written, and then restores the real exit code.
    /// </summary>
    private static Task<int> RunDetachedAsync(CancellationToken cancellationToken, Func<Task> work)
    {
        var completion = new TaskCompletionSource<int>();
        var thread = new Thread(() =>
        {
            int code;
            try
            {
                work().GetAwaiter().GetResult();
                code = 0;
            }
            catch (Exception ex)
            {
                code = WriteError(ex);
            }

            if (cancellationToken.IsCancellationRequested)
                Environment.ExitCode = code;
            completion.SetResult(code);
        }) { IsBackground = false, Name = "audio" };
        thread.Start();
        return completion.Task;
    }

    private static int WriteError(Exception ex)
    {
        // Redirected output has no real terminal width; stop Spectre wrapping the message mid-sentence.
        if (!ToolkitConsole.IsCaptured && Console.IsOutputRedirected && AnsiConsole.Profile.Width < 4096)
            AnsiConsole.Profile.Width = 4096;
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        return 1;
    }

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
            var component = new AudioRecordComponent(new OpusCodecFactory(), new AudioInputCollector(new ConsoleTerminalInput(), output), output);
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
            var component = new AudioInsertComponent(new OpusCodecFactory(), new AudioInputCollector(new ConsoleTerminalInput(), output), output);
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
            var component = new AudioOverwriteComponent(new OpusCodecFactory(), new AudioInputCollector(new ConsoleTerminalInput(), output), output);
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
