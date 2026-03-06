namespace Pixelbadger.Toolkit.Models;

public class OAuthProfile
{
    public string Name { get; set; } = string.Empty;
    public string AuthorityUri { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Scope { get; set; }
}
