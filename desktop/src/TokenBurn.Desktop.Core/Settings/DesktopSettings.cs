namespace TokenBurn.Desktop.Core.Settings;

/// <summary>
/// Typed, fail-fast-validated settings for the desktop operator console. Validated once at the
/// composition root before any service is built.
/// </summary>
public sealed class DesktopSettings
{
    public Uri ApiBaseUrl { get; set; } = new("https://localhost/");

    public Uri IdentityAuthorityUrl { get; set; } = new("https://localhost/connect");

    public string ClientId { get; set; } = "tokenburn-desktop";

    public Uri RedirectUri { get; set; } = new("http://127.0.0.1:7891/callback");

    public int LoopbackPort { get; set; } = 7891;

    public IReadOnlyList<string> Scopes { get; set; } = ["openid", "insights.read", "ask.invoke", "admin"];

    public TimeSpan RefreshLoopInterval { get; set; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (ApiBaseUrl is null || !ApiBaseUrl.IsAbsoluteUri || ApiBaseUrl.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"DesktopSettings.ApiBaseUrl must be an absolute https URL (was '{ApiBaseUrl}').");
        if (IdentityAuthorityUrl is null || !IdentityAuthorityUrl.IsAbsoluteUri || IdentityAuthorityUrl.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"DesktopSettings.IdentityAuthorityUrl must be an absolute https URL (was '{IdentityAuthorityUrl}').");
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("DesktopSettings.ClientId must not be empty.");
        if (RedirectUri is null || !RedirectUri.IsAbsoluteUri || RedirectUri.Port != LoopbackPort)
            throw new InvalidOperationException($"DesktopSettings.RedirectUri must be absolute and use the loopback port {LoopbackPort} (was '{RedirectUri}').");
        if (RefreshLoopInterval <= TimeSpan.Zero)
            throw new InvalidOperationException($"DesktopSettings.RefreshLoopInterval must be positive (was '{RefreshLoopInterval}').");
    }
}
