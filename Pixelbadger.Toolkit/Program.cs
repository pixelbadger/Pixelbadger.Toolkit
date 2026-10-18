using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Commands;
using Pixelbadger.Toolkit.Services;

var store = new SqliteConfigStore();
var rootCommand = ToolkitCommandTree.Create(store);
return await ConfigBootstrap.InvokeAsync(rootCommand, args, store);
