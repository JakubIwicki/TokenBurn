using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class SourceDispatcherTests
{
    private static readonly string OtlpFixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures/delegate-ledger.otlp.json");
    private static readonly string LedgerPath = Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-ledger.jsonl");
    private static readonly string RunLogPath = Path.Combine(AppContext.BaseDirectory, "fixtures/delegate-run-log-sample.jsonl");
    private static readonly string TranscriptPath = Path.Combine(AppContext.BaseDirectory, "fixtures/transcript-084aea47-5ae6-48dd-a5ca-c3d8c626c46a.jsonl");
    private static readonly string JiCachingPath = Path.Combine(AppContext.BaseDirectory, "fixtures/jicaching-sample.jsonl");

    public static IEnumerable<object[]> RoutingFixtures =>
    [
        ["delegate-ledger", LedgerPath, 282],
        // The run-log sample concatenates two logs; the adapter maps one FILE
        // (first meta+result) to one envelope.
        ["delegate-run-log", RunLogPath, 1],
        ["claude-code-transcript", TranscriptPath, 1],
        ["jicaching", JiCachingPath, 2]
    ];

    [Fact]
    public void AutoDetectsOtlp_WhenSourceIsNull()
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(OtlpFixturePath));

        envelopes.Should().HaveCount(3);
        envelopes.Should().OnlyContain(e => e.Source == "delegate-ledger");
    }

    [Theory]
    [MemberData(nameof(RoutingFixtures))]
    public void RoutesBySource(string source, string fixturePath, int expected)
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(fixturePath), source);

        envelopes.Should().HaveCount(expected);
    }

    [Fact]
    public void ReturnsEmpty_ForUnknownSource()
    {
        var envelopes = CreateSut().Map("{}", "unknown-source");

        envelopes.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsEmpty_WhenSourceNullAndPayloadNotOtlp()
    {
        var envelopes = CreateSut().Map("{\"foo\": 1}");

        envelopes.Should().BeEmpty();
    }

    private static SourceDispatcher CreateSut()
        => new(
            new OtlpGenAiAdapter(MockLogger<OtlpGenAiAdapter>.GetSuccessful().Object),
            new DelegateLedgerAdapter(MockLogger<DelegateLedgerAdapter>.GetSuccessful().Object),
            new DelegateRunLogAdapter(MockLogger<DelegateRunLogAdapter>.GetSuccessful().Object),
            new ClaudeCodeTranscriptAdapter(MockLogger<ClaudeCodeTranscriptAdapter>.GetSuccessful().Object),
            new JiCachingAdapter(MockLogger<JiCachingAdapter>.GetSuccessful().Object),
            MockLogger<SourceDispatcher>.GetSuccessful().Object);
}
