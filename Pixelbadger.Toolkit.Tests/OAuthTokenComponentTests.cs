using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class OAuthTokenComponentTests
{
    private readonly Mock<IOAuthProfileService> _mockProfileService;
    private readonly Mock<IOAuthHttpClient> _mockHttpClient;
    private readonly OAuthTokenComponent _component;

    public OAuthTokenComponentTests()
    {
        _mockProfileService = new Mock<IOAuthProfileService>();
        _mockHttpClient = new Mock<IOAuthHttpClient>();
        _component = new OAuthTokenComponent(_mockProfileService.Object, _mockHttpClient.Object);
    }

    [Fact]
    public async Task GetTokenAsync_ShouldReturnAccessToken_WhenProfileExistsAndRequestSucceeds()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "my-client-id",
            ClientSecret = "my-secret",
            Scope = "api://my-api/.default"
        };

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync("https://auth.example.com"))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(
                "https://auth.example.com/token",
                It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "eyJaccess_token" });

        // Act
        var result = await _component.GetTokenAsync("dev", "user@example.com", "password123");

        // Assert
        result.Should().Be("eyJaccess_token");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldThrow_WhenProfileNotFound()
    {
        // Arrange
        _mockProfileService.Setup(x => x.GetProfileAsync("missing")).ReturnsAsync((OAuthProfile?)null);

        // Act
        var act = async () => await _component.GetTokenAsync("missing", "user", "pass");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'missing'*not found*");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldIncludeClientSecret_WhenProfileHasOne()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "client-id",
            ClientSecret = "secret",
            Scope = null
        };

        Dictionary<string, string>? capturedParams = null;

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, Dictionary<string, string>>((_, p) => capturedParams = p)
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "token" });

        // Act
        await _component.GetTokenAsync("dev", "user", "pass");

        // Assert
        capturedParams.Should().ContainKey("client_secret").WhoseValue.Should().Be("secret");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldNotIncludeClientSecret_WhenProfileHasNone()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "client-id",
            ClientSecret = string.Empty,
            Scope = null
        };

        Dictionary<string, string>? capturedParams = null;

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, Dictionary<string, string>>((_, p) => capturedParams = p)
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "token" });

        // Act
        await _component.GetTokenAsync("dev", "user", "pass");

        // Assert
        capturedParams.Should().NotContainKey("client_secret");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldIncludeScope_WhenProfileHasOne()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "client-id",
            ClientSecret = string.Empty,
            Scope = "api://resource/.default"
        };

        Dictionary<string, string>? capturedParams = null;

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, Dictionary<string, string>>((_, p) => capturedParams = p)
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "token" });

        // Act
        await _component.GetTokenAsync("dev", "user", "pass");

        // Assert
        capturedParams.Should().ContainKey("scope").WhoseValue.Should().Be("api://resource/.default");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldNotIncludeScope_WhenProfileHasNone()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "client-id",
            ClientSecret = string.Empty,
            Scope = null
        };

        Dictionary<string, string>? capturedParams = null;

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, Dictionary<string, string>>((_, p) => capturedParams = p)
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "token" });

        // Act
        await _component.GetTokenAsync("dev", "user", "pass");

        // Assert
        capturedParams.Should().NotContainKey("scope");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldAlwaysIncludeGrantTypePasswordUsernameClientId()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "my-client",
            ClientSecret = string.Empty,
            Scope = null
        };

        Dictionary<string, string>? capturedParams = null;

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, Dictionary<string, string>>((_, p) => capturedParams = p)
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = "token" });

        // Act
        await _component.GetTokenAsync("dev", "testuser", "testpass");

        // Assert
        capturedParams.Should().ContainKey("grant_type").WhoseValue.Should().Be("password");
        capturedParams.Should().ContainKey("username").WhoseValue.Should().Be("testuser");
        capturedParams.Should().ContainKey("password").WhoseValue.Should().Be("testpass");
        capturedParams.Should().ContainKey("client_id").WhoseValue.Should().Be("my-client");
    }

    [Fact]
    public async Task GetTokenAsync_ShouldThrow_WhenTokenResponseHasEmptyAccessToken()
    {
        // Arrange
        var profile = new OAuthProfile
        {
            Name = "dev",
            AuthorityUri = "https://auth.example.com",
            ClientId = "client-id",
            ClientSecret = string.Empty,
            Scope = null
        };

        _mockProfileService.Setup(x => x.GetProfileAsync("dev")).ReturnsAsync(profile);
        _mockHttpClient.Setup(x => x.DiscoverTokenEndpointAsync(It.IsAny<string>()))
            .ReturnsAsync("https://auth.example.com/token");
        _mockHttpClient.Setup(x => x.RequestTokenAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new OAuthTokenResponse { AccessToken = string.Empty });

        // Act
        var act = async () => await _component.GetTokenAsync("dev", "user", "pass");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*access token*");
    }
}
