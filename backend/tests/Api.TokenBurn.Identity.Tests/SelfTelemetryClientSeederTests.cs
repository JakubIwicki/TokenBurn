using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Testcontainers.PostgreSql;

namespace Api.TokenBurn.Identity.Tests;

public sealed class SelfTelemetryClientSeederTests : IAsyncLifetime
{
    private const string SelfTelemetryClientId = "tokenburn-self";
    private const string SelfTelemetryClientSecret = "self-telemetry-secret";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await _database.StartAsync(timeout.Token);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Identity", _database.GetConnectionString());
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Identity:CollectorClientSecret", "collector-secret");
            builder.UseSetting("Identity:SelfTelemetryClientSecret", SelfTelemetryClientSecret);
            builder.UseSetting("Identity:DevUser:Username", "test-user");
            builder.UseSetting("Identity:DevUser:Password", "test-password");
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task SeedsSelfTelemetryClient_WhenSecretConfigured()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using IServiceScope scope = _factory.Services.CreateScope();
        IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        object? application = await applications.FindByClientIdAsync(SelfTelemetryClientId, timeout.Token);
        Assert.NotNull(application);

        IEnumerable<string> permissions = await applications.GetPermissionsAsync(application!, timeout.Token);
        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.Token, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials, permissions);
        Assert.Contains(OpenIddictConstants.Permissions.Prefixes.Scope + "telemetry.write", permissions);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Prefixes.Scope + "insights.read", permissions);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Prefixes.Scope + "admin", permissions);

        Assert.True(await applications.ValidateClientSecretAsync(application!, SelfTelemetryClientSecret, timeout.Token));
        Assert.False(await applications.ValidateClientSecretAsync(application!, "wrong-secret", timeout.Token));
    }
}
