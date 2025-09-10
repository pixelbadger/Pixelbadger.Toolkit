using System.CommandLine;
using Pixelbadger.Toolkit.Commands;

var rootCommand = new RootCommand("CLI toolkit exposing varied functionality");

rootCommand.AddCommand(ReverseStringCommand.Create());
rootCommand.AddCommand(LevenshteinDistanceCommand.Create());

return await rootCommand.InvokeAsync(args);
