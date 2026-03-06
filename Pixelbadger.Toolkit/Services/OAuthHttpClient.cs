using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixelbadger.Toolkit.Services;

public class OAuthHttpClient : IOAuthHttpClient
{
    private readonly HttpClient _httpClient = new();

    public async Task<string> DiscoverTokenEndpointAsync(string authorityUri)
    {
        var discoveryUrl = $"{authorityUri.TrimEnd('/')}/.well-known/openid-configuration";
        var response = await _httpClient.GetAsync(discoveryUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("token_endpoint", out var tokenEndpointElement))
            throw new InvalidOperationException($"OIDC discovery document from '{discoveryUrl}' does not contain 'token_endpoint'.");

        return tokenEndpointElement.GetString()
            ?? throw new InvalidOperationException("'token_endpoint' in OIDC discovery document is null.");
    }

    public async Task<OAuthTokenResponse> RequestTokenAsync(string tokenEndpoint, Dictionary<string, string> parameters)
    {
        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(tokenEndpoint, content);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponseJson>()
            ?? throw new InvalidOperationException("Failed to deserialize token response.");

        return new OAuthTokenResponse
        {
            AccessToken = tokenResponse.AccessToken ?? string.Empty,
            TokenType = tokenResponse.TokenType,
            ExpiresIn = tokenResponse.ExpiresIn
        };
    }

    private sealed class OAuthTokenResponseJson
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }
}
