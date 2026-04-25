using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;

namespace Pixelbadger.Toolkit.Tests;

public class WebCommandIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public WebCommandIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }

    [Fact]
    public async Task ServeHtml_ShouldNotExposeSiblingFiles_WhenFileIsServed()
    {
        var htmlFile = Path.Combine(_testDirectory, "index.html");
        var secretFile = Path.Combine(_testDirectory, "secret.txt");
        await File.WriteAllTextAsync(htmlFile, "<h1>Hello</h1>");
        await File.WriteAllTextAsync(secretFile, "do not serve");

        var port = GetAvailablePort();
        using var process = StartToolkitCommand("web", "serve-html", "--file", htmlFile, "--port", port.ToString());

        try
        {
            using var httpClient = new HttpClient();
            await WaitForServerAsync(httpClient, port, process);

            var root = await httpClient.GetStringAsync($"http://localhost:{port}/");
            var siblingResponse = await httpClient.GetAsync($"http://localhost:{port}/secret.txt");

            root.Should().Be("<h1>Hello</h1>");
            siblingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static Process StartToolkitCommand(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(GetToolkitProjectPath());
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return Process.Start(startInfo)!;
    }

    private static async Task WaitForServerAsync(HttpClient httpClient, int port, Process process)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Server exited early. stdout: {stdout} stderr: {stderr}");
            }

            try
            {
                using var response = await httpClient.GetAsync($"http://localhost:{port}/");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Timed out waiting for web server to start.");
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetToolkitProjectPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Pixelbadger.Toolkit/Pixelbadger.Toolkit.csproj"));
}
