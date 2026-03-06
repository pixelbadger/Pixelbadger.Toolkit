using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Services;

public interface IOAuthProfileService
{
    Task<List<OAuthProfile>> LoadProfilesAsync();
    Task SaveProfilesAsync(List<OAuthProfile> profiles);
    Task<OAuthProfile?> GetProfileAsync(string name);
    string ProfilesFilePath { get; }
}
