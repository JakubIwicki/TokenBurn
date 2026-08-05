using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TokenBurn.Common.Security;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Features.Imports;

public static class ImportsEndpoints
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapImportsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/imports", PostImportAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin);
        app.MapGet("/api/imports/{id:guid}", GetImportAsync)
            .RequireAuthorization(AuthorizationPolicies.Admin);
        return app;
    }

    private static async Task<IResult> PostImportAsync(
        ImportCommandRequest request,
        TelemetryDbContext db,
        IEnumerable<IImportCommandExecutor> executors,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (!executors.Any(executor => executor.CommandType == request.Source))
            return Results.BadRequest(new { error = "Unknown import source." });

        if (string.IsNullOrWhiteSpace(request.Path) || !Path.IsPathFullyQualified(request.Path))
            return Results.BadRequest(new { error = "Path must be absolute." });

        DateTimeOffset? normalizedSince = request.Since?.ToUniversalTime();
        string payload = JsonSerializer.Serialize(new { path = request.Path, since = normalizedSince }, PayloadJsonOptions);
        ImportCommand command = ImportCommand.Create(request.Source, payload, timeProvider.GetUtcNow());
        db.ImportCommands.Add(command);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            ImportCommand? existing = await FindActiveDuplicateAsync(db, request.Source, payload, ct);
            if (existing is not null)
                return Results.Conflict(new { commandId = existing.Id });

            // The conflicting row left Queued/Running between the insert attempt and this lookup,
            // so the command is no longer duplicating an active run — re-insert is a legitimate new run.
            // That re-insert can itself race a concurrent caller doing the same thing, so it is guarded
            // by the identical conflict handling below instead of an unhandled second violation.
            db.ChangeTracker.Clear();
            ImportCommand fresh = ImportCommand.Create(request.Source, payload, timeProvider.GetUtcNow());
            db.ImportCommands.Add(fresh);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException retryException) when (retryException.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                ImportCommand? winner = await FindActiveDuplicateAsync(db, request.Source, payload, ct);
                return winner is not null ? Results.Conflict(new { commandId = winner.Id }) : Results.Conflict();
            }
            return Results.Accepted($"/api/imports/{fresh.Id}", new { commandId = fresh.Id });
        }

        return Results.Accepted($"/api/imports/{command.Id}", new { commandId = command.Id });
    }

    private static Task<ImportCommand?> FindActiveDuplicateAsync(TelemetryDbContext db, string type, string payload, CancellationToken ct)
        => db.ImportCommands
            .Where(c => c.Type == type && c.Payload == payload &&
                        (c.Status == ImportCommandStatus.Queued || c.Status == ImportCommandStatus.Running))
            .FirstOrDefaultAsync(ct);

    private static async Task<IResult> GetImportAsync(Guid id, TelemetryDbContext db, CancellationToken ct)
    {
        ImportCommand? command = await db.ImportCommands.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (command is null)
            return Results.NotFound();

        JsonNode? progress = command.Payload is null ? null : JsonNode.Parse(command.Payload)?["progress"];
        return Results.Ok(new { id = command.Id, status = command.Status.ToString(), progress, error = command.LastError });
    }
}
