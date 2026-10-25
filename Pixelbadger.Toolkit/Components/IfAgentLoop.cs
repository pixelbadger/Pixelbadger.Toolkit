using Microsoft.Extensions.AI;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

/// <summary>One stateless agent step: build the prompt from persisted state, call the model once, run its tool calls in order.</summary>
public class IfAgentLoop
{
    private readonly Func<IChatClient> _chatClientFactory;
    private readonly IChatReasoningConfigurator _reasoningConfigurator;
    private readonly IIfSession _session;
    private readonly string? _instructions;
    private readonly string? _reasoningEffort;
    private readonly IfTools _tools;
    private List<string> _feedback = [];
    private int? _lastStepEventCount;

    public IfAgentLoop(Func<IChatClient> chatClientFactory, IChatReasoningConfigurator reasoningConfigurator,
        IIfSession session, string? instructions, string? reasoningEffort)
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _reasoningConfigurator = reasoningConfigurator ?? throw new ArgumentNullException(nameof(reasoningConfigurator));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _instructions = instructions;
        _reasoningEffort = reasoningEffort;
        _tools = new IfTools(session);
    }

    public async Task<IfStepResult> StepAsync(CancellationToken modelCancellation)
    {
        var messages = IfPromptBuilder.Build(_session.Events, _session.ReadNotes(), _instructions, _lastStepEventCount);
        if (_feedback.Count > 0)
        {
            const string tail = "Continue playing.";
            var user = messages[1].Text;
            var cut = user.EndsWith(tail, StringComparison.Ordinal) ? user.Length - tail.Length : user.Length;
            messages[1] = new ChatMessage(ChatRole.User, user[..cut]
                + "## Results of your last tool calls not shown in the transcript:\n"
                + string.Join("\n", _feedback) + "\n\n" + tail);
        }
        var options = new ChatOptions { Tools = [.. _tools.Tools], ToolMode = ChatToolMode.Auto };
        if (_reasoningEffort is not null)
            _reasoningConfigurator.Apply(options, _reasoningEffort);

        var client = _chatClientFactory();
        var response = await client.GetResponseAsync(messages, options, modelCancellation);
        modelCancellation.ThrowIfCancellationRequested();
        _lastStepEventCount = _session.Events.Count;

        _feedback = [];
        var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
        if (calls.Count == 0)
        {
            var text = response.Text;
            if (!string.IsNullOrWhiteSpace(text)) _session.RecordThought(text.Trim());
            return new IfStepResult(false, null, 0);
        }

        var finished = false;
        string? summary = null;
        foreach (var call in calls)
        {
            var outcome = await _tools.InvokeAsync(call, CancellationToken.None);
            var resultText = outcome.Result.Result as string ?? "";
            if (call.Name == "list_slots" || resultText.StartsWith("Error: ", StringComparison.Ordinal))
                _feedback.Add($"{outcome.Description} → {resultText}");
            if (outcome.Finished)
            {
                finished = true;
                summary = outcome.Summary;
            }
        }
        return new IfStepResult(finished, summary, calls.Count);
    }
}
