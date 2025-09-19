namespace Pixelbadger.Toolkit.Components;

public class BfToOokComponent
{
    private readonly OokBrainfuckTranslator _translator;

    public BfToOokComponent()
    {
        _translator = new OokBrainfuckTranslator();
    }

    public async Task TranslateFileAsync(string sourceFile, string outputFile)
    {
        var ookCode = await _translator.TranslateBrainfuckFileToOokAsync(sourceFile);

        var outputDirectory = Path.GetDirectoryName(outputFile);
        if (outputDirectory != null && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputFile, ookCode);
    }
}