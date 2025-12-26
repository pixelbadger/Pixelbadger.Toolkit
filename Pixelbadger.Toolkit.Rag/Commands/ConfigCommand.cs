using System.CommandLine;
using Pixelbadger.Toolkit.Rag.Components;

namespace Pixelbadger.Toolkit.Rag.Commands;

public static class ConfigCommand
{
    public static Command Create()
    {
        var command = new Command("config", "Manage pbrag configuration settings");

        command.AddCommand(CreateSetCommand());
        command.AddCommand(CreateGetCommand());
        command.AddCommand(CreateListCommand());
        command.AddCommand(CreateUnsetCommand());
        command.AddCommand(CreatePathCommand());

        return command;
    }

    private static Command CreateSetCommand()
    {
        var command = new Command("set", "Set a configuration value");

        var keyArgument = new Argument<string>(
            name: "key",
            description: "Configuration key (index-path, max-results, chunking-strategy)");

        var valueArgument = new Argument<string>(
            name: "value",
            description: "Configuration value");

        command.AddArgument(keyArgument);
        command.AddArgument(valueArgument);

        command.SetHandler((string key, string value) =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                configManager.SetValue(key, value);
                Console.WriteLine($"Configuration updated: {key} = {value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, keyArgument, valueArgument);

        return command;
    }

    private static Command CreateGetCommand()
    {
        var command = new Command("get", "Get a configuration value");

        var keyArgument = new Argument<string>(
            name: "key",
            description: "Configuration key (index-path, max-results, chunking-strategy)");

        command.AddArgument(keyArgument);

        command.SetHandler((string key) =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                var value = configManager.GetValue(key);

                if (value == null)
                {
                    Console.WriteLine($"{key} is not set");
                }
                else
                {
                    Console.WriteLine(value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, keyArgument);

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List all configuration values");

        command.SetHandler(() =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                var config = configManager.ListAll();

                Console.WriteLine("Configuration:");
                foreach (var kvp in config)
                {
                    var displayValue = kvp.Value ?? "(not set)";
                    Console.WriteLine($"  {kvp.Key} = {displayValue}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        });

        return command;
    }

    private static Command CreateUnsetCommand()
    {
        var command = new Command("unset", "Remove a configuration value");

        var keyArgument = new Argument<string>(
            name: "key",
            description: "Configuration key (index-path, max-results, chunking-strategy)");

        command.AddArgument(keyArgument);

        command.SetHandler((string key) =>
        {
            try
            {
                var configManager = new ConfigurationManager();
                configManager.UnsetValue(key);
                Console.WriteLine($"Configuration cleared: {key}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, keyArgument);

        return command;
    }

    private static Command CreatePathCommand()
    {
        var command = new Command("path", "Show the path to the configuration file");

        command.SetHandler(() =>
        {
            var configManager = new ConfigurationManager();
            Console.WriteLine(configManager.GetConfigFilePath());
        });

        return command;
    }
}
