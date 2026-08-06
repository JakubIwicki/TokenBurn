namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// Persists the OIDC token bundle. The WPF app provides the DPAPI-backed implementation; Core and
/// tests remain UI-free (tests use an in-memory fake).
/// </summary>
public interface ITokenStore
{
    Task<TokenBundle?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(TokenBundle bundle, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
