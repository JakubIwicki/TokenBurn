using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class CrossSourceDedupeTests : TelemetryHandlerTestBase
{
    private const string LedgerSession = "004361d7-c20b-4850-b7ed-054734d759cc";
    private static readonly string LedgerPath = Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-ledger.jsonl");
    // The transcript session's ledger row ends 2026-07-24T23:58:45 + 54.2s = 23:59:39.2, which is
    // after the transcript's own last event (21:58:11.470), so the ledger completion marker wins.
    private static readonly DateTimeOffset LedgerEndedAt = new(2026, 7, 24, 23, 59, 39, 200, TimeSpan.Zero);
    private static readonly string[] TranscriptPaths =
    [
        Path.Combine(AppContext.BaseDirectory, "fixtures/transcript-004361d7-c20b-4850-b7ed-054734d759cc.jsonl"),
        Path.Combine(AppContext.BaseDirectory, "fixtures/transcript-084aea47-5ae6-48dd-a5ca-c3d8c626c46a.jsonl"),
        Path.Combine(AppContext.BaseDirectory, "fixtures/transcript-00e942db-e64f-4e9e-99d9-f89c8d96d78b.jsonl")
    ];

    [Fact]
    public async Task CollapsesTranscriptSessions_WhenLedgerIngestedFirst()
    {
        (PricingEngine engine, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        IReadOnlyList<AgentRun> ledgerRuns = MapLedgerRuns();
        IReadOnlyList<AgentRun> transcriptRuns = MapTranscriptRuns();
        ledgerRuns.Should().HaveCount(282);
        transcriptRuns.Should().HaveCount(3);
        transcriptRuns.Select(r => r.SessionId).Should().BeSubsetOf(ledgerRuns.Select(r => r.SessionId));

        await IngestAsync(ledgerRuns, engine, upserter);
        (await Context.AgentRuns.CountAsync()).Should().Be(282);

        await IngestAsync(transcriptRuns, engine, upserter);
        (await Context.AgentRuns.CountAsync()).Should().Be(282);

        await IngestAsync(ledgerRuns, engine, upserter);
        await IngestAsync(transcriptRuns, engine, upserter);
        (await Context.AgentRuns.CountAsync()).Should().Be(282);

        AgentRun run = LoadByKey(LedgerSession, "");
        run.Status.Should().Be(RunStatus.Completed);
        run.AgentId.Should().Be("");
        run.EndedAt.Should().Be(LedgerEndedAt);
    }

    [Fact]
    public async Task CollapsesOneOverlappingSession_FromLedgerAndTranscript()
    {
        (PricingEngine engine, AgentRunUpserter upserter) = await CreateSeededPipelineAsync();
        AgentRun ledgerRun = MapLedgerRuns().Single(r => r.SessionId == LedgerSession);
        AgentRun transcriptRun = MapTranscriptRuns().Single(r => r.SessionId == LedgerSession);

        await IngestAsync([ledgerRun], engine, upserter);
        await IngestAsync([transcriptRun], engine, upserter);

        (await Context.AgentRuns.CountAsync()).Should().Be(1);
        AgentRun run = LoadByKey(LedgerSession, "");
        run.Status.Should().Be(RunStatus.Completed);
        run.Source.Should().Be("delegate-ledger");
    }

    private async Task<(PricingEngine Engine, AgentRunUpserter Upserter)> CreateSeededPipelineAsync()
    {
        await new PricingSeeder(Context).SeedAsync();
        return (new PricingEngine(Context), new AgentRunUpserter(Context));
    }

    private AgentRun LoadByKey(string sessionId, string agentId)
    {
        Context.ChangeTracker.Clear();
        return Context.AgentRuns.Single(r => r.SessionId == sessionId && r.AgentId == agentId);
    }

    private static async Task IngestAsync(IReadOnlyList<AgentRun> runs, PricingEngine engine, AgentRunUpserter upserter)
    {
        foreach (AgentRun run in runs)
        {
            await engine.PriceRunAsync(run, CancellationToken.None);
            await upserter.UpsertAsync(run, CancellationToken.None);
        }
    }

    private static IReadOnlyList<AgentRun> MapLedgerRuns()
        => new DelegateLedgerAdapter(MockLogger<DelegateLedgerAdapter>.GetSuccessful().Object)
            .Map(File.ReadAllText(LedgerPath))
            .Select(AgentRunEnvelopeMapper.ToAgentRun)
            .ToList();

    private static IReadOnlyList<AgentRun> MapTranscriptRuns()
    {
        var adapter = new ClaudeCodeTranscriptAdapter(MockLogger<ClaudeCodeTranscriptAdapter>.GetSuccessful().Object);
        return TranscriptPaths
            .SelectMany(path => adapter.Map(File.ReadAllText(path)))
            .Select(AgentRunEnvelopeMapper.ToAgentRun)
            .ToList();
    }
}
