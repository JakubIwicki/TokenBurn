namespace TokenBurn.Contracts;

/// <summary>
///     Topic names for the telemetry chain. The pre-existing <c>telemetry.raw</c>
///     literals in <c>OutboxPublisher.cs</c> and <c>EnvelopeInbox.cs</c> are left
///     untouched deliberately (surgical — this phase adds, it does not refactor).
/// </summary>
public static class KafkaTopics
{
    public const string Raw = "telemetry.raw";
    public const string Priced = "telemetry.priced";
    public const string Indexed = "telemetry.indexed";
}
