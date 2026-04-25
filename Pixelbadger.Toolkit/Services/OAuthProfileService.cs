using System.Text.Json;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Services;

public class OAuthProfileService : IOAuthProfileService
{
    public string ProfilesFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pbtk",
        "oauth-profiles.json");

    public async Task<List<OAuthProfile>> LoadProfilesAsync()
    {
        if (!File.Exists(ProfilesFilePath))
            return [];

        var json = await File.ReadAllTextAsync(ProfilesFilePath);
        return JsonSerializer.Deserialize<List<OAuthProfile>>(json) ?? [];
    }

    public async Task SaveProfilesAsync(List<OAuthProfile> profiles)
    {
        var directory = Path.GetDirectoryName(ProfilesFilePath)!;
        Directory.CreateDirectory(directory);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ProfilesFilePath, json);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(ProfilesFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public async Task<OAuthProfile?> GetProfileAsync(string name)
    {
        var profiles = await LoadProfilesAsync();
        return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
