using System.CommandLine;
using Pixelbadger.Toolkit.Commands;

var rootCommand = new RootCommand("CLI toolkit exposing varied functionality organized by topic");

rootCommand.AddCommand(StringsCommand.Create());
rootCommand.AddCommand(SearchCommand.Create());
rootCommand.AddCommand(McpCommand.Create());
rootCommand.AddCommand(InterpretersCommand.Create());
rootCommand.AddCommand(ImagesCommand.Create());
rootCommand.AddCommand(WebCommand.Create());
rootCommand.AddCommand(LlmCommand.Create());

return await rootCommand.InvokeAsync(args);
