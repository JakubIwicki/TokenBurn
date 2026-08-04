using Api.TokenBurn.Identity;
using Api.TokenBurn.Identity.Controllers;
using Api.TokenBurn.Identity.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Threading.RateLimiting;
using TokenBurn.Common.Messaging;

namespace Api.TokenBurn.Identity.Extensions;

public static class ServiceHostExtensions
{
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        string connectionString = builder.Configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("ConnectionStrings:Identity must be configured.");
        string issuer = builder.Configuration["Jwt:Authority"]
            ?? throw new InvalidOperationException("Jwt:Authority must be configured.");

        builder.Services.AddHealthChecks();
        builder.Services.AddTokenBurnMediatR(typeof(Program).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddControllers(options =>
        {
            options.Conventions.Add(new TokenEndpointRateLimitConvention());
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("token", context => RateLimitPartition.GetTokenBucketLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 100,
                    TokensPerPeriod = 100,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
        builder.Services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName))
                .UseOpenIddict());

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            })
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(issuer));
                options.SetTokenEndpointUris("/connect/token");
                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetEndSessionEndpointUris("/connect/logout");
                options.SetConfigurationEndpointUris("/.well-known/openid-configuration");
                options.AllowClientCredentialsFlow()
                    .AllowPasswordFlow()
                    .AllowRefreshTokenFlow()
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();
                options.RegisterScopes(
                    "telemetry.write",
                    "insights.read",
                    "ask.invoke",
                    "admin");
                options.DisableAccessTokenEncryption();
                if (builder.Environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate();
                    options.AddDevelopmentSigningCertificate();
                }
                OpenIddictServerAspNetCoreBuilder aspNetCore = options.UseAspNetCore()
                    .EnableTokenEndpointPassthrough()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
                if (builder.Environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });

        builder.Services.AddAuthorization();
        // The login-cookie scheme backs the interactive sign-in page for the
        // authorization-code flow: the authorize action challenges it, the login
        // form signs in to it. Referenced by name, never as the default scheme.
        builder.Services.AddAuthentication()
            .AddCookie(AuthorizationController.LoginCookieScheme, options =>
            {
                options.LoginPath = "/connect/login";
                options.ReturnUrlParameter = "returnUrl";
            });
        builder.Services.AddScoped<IdentitySeeder>();
        return builder;
    }

    public static async Task<WebApplication> InitializeIdentityAsync(this WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        return app;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        return app;
    }

    /// <summary>
    /// Applies the IP-partitioned "token" rate-limit policy to the OpenIddict token endpoint
    /// without touching TokenController: credential endpoints are partitioned by remote address,
    /// not by user identity, because an unauthenticated caller has no user claim to key on.
    /// </summary>
    private sealed class TokenEndpointRateLimitConvention : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            foreach (ActionModel action in controller.Actions)
            {
                if (action.Selectors.Any(selector => selector.AttributeRouteModel?.Template == "/connect/token"))
                {
                    foreach (SelectorModel selector in action.Selectors)
                    {
                        selector.EndpointMetadata.Add(new EnableRateLimitingAttribute("token"));
                    }
                }
            }
        }
    }
}
