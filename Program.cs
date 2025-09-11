using System.CommandLine;
using Pixelbadger.Toolkit.Commands;

var rootCommand = new RootCommand("CLI toolkit exposing varied functionality");

rootCommand.AddCommand(ReverseStringCommand.Create());
rootCommand.AddCommand(LevenshteinDistanceCommand.Create());
rootCommand.AddCommand(BrainfuckCommand.Create());
rootCommand.AddCommand(OokCommand.Create());
rootCommand.AddCommand(SteganographyCommand.Create());
rootCommand.AddCommand(HttpServerCommand.Create());

return await rootCommand.InvokeAsync(args);
