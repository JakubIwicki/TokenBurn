using System.Text;
using System.Text.RegularExpressions;
using Api.TokenBurn.Insights.Features.Ask.Retrieval;
using Api.TokenBurn.Insights.Features.Search;

namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     The single allowed projection from retrieved corpus text into the LLM prompt
///     (privacy-boundary rule 7, DEFAULT-DENY). From a trace hit only
///     runId/sessionId/persona/modelSlug/status/startedAt/tokens/cost and a redacted excerpt
///     survive; from a document chunk only uri/title/ordinal and a redacted chunk excerpt.
///     The trace excerpt is the run's searchable text with its workspace/external_id values
///     removed, then scrubbed of secret-shaped strings and absolute paths, then capped at
///     <see cref="AskOptions.MaxExcerptChars" />. Redaction happens BEFORE the prompt is built.
/// </summary>
public sealed class ContextRedactor(AskOptions options)
{
    private const string RedactedMarker = "[REDACTED]";

    // Absolute-path-shaped content is structural, not configurable: a workspace or doc path
    // in an excerpt would leak filesystem layout to a third-party model.
    private static readonly Regex[] PathPatterns =
    [
        new(@"/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+", RegexOptions.Compiled),
        new(@"[A-Za-z]:\\[^\s|]+", RegexOptions.Compiled),
        new(@"~/[^\s|]+", RegexOptions.Compiled)
    ];

    private readonly IReadOnlyList<Regex> _secretPatterns = options.SecretPatterns
        .Select(pattern => new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase))
        .ToList();

    public RedactedTraceContext RedactTrace(SearchRunHit hit) => new(
        hit.Id,
        hit.SessionId,
        hit.Persona,
        hit.ModelSlug,
        hit.Status,
        hit.StartedAt,
        (hit.InputTokens ?? 0) + (hit.OutputTokens ?? 0),
        hit.CostUsd,
        RedactExcerpt(hit.SearchableText, [hit.Workspace, hit.ExternalId]));

    public RedactedDocumentContext RedactDocument(DocumentChunkHit hit) => new(
        hit.Uri,
        hit.Title,
        hit.Ordinal,
        RedactExcerpt(hit.ChunkText));

    /// <summary>
    ///     Scrub deny-listed values, secret-shaped strings and absolute paths, then cap the
    ///     result on a <see cref="Rune" /> boundary so the excerpt never splits a surrogate pair.
    /// </summary>
    public string RedactExcerpt(string? text, IReadOnlyList<string?>? deniedValues = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string scrubbed = text;
        if (deniedValues is not null)
        {
            foreach (string? denied in deniedValues)
                if (!string.IsNullOrWhiteSpace(denied))
                    scrubbed = scrubbed.Replace(denied, RedactedMarker, StringComparison.Ordinal);
        }

        scrubbed = _secretPatterns.Aggregate(scrubbed, (current, pattern) => pattern.Replace(current, RedactedMarker));
        foreach (Regex pathPattern in PathPatterns)
            scrubbed = pathPattern.Replace(scrubbed, RedactedMarker);

        return TruncateToRunes(scrubbed, options.MaxExcerptChars);
    }

    private static string TruncateToRunes(string text, int maxRunes)
    {
        if (text.Length <= maxRunes)
            return text;

        // Slice on a Rune boundary so the cut never splits a UTF-16 surrogate pair, matching
        // the Processor's RunEmbeddingTextBuilder convention.
        int charIndex = 0;
        int runeCount = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (runeCount == maxRunes)
                break;
            charIndex += rune.Utf16SequenceLength;
            runeCount++;
        }
        return text[..charIndex];
    }
}

/// <summary>
///     The redacted trace context that may enter the prompt: ONLY allow-listed fields plus a
///     redacted excerpt. No workspace/external_id/agent_id/path/full-message content.
/// </summary>
public sealed record RedactedTraceContext(
    Guid RunId,
    string SessionId,
    string? Persona,
    string? ModelSlug,
    string Status,
    DateTimeOffset? StartedAt,
    long Tokens,
    decimal? CostUsd,
    string Excerpt);

/// <summary>
///     The redacted document context that may enter the prompt: ONLY allow-listed fields plus
///     a redacted chunk excerpt.
/// </summary>
public sealed record RedactedDocumentContext(
    string Uri,
    string Title,
    int Ordinal,
    string Excerpt);
