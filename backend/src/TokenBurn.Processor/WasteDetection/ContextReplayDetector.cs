using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Flags context replay: a message that wrote a large working set to the cache
///     (cache_write &gt;= CacheCollapseMinWriteTokens) followed, within
///     CacheCollapseWindowMessages messages, by a message that re-read that set
///     (cache_read &gt;= ContextReplayMinReadTokens). One finding per qualifying (write, read) pair.
/// </summary>
public static class ContextReplayDetector
{
    public static IReadOnlyList<WasteFindingDraft> Detect(
        WasteDetectionOptions options,
        AgentRun run,
        IReadOnlyList<AgentMessage> messages,
        PriceRow? price,
        decimal multiplier)
    {
        var findings = new List<WasteFindingDraft>();
        for (int i = 0; i < messages.Count; i++)
        {
            AgentMessage write = messages[i];
            if (write.CacheWriteTokens < options.CacheCollapseMinWriteTokens)
                continue;

            int windowEnd = Math.Min(messages.Count - 1, i + options.CacheCollapseWindowMessages);
            for (int j = i + 1; j <= windowEnd; j++)
            {
                AgentMessage read = messages[j];
                if (read.CacheReadTokens < options.ContextReplayMinReadTokens)
                    continue;

                decimal? wastedCost = write.CostUsd ?? WasteCost.ForMessage(write, price, multiplier);
                object evidence = new
                {
                    kind = nameof(WasteFindingKind.ContextReplay),
                    rule = "context-replay",
                    messageSequences = new[] { write.Sequence, read.Sequence },
                    modelSlug = write.ModelSlug ?? run.ModelSlug,
                    cacheWriteTokens = write.CacheWriteTokens,
                    cacheReadTokens = read.CacheReadTokens,
                    runTotals = new
                    {
                        input = run.InputTokens,
                        cacheRead = run.CacheReadTokens,
                        cacheWrite = run.CacheWriteTokens,
                        output = run.OutputTokens
                    }
                };
                findings.Add(new WasteFindingDraft(
                    run.Id, WasteFindingKind.ContextReplay, WasteSeverity.For(wastedCost, options),
                    evidence, wastedCost, ""));
            }
        }
        return findings;
    }
}
