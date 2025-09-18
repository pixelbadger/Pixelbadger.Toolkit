using System.CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Pixelbadger.Toolkit.Commands;

public static class WebCommand
{
    public static Command Create()
    {
        var command = new Command("web", "Web server utilities");

        command.AddCommand(CreateServeHtmlCommand());

        return command;
    }

    private static Command CreateServeHtmlCommand()
    {
        var command = new Command("serve-html", "Serves a static HTML file via HTTP server");

        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "Path to the HTML file to serve")
        {
            IsRequired = true
        };

        var portOption = new Option<int>(
            aliases: ["--port"],
            description: "Port to bind the server to")
        {
            IsRequired = false
        };
        portOption.SetDefaultValue(8080);

        command.AddOption(fileOption);
        command.AddOption(portOption);

        command.SetHandler(async (string file, int port) =>
        {
            try
            {
                if (!File.Exists(file))
                {
                    Console.WriteLine($"Error: File '{file}' not found");
                    Environment.Exit(1);
                    return;
                }

                var builder = WebApplication.CreateBuilder();
                var app = builder.Build();

                var fileInfo = new FileInfo(file);
                var directory = fileInfo.Directory?.FullName ?? Directory.GetCurrentDirectory();
                var fileName = fileInfo.Name;

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(directory),
                    RequestPath = ""
                });

                var fileContent = await File.ReadAllTextAsync(file);
                var contentType = Path.GetExtension(file).ToLowerInvariant() switch
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

                Console.WriteLine($"Serving '{file}' on http://localhost:{port}");
                Console.WriteLine("Press Ctrl+C to stop the server");

                await app.RunAsync($"http://localhost:{port}");
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