using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

public static class OAuthCommand
{
    public static Command Create()
    {
        var command = new Command("oauth", "OAuth utilities for managing profiles and acquiring tokens");

        command.AddCommand(CreateTokenCommand());
        command.AddCommand(CreateProfileCommand());

        return command;
    }

    private static Command CreateTokenCommand()
    {
        var command = new Command("token", "Acquire an OAuth access token using Resource Owner Password Credentials");

        var profileOption = new Option<string>(
            aliases: ["--profile"],
            description: "Name of the OAuth profile to use")
        {
            IsRequired = true
        };

        command.AddOption(profileOption);

        command.SetHandler(async (string profile) =>
        {
            try
            {
                Console.Write("Username: ");
                var username = Console.ReadLine() ?? string.Empty;

                var password = ReadSecureInput("Password: ");

                var profileService = new OAuthProfileService();
                var httpClient = new OAuthHttpClient();
                var tokenComponent = new OAuthTokenComponent(profileService, httpClient);
                var accessToken = await tokenComponent.GetTokenAsync(profile, username, password);

                Console.WriteLine(accessToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, profileOption);

        return command;
    }

    private static Command CreateProfileCommand()
    {
        var command = new Command("profile", "Manage OAuth profiles");

        command.AddCommand(CreateProfileAddCommand());
        command.AddCommand(CreateProfileUpdateCommand());
        command.AddCommand(CreateProfileDeleteCommand());

        return command;
    }

    private static Command CreateProfileAddCommand()
    {
        var command = new Command("add", "Add a new OAuth profile");

        var nameOption = new Option<string>(
            aliases: ["--name"],
            description: "Profile name")
        {
            IsRequired = true
        };

        var authorityOption = new Option<string>(
            aliases: ["--authority"],
            description: "OAuth authority URI (e.g. https://login.microsoftonline.com/tenant)")
        {
            IsRequired = true
        };

        var clientIdOption = new Option<string>(
            aliases: ["--client-id"],
            description: "OAuth client ID")
        {
            IsRequired = true
        };

        var clientSecretOption = new Option<string?>(
            aliases: ["--client-secret"],
            description: "OAuth client secret (will be prompted securely if omitted)")
        {
            IsRequired = false
        };

        var scopeOption = new Option<string?>(
            aliases: ["--scope"],
            description: "OAuth scope (optional)")
        {
            IsRequired = false
        };

        command.AddOption(nameOption);
        command.AddOption(authorityOption);
        command.AddOption(clientIdOption);
        command.AddOption(clientSecretOption);
        command.AddOption(scopeOption);

        command.SetHandler(async (string name, string authority, string clientId, string? clientSecret, string? scope) =>
        {
            try
            {
                if (clientSecret is null)
                    clientSecret = ReadSecureInput("Client Secret (leave blank if none): ");

                var profileService = new OAuthProfileService();
                var profileComponent = new OAuthProfileComponent(profileService);
                await profileComponent.AddProfileAsync(name, authority, clientId, clientSecret, scope);

                Console.WriteLine($"Profile '{name}' added successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameOption, authorityOption, clientIdOption, clientSecretOption, scopeOption);

        return command;
    }

    private static Command CreateProfileUpdateCommand()
    {
        var command = new Command("update", "Update an existing OAuth profile");

        var nameOption = new Option<string>(
            aliases: ["--name"],
            description: "Profile name to update")
        {
            IsRequired = true
        };

        var authorityOption = new Option<string?>(
            aliases: ["--authority"],
            description: "New OAuth authority URI")
        {
            IsRequired = false
        };

        var clientIdOption = new Option<string?>(
            aliases: ["--client-id"],
            description: "New OAuth client ID")
        {
            IsRequired = false
        };

        var clientSecretOption = new Option<string?>(
            aliases: ["--client-secret"],
            description: "New OAuth client secret (will be prompted securely if omitted)")
        {
            IsRequired = false
        };

        var scopeOption = new Option<string?>(
            aliases: ["--scope"],
            description: "New OAuth scope")
        {
            IsRequired = false
        };

        command.AddOption(nameOption);
        command.AddOption(authorityOption);
        command.AddOption(clientIdOption);
        command.AddOption(clientSecretOption);
        command.AddOption(scopeOption);

        command.SetHandler(async (string name, string? authority, string? clientId, string? clientSecret, string? scope) =>
        {
            try
            {
                var profileService = new OAuthProfileService();
                var profileComponent = new OAuthProfileComponent(profileService);
                await profileComponent.UpdateProfileAsync(name, authority, clientId, clientSecret, scope);

                Console.WriteLine($"Profile '{name}' updated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameOption, authorityOption, clientIdOption, clientSecretOption, scopeOption);

        return command;
    }

    private static Command CreateProfileDeleteCommand()
    {
        var command = new Command("delete", "Delete an OAuth profile");

        var nameOption = new Option<string>(
            aliases: ["--name"],
            description: "Profile name to delete")
        {
            IsRequired = true
        };

        command.AddOption(nameOption);

        command.SetHandler(async (string name) =>
        {
            try
            {
                var profileService = new OAuthProfileService();
                var profileComponent = new OAuthProfileComponent(profileService);
                await profileComponent.DeleteProfileAsync(name);

                Console.WriteLine($"Profile '{name}' deleted successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, nameOption);

        return command;
    }

    private static string ReadSecureInput(string prompt)
    {
        Console.Write(prompt);

        var input = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.Write("\b \b");
                }
                continue;
            }

            input.Append(key.KeyChar);
            Console.Write('*');
        }

        return input.ToString();
    }
}
