using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Components;

public class OAuthTokenComponent(IOAuthProfileService profileService, IOAuthHttpClient httpClient)
{
    public async Task<string> GetTokenAsync(string profileName, string username, string password)
    {
        var profile = await profileService.GetProfileAsync(profileName)
            ?? throw new InvalidOperationException($"Profile '{profileName}' not found.");

        var authorityUri = ValidateHttpsUri(profile.AuthorityUri, "authority URI");
        var tokenEndpoint = await httpClient.DiscoverTokenEndpointAsync(profile.AuthorityUri);
        var tokenEndpointUri = ValidateHttpsUri(tokenEndpoint, "token endpoint");

        if (!string.Equals(authorityUri.Host, tokenEndpointUri.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OAuth token endpoint host must match the authority host.");

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

    private static Uri ValidateHttpsUri(string value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"OAuth {description} must be an absolute HTTPS URI.");

        return uri;
    }
}
