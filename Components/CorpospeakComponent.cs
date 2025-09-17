using OpenAI.Chat;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class CorpospeakComponent
{
    private readonly IOpenAiClientService _openAiClientService;

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

    public CorpospeakComponent(IOpenAiClientService openAiClientService)
    {
        _openAiClientService = openAiClientService;
    }

    public async Task<string> CorpospeakAsync(string source, string audience, string[] userMessages, string model = "gpt-5-nano")
    {
        ValidateAudience(audience);

        // Step 1: Convert source text for the target audience
        var audienceText = await ConvertForAudienceAsync(source, audience, model);

        // Step 2: Optional idiolect rewrite if user messages provided
        if (userMessages.Length > 0)
        {
            return await RewriteForIdiolectAsync(audienceText, userMessages, model);
        }

        return audienceText;
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

    private async Task<string> ConvertForAudienceAsync(string source, string audience, string model)
    {
        var prompt = GetAudiencePrompt(audience, source);

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateUserMessage(prompt)
        };

        var chatClient = _openAiClientService.GetChatClient(model);
        var response = await chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
    }

    private async Task<string> RewriteForIdiolectAsync(string audienceText, string[] userMessages, string model)
    {
        var messages = new List<ChatMessage>();

        // Add user messages as examples of the user's writing style
        foreach (var userMessage in userMessages)
        {
            messages.Add(ChatMessage.CreateUserMessage(userMessage));
            // Add a placeholder assistant response to maintain conversation structure
            messages.Add(ChatMessage.CreateAssistantMessage("I understand your writing style."));
        }

        // Add the main rewrite request
        var rewritePrompt = $@"Based on the writing style demonstrated in the previous messages, please rewrite the following text to match that idiolect and tone:

{audienceText}

Maintain the core content and message, but adapt the language, tone, and style to match the user's demonstrated writing patterns.";

        messages.Add(ChatMessage.CreateUserMessage(rewritePrompt));

        var chatClient = _openAiClientService.GetChatClient(model);
        var response = await chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
    }

    private static string GetAudiencePrompt(string audience, string source)
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

        return $@"You are an expert technical communicator specializing in enterprise technology organizations.

Rewrite the following source text to be appropriate for {audienceContext}

Source text:
{source}

Requirements:
- Maintain all key information and technical accuracy
- Adapt tone, language, and emphasis for the target audience
- Use terminology and framing that resonates with this audience
- Keep the same general structure but optimize for audience comprehension and engagement
- Be concise but comprehensive

Provide only the rewritten text, no meta-commentary.";
    }
}