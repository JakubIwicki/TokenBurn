using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Api.TokenBurn.Identity.Domain;
using Api.TokenBurn.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

[assembly: InternalsVisibleTo("Api.TokenBurn.Identity.Tests")]

namespace Api.TokenBurn.Identity.Controllers;

/// <summary>
///     Serves the interactive authorization-code + PKCE flow for the desktop client:
///     /connect/authorize (challenge or sign in), /connect/login (inline HTML form)
///     and /connect/logout. Plain MVC — not [ApiController] — because the actions
///     return redirects and HTML, never JSON.
/// </summary>
public sealed class AuthorizationController(IdentitySeeder users) : ControllerBase
{
    // Login-cookie scheme backed by the Cookie handler registered in AddApiServices.
    public const string LoginCookieScheme = "IdentityLoginCookie";

    // Fixed PBKDF2-HMACSHA512 (100k iterations) dummy hash, same contract as
    // TokenController: when the requested username does not exist, the password is
    // still verified against it so the missing-user path pays the same hash cost.
    private const string DummyPasswordHash = "AQAAAAMAAYagAAAAEAABAgMEBQYHCAkKCwwNDg85L1hW7G6JsoVP1Bd/6o+GOoKtSt9pPLB2AcJ6wH57ug==";
    private static readonly IdentityUser DummyUser = IdentityUser.Create("dummy-user", DummyPasswordHash);

    [HttpGet("/connect/authorize")]
    public async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken)
    {
        OpenIddictRequest? request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(HttpContext);
        if (request is null)
            return BadRequest();

        AuthenticateResult signInResult = await HttpContext.AuthenticateAsync(LoginCookieScheme);
        if (!signInResult.Succeeded)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + Request.QueryString
                },
                [LoginCookieScheme]);
        }

        string? subject = signInResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (subject is null)
            return BadRequest();

        ClaimsIdentity identity = CreateIdentity(subject);
        string? name = signInResult.Principal.FindFirstValue(ClaimTypes.Name);
        if (name is not null)
            identity.AddClaim(ClaimTypes.Name, name);
        // OpenIddict issues a refresh token only when the principal's scopes carry the
        // offline_access marker, so preserve it alongside the granted resource scopes.
        identity.SetScopes(request.GetScopes().Where(scope =>
            scope == OpenIddictConstants.Scopes.OfflineAccess
            || IdentitySeeder.GetUserScopes().Contains(scope, StringComparer.Ordinal)));
        identity.SetResources("tokenburn-api");

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("/connect/login")]
    public async Task<IActionResult> ShowLoginAsync(CancellationToken cancellationToken)
    {
        string? returnUrl = Request.Query["returnUrl"];
        if ((await HttpContext.AuthenticateAsync(LoginCookieScheme)).Succeeded)
            return RedirectAfterLogin(IsSafeReturnUrl(returnUrl) ? returnUrl! : "/");

        return Content(BuildLoginForm(returnUrl ?? "/", error: null), "text/html");
    }

    [HttpPost("/connect/login")]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("token")]
    public async Task<IActionResult> LoginAsync(string? username, string? password, string? returnUrl, CancellationToken cancellationToken)
    {
        if (username is null || password is null)
            return Content(BuildLoginForm(returnUrl ?? "/", "Username and password are required."), "text/html");

        IdentityUser? user = await users.FindUserAsync(username, cancellationToken);
        if (user is null)
        {
            IdentitySeeder.VerifyPassword(DummyUser, password);
            return Content(BuildLoginForm(returnUrl ?? "/", "Invalid username or password."), "text/html");
        }
        if (!IdentitySeeder.VerifyPassword(user, password))
            return Content(BuildLoginForm(returnUrl ?? "/", "Invalid username or password."), "text/html");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, LoginCookieScheme));
        await HttpContext.SignInAsync(LoginCookieScheme, principal);

        return RedirectAfterLogin(IsSafeReturnUrl(returnUrl) ? returnUrl! : "/");
    }

    // RedirectResult re-encodes the query (e.g. '?' -> '%3F', '%' -> '%25') when the
    // returnUrl already carries percent-encoded values, which would corrupt the authorize
    // URL the browser follows. Setting a relative Location directly keeps the URL intact
    // and lets the browser resolve it against its own origin (correct scheme either way).
    private IActionResult RedirectAfterLogin(string target)
    {
        Response.StatusCode = StatusCodes.Status302Found;
        Response.Headers.Location = target;
        return new EmptyResult();
    }

    [HttpPost("/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(LoginCookieScheme);
        return SignOut(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static ClaimsIdentity CreateIdentity(string subject)
    {
        ClaimsIdentity identity = new(
            "TokenBurn.Identity",
            ClaimsIdentity.DefaultNameClaimType,
            ClaimsIdentity.DefaultRoleClaimType);
        // OpenIddict 7 requires the mandatory 'sub' claim on the signed-in principal;
        // without it the token request fails validation. ClaimTypes.NameIdentifier alone
        // is not the 'sub' claim OpenIddict looks for.
        identity.SetClaim(OpenIddictConstants.Claims.Subject, subject);
        return identity;
    }

    private static string BuildLoginForm(string returnUrl, string? error)
    {
        string encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
        string errorHtml = error is null
            ? string.Empty
            : $"<p style=\"color:#ff5f87\">{WebUtility.HtmlEncode(error)}</p>";
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <title>TokenBurn sign in</title>
            </head>
            <body>
                <h1>TokenBurn</h1>
                {errorHtml}
                <form method="post" action="/connect/login">
                    <input type="hidden" name="returnUrl" value="{encodedReturnUrl}" />
                    <label>Username <input type="text" name="username" autocomplete="username" /></label>
                    <label>Password <input type="password" name="password" autocomplete="current-password" /></label>
                    <button type="submit">Sign in</button>
                </form>
            </body>
            </html>
            """;
    }

    private bool IsSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && (Url.IsLocalUrl(returnUrl)
            || IsSafeAbsoluteReturnUrl(returnUrl, Request.Scheme, Request.Host));

    // Url.IsLocalUrl covers the relative case the browser flow uses; this guards the
    // absolute fallback against open-redirect tricks (host-prefix lookalikes and
    // user-info confusion) by demanding an exact same-origin match.
    internal static bool IsSafeAbsoluteReturnUrl(string returnUrl, string requestScheme, HostString requestHost)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out Uri? uri))
            return false;
        if (!string.Equals(uri.Scheme, requestScheme, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (!uri.AbsolutePath.StartsWith('/'))
            return false;

        // Treat an absent request port as the scheme default, so https:443 and
        // http:80 are equivalent to their portless forms on both sides.
        int effectiveRequestPort = requestHost.Port
            ?? (string.Equals(requestScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return uri.Port == effectiveRequestPort;
    }
}
