using System.Reflection;
using IdentityModel.Client;
using IdentityModel.OidcClient;

namespace TokenBurn.Desktop.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="OidcClient"/> double. The OidcClient result types only expose internal
/// setters (and <c>IsError</c> is computed from <c>Error</c>), so success data is written through a
/// small backing-field helper in <see cref="ScriptedResults"/>.
/// </summary>
internal sealed class ScriptedOidcClient : OidcClient
{
    public Func<LoginRequest?, CancellationToken, Task<LoginResult>> OnLogin { get; set; }
    public Func<string, Parameters?, string?, CancellationToken, Task<RefreshTokenResult>> OnRefresh { get; set; }

    public ScriptedOidcClient()
        : base(new OidcClientOptions
        {
            Authority = "https://localhost/connect",
            ClientId = "tokenburn-desktop",
            RedirectUri = "http://127.0.0.1:7891/callback",
            Scope = "openid insights.read ask.invoke admin",
        })
    {
        OnLogin = (_, _) => Task.FromResult(ScriptedResults.ErrorLogin("not_scripted"));
        OnRefresh = (_, _, _, _) => Task.FromResult(ScriptedResults.ErrorRefresh("not_scripted"));
    }

    public override Task<LoginResult> LoginAsync(LoginRequest? request = null, CancellationToken cancellationToken = default) =>
        OnLogin(request, cancellationToken);

    public override Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken, Parameters? parameters = null, string? backendClientId = null, CancellationToken cancellationToken = default) =>
        OnRefresh(refreshToken, parameters, backendClientId, cancellationToken);
}

internal static class ScriptedResults
{
    public static LoginResult SuccessLogin(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        var result = new LoginResult();
        Set(result, "AccessToken", accessToken);
        Set(result, "RefreshToken", refreshToken);
        Set(result, "AccessTokenExpiration", expiresAt);
        return result;
    }

    public static RefreshTokenResult SuccessRefresh(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        var result = new RefreshTokenResult();
        Set(result, "AccessToken", accessToken);
        Set(result, "RefreshToken", refreshToken);
        Set(result, "AccessTokenExpiration", expiresAt);
        return result;
    }

    public static LoginResult ErrorLogin(string error) => new(error);

    public static RefreshTokenResult ErrorRefresh(string error) => new() { Error = error };

    private static void Set(object target, string propertyName, object value)
    {
        var field = target.GetType().GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"No backing field for {target.GetType().Name}.{propertyName}.");
        field.SetValue(target, value);
    }
}
