namespace Pixelbadger.Toolkit.Components;

public class OokInterpreter
{
    private readonly BrainfuckInterpreter _brainfuckInterpreter;

    public OokInterpreter()
    {
        _brainfuckInterpreter = new BrainfuckInterpreter();
    }

    public async Task<string> ExecuteAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Ook program file not found: {filePath}");
        }

        var ookProgram = await File.ReadAllTextAsync(filePath);
        return Execute(ookProgram);
    }

    public string Execute(string ookProgram)
    {
        var brainfuckProgram = TranslateOokToBrainfuck(ookProgram);
        return _brainfuckInterpreter.Execute(brainfuckProgram);
    }

    private string TranslateOokToBrainfuck(string ookProgram)
    {
        var tokens = TokenizeOok(ookProgram);
        var brainfuckCode = new List<char>();

        for (int i = 0; i < tokens.Count - 1; i += 2)
        {
            var token1 = tokens[i];
            var token2 = tokens[i + 1];
            
            var command = (token1, token2) switch
            {
                ("Ook.", "Ook?") => '>',
                ("Ook?", "Ook.") => '<',
                ("Ook.", "Ook.") => '+',
                ("Ook!", "Ook!") => '-',
                ("Ook!", "Ook.") => '.',
                ("Ook.", "Ook!") => ',',
                ("Ook!", "Ook?") => '[',
                ("Ook?", "Ook!") => ']',
                _ => '\0'
            };

            if (command != '\0')
            {
                brainfuckCode.Add(command);
            }
        }

        return new string(brainfuckCode.ToArray());
    }

    private List<string> TokenizeOok(string ookProgram)
    {
        var tokens = new List<string>();
        var cleanedProgram = ookProgram.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        var words = cleanedProgram.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (word.StartsWith("Ook") && (word.EndsWith(".") || word.EndsWith("?") || word.EndsWith("!")))
            {
                tokens.Add(word);
            }
        }

        return tokens;
    }
}