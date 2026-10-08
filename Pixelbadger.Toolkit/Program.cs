using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

var rootCommand = new RootCommand("CLI toolkit exposing varied functionality organized by topic");

rootCommand.Add(StringsCommand.Create());
rootCommand.Add(InterpretersCommand.Create());
rootCommand.Add(ImagesCommand.Create());
rootCommand.Add(WebCommand.Create());
rootCommand.Add(LlmCommand.Create());
rootCommand.Add(OAuthCommand.Create());
rootCommand.Add(CryptoCommand.Create());
rootCommand.Add(MarkovCommand.Create());
rootCommand.Add(DemosceneCommand.Create());
rootCommand.Add(GptCommand.Create());

var store = new SqliteConfigStore();
ConfigBootstrap.AddConfigTopic(rootCommand, store);
return await ConfigBootstrap.InvokeAsync(rootCommand, args, store);
