using OpenAI.Chat;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class OcaaarComponent
{
    private readonly ILlmClientService _llmClientService;
    private readonly IHistoryService _historyService;

    public OcaaarComponent(ILlmClientService llmClientService, IHistoryService historyService)
    {
        _llmClientService = llmClientService;
        _historyService = historyService;
    }

    public async Task<string> OcaaarAsync(string imagePath)
    {
        var resolvedPath = Path.GetFullPath(imagePath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }

        var imageBytes = await File.ReadAllBytesAsync(resolvedPath);
        var imageData = BinaryData.FromBytes(imageBytes);

        var systemPrompt = "You are a salty and seasoned pirate, with excellent reading capabilities. The user will submit an image. You are to extract the text from that image and translate that text into your bucaneering dialect. Return ONLY the pirate-translated text, nothing else - no explanations, no original text, no additional commentary. Just the buccaneer version of what ye read, savvy? Aaargh me hearties, splice the main brace!";

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateImagePart(imageData, GetImageMediaType(imagePath))
            )
        };

        var chatResult = await _llmClientService.CompleteChatAsync(messages);

        var sessionId = await _historyService.CreateSessionAsync("ocaaar");
        await _historyService.AddMessageAsync(sessionId, "system", systemPrompt);
        await _historyService.AddMessageAsync(sessionId, "user", $"[image: {resolvedPath}]");
        await _historyService.AddMessageAsync(sessionId, "assistant", chatResult.Content);
        await _historyService.UpdateTokenUsageAsync(sessionId, chatResult.PromptTokens, chatResult.CompletionTokens);

        return chatResult.Content;
    }

    private static string GetImageMediaType(string imagePath)
    {
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }
}
