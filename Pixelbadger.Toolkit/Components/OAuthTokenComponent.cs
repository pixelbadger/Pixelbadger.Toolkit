using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class OAuthTokenComponent(IOAuthProfileService profileService, IOAuthHttpClient httpClient)
{
    public async Task<string> GetTokenAsync(string profileName, string username, string password)
    {
        var profile = await profileService.GetProfileAsync(profileName)
            ?? throw new InvalidOperationException($"Profile '{profileName}' not found.");

        var tokenEndpoint = await httpClient.DiscoverTokenEndpointAsync(profile.AuthorityUri);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["client_id"] = profile.ClientId
        };

        if (!string.IsNullOrEmpty(profile.ClientSecret))
            parameters["client_secret"] = profile.ClientSecret;

        if (!string.IsNullOrEmpty(profile.Scope))
            parameters["scope"] = profile.Scope;

        var tokenResponse = await httpClient.RequestTokenAsync(tokenEndpoint, parameters);

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Token response did not contain an access token.");

        return tokenResponse.AccessToken;
    }
}
