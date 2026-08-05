using System.Text;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Adapters;

public sealed class DelegateRunLogAdapterTests
{
    private static readonly string RunLogPath = Path.Combine(AppContext.BaseDirectory, "fixtures/delegate-run-log-sample.jsonl");

    [Fact]
    public void MapsTwoLogs_ToCompletedEnvelopes_WithExactUsage()
    {
        // The adapter maps one run-log FILE to one envelope; the sample fixture
        // concatenates two logs, so split it at each meta line before mapping.
        string[] perLog = SplitLogs(File.ReadAllText(RunLogPath)).ToArray();
        IReadOnlyList<NormalizedRun> envelopes = perLog.SelectMany(log => CreateSut().Map(log)).ToList();

        envelopes.Should().HaveCount(2);
        envelopes.Should().OnlyContain(e => e.AgentId == "");
        envelopes.Should().OnlyContain(e => e.Source == "delegate-run-log");
        envelopes.Should().OnlyContain(e => e.Status == RunStatus.Completed);

        NormalizedRun first = envelopes.Single(e => e.SessionId == "c7ca8087-8c9d-4099-adde-fa46684b4ab7");
        first.ExternalId.Should().Be("20260803-211558-226166-2a5e52");
        first.Persona.Should().Be("csharp-coder");
        first.StartedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_785_784_558_806));
        first.EndedAt.Should().BeNull();
        first.InputTokens.Should().Be(282_339);
        first.CacheReadTokens.Should().Be(4_344_064);
        first.CacheWriteTokens.Should().Be(0);
        first.OutputTokens.Should().Be(56_905);
        first.ReportedCostUsd.Should().BeNull();

        NormalizedRun second = envelopes.Single(e => e.SessionId == "8f3a54b0-f9c9-4671-a2af-0a355d635abb");
        second.ExternalId.Should().Be("20260803-211602-226166-02f794");
        second.Persona.Should().Be("react-native-coder");
        second.StartedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1_785_784_562_813));
        second.InputTokens.Should().Be(73_183);
        second.CacheReadTokens.Should().Be(1_334_528);
        second.CacheWriteTokens.Should().Be(0);
        second.OutputTokens.Should().Be(12_364);
    }

    private static IEnumerable<string> SplitLogs(string multiLogJsonl)
    {
        StringBuilder? current = null;
        foreach (string line in multiLogJsonl.Split('\n'))
        {
            if (line.Contains("\"type\": \"meta\"", StringComparison.Ordinal))
            {
                if (current is not null)
                    yield return current.ToString();
                current = new StringBuilder();
            }
            if (current is not null)
                current.AppendLine(line);
        }
        if (current is not null)
            yield return current.ToString();
    }

    [Fact]
    public void ReturnsFailed_WhenResultIsError()
    {
        string payload = """
            {"type": "meta", "started": 1785789000.0, "handle": "20260803-000000-000000-abc123", "persona": "python-coder"}
            {"type": "result", "is_error": true, "stop_reason": "error", "session_id": "sess-fail-1", "usage": {"input_tokens": 10, "cache_read_input_tokens": 20, "cache_creation_input_tokens": 0, "output_tokens": 30}}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public void ReturnsRunning_WhenStopReasonIsBlank()
    {
        string payload = """
            {"type": "meta", "started": 1785789000.0, "handle": "20260803-000000-000000-def456", "persona": "python-coder"}
            {"type": "result", "is_error": false, "stop_reason": "", "session_id": "sess-run-1", "usage": {"input_tokens": 10}}
            """;

        NormalizedRun run = CreateSut().Map(payload).Should().ContainSingle().Which;

        run.Status.Should().Be(RunStatus.Running);
    }

    [Fact]
    public void ReturnsEmpty_WhenMetaOrResultMissing()
    {
        string payload = """
            {"type": "user", "session_id": "sess-x"}
            """;

        CreateSut().Map(payload).Should().BeEmpty();
    }

    [Fact]
    public void ReturnsEmpty_WhenSessionIdIsBlank()
    {
        string payload = """
            {"type": "meta", "started": 1785789000.0, "handle": "h1", "persona": "p"}
            {"type": "result", "is_error": false, "stop_reason": "end_turn", "session_id": "", "usage": {"input_tokens": 1}}
            """;

        CreateSut().Map(payload).Should().BeEmpty();
    }

    private static DelegateRunLogAdapter CreateSut() => new(MockLogger<DelegateRunLogAdapter>.GetSuccessful().Object);
}
