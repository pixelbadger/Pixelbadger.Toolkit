using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class OAuthProfileComponent(IOAuthProfileService profileService)
{
    public async Task AddProfileAsync(string name, string authorityUri, string clientId, string clientSecret, string? scope)
    {
        var profiles = await profileService.LoadProfilesAsync();

        if (profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Profile '{name}' already exists.");

        profiles.Add(new OAuthProfile
        {
            Name = name,
            AuthorityUri = authorityUri,
            ClientId = clientId,
            ClientSecret = clientSecret,
            Scope = scope
        });

        await profileService.SaveProfilesAsync(profiles);
    }

    public async Task UpdateProfileAsync(string name, string? authorityUri, string? clientId, string? clientSecret, string? scope)
    {
        var profiles = await profileService.LoadProfilesAsync();
        var profile = profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Profile '{name}' not found.");

        if (authorityUri is not null) profile.AuthorityUri = authorityUri;
        if (clientId is not null) profile.ClientId = clientId;
        if (clientSecret is not null) profile.ClientSecret = clientSecret;
        if (scope is not null) profile.Scope = scope;

        await profileService.SaveProfilesAsync(profiles);
    }

    public async Task DeleteProfileAsync(string name)
    {
        var profiles = await profileService.LoadProfilesAsync();
        var profile = profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Profile '{name}' not found.");

        profiles.Remove(profile);
        await profileService.SaveProfilesAsync(profiles);
    }
}
