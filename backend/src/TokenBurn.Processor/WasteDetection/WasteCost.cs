using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Wasted-cost computation shared by the detectors. A priced message reports its own cost;
///     an unpriced one is recomputed from the run's price row and peak multiplier (resolved
///     best-effort by the service) so the detector stays pure — no price row means null cost.
/// </summary>
internal static class WasteCost
{
    public static decimal? ForMessage(AgentMessage message, PriceRow? price, decimal multiplier)
        => message.CostUsd
           ?? (price is null
               ? null
               : CostCalculator.Compute(
                   message.InputTokens, message.CacheReadTokens, message.CacheWriteTokens,
                   message.OutputTokens, price, multiplier));

    public static decimal? SumMessages(IReadOnlyList<AgentMessage> messages, PriceRow? price, decimal multiplier)
    {
        decimal? total = 0m;
        foreach (AgentMessage message in messages)
        {
            decimal? cost = ForMessage(message, price, multiplier);
            if (cost is null)
                return null;
            total += cost;
        }
        return total;
    }
}
