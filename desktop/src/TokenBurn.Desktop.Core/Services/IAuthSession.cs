namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// The OIDC session contract driving sign-in, sign-out and automatic token refresh. The shell
/// listens for <see cref="Unauthenticated"/> to flip back to the signed-out UI.
/// </summary>
public interface IAuthSession
{
    bool IsAuthenticated { get; }
    IReadOnlyList<string> GrantedScopes { get; }
    event EventHandler? Unauthenticated;

    Task<bool> SignInAsync(CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);

    /// <summary>Current access token, or null when signed out.</summary>
    Task<TokenBundle?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Single-flight refresh. Null when the refresh failed (caller signs out).</summary>
    Task<TokenBundle?> RefreshTokenAsync(CancellationToken cancellationToken);
}
