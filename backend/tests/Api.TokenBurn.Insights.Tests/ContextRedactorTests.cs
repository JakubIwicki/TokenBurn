using Api.TokenBurn.Insights.Features.Ask.Chat;
using Api.TokenBurn.Insights.Features.Ask.Retrieval;
using Api.TokenBurn.Insights.Features.Search;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Api.TokenBurn.Insights.Tests;

public sealed class ContextRedactorTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly ContextRedactor Redactor =
        new(AskOptions.FromConfiguration(new ConfigurationBuilder().Build()));

    [Fact]
    public void KeepsOnlyAllowListedFields_ForTraceHits()
    {
        Guid runId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var hit = new SearchRunHit
        {
            Id = runId,
            SessionId = "sess-x",
            Source = "delegate-ledger",
            ExternalId = "ext-secret",
            Workspace = "/home/jakub/private-repo",
            Persona = "engineer",
            ModelSlug = "claude-opus-5",
            Status = "Completed",
            PricingStatus = "Priced",
            StartedAt = StartedAt,
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0.01m,
            SearchableText = "/home/jakub/private-repo engineer ext-secret sess-x"
        };

        RedactedTraceContext redacted = Redactor.RedactTrace(hit);

        redacted.RunId.Should().Be(runId);
        redacted.SessionId.Should().Be("sess-x");
        redacted.Persona.Should().Be("engineer");
        redacted.ModelSlug.Should().Be("claude-opus-5");
        redacted.Status.Should().Be("Completed");
        redacted.StartedAt.Should().Be(StartedAt);
        redacted.Tokens.Should().Be(150);
        redacted.CostUsd.Should().Be(0.01m);
        // The excerpt is the run's searchable text with the deny-listed VALUES (workspace,
        // external_id) removed and absolute paths scrubbed — never a workspace path.
        redacted.Excerpt.Should().NotContain("/home/jakub/private-repo");
        redacted.Excerpt.Should().NotContain("ext-secret");
        redacted.Excerpt.Should().Contain("sess-x");
    }

    [Fact]
    public void KeepsOnlyAllowListedFields_ForDocumentHits()
    {
        var hit = new DocumentChunkHit
        {
            Id = "1:0",
            DocumentId = 1,
            Uri = "https://docs.acme.example/guide",
            Title = "Acme Guide",
            Ordinal = 0,
            ChunkText = "acme document content"
        };

        RedactedDocumentContext redacted = Redactor.RedactDocument(hit);

        redacted.Uri.Should().Be("https://docs.acme.example/guide");
        redacted.Title.Should().Be("Acme Guide");
        redacted.Ordinal.Should().Be(0);
        redacted.Excerpt.Should().Be("acme document content");
    }

    [Fact]
    public void ScrubSecrets_FromExcerpts()
    {
        string excerpt = Redactor.RedactExcerpt("acme sk-testprobeab12");

        excerpt.Should().NotContain("sk-testprobeab12");
        excerpt.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void ScrubAbsolutePaths_FromExcerpts()
    {
        string excerpt = Redactor.RedactExcerpt("acme /home/jakub/private-repo");

        excerpt.Should().NotContain("/home/jakub/private-repo");
        excerpt.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void TruncatesExcerpts_ToMaxExcerptChars()
    {
        var configuration = new ConfigurationManager { ["Ask:Redaction:MaxExcerptChars"] = "10" };
        ContextRedactor shortRedactor = new(AskOptions.FromConfiguration(configuration));

        string excerpt = shortRedactor.RedactExcerpt("abcdefghijklmnopqrstuvwxyz");

        excerpt.Length.Should().BeLessThanOrEqualTo(10);
    }
}
