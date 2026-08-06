using IdentityModel.Client;
using IdentityModel.OidcClient;
using IdentityModel.OidcClient.Browser;
using TokenBurn.Desktop.Core.Settings;

namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// <see cref="IAuthSession"/> backed by IdentityModel.OidcClient's authorization-code + PKCE flow.
/// The inner <see cref="OidcClient"/> is the scripted test seam: tests subclass and override
/// <see cref="CreateClient"/> to return a fake. The bundle is persisted through <see cref="ITokenStore"/>
/// and refreshed single-flight through a <see cref="SemaphoreSlim"/>.
/// </summary>
public class OidcService : IAuthSession, IDisposable
{
    private readonly ITokenStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<string> _requestedScopes;
    private readonly Lazy<OidcClient> _client;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<string> _grantedScopes = [];
    private bool _authenticated;

    protected OidcClient Client => _client.Value;

    public OidcService(ITokenStore store, TimeProvider timeProvider, DesktopSettings settings, IBrowser? browser = null)
    {
        _store = store;
        _timeProvider = timeProvider;
        _requestedScopes = settings.Scopes;
        _client = new Lazy<OidcClient>(() => CreateClient(BuildOptions(settings, browser)));
    }

    public bool IsAuthenticated => _authenticated;

    public IReadOnlyList<string> GrantedScopes => _grantedScopes;

    public event EventHandler? Unauthenticated;

    protected virtual OidcClient CreateClient(OidcClientOptions options) => new(options);

    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await Client.LoginAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.IsError)
        {
            _authenticated = false;
            _grantedScopes = [];
            return false;
        }

        var bundle = new TokenBundle(
            result.AccessToken,
            result.RefreshToken,
            result.AccessTokenExpiration,
            GrantedScopesFrom(result, _requestedScopes));

        await _store.SaveAsync(bundle, cancellationToken).ConfigureAwait(false);
        _grantedScopes = bundle.Scopes;
        _authenticated = true;
        return true;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var wasAuthenticated = _authenticated;
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        _authenticated = false;
        _grantedScopes = [];
        if (wasAuthenticated)
            Unauthenticated?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TokenBundle?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var bundle = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null)
        {
            _authenticated = false;
            return null;
        }

        _grantedScopes = bundle.Scopes;
        var now = _timeProvider.GetUtcNow();
        var refreshable = bundle.ExpiresAt - now <= TimeSpan.FromMinutes(1) && !string.IsNullOrEmpty(bundle.RefreshToken);
        if (refreshable)
        {
            var refreshed = await RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            _authenticated = refreshed is not null;
            return refreshed;
        }

        if (bundle.ExpiresAt <= now)
        {
            _authenticated = false;
            return null;
        }

        _authenticated = true;
        return bundle;
    }

    public async Task<TokenBundle?> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current is null || string.IsNullOrEmpty(current.RefreshToken))
                return null;

            var result = await Client.RefreshTokenAsync(current.RefreshToken, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.IsError)
                return null;

            var bundle = new TokenBundle(
                result.AccessToken,
                string.IsNullOrEmpty(result.RefreshToken) ? current.RefreshToken : result.RefreshToken,
                result.AccessTokenExpiration,
                current.Scopes);

            await _store.SaveAsync(bundle, cancellationToken).ConfigureAwait(false);
            _grantedScopes = bundle.Scopes;
            _authenticated = true;
            return bundle;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
        if (_client.IsValueCreated && _client.Value is IDisposable disposable)
            disposable.Dispose();
    }

    private static OidcClientOptions BuildOptions(DesktopSettings settings, IBrowser? browser) => new()
    {
        Authority = settings.IdentityAuthorityUrl.ToString(),
        ClientId = settings.ClientId,
        RedirectUri = settings.RedirectUri.ToString(),
        Scope = string.Join(" ", settings.Scopes),
        Browser = browser ?? new LoopbackBrowser(settings.LoopbackPort),
        Policy = new Policy
        {
            Discovery = new DiscoveryPolicy
            {
                ValidateIssuerName = false,
                ValidateEndpoints = false,
            },
        },
    };

    private static IReadOnlyList<string> GrantedScopesFrom(LoginResult result, IReadOnlyList<string> fallback)
    {
        var scope = result.TokenResponse?.Scope;
        return string.IsNullOrWhiteSpace(scope)
            ? fallback
            : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
