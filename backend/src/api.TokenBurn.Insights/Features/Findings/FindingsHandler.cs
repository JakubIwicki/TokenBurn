using Api.TokenBurn.Insights.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TokenBurn.Common.Pagination;

namespace Api.TokenBurn.Insights.Features.Findings;

public sealed class FindingsHandler(InsightsDbContext db) : IRequestHandler<FindingsQuery, FindingsResponse>
{
    public Task<FindingsResponse> Handle(FindingsQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<FindingsResponse> HandleAsync(FindingsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<WasteFindingReadModel> query = db.WasteFindings.AsNoTracking();

        if (request.Kind is not null)
            query = query.Where(finding => finding.Kind == request.Kind);
        if (request.Severity is not null)
            query = query.Where(finding => finding.Severity == request.Severity);
        if (request.Acknowledged == true)
            query = query.Where(finding => finding.AcknowledgedAt != null);
        if (request.Acknowledged == false)
            query = query.Where(finding => finding.AcknowledgedAt == null);

        // Keyset over (detected_at, id) DESC. detected_at is NOT NULL in the
        // schema, so the cursor never carries a null key and there is no
        // null-tail branch (unlike the runs cursor).
        if (request.Cursor is not null && CursorCodec.TryDecode(request.Cursor, out DateTimeOffset? detectedAt, out Guid id))
        {
            query = query.Where(finding =>
                finding.DetectedAt < detectedAt ||
                (finding.DetectedAt == detectedAt && finding.Id < id));
        }

        List<WasteFindingReadModel> page = await query
            .OrderByDescending(finding => finding.DetectedAt)
            .ThenByDescending(finding => finding.Id)
            .Take(request.Limit + 1)
            .ToListAsync(cancellationToken);

        var summaries = page.Take(request.Limit).Select(ToSummary).ToList();
        string? nextCursor = page.Count > request.Limit
            ? CursorCodec.Encode(page[request.Limit - 1].DetectedAt, page[request.Limit - 1].Id)
            : null;

        return new FindingsResponse { Findings = summaries, NextCursor = nextCursor };
    }

    public static FindingSummary ToSummary(WasteFindingReadModel finding) => new()
    {
        Id = finding.Id,
        RunId = finding.RunId,
        Kind = finding.Kind,
        Severity = finding.Severity,
        WastedCostUsd = finding.WastedCostUsd,
        DetectedAt = finding.DetectedAt,
        AcknowledgedAt = finding.AcknowledgedAt
    };
}
