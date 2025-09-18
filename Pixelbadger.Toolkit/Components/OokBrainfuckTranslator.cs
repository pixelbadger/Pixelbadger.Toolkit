namespace Pixelbadger.Toolkit.Components;

public class OokBrainfuckTranslator
{
    public string TranslateOokToBrainfuck(string ookProgram)
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

    public string TranslateBrainfuckToOok(string brainfuckProgram)
    {
        var ookTokens = new List<string>();

        foreach (var ch in brainfuckProgram)
        {
            var (token1, token2) = ch switch
            {
                '>' => ("Ook.", "Ook?"),
                '<' => ("Ook?", "Ook."),
                '+' => ("Ook.", "Ook."),
                '-' => ("Ook!", "Ook!"),
                '.' => ("Ook!", "Ook."),
                ',' => ("Ook.", "Ook!"),
                '[' => ("Ook!", "Ook?"),
                ']' => ("Ook?", "Ook!"),
                _ => (null, null)
            };

            if (token1 != null && token2 != null)
            {
                ookTokens.Add(token1);
                ookTokens.Add(token2);
            }
        }

        return string.Join(" ", ookTokens);
    }

    public async Task<string> TranslateOokFileToBrainfuckAsync(string ookFilePath)
    {
        if (!File.Exists(ookFilePath))
        {
            throw new FileNotFoundException($"Ook program file not found: {ookFilePath}");
        }

        var ookProgram = await File.ReadAllTextAsync(ookFilePath);
        return TranslateOokToBrainfuck(ookProgram);
    }

    public async Task<string> TranslateBrainfuckFileToOokAsync(string brainfuckFilePath)
    {
        if (!File.Exists(brainfuckFilePath))
        {
            throw new FileNotFoundException($"Brainfuck program file not found: {brainfuckFilePath}");
        }

        var brainfuckProgram = await File.ReadAllTextAsync(brainfuckFilePath);
        return TranslateBrainfuckToOok(brainfuckProgram);
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