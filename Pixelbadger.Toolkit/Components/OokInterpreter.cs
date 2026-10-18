namespace Pixelbadger.Toolkit.Components;

public class OokInterpreter
{
    private readonly BrainfuckInterpreter _brainfuckInterpreter;
    private readonly OokBrainfuckTranslator _translator;

    public OokInterpreter()
    {
        _brainfuckInterpreter = new BrainfuckInterpreter();
        _translator = new OokBrainfuckTranslator();
    }

    public async Task<string> ExecuteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Ook program file not found: {filePath}");
        }

        var ookProgram = await File.ReadAllTextAsync(filePath, cancellationToken);
        return Execute(ookProgram, cancellationToken);
    }

    public string Execute(string ookProgram, CancellationToken cancellationToken = default)
    {
        var brainfuckProgram = _translator.TranslateOokToBrainfuck(ookProgram);
        return _brainfuckInterpreter.Execute(brainfuckProgram, cancellationToken);
    }

}