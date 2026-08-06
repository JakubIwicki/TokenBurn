using MediatR;

namespace Api.TokenBurn.Insights.Features.Ask;

/// <summary>
///     MediatR request for <c>/api/ask</c>. <see cref="Sub" /> is the authenticated
///     principal's subject, derived by the endpoint from the identity — the handler needs it
///     to charge the per-principal ask budget BEFORE any retrieval or LLM spend.
/// </summary>
public sealed record AskQuery(
    string Question,
    string? Model,
    string? Persona,
    string? Source,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string Sub) : IRequest<AskResponse>;
