using TokenBurn.Contracts;

namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Publishes one recomputed aggregate bucket to the <c>metrics.aggregate</c> topic. The
///     interface keeps the rebuild service unit-testable without Kafka; the bucket day is passed
///     separately because it is the message key the producer derives.
/// </summary>
public interface IAggregatePublisher
{
    Task PublishAsync(PublicAggregate aggregate, DateOnly bucketDay, CancellationToken cancellationToken);
}
