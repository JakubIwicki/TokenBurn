namespace Api.TokenBurn.Insights.Features.Ask;

/// <summary>
///     The kind of an ask citation / retrieval hit: a trace run or an imported document chunk.
/// </summary>
public static class AskCitationKind
{
    public const string Trace = "trace";
    public const string Document = "document";
}

/// <summary>
///     <c>/api/ask</c> response. <see cref="Answer" /> is the chat client's answer;
///     <see cref="Citations" /> are the allow-listed redacted citations the answer draws from;
///     <see cref="Retrieval" /> is the full allow-listed retrieval projection;
///     <see cref="PricingCoverage" /> is the fraction of retrieved trace hits that were priced
///     (0..1).
/// </summary>
public sealed class AskResponse
{
    public string Answer { get; init; } = null!;
    public IReadOnlyList<AskCitation> Citations { get; init; } = [];
    public IReadOnlyList<AskRetrievalHit> Retrieval { get; init; } = [];
    public double PricingCoverage { get; init; }
}

/// <summary>
///     One citation in the ask answer. A trace citation carries <see cref="RunId" /> +
///     <see cref="SessionId" />; a document citation carries <see cref="Uri" /> +
///     <see cref="Title" /> + <see cref="ChunkIndex" />. <see cref="Excerpt" /> is already
///     redacted. <see cref="Kind" /> discriminates the two shapes.
/// </summary>
public sealed class AskCitation
{
    public string Kind { get; init; } = null!;
    public Guid? RunId { get; init; }
    public string? SessionId { get; init; }
    public string? Uri { get; init; }
    public string? Title { get; init; }
    public int? ChunkIndex { get; init; }
    public string Excerpt { get; init; } = null!;
}

/// <summary>
///     One retrieved hit projected to the ask allow-list. Trace hits carry
///     runId/sessionId/persona/modelSlug/status/startedAt/tokens/cost; document hits carry
///     uri/title/ordinal. <see cref="Excerpt" /> is already redacted; <see cref="Kind" />
///     discriminates the two shapes.
/// </summary>
public sealed class AskRetrievalHit
{
    public string Kind { get; init; } = null!;
    public Guid? RunId { get; init; }
    public string? SessionId { get; init; }
    public string? Persona { get; init; }
    public string? ModelSlug { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public long? Tokens { get; init; }
    public decimal? Cost { get; init; }
    public string? Uri { get; init; }
    public string? Title { get; init; }
    public int? Ordinal { get; init; }
    public string Excerpt { get; init; } = null!;
}
