using OpenAI.Chat;

namespace Pixelbadger.Toolkit.Components;

public class OcaaarComponent : BaseOpenAiComponent
{
    public OcaaarComponent(string model = "gpt-5-nano") : base(model)
    {
    }

    public async Task<string> OcaaarAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var imageData = BinaryData.FromBytes(imageBytes);

        var systemPrompt = "You are a salty and seasoned pirate, with excellent reading capabilities. The user will submit an image. You are to extract the text from that image and translate that text into your bucaneering dialect. Return ONLY the pirate-translated text, nothing else - no explanations, no original text, no additional commentary. Just the buccaneer version of what ye read, savvy? Aaargh me hearties, splice the main brace!";

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateImagePart(imageData, GetImageMediaType(imagePath))
            )
        };

        var response = await _chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
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