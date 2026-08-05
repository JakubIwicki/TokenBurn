using System.Globalization;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class ClaudeCodeTranscriptAdapterTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static IEnumerable<object[]> TranscriptFixtures =>
    [
        [Path.Combine(FixtureDir, "transcript-004361d7-c20b-4850-b7ed-054734d759cc.jsonl"), "004361d7-c20b-4850-b7ed-054734d759cc", 17_338L, 66_816L, 0L, 4_532L, "deepseek-v4-flash", "2026-07-24T21:57:51.995Z", "2026-07-24T21:58:11.470Z"],
        [Path.Combine(FixtureDir, "transcript-084aea47-5ae6-48dd-a5ca-c3d8c626c46a.jsonl"), "084aea47-5ae6-48dd-a5ca-c3d8c626c46a", 5_772L, 201_351L, 11_793L, 1_002L, "openai/gpt-5.6-luna", "2026-07-31T20:21:07.942Z", "2026-07-31T20:21:14.827Z"],
        [Path.Combine(FixtureDir, "transcript-00e942db-e64f-4e9e-99d9-f89c8d96d78b.jsonl"), "00e942db-e64f-4e9e-99d9-f89c8d96d78b", 3_053L, 160_008L, 111_511L, 394L, "openai/gpt-5.6-luna", "2026-08-02T15:28:30.127Z", "2026-08-02T15:28:37.883Z"]
    ];

    [Theory]
    [MemberData(nameof(TranscriptFixtures))]
    public void MapsTranscript_ToOneEnvelope(
        string fixturePath, string sessionId, long input, long cacheRead, long cacheWrite,
        long output, string model, string firstTimestamp, string lastTimestamp)
    {
        IReadOnlyList<NormalizedRun> envelopes = CreateSut().Map(File.ReadAllText(fixturePath));

        NormalizedRun run = envelopes.Should().ContainSingle().Which;
        run.SessionId.Should().Be(sessionId);
        run.AgentId.Should().Be("");
        run.Source.Should().Be("claude-code-transcript");
        run.ReportedCostUsd.Should().BeNull();
        run.Status.Should().Be(RunStatus.Running);
        run.ModelSlug.Should().Be(model);
        run.InputTokens.Should().Be(input);
        run.CacheReadTokens.Should().Be(cacheRead);
        run.CacheWriteTokens.Should().Be(cacheWrite);
        run.OutputTokens.Should().Be(output);
        run.StartedAt.Should().Be(ParseUtc(firstTimestamp));
        run.EndedAt.Should().Be(ParseUtc(lastTimestamp));
    }

    [Theory]
    [InlineData("result", RunStatus.Completed)]
    [InlineData("summary", RunStatus.Completed)]
    [InlineData("error", RunStatus.Failed)]
    [InlineData("abort", RunStatus.Failed)]
    public void MapsTerminalEventType_ToStatus(string terminalType, RunStatus expected)
    {
        string payload = $$"""
            {"type": "user", "timestamp": "2026-07-24T21:57:52.142Z", "sessionId": "sess-status"}
            {"type": "{{terminalType}}", "timestamp": "2026-07-24T21:58:00.000Z", "sessionId": "sess-status"}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.Status.Should().Be(expected);
    }

    [Fact]
    public void ReturnsEmpty_WhenAllEventsAreSessionless()
    {
        string payload = """
            {"type": "user", "timestamp": "2026-07-24T21:57:52.142Z"}
            {"type": "assistant", "timestamp": "2026-07-24T21:57:54.880Z", "message": {"role": "assistant", "model": "deepseek-v4-flash", "usage": {"input_tokens": 10}}}
            """;

        CreateSut().Map(payload).Should().BeEmpty();
    }

    private static ClaudeCodeTranscriptAdapter CreateSut() => new(MockLogger<ClaudeCodeTranscriptAdapter>.GetSuccessful().Object);

    private static DateTimeOffset ParseUtc(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
