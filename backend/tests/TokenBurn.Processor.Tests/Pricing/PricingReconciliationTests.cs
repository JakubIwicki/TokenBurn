using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Processor.Tests.Fixtures;
using Xunit.Abstractions;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class PricingReconciliationTests : TelemetryHandlerTestBase
{
    private readonly ITestOutputHelper _output;

    public PricingReconciliationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Reconciles_CorpusCosts_AgainstThePinnedSet()
    {
        await new PricingSeeder(Context).SeedAsync();
        IReadOnlyList<AgentRun> runs = LedgerCorpusReader.Read(CorpusPath);
        runs.Should().HaveCount(302);

        var engine = new PricingEngine(Context);
        var upserter = new AgentRunUpserter(Context);
        foreach (AgentRun run in runs)
        {
            await engine.PriceRunAsync(run, CancellationToken.None);
            await upserter.UpsertAsync(run, CancellationToken.None);
        }

        IReadOnlyList<PinnedEntry> pinned = LoadPinnedSet();
        foreach (PinnedEntry entry in pinned)
        {
            AgentRun row = Db.Query<AgentRun>().Single(r => r.SessionId == entry.SessionId && r.AgentId == entry.AgentId);
            row.PricingStatus.Should().Be(PricingStatus.Priced);
            Math.Abs(row.CostUsd!.Value - entry.ReportedCostUsd).Should().BeLessThanOrEqualTo(0.000001m);
        }

        (await Context.AgentRuns.CountAsync()).Should().Be(302);

        ReportCoverage(runs, pinned);
    }

    private static string CorpusPath => Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-ledger.jsonl");

    private IReadOnlyList<PinnedEntry> LoadPinnedSet()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/reconciliation-comparison-set.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        List<PinnedEntry> entries = [];
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            entries.Add(new PinnedEntry(
                element.GetProperty("session_id").GetString()!,
                element.GetProperty("agent_id").GetString()!,
                element.GetProperty("reported_cost_usd").GetDecimal()));
        }
        return entries;
    }

    private void ReportCoverage(IReadOnlyList<AgentRun> runs, IReadOnlyList<PinnedEntry> pinned)
    {
        long totalTokens = runs.Sum(r => (r.InputTokens ?? 0) + (r.CacheReadTokens ?? 0) + (r.OutputTokens ?? 0));
        HashSet<(string, string)> pinnedKeys = pinned.Select(p => (p.SessionId, p.AgentId)).ToHashSet();
        long pinnedTokens = runs
            .Where(r => pinnedKeys.Contains((r.SessionId, r.AgentId)))
            .Sum(r => (r.InputTokens ?? 0) + (r.CacheReadTokens ?? 0) + (r.OutputTokens ?? 0));
        double coveragePct = totalTokens == 0 ? 0 : 100.0 * pinnedTokens / totalTokens;
        _output.WriteLine(
            $"Reconciliation: resolved={runs.Count}, pinned={pinned.Count}, token_coverage={coveragePct:F1}% ({pinnedTokens}/{totalTokens})");
    }

    private sealed record PinnedEntry(string SessionId, string AgentId, decimal ReportedCostUsd);
}
