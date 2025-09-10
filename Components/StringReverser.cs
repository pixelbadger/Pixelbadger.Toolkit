namespace Pixelbadger.Toolkit.Components;

public class StringReverser
{
    public async Task ReverseFileAsync(string inputFilePath, string outputFilePath)
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"Input file '{inputFilePath}' does not exist.");
        }

        var content = await File.ReadAllTextAsync(inputFilePath);
        var reversedContent = new string(content.Reverse().ToArray());
        await File.WriteAllTextAsync(outputFilePath, reversedContent);
    }
}