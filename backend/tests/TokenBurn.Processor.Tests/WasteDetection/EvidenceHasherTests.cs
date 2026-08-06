using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.WasteDetection;
using TokenBurn.Testing.Common.Assertions;

namespace TokenBurn.Processor.Tests.WasteDetection;

public sealed class EvidenceHasherTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly WasteDetectionOptions Options = WasteDetectionOptions.FromConfiguration(new ConfigurationBuilder().Build());
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Compute_SameEvidence_ReturnsSameHash()
    {
        object evidence = new { kind = "Loop", rule = "loop", sequences = new[] { 1, 3 } };

        string first = EvidenceHasher.Compute(evidence);
        string second = EvidenceHasher.Compute(evidence);

        first.Should().Be(second);
    }

    [Fact]
    public void Compute_EquivalentEvidenceAcrossInstances_ReturnsSameHash()
    {
        string first = EvidenceHasher.Compute(new { kind = "Loop", rule = "loop", sequences = new[] { 1, 3 } });
        string second = EvidenceHasher.Compute(new { kind = "Loop", rule = "loop", sequences = new[] { 1, 3 } });

        first.Should().Be(second);
    }

    [Fact]
    public void Compute_CamelCasePolicy_NormalizesPropertyNameCasing()
    {
        EvidenceHasher.Compute(new { FooBar = 1 }).Should().Be(EvidenceHasher.Compute(new { fooBar = 1 }));
    }

    [Fact]
    public void Compute_DifferentValues_ProduceDifferentHashes()
    {
        EvidenceHasher.Compute(new { fooBar = 1 }).Should().NotBe(EvidenceHasher.Compute(new { fooBar = 2 }));
    }

    [Fact]
    public void Compute_NullFields_DoNotAlterHash()
    {
        EvidenceHasher.Compute(new { value = (long?)null, label = "x" }).Should()
            .Be(EvidenceHasher.Compute(new { label = "x" }));
    }

    [Fact]
    public void Compute_AllDetectorEvidence_ContainsNoContentFieldAndNoContentJson()
    {
        const string probe = "PROBE-SECRET-1f2e";
        AgentRun run = AgentRun.Create(
            "session-1", "agent-1", "test", null, null, "deepseek-v4-flash", RunStatus.Completed,
            OccurredAt, OccurredAt.AddMinutes(1), 1_000_000, 500_000, 200_000, 100_000, null);
        run.TryMarkPriced(1.50m, 1.0m).AssertSuccess();

        object[] evidences =
        [
            ContextReplayDetector.Detect(Options, run, WriteThenReadMessages(run.Id, probe), null, 1m).Single().Evidence,
            LoopDetector.Detect(Options, run, RepeatedMessages(run.Id, probe), null, 1m).Single().Evidence,
            CostThresholdDetector.Detect(Options, run, [], null, 1m).Single().Evidence
        ];

        foreach (object evidence in evidences)
        {
            AssertNoContentProperty(evidence);
            JsonSerializer.Serialize(evidence, SerializerOptions).ToLowerInvariant()
                .Should().NotContain("probe-secret-1f2e");
        }
    }

    private static AgentMessage[] WriteThenReadMessages(Guid runId, string content)
        => [
            AgentMessage.Create(runId, 1, "user", content, null, "deepseek-v4-flash", 10_000, 0, 150_000, 5_000, OccurredAt),
            AgentMessage.Create(runId, 2, "assistant", content, null, "deepseek-v4-flash", 0, 631_936, 0, 0, OccurredAt)
        ];

    private static AgentMessage[] RepeatedMessages(Guid runId, string content)
        => [
            AgentMessage.Create(runId, 1, "user", content, null, "deepseek-v4-flash", 50_000, 10_000, 5_000, 2_000, OccurredAt),
            AgentMessage.Create(runId, 2, "user", content, null, "deepseek-v4-flash", 51_000, 10_200, 5_100, 2_000, OccurredAt)
        ];

    private static void AssertNoContentProperty(object value)
    {
        foreach (PropertyInfo property in value.GetType().GetProperties())
        {
            property.Name.Contains("content", StringComparison.OrdinalIgnoreCase).Should().BeFalse(
                $"evidence must never carry a content field, found '{property.Name}'");
            object? nested = property.GetValue(value);
            if (nested is null || IsScalar(property.PropertyType) || nested is System.Collections.IEnumerable)
                continue;
            AssertNoContentProperty(nested);
        }
    }

    private static bool IsScalar(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string) || type == typeof(Guid))
            return true;
        return Nullable.GetUnderlyingType(type) is { } underlying && IsScalar(underlying);
    }
}
