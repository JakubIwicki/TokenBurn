using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using IdentityUser = Api.TokenBurn.Identity.Domain.IdentityUser;

namespace Api.TokenBurn.Identity.Persistence;

public sealed class IdentitySeeder(
    IdentityDbContext db,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    private static readonly string[] UserScopes = ["insights.read", "ask.invoke", "admin"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedScopesAsync(cancellationToken);
        await SeedCollectorAsync(cancellationToken);
        await SeedSelfTelemetryClientAsync(cancellationToken);
        await SeedDesktopClientAsync(cancellationToken);
        await SeedUserAsync(cancellationToken);
    }

    private async Task SeedScopesAsync(CancellationToken cancellationToken)
    {
        foreach (string scope in new[] { "telemetry.write", "insights.read", "ask.invoke", "admin" })
        {
            if (await scopes.FindByNameAsync(scope, cancellationToken) is not null)
                continue;

            await scopes.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = scope,
                DisplayName = scope,
                Resources = { "tokenburn-api" }
            }, cancellationToken);
        }
    }

    private async Task SeedCollectorAsync(CancellationToken cancellationToken)
    {
        const string clientId = "tokenburn-collector";
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null)
            return;

        string secret = configuration["Identity:CollectorClientSecret"]
            ?? throw new InvalidOperationException("Identity:CollectorClientSecret must be configured.");
        await applications.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            DisplayName = "TokenBurn Collector",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "telemetry.write"
            }
        }, cancellationToken);
    }

    private async Task SeedSelfTelemetryClientAsync(CancellationToken cancellationToken)
    {
        const string clientId = "tokenburn-self";
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null)
            return;

        string? secret = configuration["Identity:SelfTelemetryClientSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Self-telemetry is default-off: a blank secret must not fail the boot — the
            // Processor emitter simply has no credential to use until one is configured.
            logger.LogInformation(
                "Identity:SelfTelemetryClientSecret is not configured; skipping the {ClientId} client seed.",
                clientId);
            return;
        }

        await applications.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            DisplayName = "TokenBurn Self-Telemetry",
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "telemetry.write"
            }
        }, cancellationToken);
    }

    private async Task SeedDesktopClientAsync(CancellationToken cancellationToken)
    {
        const string clientId = "tokenburn-desktop";
        if (await applications.FindByClientIdAsync(clientId, cancellationToken) is not null)
            return;

        // Fixed loopback redirect (port 7891) per the desktop blueprint: the OIDC client's
        // system browser listens there, and OpenIddict requires an exact redirect_uri match.
        await applications.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            DisplayName = "TokenBurn Desktop",
            // Public client: no client secret — the desktop cannot keep one secret.
            RedirectUris = { new Uri("http://127.0.0.1:7891/callback") },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.GrantTypes.Password,
                OpenIddictConstants.Permissions.Prefixes.Scope + "insights.read",
                OpenIddictConstants.Permissions.Prefixes.Scope + "ask.invoke",
                OpenIddictConstants.Permissions.Prefixes.Scope + "admin"
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        }, cancellationToken);
    }

    private async Task SeedUserAsync(CancellationToken cancellationToken)
    {
        string username = configuration["Identity:DevUser:Username"]
            ?? throw new InvalidOperationException("Identity:DevUser:Username must be configured.");
        string password = configuration["Identity:DevUser:Password"]
            ?? throw new InvalidOperationException("Identity:DevUser:Password must be configured.");
        IdentityUser? user = await db.Users.SingleOrDefaultAsync(x => x.Username == username, cancellationToken);
        if (user is null)
        {
            // The default PasswordHasher<TUser> derives the PBKDF2 digest from the password +
            // random salt and never reads the user argument; a throwaway satisfies its non-null
            // signature while the seeded user is created with the final hash.
            string passwordHash = new PasswordHasher<IdentityUser>()
                .HashPassword(IdentityUser.Create(username, "unused-hash"), password);
            db.Users.Add(IdentityUser.Create(username, passwordHash));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IdentityUser?> FindUserAsync(string username, CancellationToken cancellationToken)
        => await db.Users.SingleOrDefaultAsync(x => x.Username == username, cancellationToken);

    public static bool VerifyPassword(IdentityUser user, string password)
        => new PasswordHasher<IdentityUser>().VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    public static IReadOnlyList<string> GetUserScopes() => UserScopes;
}
