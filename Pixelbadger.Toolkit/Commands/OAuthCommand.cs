using System.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Commands;

public static class OAuthCommand
{
    public static Command Create()
    {
        var command = new Command("oauth", "OAuth utilities for managing profiles and acquiring tokens");

        command.Add(CreateTokenCommand());
        command.Add(CreateProfileCommand());

        return command;
    }

    private static Command CreateTokenCommand()
    {
        var command = new Command("token", "Acquire an OAuth access token using Resource Owner Password Credentials");

        var profileOption = new Option<string>("--profile") { Description = "Name of the OAuth profile to use", Required = true };

        command.Add(profileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var profile = parseResult.GetValue(profileOption)!;
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
        });

        return command;
    }

    private static Command CreateProfileCommand()
    {
        var command = new Command("profile", "Manage OAuth profiles");

        command.Add(CreateProfileAddCommand());
        command.Add(CreateProfileUpdateCommand());
        command.Add(CreateProfileDeleteCommand());

        return command;
    }

    private static Command CreateProfileAddCommand()
    {
        var command = new Command("add", "Add a new OAuth profile");

        var nameOption = new Option<string>("--name") { Description = "Profile name", Required = true };
        var authorityOption = new Option<string>("--authority") { Description = "OAuth authority URI (e.g. https://login.microsoftonline.com/tenant)", Required = true };
        var clientIdOption = new Option<string>("--client-id") { Description = "OAuth client ID", Required = true };
        var clientSecretOption = new Option<string?>("--client-secret") { Description = "OAuth client secret (will be prompted securely if omitted)" };
        var scopeOption = new Option<string?>("--scope") { Description = "OAuth scope (optional)" };

        command.Add(nameOption);
        command.Add(authorityOption);
        command.Add(clientIdOption);
        command.Add(clientSecretOption);
        command.Add(scopeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var name = parseResult.GetValue(nameOption)!;
                var authority = parseResult.GetValue(authorityOption)!;
                var clientId = parseResult.GetValue(clientIdOption)!;
                var clientSecret = parseResult.GetValue(clientSecretOption);
                var scope = parseResult.GetValue(scopeOption);

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
        });

        return command;
    }

    private static Command CreateProfileUpdateCommand()
    {
        var command = new Command("update", "Update an existing OAuth profile");

        var nameOption = new Option<string>("--name") { Description = "Profile name to update", Required = true };
        var authorityOption = new Option<string?>("--authority") { Description = "New OAuth authority URI" };
        var clientIdOption = new Option<string?>("--client-id") { Description = "New OAuth client ID" };
        var clientSecretOption = new Option<string?>("--client-secret") { Description = "New OAuth client secret (will be prompted securely if omitted)" };
        var scopeOption = new Option<string?>("--scope") { Description = "New OAuth scope" };

        command.Add(nameOption);
        command.Add(authorityOption);
        command.Add(clientIdOption);
        command.Add(clientSecretOption);
        command.Add(scopeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var name = parseResult.GetValue(nameOption)!;
                var authority = parseResult.GetValue(authorityOption);
                var clientId = parseResult.GetValue(clientIdOption);
                var clientSecret = parseResult.GetValue(clientSecretOption);
                var scope = parseResult.GetValue(scopeOption);

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
        });

        return command;
    }

    private static Command CreateProfileDeleteCommand()
    {
        var command = new Command("delete", "Delete an OAuth profile");

        var nameOption = new Option<string>("--name") { Description = "Profile name to delete", Required = true };

        command.Add(nameOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var name = parseResult.GetValue(nameOption)!;
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
        });

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
