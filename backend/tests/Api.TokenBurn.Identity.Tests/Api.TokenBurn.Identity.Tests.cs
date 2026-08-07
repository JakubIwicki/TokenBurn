using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Testcontainers.PostgreSql;

namespace Api.TokenBurn.Identity.Tests;

public sealed class ApiTokenBurnIdentityTests : IAsyncLifetime
{
    private const string CollectorClientId = "tokenburn-collector";
    private const string CollectorClientSecret = "collector-secret";
    private const string SelfTelemetryClientId = "tokenburn-self";
    private const string RequestedScope = "telemetry.write";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder().Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await _database.StartAsync(timeout.Token);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The host gates the OpenIddict development signing certificate and the HTTPS
            // transport-security relaxation behind IsDevelopment(); the test host must opt in so
            // token issuance over HTTP keeps working under the test scheme.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Identity", _database.GetConnectionString());
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Identity:CollectorClientSecret", CollectorClientSecret);
            builder.UseSetting("Identity:DevUser:Username", "test-user");
            builder.UseSetting("Identity:DevUser:Password", "test-password");
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
                    new ConfigureNamedOptions<AuthenticationOptions>(Options.DefaultName, options =>
                    {
                        options.DefaultScheme = "Test";
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                        options.DefaultSignInScheme = "Test";
                        options.DefaultSignOutScheme = "Test";
                    }));
            });
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task AdvertisesConfiguredOpenIddictMetadata()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await _client.GetAsync("/.well-known/openid-configuration", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        JsonElement metadata = document.RootElement;
        Assert.Equal("http://localhost/connect/token", metadata.GetProperty("token_endpoint").GetString());
        string[] scopes = metadata.GetProperty("scopes_supported").EnumerateArray()
            .Select(scope => scope.GetString()!)
            .ToArray();
        Assert.Contains("telemetry.write", scopes);
        Assert.Contains("insights.read", scopes);
        Assert.Contains("ask.invoke", scopes);
        Assert.Contains("admin", scopes);
    }

    [Fact]
    public async Task IssuesCollectorTokenForCorrectSecret()
    {
        using HttpResponseMessage response = await RequestTokenAsync(CollectorClientSecret);
        string responseBody = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseBody);
        using JsonDocument document = JsonDocument.Parse(responseBody);
        string token = document.RootElement.GetProperty("access_token").GetString()!;
        JsonElement tokenPayload = ReadJwtPayload(token);
        Assert.Contains(RequestedScope, tokenPayload.GetProperty("scope").GetString()!.Split(' '));
        Assert.Equal("tokenburn-api", ReadAudience(tokenPayload));
    }

    [Fact]
    public async Task RejectsCollectorTokenForWrongSecret()
    {
        using HttpResponseMessage response = await RequestTokenAsync("wrong-secret");
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });
        Assert.Equal("invalid_client", document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task DoesNotSeedSelfTelemetryClient_WhenSecretNotConfigured()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using IServiceScope scope = _factory.Services.CreateScope();
        IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        object? application = await applications.FindByClientIdAsync(SelfTelemetryClientId, timeout.Token);

        Assert.Null(application);
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string secret)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using FormUrlEncodedContent content = new(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", CollectorClientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", RequestedScope)
        ]);
        return await _client.PostAsync("/connect/token", content, timeout.Token);
    }

    private static string ReadAudience(JsonElement tokenPayload)
    {
        JsonElement audience = tokenPayload.GetProperty("aud");
        return audience.ValueKind == JsonValueKind.String
            ? audience.GetString()!
            : string.Join(",", audience.EnumerateArray().Select(value => value.GetString()));
    }

    private static JsonElement ReadJwtPayload(string token)
    {
        string[] segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        string payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.Clone();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([], Scheme.Name)), Scheme.Name)));
    }
}
