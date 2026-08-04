using Api.TokenBurn.Identity.Domain;

namespace Api.TokenBurn.Identity.Tests.Domain;

public sealed class IdentityUserTests
{
    [Fact]
    public void Creates_WithUsernameAndPasswordHash()
    {
        var user = IdentityUser.Create("alice", "hash");

        Assert.Equal("alice", user.Username);
        Assert.Equal("hash", user.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_WhenUsernameBlank(string username)
    {
        Assert.Throws<ArgumentException>(() => IdentityUser.Create(username, "hash"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_WhenPasswordHashBlank(string passwordHash)
    {
        Assert.Throws<ArgumentException>(() => IdentityUser.Create("alice", passwordHash));
    }

    [Fact]
    public void Id_IsZero_UntilPersisted()
    {
        var user = IdentityUser.Create("alice", "hash");

        Assert.Equal(0, user.Id);
    }
}
