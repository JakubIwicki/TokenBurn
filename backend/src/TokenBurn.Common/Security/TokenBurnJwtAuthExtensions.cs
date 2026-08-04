using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace TokenBurn.Common.Security;

public static class TokenBurnScopes
{
    public const string TelemetryWrite = "telemetry.write";
    public const string InsightsRead = "insights.read";
    public const string AskInvoke = "ask.invoke";
    public const string Admin = "admin";

    /// <summary>
    /// The OAuth2 resource every Identity-issued access token is scoped to
    /// (<c>identity.SetResources(...)</c> in TokenController), which becomes the JWT <c>aud</c>
    /// claim that resource APIs validate against.
    /// </summary>
    public const string TokenBurnApiResource = "tokenburn-api";
}

public static class AuthorizationPolicies
{
    public const string TelemetryWrite = TokenBurnScopes.TelemetryWrite;
    public const string InsightsRead = TokenBurnScopes.InsightsRead;
    public const string AskInvoke = TokenBurnScopes.AskInvoke;
    public const string Admin = TokenBurnScopes.Admin;
}

public static class TokenBurnJwtAuthExtensions
{
    public static WebApplicationBuilder AddTokenBurnJwtAuth(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Jwt:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("Jwt:Authority must be configured for a resource API.");
        }

        var metadataAddress = builder.Configuration["Jwt:MetadataAddress"];

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                if (!string.IsNullOrWhiteSpace(metadataAddress))
                {
                    options.MetadataAddress = metadataAddress;
                }

                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = TokenBurnScopes.TokenBurnApiResource,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            AddScopePolicy(options, AuthorizationPolicies.TelemetryWrite);
            AddScopePolicy(options, AuthorizationPolicies.InsightsRead);
            AddScopePolicy(options, AuthorizationPolicies.AskInvoke);
            AddScopePolicy(options, AuthorizationPolicies.Admin);
        });

        return builder;
    }

    public static WebApplication UseTokenBurnAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    private static void AddScopePolicy(AuthorizationOptions options, string scope)
    {
        options.AddPolicy(scope, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HasScope(context.User, scope)));
    }

    private static bool HasScope(System.Security.Claims.ClaimsPrincipal principal, string requiredScope)
        => principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);
}
