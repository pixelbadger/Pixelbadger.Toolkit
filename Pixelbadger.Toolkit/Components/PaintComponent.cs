using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public enum PaintEndReason { Finished, NoToolCalls, StepLimit }

public sealed record PaintResult(int Steps, PaintEndReason EndReason, string? Summary, string? OutFile, string? AnimationOut = null);

/// <summary>Provider-neutral agentic loop in which an LLM paints a canvas through tools.</summary>
public class PaintComponent
{
    public const int MinSteps = 1;
    public const int MaxSteps = 200;

    private readonly IChatClient _chatClient;
    private readonly IChatReasoningConfigurator _reasoningConfigurator;

    public PaintComponent(IChatClient chatClient, IChatReasoningConfigurator reasoningConfigurator)
    {
        _chatClient = chatClient;
        _reasoningConfigurator = reasoningConfigurator;
    }

    private static string SystemPrompt(int maxSteps) => $$"""
        You are a pixel artist painting on a canvas using tools. The canvas is {{PaintCanvas.Size}}x{{PaintCanvas.Size}} pixels, full-colour RGB, and starts entirely white (#FFFFFF). The origin (0,0) is the top-left corner, x increases to the right and y increases downwards. Colours are written as #RRGGBB.

        Tools:
        - read_canvas(): returns the whole canvas as a PNG image.
        - read_region(x, y, width, height): returns exact hex values; width and height 1-{{PaintCanvas.MaxRegionSide}}, fully inside the canvas.
        - set_pixels(pixels: [{x, y, color}]): at most {{PaintCanvas.MaxPixelsPerCall}} pixels per call; out-of-canvas entries are skipped.
        - fill_rect(x, y, width, height, color): width and height at least 1; clipped to the canvas.
        - draw_line(x0, y0, x1, y1, color, thickness=1): thickness 1-{{PaintCanvas.MaxThickness}}; clipped.
        - fill_ellipse(cx, cy, rx, ry, color): rx and ry at least 0; clipped.
        - finish(summary): ends the painting session.

        Plan the composition first. Paint large background shapes first, then add details. Be efficient by making several tool calls in each response. Call read_canvas to check the result before finishing, and call finish with a one-sentence summary when you are done. You have a limit of {{maxSteps}} turns (model responses), so finish before you run out.
        """;

    public async Task<PaintResult> PaintAsync(
        string prompt,
        int maxSteps,
        string? outFile,
        bool overwrite,
        IPaintRenderer renderer,
        string? animationOut = null,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        if (maxSteps < MinSteps || maxSteps > MaxSteps)
            throw new ArgumentOutOfRangeException(nameof(maxSteps), maxSteps, "--max-steps must be between 1 and 200.");

        string? fullPath = null;
        if (outFile is not null)
        {
            fullPath = Path.GetFullPath(outFile);
            if (File.Exists(fullPath) && !overwrite)
                throw new IOException($"Output file already exists: {fullPath}. Use --overwrite to replace it.");
        }

        string? animationPath = null;
        if (animationOut is not null)
        {
            animationPath = Path.GetFullPath(animationOut);
            if (File.Exists(animationPath) && !overwrite)
                throw new IOException($"Output file already exists: {animationPath}. Use --overwrite to replace it.");
            if (fullPath is not null && string.Equals(fullPath, animationPath, StringComparison.Ordinal))
                throw new ArgumentException("--out-file and --animation-out must be different paths.");
        }

        var canvas = new PaintCanvas();
        var recorder = animationPath is not null ? new PaintAnimationRecorder(PaintCanvas.Size) : null;
        var tools = new PaintTools(canvas);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt(maxSteps)),
            new(ChatRole.User, prompt)
        };
        var options = new ChatOptions { Tools = [.. tools.Tools], ToolMode = ChatToolMode.Auto };
        if (reasoningEffort is not null)
            _reasoningConfigurator.Apply(options, reasoningEffort);

        var steps = 0;
        PaintEndReason reason = PaintEndReason.StepLimit;
        string? summary = null;

        renderer.Begin();
        try
        {
            renderer.RenderFrame(canvas.Snapshot(), PaintCanvas.Size, $"Step 0/{maxSteps} · starting");
            recorder?.AddFrame(canvas.Snapshot());

            for (var step = 1; step <= maxSteps; step++)
            {
                var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
                steps = step;
                messages.AddRange(response.Messages);

                var calls = response.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                if (calls.Count == 0)
                {
                    reason = PaintEndReason.NoToolCalls;
                    break;
                }

                var modelText = Regex.Replace(response.Text ?? string.Empty, @"\s+", " ").Trim();
                var results = new List<AIContent>();
                var finished = false;

                foreach (var call in calls)
                {
                    var outcome = await tools.InvokeAsync(call, cancellationToken);
                    renderer.LogToolCall(outcome.Description);

                    if (outcome.CanvasChanged)
                    {
                        var status = $"Step {step}/{maxSteps} · {outcome.Description}";
                        if (modelText.Length > 0)
                            status += $" · {modelText}";
                        renderer.RenderFrame(canvas.Snapshot(), PaintCanvas.Size, status);
                        recorder?.AddFrame(canvas.Snapshot());
                    }

                    results.Add(outcome.Result);

                    if (outcome.Finished)
                    {
                        finished = true;
                        summary = ExtractSummary(call);
                    }
                }

                messages.Add(new ChatMessage(ChatRole.Tool, results));

                if (finished)
                {
                    reason = PaintEndReason.Finished;
                    break;
                }

                if (step == maxSteps)
                    reason = PaintEndReason.StepLimit;
            }
        }
        finally
        {
            renderer.End(canvas.Snapshot(), PaintCanvas.Size);
        }

        if (fullPath is not null)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(fullPath, canvas.ToPng(), cancellationToken);
        }

        if (recorder is not null && animationPath is not null)
        {
            var dir = Path.GetDirectoryName(animationPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(animationPath, recorder.ToGif(canvas.Snapshot()), cancellationToken);
        }

        return new PaintResult(steps, reason, summary, fullPath, animationPath);
    }

    private static string? ExtractSummary(FunctionCallContent call)
    {
        if (call.Arguments is null || !call.Arguments.TryGetValue("summary", out var value) || value is null)
            return null;
        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => value.ToString()
        };
    }
}
