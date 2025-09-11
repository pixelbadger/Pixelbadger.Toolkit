using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pixelbadger.Toolkit.Commands;

public static class HttpServerCommand
{
    public static Command Create()
    {
        var command = new Command("serve-html", "Serves a static HTML file via HTTP");

        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "Path to the HTML file to serve")
        {
            IsRequired = true
        };

        var portOption = new Option<int>(
            aliases: ["--port"],
            description: "Port to bind the server to",
            getDefaultValue: () => 8080);

        command.AddOption(fileOption);
        command.AddOption(portOption);

        command.SetHandler(async (string filePath, int port) =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Error: File '{filePath}' does not exist.");
                    Environment.Exit(1);
                    return;
                }

                var builder = WebApplication.CreateBuilder();
                
                builder.Services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                });

                builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

                var app = builder.Build();

                var fileContent = await File.ReadAllTextAsync(filePath);
                var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
                {
                    ".html" or ".htm" => "text/html",
                    ".css" => "text/css",
                    ".js" => "application/javascript",
                    ".json" => "application/json",
                    ".xml" => "application/xml",
                    _ => "text/plain"
                };

                app.MapGet("/", async context =>
                {
                    context.Response.ContentType = contentType;
                    await context.Response.WriteAsync(fileContent);
                });

                Console.WriteLine($"Serving '{filePath}' at http://0.0.0.0:{port}");
                Console.WriteLine("Press Ctrl+C to stop the server");

                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }, fileOption, portOption);

        return command;
    }
}