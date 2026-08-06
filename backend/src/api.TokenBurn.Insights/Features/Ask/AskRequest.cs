namespace Api.TokenBurn.Insights.Features.Ask;

/// <summary>
///     POST body for <c>/api/ask</c>. <see cref="Question" /> is the RAG query; the optional
///     fields filter the retrieved traces (mirroring the search endpoint's filters).
/// </summary>
public sealed record AskRequest(
    string Question,
    string? Model,
    string? Persona,
    string? Source,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To);
