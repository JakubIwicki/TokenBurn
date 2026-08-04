using System.Security.Claims;
using Api.TokenBurn.Identity.Domain;
using Api.TokenBurn.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Api.TokenBurn.Identity.Controllers;

[ApiController]
public sealed class TokenController(IdentitySeeder users) : ControllerBase
{
    // Fixed PBKDF2-HMACSHA512 (100k iterations) dummy hash. When the requested username
    // does not exist, the password is still verified against it so the missing-user path
    // pays the same hash cost as the real check — otherwise the fast "no such user" branch
    // is a timing oracle that enumerates registered usernames.
    private const string DummyPasswordHash = "AQAAAAMAAYagAAAAEAABAgMEBQYHCAkKCwwNDg85L1hW7G6JsoVP1Bd/6o+GOoKtSt9pPLB2AcJ6wH57ug==";
    private static readonly IdentityUser DummyUser = IdentityUser.Create("dummy-user", DummyPasswordHash);

    [HttpPost("/connect/token")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ExchangeAsync(CancellationToken cancellationToken)
    {
        OpenIddictRequest? request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(HttpContext);
        if (request is null)
            return BadRequest();

        ClaimsIdentity identity;
        if (request.IsClientCredentialsGrantType())
        {
            identity = CreateIdentity(request.ClientId!);
            identity.SetScopes(request.GetScopes());
        }
        else if (request.IsPasswordGrantType())
        {
            if (request.Username is null || request.Password is null)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            IdentityUser? user = await users.FindUserAsync(request.Username, cancellationToken);
            if (user is null)
            {
                IdentitySeeder.VerifyPassword(DummyUser, request.Password);
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }
            if (!IdentitySeeder.VerifyPassword(user, request.Password))
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            identity = CreateIdentity(user.Id.ToString());
            identity.AddClaim(ClaimTypes.Name, user.Username);
            identity.SetScopes(request.GetScopes().Intersect(IdentitySeeder.GetUserScopes(), StringComparer.Ordinal));
        }
        else if (request.IsRefreshTokenGrantType())
        {
            return await SignInStoredPrincipalAsync();
        }
        else if (request.IsAuthorizationCodeGrantType())
        {
            // The principal was captured at /connect/authorize time and stored in the
            // authorization code; the token exchange re-signs it in as-is.
            return await SignInStoredPrincipalAsync();
        }
        else
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        identity.SetResources("tokenburn-api");
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // HttpContext.User is not populated for token passthrough unless OpenIddict is the
    // default authentication scheme, so the principal stored in the authorization code
    // or refresh token is retrieved by authenticating against the OpenIddict scheme.
    private async Task<IActionResult> SignInStoredPrincipalAsync()
    {
        AuthenticateResult result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        return result.Succeeded
            ? SignIn(result.Principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            : Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
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
}
