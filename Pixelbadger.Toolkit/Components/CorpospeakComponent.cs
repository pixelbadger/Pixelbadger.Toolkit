using OpenAI.Chat;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class CorpospeakComponent
{
    private readonly IOpenAiClientService _openAiClientService;
    private readonly IHistoryService _historyService;

    private static readonly string[] ValidAudiences = [
        "csuite", "c-suite", "executive", "leadership",
        "engineering", "technical", "dev", "developers",
        "product", "pm", "product-management",
        "sales", "business-development", "revenue",
        "marketing", "growth", "demand-gen",
        "operations", "ops", "infrastructure",
        "finance", "financial", "accounting",
        "legal", "compliance", "risk",
        "hr", "human-resources", "people",
        "customer-success", "support", "cs"
    ];

    public CorpospeakComponent(IOpenAiClientService openAiClientService, IHistoryService historyService)
    {
        _openAiClientService = openAiClientService;
        _historyService = historyService;
    }

    public async Task<string> CorpospeakAsync(string source, string audience, string[] userMessages)
    {
        ValidateAudience(audience);

        var resolvedSource = await ResolveTextOrFilePath(source);
        var resolvedUserMessages = await ResolveTextOrFilePathArray(userMessages);

        var prompt = GetCombinedPrompt(audience, resolvedSource, resolvedUserMessages);
        var (chatMessages, historyEntries) = BuildChatMessages(resolvedUserMessages, prompt);

        var chatResult = await _openAiClientService.CompleteChatAsync(chatMessages);

        var sessionId = await _historyService.CreateSessionAsync("corpospeak");
        foreach (var (role, content) in historyEntries)
            await _historyService.AddMessageAsync(sessionId, role, content);
        await _historyService.AddMessageAsync(sessionId, "assistant", chatResult.Content);
        await _historyService.UpdateTokenUsageAsync(sessionId, chatResult.PromptTokens, chatResult.CompletionTokens);

        return chatResult.Content;
    }

    private static void ValidateAudience(string audience)
    {
        var normalizedAudience = audience.ToLowerInvariant().Trim();
        if (!ValidAudiences.Contains(normalizedAudience))
        {
            var validAudiencesList = string.Join(", ", ValidAudiences);
            throw new ArgumentException($"Invalid audience '{audience}'. Valid audiences: {validAudiencesList}");
        }
    }

    private static (List<ChatMessage> Messages, List<(string Role, string Content)> HistoryEntries) BuildChatMessages(string[] userMessages, string prompt)
    {
        var messages = new List<ChatMessage>();
        var history = new List<(string Role, string Content)>();

        if (userMessages.Length > 0)
        {
            foreach (var userMessage in userMessages)
            {
                messages.Add(ChatMessage.CreateUserMessage(userMessage));
                history.Add(("user", userMessage));
                messages.Add(ChatMessage.CreateAssistantMessage("I understand your writing style."));
                history.Add(("assistant", "I understand your writing style."));
            }
        }

        messages.Add(ChatMessage.CreateUserMessage(prompt));
        history.Add(("user", prompt));

        return (messages, history);
    }

    private static string GetCombinedPrompt(string audience, string source, string[] userMessages)
    {
        var normalizedAudience = audience.ToLowerInvariant().Trim();

        var audienceContext = normalizedAudience switch
        {
            "csuite" or "c-suite" or "executive" or "leadership" =>
                "C-suite executives who need strategic, high-level insights focused on business impact, ROI, and competitive advantage. Use executive language with emphasis on outcomes and business value.",

            "engineering" or "technical" or "dev" or "developers" =>
                "Engineering teams who appreciate technical precision, implementation details, and architectural considerations. Use technical terminology and focus on how-it-works rather than why-it-matters.",

            "product" or "pm" or "product-management" =>
                "Product managers who need to understand user impact, feature priorities, and roadmap implications. Focus on user experience, metrics, and product strategy.",

            "sales" or "business-development" or "revenue" =>
                "Sales teams who need compelling value propositions, competitive differentiators, and customer-facing messaging. Emphasize benefits, ROI, and how this helps close deals.",

            "marketing" or "growth" or "demand-gen" =>
                "Marketing professionals who need messaging that resonates with target audiences, campaign angles, and brand positioning. Focus on market impact and customer appeal.",

            "operations" or "ops" or "infrastructure" =>
                "Operations teams who need to understand scalability, reliability, and operational impact. Focus on system implications, processes, and operational excellence.",

            "finance" or "financial" or "accounting" =>
                "Finance teams who need cost implications, budget impact, and financial modeling considerations. Emphasize numbers, costs, savings, and financial metrics.",

            "legal" or "compliance" or "risk" =>
                "Legal and compliance teams who need risk assessment, regulatory implications, and compliance considerations. Focus on legal impact and risk mitigation.",

            "hr" or "human-resources" or "people" =>
                "HR and people teams who need to understand impact on employees, culture, and organizational dynamics. Focus on people implications and change management.",

            "customer-success" or "support" or "cs" =>
                "Customer success and support teams who need to understand customer impact, support implications, and customer communication strategies. Focus on customer experience and support considerations.",

            _ => "a professional enterprise technology audience"
        };

        var basePrompt = $@"You are an expert technical communicator specializing in enterprise technology organizations.

Rewrite the following source text to be appropriate for {audienceContext}

Source text:
{source}

Requirements:
- Maintain all key information and technical accuracy
- Adapt tone, language, and emphasis for the target audience
- Use terminology and framing that resonates with this audience
- Keep the same general structure but optimize for audience comprehension and engagement
- Be concise but comprehensive";

        if (userMessages.Length > 0)
        {
            basePrompt += @"
- Additionally, adapt the writing style, tone, and idiolect to match the patterns demonstrated in the previous messages in this conversation
- Maintain the core content and message while matching the user's demonstrated communication style";
        }

        basePrompt += "\n\nProvide only the rewritten text, no meta-commentary.";
        return basePrompt;
    }

    private static async Task<string> ResolveTextOrFilePath(string input)
    {
        if (File.Exists(input))
        {
            return await File.ReadAllTextAsync(input);
        }
        return input;
    }

    private static async Task<string[]> ResolveTextOrFilePathArray(string[] inputs)
    {
        var resolvedInputs = new string[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            resolvedInputs[i] = await ResolveTextOrFilePath(inputs[i]);
        }
        return resolvedInputs;
    }
}
