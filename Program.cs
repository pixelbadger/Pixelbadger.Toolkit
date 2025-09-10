using System.CommandLine;

var rootCommand = new RootCommand("CLI toolkit exposing varied functionality");

var reverseStringCommand = new Command("reverse-string", "Reverses the content of a file");

var inFileOption = new Option<string>(
    aliases: ["--in-file"],
    description: "Input file path")
{
    IsRequired = true
};

var outFileOption = new Option<string>(
    aliases: ["--out-file"],
    description: "Output file path")
{
    IsRequired = true
};

reverseStringCommand.AddOption(inFileOption);
reverseStringCommand.AddOption(outFileOption);

reverseStringCommand.SetHandler(async (string inFile, string outFile) =>
{
    try
    {
        if (!File.Exists(inFile))
        {
            Console.WriteLine($"Error: Input file '{inFile}' does not exist.");
            Environment.Exit(1);
        }

        var content = await File.ReadAllTextAsync(inFile);
        var reversedContent = new string(content.Reverse().ToArray());
        await File.WriteAllTextAsync(outFile, reversedContent);
        
        Console.WriteLine($"Successfully reversed content from '{inFile}' to '{outFile}'");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}, inFileOption, outFileOption);

rootCommand.AddCommand(reverseStringCommand);

return await rootCommand.InvokeAsync(args);
