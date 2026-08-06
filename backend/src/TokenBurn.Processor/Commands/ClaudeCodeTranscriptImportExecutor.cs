using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using PricingStatus = TokenBurn.Processor.Domain.PricingStatus;

namespace TokenBurn.Processor.Commands;

/// <summary>
///     Imports a directory tree of Claude Code transcripts in-process (NOT via Kafka):
///     each *.jsonl file is adapted to a normalized run, priced against the versioned
///     registry, and upserted. Progress is reported to the worker every 25 files; the
///     worker merges it into the command payload jsonb and refreshes the handling lease.
/// </summary>
public sealed class ClaudeCodeTranscriptImportExecutor(
    ClaudeCodeTranscriptAdapter adapter,
    PricingEngine pricingEngine,
    AgentRunUpserter upserter,
    AgentMessageUpserter messageUpserter,
    TimeProvider timeProvider,
    ILogger<ClaudeCodeTranscriptImportExecutor> logger) : IImportCommandExecutor
{
    public string CommandType => "claude-code-transcript";

    public async Task ExecuteAsync(
        ImportCommand command,
        Func<string, CancellationToken, Task> updateProgress,
        CancellationToken ct)
    {
        TranscriptPayload payload = TranscriptPayload.Parse(command.Payload);
        if (!Path.IsPathFullyQualified(payload.Path) || !Directory.Exists(payload.Path))
            throw new InvalidOperationException(
                $"Transcript import path '{payload.Path}' must be a fully qualified existing directory.");

        DateTimeOffset now = timeProvider.GetUtcNow();
        int filesProcessed = 0;
        int runsUpserted = 0;
        int filesSkipped = 0;
        string? lastSkippedFile = null;

        foreach (string file in Directory.EnumerateFiles(payload.Path, "*.jsonl", SearchOption.AllDirectories))
        {
            DateTimeOffset writtenAt = new(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            // A file cannot legitimately carry an mtime in the future (clock skew, a write in
            // flight); skip it so a half-written transcript is not picked up mid-write. The
            // optional `since` filter below drives incremental imports.
            if (writtenAt > now)
                continue;
            if (payload.Since is { } since && writtenAt < since)
                continue;

            filesProcessed++;
            try
            {
                foreach (NormalizedRun envelope in adapter.Map(File.ReadAllText(file)))
                {
                    AgentRun run = AgentRunEnvelopeMapper.ToAgentRun(envelope);
                    await pricingEngine.PriceRunAsync(run, ct);
                    (Guid storedId, bool applied) = await upserter.UpsertAsync(run, ct);
                    // Applied is false when the ended_at guard rejected the replay: the stored
                    // run kept its original pricing, so re-pricing messages would break
                    // SUM(messages.cost) = run.cost. Keep the stored run's messages untouched.
                    if (applied && run.PricingStatus == PricingStatus.Priced && envelope.Messages.Count > 0)
                    {
                        AgentMessage[] messages = envelope.Messages
                            .Select(message => AgentMessageEnvelopeMapper.ToAgentMessage(storedId, message))
                            .ToArray();
                        var messagePricing = await pricingEngine.PriceMessagesAsync(run, messages, ct);
                        if (!messagePricing.IsSuccess)
                            logger.LogWarning("Pricing messages for run {RunId} failed: {Error}", storedId, messagePricing.ErrorMessage);
                        else
                            await messageUpserter.UpsertAsync(storedId, messages, ct);
                    }
                    runsUpserted++;
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                filesSkipped++;
                lastSkippedFile = file;
            }

            if (filesProcessed % 25 == 0)
                await updateProgress(SerializeProgress(filesProcessed, runsUpserted, filesSkipped, lastSkippedFile), ct);
        }

        // Always flush the final counters: without this, an import whose file count isn't a
        // multiple of 25 never reports progress for its last (partial) batch, and a Completed
        // command whose total was under 25 files persists no progress at all.
        await updateProgress(SerializeProgress(filesProcessed, runsUpserted, filesSkipped, lastSkippedFile), ct);

        if (filesProcessed > 0 && filesSkipped == filesProcessed)
        {
            logger.LogError(
                "Transcript import failed: all {FilesProcessed} processed files were skipped (last skipped: {LastSkippedFile}).",
                filesProcessed, lastSkippedFile);
            throw new InvalidOperationException(
                $"Transcript import failed: all {filesProcessed} processed files were skipped (last skipped: {lastSkippedFile}).");
        }
    }

    private static string SerializeProgress(int filesProcessed, int runsUpserted, int filesSkipped, string? lastSkippedFile)
        => JsonSerializer.Serialize(new { progress = new { filesProcessed, runsUpserted, filesSkipped, lastSkippedFile } });

    private sealed record TranscriptPayload(string Path, DateTimeOffset? Since)
    {
        public static TranscriptPayload Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Import command payload must contain a JSON object with a 'path'.");

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("path", out JsonElement path) || string.IsNullOrWhiteSpace(path.GetString()))
                throw new InvalidOperationException("Import command payload is missing a non-empty 'path'.");

            DateTimeOffset? since = null;
            if (root.TryGetProperty("since", out JsonElement sinceElement) &&
                DateTimeOffset.TryParse(sinceElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                since = parsed;
            }

            return new TranscriptPayload(path.GetString()!, since);
        }
    }
}
