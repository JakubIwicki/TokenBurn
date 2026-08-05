using Api.TokenBurn.Insights.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TokenBurn.Common.Pagination;

namespace Api.TokenBurn.Insights.Features.Runs;

public sealed class RunsHandler(InsightsDbContext db) : IRequestHandler<RunsQuery, RunsResponse>
{
    public Task<RunsResponse> Handle(RunsQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<RunsResponse> HandleAsync(RunsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<AgentRunReadModel> query = db.AgentRuns.AsNoTracking();

        if (request.From is not null)
            query = query.Where(run => run.StartedAt >= request.From);
        if (request.To is not null)
            query = query.Where(run => run.StartedAt <= request.To);
        if (request.Model is not null)
            query = query.Where(run => run.ModelSlug == request.Model);
        if (request.Persona is not null)
            query = query.Where(run => run.Persona == request.Persona);
        if (request.MinCost is not null)
            query = query.Where(run => run.CostUsd >= request.MinCost);

        // Keyset over (started_at, id) DESC with NULL started_at pinned last,
        // so the null block pages by id and terminates into the non-null set
        // instead of dead-ending (Postgres orders NULLS FIRST by default).
        if (request.Cursor is not null && CursorCodec.TryDecode(request.Cursor, out DateTimeOffset? startedAt, out Guid id))
        {
            if (startedAt is null)
                query = query.Where(run => run.StartedAt == null && run.Id < id);
            else
                query = query.Where(run =>
                    run.StartedAt == null ||
                    run.StartedAt < startedAt ||
                    (run.StartedAt == startedAt && run.Id < id));
        }

        List<AgentRunReadModel> page = await query
            .OrderByDescending(run => run.StartedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(run => run.Id)
            .Take(request.Limit + 1)
            .ToListAsync(cancellationToken);

        var summaries = page.Take(request.Limit).Select(ToSummary).ToList();
        string? nextCursor = page.Count > request.Limit
            ? CursorCodec.Encode(page[request.Limit - 1].StartedAt, page[request.Limit - 1].Id)
            : null;

        return new RunsResponse { Runs = summaries, NextCursor = nextCursor };
    }

    public static RunSummary ToSummary(AgentRunReadModel run) => new()
    {
        Id = run.Id,
        SessionId = run.SessionId,
        Source = run.Source,
        ExternalId = run.ExternalId,
        Workspace = run.Workspace,
        Persona = run.Persona,
        ModelSlug = run.ModelSlug,
        Status = run.Status,
        PricingStatus = run.PricingStatus,
        StartedAt = run.StartedAt,
        EndedAt = run.EndedAt,
        InputTokens = run.InputTokens,
        OutputTokens = run.OutputTokens,
        CostUsd = run.CostUsd,
        ReportedCostUsd = run.ReportedCostUsd
    };
}

public sealed class RunDetailHandler(InsightsDbContext db) : IRequestHandler<RunDetailQuery, RunDetailResponse?>
{
    public Task<RunDetailResponse?> Handle(RunDetailQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<RunDetailResponse?> HandleAsync(RunDetailQuery request, CancellationToken cancellationToken)
    {
        AgentRunReadModel? run = await db.AgentRuns.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (run is null)
            return null;

        return new RunDetailResponse { Run = RunsHandler.ToSummary(run) };
    }
}
