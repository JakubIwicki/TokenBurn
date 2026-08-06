using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Settings;

namespace TokenBurn.Desktop.Tests.Services;

public sealed class OidcServiceTests
{
    private static readonly DateTimeOffset Expiry = new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class ScriptedOidcService : OidcService
    {
        private readonly ScriptedOidcClient _scripted;

        public ScriptedOidcService(ScriptedOidcClient scripted, ITokenStore store, TimeProvider timeProvider, DesktopSettings settings)
            : base(store, timeProvider, settings) => _scripted = scripted;

        protected override OidcClient CreateClient(OidcClientOptions options) => _scripted;
    }

    private sealed class Fixture
    {
        public FakeTokenStore Store { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public ScriptedOidcClient Client { get; } = new();
        public DesktopSettings Settings { get; } = new();
        public ScriptedOidcService Sut { get; }

        public Fixture() => Sut = new ScriptedOidcService(Client, Store, Clock, Settings);
    }

    [Fact]
    public async Task SignInAsync_Success_WritesBundleToStoreAndSetsScopes()
    {
        var fx = new Fixture();
        fx.Client.OnLogin = (_, _) => Task.FromResult(ScriptedResults.SuccessLogin("access-1", "refresh-1", Expiry));

        var ok = await fx.Sut.SignInAsync(CancellationToken.None);

        ok.Should().BeTrue();
        fx.Sut.IsAuthenticated.Should().BeTrue();
        fx.Sut.GrantedScopes.Should().Equal(fx.Settings.Scopes);
        fx.Store.Stored.Should().NotBeNull();
        fx.Store.Stored!.AccessToken.Should().Be("access-1");
        fx.Store.Stored!.RefreshToken.Should().Be("refresh-1");
        fx.Store.Stored!.ExpiresAt.Should().Be(Expiry);
        fx.Store.Stored!.Scopes.Should().Equal(fx.Settings.Scopes);
    }

    [Fact]
    public async Task SignInAsync_Failure_DoesNotWriteStore()
    {
        var fx = new Fixture();
        fx.Client.OnLogin = (_, _) => Task.FromResult(ScriptedResults.ErrorLogin("access_denied"));

        var ok = await fx.Sut.SignInAsync(CancellationToken.None);

        ok.Should().BeFalse();
        fx.Sut.IsAuthenticated.Should().BeFalse();
        fx.Store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_Success_WritesRefreshedBundle()
    {
        var fx = new Fixture();
        await fx.Store.SaveAsync(new TokenBundle("old-access", "old-refresh", Expiry, ["openid"]), CancellationToken.None);
        var newExpiry = new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero);
        string? seenRefreshToken = null;
        fx.Client.OnRefresh = (refreshToken, _, _, _) =>
        {
            seenRefreshToken = refreshToken;
            return Task.FromResult(ScriptedResults.SuccessRefresh("new-access", "new-refresh", newExpiry));
        };

        var bundle = await fx.Sut.RefreshTokenAsync(CancellationToken.None);

        bundle.Should().NotBeNull();
        seenRefreshToken.Should().Be("old-refresh");
        fx.Store.Stored!.AccessToken.Should().Be("new-access");
        fx.Store.Stored!.RefreshToken.Should().Be("new-refresh");
        fx.Store.Stored!.ExpiresAt.Should().Be(newExpiry);
        fx.Sut.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenRefreshFails_ReturnsNullAndKeepsStore()
    {
        var fx = new Fixture();
        await fx.Store.SaveAsync(new TokenBundle("old-access", "old-refresh", Expiry, ["openid"]), CancellationToken.None);
        fx.Client.OnRefresh = (_, _, _, _) => Task.FromResult(ScriptedResults.ErrorRefresh("invalid_grant"));

        var bundle = await fx.Sut.RefreshTokenAsync(CancellationToken.None);

        bundle.Should().BeNull();
        fx.Store.Stored!.AccessToken.Should().Be("old-access");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithoutRefreshToken_ReturnsNullWithoutCallingClient()
    {
        var fx = new Fixture();
        await fx.Store.SaveAsync(new TokenBundle("old-access", null, Expiry, ["openid"]), CancellationToken.None);
        var refreshInvoked = false;
        fx.Client.OnRefresh = (_, _, _, _) =>
        {
            refreshInvoked = true;
            return Task.FromResult(ScriptedResults.ErrorRefresh("never"));
        };

        var bundle = await fx.Sut.RefreshTokenAsync(CancellationToken.None);

        bundle.Should().BeNull();
        refreshInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task SignOutAsync_ClearsStoreAndRaisesUnauthenticated()
    {
        var fx = new Fixture();
        fx.Client.OnLogin = (_, _) => Task.FromResult(ScriptedResults.SuccessLogin("access-1", "refresh-1", Expiry));
        await fx.Sut.SignInAsync(CancellationToken.None);
        var raised = false;
        fx.Sut.Unauthenticated += (_, _) => raised = true;

        await fx.Sut.SignOutAsync(CancellationToken.None);

        raised.Should().BeTrue();
        fx.Store.Stored.Should().BeNull();
        fx.Sut.IsAuthenticated.Should().BeFalse();
        fx.Sut.GrantedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTokenAsync_WhenFresh_ReturnsStoredBundle()
    {
        var fx = new Fixture();
        await fx.Store.SaveAsync(new TokenBundle("access-1", "refresh-1", Expiry, ["openid"]), CancellationToken.None);

        var bundle = await fx.Sut.GetTokenAsync(CancellationToken.None);

        bundle.Should().NotBeNull();
        bundle!.AccessToken.Should().Be("access-1");
        fx.Sut.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenExpiring_Refreshes()
    {
        var fx = new Fixture();
        // now (2030-01-01T00:00:00Z) + 30s — inside the 1-minute refresh window.
        var expiring = new DateTimeOffset(2030, 1, 1, 0, 0, 30, TimeSpan.Zero);
        await fx.Store.SaveAsync(new TokenBundle("old-access", "old-refresh", expiring, ["openid"]), CancellationToken.None);
        var newExpiry = new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero);
        fx.Client.OnRefresh = (_, _, _, _) => Task.FromResult(ScriptedResults.SuccessRefresh("new-access", "new-refresh", newExpiry));

        var bundle = await fx.Sut.GetTokenAsync(CancellationToken.None);

        bundle.Should().NotBeNull();
        bundle!.AccessToken.Should().Be("new-access");
        fx.Store.Stored!.AccessToken.Should().Be("new-access");
    }

    [Fact]
    public async Task GetTokenAsync_WhenExpiredAndNoRefreshToken_ReturnsNull()
    {
        var fx = new Fixture();
        var expired = new DateTimeOffset(2029, 12, 31, 23, 59, 0, TimeSpan.Zero);
        await fx.Store.SaveAsync(new TokenBundle("old-access", null, expired, ["openid"]), CancellationToken.None);

        var bundle = await fx.Sut.GetTokenAsync(CancellationToken.None);

        bundle.Should().BeNull();
        fx.Sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task GetTokenAsync_WhenStoreEmpty_ReturnsNull()
    {
        var fx = new Fixture();

        var bundle = await fx.Sut.GetTokenAsync(CancellationToken.None);

        bundle.Should().BeNull();
        fx.Sut.IsAuthenticated.Should().BeFalse();
    }
}
