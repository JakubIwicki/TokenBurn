using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Flags repeated prompt patterns: a message is a "repeat" when an earlier message within
///     LoopWindowMessages of it has the same role and model and each of the four token counters
///     within LoopTokenTolerance (relative to the larger of the two). Repeats that chain to the
///     same first occurrence form one loop group; a run may produce several groups. Messages with
///     fewer than LoopMinInputTokens input tokens are ignored so tiny accidental repeats stay quiet.
/// </summary>
public static class LoopDetector
{
    public static IReadOnlyList<WasteFindingDraft> Detect(
        WasteDetectionOptions options,
        AgentRun run,
        IReadOnlyList<AgentMessage> messages,
        PriceRow? price,
        decimal multiplier)
    {
        List<LoopGroup> groups = [];
        Dictionary<Guid, LoopGroup> groupByMessage = [];
        for (int i = 0; i < messages.Count; i++)
        {
            AgentMessage current = messages[i];
            if (current.InputTokens < options.LoopMinInputTokens)
                continue;

            for (int j = i - 1; j >= 0 && i - j <= options.LoopWindowMessages; j--)
            {
                AgentMessage earlier = messages[j];
                if (!IsRepeat(earlier, current, options))
                    continue;

                LoopGroup group = groupByMessage.TryGetValue(earlier.Id, out LoopGroup? existing)
                    ? existing
                    : CreateGroup(earlier, groups, groupByMessage);
                group.Add(current);
                groupByMessage[current.Id] = group;
                break;
            }
        }

        var findings = new List<WasteFindingDraft>(groups.Count);
        foreach (LoopGroup group in groups)
        {
            decimal? wastedCost = WasteCost.SumMessages(group.Messages, price, multiplier);
            object evidence = new
            {
                kind = nameof(WasteFindingKind.Loop),
                rule = "loop",
                sequences = group.Sequences.ToArray(),
                occurrences = group.Sequences.Count,
                tokens = new
                {
                    input = group.Fingerprint.InputTokens,
                    cacheRead = group.Fingerprint.CacheReadTokens,
                    cacheWrite = group.Fingerprint.CacheWriteTokens,
                    output = group.Fingerprint.OutputTokens
                }
            };
            findings.Add(new WasteFindingDraft(
                run.Id, WasteFindingKind.Loop, WasteSeverity.For(wastedCost, options),
                evidence, wastedCost, ""));
        }
        return findings;
    }

    private static bool IsRepeat(AgentMessage earlier, AgentMessage current, WasteDetectionOptions options)
        => earlier.Role == current.Role
            && earlier.ModelSlug == current.ModelSlug
            && WithinTolerance(earlier.InputTokens, current.InputTokens, options)
            && WithinTolerance(earlier.CacheReadTokens, current.CacheReadTokens, options)
            && WithinTolerance(earlier.CacheWriteTokens, current.CacheWriteTokens, options)
            && WithinTolerance(earlier.OutputTokens, current.OutputTokens, options);

    private static bool WithinTolerance(long first, long second, WasteDetectionOptions options)
        => Math.Abs(first - second) <= options.LoopTokenTolerance * Math.Max(first, second);

    private static LoopGroup CreateGroup(
        AgentMessage fingerprint, List<LoopGroup> groups, Dictionary<Guid, LoopGroup> groupByMessage)
    {
        LoopGroup group = new(fingerprint);
        groups.Add(group);
        groupByMessage[fingerprint.Id] = group;
        return group;
    }

    /// <summary>
    ///     One repeated prompt pattern: the first occurrence plus every later message chained to
    ///     it. <see cref="Sequences" /> stays ascending because members are appended in scan order
    ///     after the first occurrence.
    /// </summary>
    private sealed class LoopGroup
    {
        private readonly List<AgentMessage> _messages = [];
        private readonly HashSet<Guid> _memberIds = [];

        public LoopGroup(AgentMessage fingerprint)
        {
            _messages.Add(fingerprint);
            _memberIds.Add(fingerprint.Id);
            Sequences.Add(fingerprint.Sequence);
        }

        public AgentMessage Fingerprint => _messages[0];

        public List<int> Sequences { get; } = [];

        public IReadOnlyList<AgentMessage> Messages => _messages;

        public void Add(AgentMessage message)
        {
            if (!_memberIds.Add(message.Id))
                return;
            _messages.Add(message);
            Sequences.Add(message.Sequence);
        }
    }
}
