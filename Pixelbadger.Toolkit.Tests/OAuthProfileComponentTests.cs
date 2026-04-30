using FluentAssertions;
using Moq;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class OAuthProfileComponentTests
{
    private readonly Mock<IOAuthProfileService> _mockProfileService;
    private readonly OAuthProfileComponent _component;

    public OAuthProfileComponentTests()
    {
        _mockProfileService = new Mock<IOAuthProfileService>();
        _component = new OAuthProfileComponent(_mockProfileService.Object);
    }

    [Fact]
    public async Task AddProfileAsync_ShouldSaveNewProfile_WhenNameIsUnique()
    {
        // Arrange
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync([]);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.AddProfileAsync("dev", "https://auth.example.com", "client-id", "secret", "api://res/.default");

        // Assert
        savedProfiles.Should().HaveCount(1);
        savedProfiles![0].Name.Should().Be("dev");
        savedProfiles[0].AuthorityUri.Should().Be("https://auth.example.com");
        savedProfiles[0].ClientId.Should().Be("client-id");
        savedProfiles[0].ClientSecret.Should().Be("secret");
        savedProfiles[0].Scope.Should().Be("api://res/.default");
    }

    [Fact]
    public async Task AddProfileAsync_ShouldSaveProfileWithNullScope_WhenScopeIsNull()
    {
        // Arrange
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync([]);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.AddProfileAsync("dev", "https://auth.example.com", "client-id", "secret", null);

        // Assert
        savedProfiles![0].Scope.Should().BeNull();
    }

    [Fact]
    public async Task AddProfileAsync_ShouldThrow_WhenNameAlreadyExists()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        // Act
        var act = async () => await _component.AddProfileAsync("dev", "https://other.com", "id2", "s2", null);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'dev'*already exists*");
    }

    [Fact]
    public async Task AddProfileAsync_ShouldThrow_WhenAuthorityIsNotHttps()
    {
        var act = async () => await _component.AddProfileAsync("dev", "http://auth.example.com", "id", "secret", null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*absolute HTTPS URI*");
    }

    [Fact]
    public async Task AddProfileAsync_ShouldBeCaseInsensitive_WhenCheckingForDuplicates()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "Dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        // Act
        var act = async () => await _component.AddProfileAsync("dev", "https://other.com", "id2", "s2", null);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldUpdateOnlyProvidedFields()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "dev", AuthorityUri = "https://old.example.com", ClientId = "old-id", ClientSecret = "old-secret", Scope = "old-scope" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.UpdateProfileAsync("dev", "https://new.example.com", null, null, null);

        // Assert
        savedProfiles![0].AuthorityUri.Should().Be("https://new.example.com");
        savedProfiles[0].ClientId.Should().Be("old-id");
        savedProfiles[0].ClientSecret.Should().Be("old-secret");
        savedProfiles[0].Scope.Should().Be("old-scope");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldUpdateAllFields_WhenAllProvided()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "dev", AuthorityUri = "https://old.example.com", ClientId = "old-id", ClientSecret = "old-secret", Scope = "old-scope" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.UpdateProfileAsync("dev", "https://new.example.com", "new-id", "new-secret", "new-scope");

        // Assert
        savedProfiles![0].AuthorityUri.Should().Be("https://new.example.com");
        savedProfiles[0].ClientId.Should().Be("new-id");
        savedProfiles[0].ClientSecret.Should().Be("new-secret");
        savedProfiles[0].Scope.Should().Be("new-scope");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldThrow_WhenProfileNotFound()
    {
        // Arrange
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync([]);

        // Act
        var act = async () => await _component.UpdateProfileAsync("missing", null, null, null, null);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'missing'*not found*");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldThrow_WhenAuthorityIsNotHttps()
    {
        var existing = new List<OAuthProfile>
        {
            new() { Name = "dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        var act = async () => await _component.UpdateProfileAsync("dev", "http://auth.example.com", null, null, null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*absolute HTTPS URI*");
    }

    [Fact]
    public async Task UpdateProfileAsync_ShouldBeCaseInsensitive_WhenFindingProfile()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "Dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret", Scope = null }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>())).Returns(Task.CompletedTask);

        // Act
        var act = async () => await _component.UpdateProfileAsync("dev", "https://new.example.com", null, null, null);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldRemoveProfile_WhenItExists()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret" },
            new() { Name = "prod", AuthorityUri = "https://prod.example.com", ClientId = "id2", ClientSecret = "secret2" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.DeleteProfileAsync("dev");

        // Assert
        savedProfiles.Should().HaveCount(1);
        savedProfiles![0].Name.Should().Be("prod");
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldThrow_WhenProfileNotFound()
    {
        // Arrange
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync([]);

        // Act
        var act = async () => await _component.DeleteProfileAsync("missing");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*'missing'*not found*");
    }

    [Fact]
    public async Task DeleteProfileAsync_ShouldBeCaseInsensitive_WhenFindingProfile()
    {
        // Arrange
        var existing = new List<OAuthProfile>
        {
            new() { Name = "Dev", AuthorityUri = "https://auth.example.com", ClientId = "id", ClientSecret = "secret" }
        };
        _mockProfileService.Setup(x => x.LoadProfilesAsync()).ReturnsAsync(existing);

        List<OAuthProfile>? savedProfiles = null;
        _mockProfileService.Setup(x => x.SaveProfilesAsync(It.IsAny<List<OAuthProfile>>()))
            .Callback<List<OAuthProfile>>(p => savedProfiles = p)
            .Returns(Task.CompletedTask);

        // Act
        await _component.DeleteProfileAsync("dev");

        // Assert
        savedProfiles.Should().BeEmpty();
    }
}
