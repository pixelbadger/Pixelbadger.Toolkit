namespace Pixelbadger.Toolkit.Services;

public interface IOAuthHttpClient
{
    Task<string> DiscoverTokenEndpointAsync(string authorityUri);
    Task<OAuthTokenResponse> RequestTokenAsync(string tokenEndpoint, Dictionary<string, string> parameters);
}

public class OAuthTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string? TokenType { get; set; }
    public int? ExpiresIn { get; set; }
}
