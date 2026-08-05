using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TokenBurn.Common.Security;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Data;

namespace TokenBurn.Processor.Tests.Features.Imports;

public sealed class ImportsEndpointsTests : IAsyncLifetime
{
    private const string NoAuthHeader = "X-Test-No-Auth";

    private WebApplicationFactory<TelemetryDbContext> _factory = null!;
    private HttpClient _client = null!;
    private string _cloneDatabaseName = null!;

    public async Task InitializeAsync()
    {
        string template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", TelemetryDbMigration.RunAsync);
        string connectionString = await SharedPostgres.CloneAsync(template);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(connectionString).Database!;

        _factory = new WebApplicationFactory<TelemetryDbContext>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Processor", connectionString);
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
                    new ConfigureNamedOptions<AuthenticationOptions>(Options.DefaultName, options =>
                    {
                        options.DefaultScheme = "Test";
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }));
            });
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);
    }

    [Fact]
    public async Task Post_ValidCommand_Returns202_AndPersists()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Guid commandId = await ReadCommandIdAsync(response);
        Assert.Equal($"/api/imports/{commandId}", response.Headers.Location?.ToString());
        ImportCommand row = await LoadSingleAsync();
        Assert.Equal("claude-code-transcript", row.Type);
        Assert.Equal(ImportCommandStatus.Queued, row.Status);
        row.Payload.Should().Contain("/tmp/transcripts");
    }

    [Fact]
    public async Task Post_UnknownSource_Returns400()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/imports",
            new { source = "bogus", path = "/tmp/x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoCommandsAsync();
    }

    [Fact]
    public async Task Post_RelativePath_Returns400()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "relative/dir" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoCommandsAsync();
    }

    [Fact]
    public async Task Post_IdenticalCommandTwice_SecondReturns409()
    {
        using HttpResponseMessage first = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts" });
        using HttpResponseMessage second = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Guid firstId = await ReadCommandIdAsync(first);
        Guid secondId = await ReadCommandIdAsync(second);
        Assert.Equal(firstId, secondId);
        ImportCommand row = await LoadSingleAsync();
        Assert.Equal(firstId, row.Id);
    }

    [Fact]
    public async Task Post_SameInstant_DifferentOffsetSpelling_SecondReturns409()
    {
        DateTimeOffset utcMidnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sameInstantAtPlusTwo = new(2026, 1, 1, 2, 0, 0, TimeSpan.FromHours(2));

        using HttpResponseMessage first = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts", since = utcMidnight });
        using HttpResponseMessage second = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts", since = sameInstantAtPlusTwo });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Guid firstId = await ReadCommandIdAsync(first);
        Guid secondId = await ReadCommandIdAsync(second);
        Assert.Equal(firstId, secondId);
        ImportCommand row = await LoadSingleAsync();
        Assert.Equal(firstId, row.Id);
    }

    [Fact]
    public async Task Post_ConcurrentDuplicates_WhileWinningRowCompletes_NeverReturns500()
    {
        using HttpResponseMessage seedResponse = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/race-transcripts" });
        Guid seededId = await ReadCommandIdAsync(seedResponse);

        Task<HttpResponseMessage>[] postTasks = Enumerable.Range(0, 8)
            .Select(_ => _client.PostAsJsonAsync("/api/imports",
                new { source = "claude-code-transcript", path = "/tmp/race-transcripts" }))
            .ToArray();
        Task completeSeededRowTask = CompleteCommandAsync(seededId);

        HttpResponseMessage[] responses = await Task.WhenAll(postTasks);
        await completeSeededRowTask;

        responses.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.Accepted || r.StatusCode == HttpStatusCode.Conflict);
        foreach (HttpResponseMessage response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task Post_Anonymous_Returns401()
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/api/imports")
        {
            Content = JsonContent.Create(new { source = "claude-code-transcript", path = "/tmp/transcripts" })
        };
        message.Headers.Add(NoAuthHeader, "true");

        using HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoCommandsAsync();
    }

    [Fact]
    public async Task Get_Anonymous_Returns401()
    {
        using HttpRequestMessage message = new(HttpMethod.Get, $"/api/imports/{Guid.NewGuid()}");
        message.Headers.Add(NoAuthHeader, "true");

        using HttpResponseMessage response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ExistingCommand_Returns200_WithStatusAndProgress()
    {
        using HttpResponseMessage post = await _client.PostAsJsonAsync("/api/imports",
            new { source = "claude-code-transcript", path = "/tmp/transcripts" });
        Guid commandId = await ReadCommandIdAsync(post);

        using HttpResponseMessage response = await _client.GetAsync($"/api/imports/{commandId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(commandId, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Queued", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("progress").ValueKind);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using HttpResponseMessage response = await _client.GetAsync($"/api/imports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<ImportCommand> LoadSingleAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        TelemetryDbContext db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        return await db.ImportCommands.SingleAsync();
    }

    private async Task CompleteCommandAsync(Guid id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        TelemetryDbContext db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE telemetry.import_commands SET status='Completed' WHERE id={id}");
    }

    private async Task AssertNoCommandsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        TelemetryDbContext db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();
        Assert.Empty(await db.ImportCommands.ToListAsync());
    }

    private static async Task<Guid> ReadCommandIdAsync(HttpResponseMessage response)
    {
        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("commandId").GetGuid();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey(NoAuthHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            ClaimsIdentity identity = new(
                [new Claim("scope", TokenBurnScopes.Admin), new Claim("sub", "test-admin")],
                Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
