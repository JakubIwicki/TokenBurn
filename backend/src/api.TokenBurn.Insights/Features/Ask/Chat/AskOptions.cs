using Microsoft.Extensions.Configuration;

namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     Tunables for the <c>/api/ask</c> RAG endpoint. Read from the <c>Ask:</c> config
///     section with raw <see cref="IConfiguration" /> calls (no IOptions), mirroring the
///     embedding and hybrid-search options. <see cref="DeepSeekApiKey" /> comes from the
///     environment only in compose and is never logged. <see cref="SecretPatterns" /> are the
///     regexes the redactor applies to excerpts; when unset, a built-in secret-shaped set is
///     used.
/// </summary>
public sealed record AskOptions(
    string? Provider,
    string? DeepSeekEndpoint,
    string? DeepSeekModel,
    string? DeepSeekApiKey,
    int DeepSeekMaxOutputTokens,
    int MaxRequestsPerHour,
    int TraceTopK,
    int DocTopK,
    int MaxExcerptChars,
    IReadOnlyList<string> SecretPatterns)
{
    public static AskOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["Ask:Provider"],
        configuration["Ask:DeepSeekEndpoint"],
        configuration["Ask:DeepSeekModel"] ?? "deepseek-chat",
        configuration["Ask:DeepSeekApiKey"],
        configuration.GetValue("Ask:DeepSeekMaxOutputTokens", 1024),
        configuration.GetValue("Ask:Budget:MaxRequestsPerHour", 20),
        configuration.GetValue("Ask:Retrieval:TraceTopK", 6),
        configuration.GetValue("Ask:Retrieval:DocTopK", 4),
        configuration.GetValue("Ask:Redaction:MaxExcerptChars", 500),
        ParseSecretPatterns(configuration["Ask:Redaction:SecretPatterns"]));

    private static readonly string[] DefaultSecretPatterns =
    [
        // Provider API keys: sk- plus a long alphanumeric/hex suffix.
        @"sk-[A-Za-z0-9_-]{8,}",
        // api_key / apikey / api-key assignments.
        @"api[_-]?key\s*[:=]\s*\S+",
        // Long hex runs (tokens, hashes, signing secrets).
        @"\b[0-9a-fA-F]{32,}\b",
        // Long base64 runs (private keys, blobs).
        @"\b[A-Za-z0-9+/]{40,}={0,2}\b"
    ];

    private static IReadOnlyList<string> ParseSecretPatterns(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultSecretPatterns;
        return configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
