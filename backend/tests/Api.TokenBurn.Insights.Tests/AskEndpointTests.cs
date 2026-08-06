using System.Net;
using System.Text;
using System.Text.Json;
using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Persistence;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TokenBurn.Contracts;
using TokenBurn.Processor.Documents.Indexing;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

[Collection("insights-search")]
public sealed class AskEndpointTests : IAsyncLifetime
{
    private const string TracesIndex = ElasticsearchFixture.TracesIndex;
    private const string DocumentsIndex = "documents";
    private const int Dims = 384;

    private ElasticsearchClient _es = null!;
    private ElasticsearchRunIndexer _indexer = null!;
    private DocumentIndexTemplateInitializer _documentTemplate = null!;
    private WebApplicationFactory<InsightsDbContext> _factory = null!;
    private HttpClient _client = null!;
    private string _cloneDatabaseName = null!;
    private string _connectionString = null!;
    private string _elasticsearchUri = null!;

    [Fact]
    public async Task ReturnsAnswer_EchoingSeededRunAndDocument()
    {
        await ClearTracesAsync(CancellationToken.None);
        await ClearDocumentsAsync(CancellationToken.None);
        Guid runId = new("01234567-89ab-cdef-0123-456789abcdef");
        await SeedTraceWithEmbeddingAsync(runId, "sess-ask", workspace: "acme-widgets", modelSlug: "claude-opus-5", cancellationToken: CancellationToken.None);
        await SeedDocumentChunkAsync("1:0", 1, "https://docs.acme.example/guide", "Acme Guide", 0, "acme document content", UnitVectorWithX(1.0), CancellationToken.None);

        using HttpResponseMessage response = await SendAskAsync(_client, new { question = "acme" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string answer = body.RootElement.GetProperty("answer").GetString()!;
        // The fake client echoes the identifiers that reached the PROMPT: the run id and the
        // document title. The document uri never enters the prompt, so it cannot be echoed.
        answer.Should().Contain(runId.ToString("D"));
        answer.Should().Contain("Acme Guide");
        answer.Should().NotContain("https://docs.acme.example/guide");

        IReadOnlyList<JsonElement> citations = body.RootElement.GetProperty("citations").EnumerateArray().ToList();
        citations.Should().NotBeEmpty();
        citations.SelectMany(citation => citation.EnumerateObject().Select(property => property.Name))
            .Should().NotContain(["externalId", "workspace", "agentId"]);
        citations.Should().Contain(citation => citation.GetProperty("kind").GetString() == "trace"
            && citation.GetProperty("runId").GetGuid() == runId);
        // The document uri IS exposed on the authed API surface (citation), just never in the prompt.
        JsonElement documentCitation = citations.Single(citation => citation.GetProperty("kind").GetString() == "document");
        documentCitation.GetProperty("uri").GetString().Should().Be("https://docs.acme.example/guide");
        documentCitation.GetProperty("title").GetString().Should().Be("Acme Guide");
        documentCitation.GetProperty("chunkIndex").GetInt32().Should().Be(0);

        IReadOnlyList<JsonElement> retrieval = body.RootElement.GetProperty("retrieval").EnumerateArray().ToList();
        retrieval.Should().Contain(hit => hit.GetProperty("kind").GetString() == "trace");
        retrieval.Should().Contain(hit => hit.GetProperty("kind").GetString() == "document");

        double coverage = body.RootElement.GetProperty("pricingCoverage").GetDouble();
        coverage.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenQuestionEmpty()
    {
        using HttpResponseMessage response = await SendAskAsync(_client, new { question = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReturnsForbidden_WhenAskInvokeScopeMissing()
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent("""{"question":"acme"}""", Encoding.UTF8, "application/json")
        };
        message.Headers.Add(InsightsTestHost.DropScopeHeader, "ask.invoke");

        using HttpResponseMessage response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenPrincipalHasNoSub()
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent("""{"question":"acme"}""", Encoding.UTF8, "application/json")
        };
        message.Headers.Add(InsightsTestHost.DropSubHeader, "true");

        using HttpResponseMessage response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReturnsTooManyRequests_WhenBudgetExhausted()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var settings = new Dictionary<string, string?> { ["Ask:Budget:MaxRequestsPerHour"] = "1" };
        await using WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(
            _connectionString, _elasticsearchUri, new FakeEmbeddingClient(UnitVectorWithX(1.0)), clock, settings);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await SendAskAsync(client, new { question = "acme" });
        using HttpResponseMessage second = await SendAskAsync(client, new { question = "acme" });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    public async Task InitializeAsync()
    {
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _elasticsearchUri = fixture.Uri.ToString();
        _es = fixture.CreateClient();
        _indexer = new ElasticsearchRunIndexer(
            _es, new SearchIndexTemplateInitializer(_es, NullLogger<SearchIndexTemplateInitializer>.Instance));
        _documentTemplate = new DocumentIndexTemplateInitializer(
            new Lazy<ElasticsearchClient>(() => _es), NullLogger<DocumentIndexTemplateInitializer>.Instance);
        await _documentTemplate.EnsureTemplateAsync(CancellationToken.None);
        await _documentTemplate.EnsureVectorMappingAsync(CancellationToken.None);

        string template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", InsightsTestHost.MigrateTelemetryAsync);
        _connectionString = await SharedPostgres.CloneAsync(template);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;

        _factory = InsightsTestHost.Create(_connectionString, _elasticsearchUri, new FakeEmbeddingClient(UnitVectorWithX(1.0)));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);
    }

    private static async Task<HttpResponseMessage> SendAskAsync(HttpClient client, object body)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(message);
    }

    private async Task SeedTraceWithEmbeddingAsync(
        Guid id,
        string sessionId,
        string? workspace = null,
        string? externalId = null,
        string? modelSlug = null,
        CancellationToken cancellationToken = default)
    {
        await _indexer.IndexAsync(BuildRun(id, sessionId, workspace, externalId, modelSlug), cancellationToken);
        UpdateResponse<RunIndexDocument> update = await _es.UpdateAsync<RunIndexDocument, RunEmbeddingPartial>(
            TracesIndex,
            id.ToString("D"),
            descriptor => descriptor.Doc(new RunEmbeddingPartial { Embedding = UnitVectorWithX(1.0), EmbeddingText = "unused by hybrid search" }),
            cancellationToken);
        update.IsValidResponse.Should().BeTrue(update.DebugInformation);
        await RefreshTracesAsync(cancellationToken);
    }

    private async Task SeedDocumentChunkAsync(
        string id,
        long documentId,
        string uri,
        string title,
        int ordinal,
        string chunkText,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        IndexResponse response = await _es.IndexAsync(new DocumentChunkDocument
        {
            Id = id,
            DocumentId = documentId,
            Uri = uri,
            Title = title,
            Source = "documents",
            ContentHash = $"hash-{id}",
            IndexedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Ordinal = ordinal,
            ChunkText = chunkText,
            Embedding = embedding
        }, DocumentsIndex, id, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        await RefreshDocumentsAsync(cancellationToken);
    }

    private static PricedRun BuildRun(
        Guid id,
        string sessionId,
        string? workspace = null,
        string? externalId = null,
        string? modelSlug = null) => new()
    {
        Id = id,
        SessionId = sessionId,
        Source = "delegate-ledger",
        Workspace = workspace,
        ExternalId = externalId,
        Persona = "engineer",
        ModelSlug = modelSlug,
        Status = RunStatus.Completed,
        PricingStatus = PricingStatus.Priced,
        StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        InputTokens = 100,
        OutputTokens = 50,
        Version = 1
    };

    private async Task ClearTracesAsync(CancellationToken cancellationToken)
    {
        var exists = await _es.Indices.ExistsAsync(TracesIndex, cancellationToken);
        if (!exists.Exists)
            return;
        DeleteByQueryResponse response = await _es.DeleteByQueryAsync(
            TracesIndex, d => d.Query(q => q.MatchAll()).Refresh(true), cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task ClearDocumentsAsync(CancellationToken cancellationToken)
    {
        var exists = await _es.Indices.ExistsAsync(DocumentsIndex, cancellationToken);
        if (!exists.Exists)
            return;
        DeleteByQueryResponse response = await _es.DeleteByQueryAsync(
            DocumentsIndex, d => d.Query(q => q.MatchAll()).Refresh(true), cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task RefreshTracesAsync(CancellationToken cancellationToken)
    {
        var response = await _es.Indices.RefreshAsync(TracesIndex, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken)
    {
        var response = await _es.Indices.RefreshAsync(DocumentsIndex, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private static float[] UnitVectorWithX(double x)
    {
        var vector = new float[Dims];
        vector[0] = (float)x;
        vector[1] = (float)Math.Sqrt(Math.Max(0, 1 - x * x));
        return vector;
    }

    private sealed class FakeEmbeddingClient(IReadOnlyList<float> vector) : IEmbeddingClient
    {
        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => Task.FromResult(vector);
    }
}
