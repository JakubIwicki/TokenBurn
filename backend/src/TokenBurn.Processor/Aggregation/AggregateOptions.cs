using Microsoft.Extensions.Configuration;

namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Tunables for the aggregate recompute, read from the <c>Processor:Aggregate</c> config section
///     with raw <see cref="IConfiguration.GetValue{T}" /> calls (no IOptions), mirroring
///     <c>WasteDetectionOptions</c>.
/// </summary>
/// <remarks>
///     <c>MinSize</c> is the privacy-boundary minimum aggregation size (privacy-boundary rule 3); the
///     recompute enforces it via <c>HAVING COUNT(*) &gt;= @MinSize</c> at write time so the table can
///     never hold a sub-N bucket.
/// </remarks>
public sealed record AggregateOptions(bool Enabled, int MinSize)
{
    public static AggregateOptions FromConfiguration(IConfiguration configuration)
    {
        bool enabled = configuration.GetValue("Processor:Aggregate:Enabled", false);
        int minSize = configuration.GetValue("Processor:Aggregate:MinSize", 5);
        if (enabled && minSize < 1)
            throw new InvalidOperationException(
                $"Processor:Aggregate:MinSize must be at least 1, but was {minSize}. A public aggregate must derive from at least N runs.");

        return new AggregateOptions(enabled, minSize);
    }
}
