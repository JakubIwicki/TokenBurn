using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Commands;

public sealed class ClaudeCodeTranscriptImportExecutorTests : TelemetryHandlerTestBase
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly string[] TranscriptFixtures =
    [
        Path.Combine(FixtureDir, "transcript-004361d7-c20b-4850-b7ed-054734d759cc.jsonl"),
        Path.Combine(FixtureDir, "transcript-084aea47-5ae6-48dd-a5ca-c3d8c626c46a.jsonl"),
        Path.Combine(FixtureDir, "transcript-00e942db-e64f-4e9e-99d9-f89c8d96d78b.jsonl")
    ];

    [Fact]
    public async Task ImportsTranscripts_AndDedupes()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir(TranscriptFixtures);
        try
        {
            ClaudeCodeTranscriptImportExecutor sut = CreateSut();
            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            IReadOnlyList<AgentRun> runs = await Context.AgentRuns.AsNoTracking().ToListAsync();
            runs.Should().HaveCount(3);
            runs.Should().OnlyContain(r => r.PricingStatus == PricingStatus.Priced);
            runs.Select(r => r.SessionId).Should().OnlyHaveUniqueItems();

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            (await Context.AgentRuns.CountAsync()).Should().Be(3);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task QuarantinesRuns_WithUnresolvableModelSlug()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "bogus.jsonl"), Transcript("bogus-session", "no-such-model"));

            await CreateSut().ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            (await Context.AgentRuns.SingleAsync()).PricingStatus.Should().Be(PricingStatus.Quarantined);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SkipsFiles_WrittenBeforeSince()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string oldFile = Path.Combine(tempDir, "old.jsonl");
            string newFile = Path.Combine(tempDir, "new.jsonl");
            File.WriteAllText(oldFile, Transcript("old-session", "deepseek-v4-flash"));
            File.WriteAllText(newFile, Transcript("new-session", "deepseek-v4-flash"));
            File.SetLastWriteTimeUtc(oldFile, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc));

            string payload = JsonSerializer.Serialize(new
            {
                path = tempDir,
                since = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)
            });
            await CreateSut().ExecuteAsync(
                ImportCommand.Create("claude-code-transcript", payload, Clock.GetUtcNow()),
                (_, _) => Task.CompletedTask,
                CancellationToken.None);

            (await Context.AgentRuns.CountAsync()).Should().Be(1);
            Context.AgentRuns.Should().ContainSingle(r => r.SessionId == "new-session");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsProgress_EveryTwentyFiveFiles()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            for (int i = 0; i < 30; i++)
                File.WriteAllText(Path.Combine(tempDir, $"batch-{i}.jsonl"), Transcript($"sess-{i}", "deepseek-v4-flash"));

            var progress = new List<string>();
            await CreateSut().ExecuteAsync(
                CreateCommand(tempDir),
                (json, _) => { progress.Add(json); return Task.CompletedTask; },
                CancellationToken.None);

            (await Context.AgentRuns.CountAsync()).Should().Be(30);
            progress.Should().HaveCount(2);
            progress[0].Should().Contain("\"filesProcessed\":25");
            progress[0].Should().Contain("\"runsUpserted\":25");
            string last = progress[^1];
            last.Should().Contain("\"filesProcessed\":30");
            last.Should().Contain("\"runsUpserted\":30");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FlushesFinalProgress_WhenFewerThanTwentyFiveFilesImported()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir(TranscriptFixtures);
        try
        {
            var progress = new List<string>();
            await CreateSut().ExecuteAsync(
                CreateCommand(tempDir),
                (json, _) => { progress.Add(json); return Task.CompletedTask; },
                CancellationToken.None);

            string last = progress.Should().ContainSingle().Which;
            last.Should().Contain("\"filesProcessed\":3");
            last.Should().Contain("\"runsUpserted\":3");
            last.Should().Contain("\"filesSkipped\":0");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SkipsCorruptFiles_AndContinues()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            for (int i = 0; i < 24; i++)
                File.WriteAllText(Path.Combine(tempDir, $"valid-{i}.jsonl"), Transcript($"sess-{i}", "deepseek-v4-flash"));
            string corruptFile = Path.Combine(tempDir, "bad.jsonl");
            File.WriteAllText(corruptFile, "\0" + "{}");

            var progress = new List<string>();
            await CreateSut().ExecuteAsync(
                CreateCommand(tempDir),
                (json, _) => { progress.Add(json); return Task.CompletedTask; },
                CancellationToken.None);

            (await Context.AgentRuns.CountAsync()).Should().Be(24);
            progress.Should().NotBeEmpty();
            string last = progress[^1];
            last.Should().Contain("\"filesSkipped\":1");
            last.Should().Contain("\"lastSkippedFile\":\"" + corruptFile + "\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Throws_WhenAllProcessedFilesAreSkipped()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "bad-1.jsonl"), "\0" + "{}");
            File.WriteAllText(Path.Combine(tempDir, "bad-2.jsonl"), "\0" + "{}");

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ExecuteAsync(
                CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None));

            (await Context.AgentRuns.CountAsync()).Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportsRun_WithNullCacheTokenFields()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            for (int i = 0; i < 24; i++)
                File.WriteAllText(Path.Combine(tempDir, $"valid-{i}.jsonl"), Transcript($"sess-{i}", "deepseek-v4-flash"));
            File.WriteAllText(
                Path.Combine(tempDir, "null-cache.jsonl"),
                TranscriptWithNullCacheTokens("null-cache-session", "deepseek-v4-flash"));

            var progress = new List<string>();
            await CreateSut().ExecuteAsync(
                CreateCommand(tempDir),
                (json, _) => { progress.Add(json); return Task.CompletedTask; },
                CancellationToken.None);

            (await Context.AgentRuns.CountAsync()).Should().Be(25);
            AgentRun nullCacheRun = await Context.AgentRuns.AsNoTracking().SingleAsync(r => r.SessionId == "null-cache-session");
            nullCacheRun.CacheReadTokens.Should().Be(0);
            nullCacheRun.CacheWriteTokens.Should().Be(0);
            progress.Should().NotBeEmpty();
            progress[^1].Should().Contain("\"filesSkipped\":0");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportsRun_WithNullMessageLine()
    {
        await new PricingSeeder(Context).SeedAsync();
        string tempDir = CreateTempTranscriptDir();
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "null-message.jsonl"),
                TranscriptWithNullMessageLine("null-message-session", "deepseek-v4-flash"));

            var progress = new List<string>();
            await CreateSut().ExecuteAsync(
                CreateCommand(tempDir),
                (json, _) => { progress.Add(json); return Task.CompletedTask; },
                CancellationToken.None);

            AgentRun run = await Context.AgentRuns.AsNoTracking().SingleAsync(r => r.SessionId == "null-message-session");
            run.InputTokens.Should().Be(10);
            run.OutputTokens.Should().Be(5);
            string last = progress.Should().ContainSingle().Which;
            last.Should().Contain("\"filesSkipped\":0");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string Transcript(string sessionId, string model)
        => JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = "2026-07-01T00:00:00.000Z",
            sessionId,
            message = new { role = "user", model, usage = new { input_tokens = 10, output_tokens = 5 } }
        });

    private static string TranscriptWithNullCacheTokens(string sessionId, string model)
        => JsonSerializer.Serialize(new
        {
            type = "user",
            timestamp = "2026-07-01T00:00:00.000Z",
            sessionId,
            message = new
            {
                role = "user",
                model,
                usage = new
                {
                    input_tokens = 10,
                    cache_read_input_tokens = (long?)null,
                    cache_creation_input_tokens = (long?)null,
                    output_tokens = 5
                }
            }
        });

    private static string TranscriptWithNullMessageLine(string sessionId, string model)
    {
        string nullMessageLine = JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp = "2026-07-01T00:00:00.000Z",
            sessionId,
            message = (object?)null
        });
        return nullMessageLine + "\n" + Transcript(sessionId, model);
    }

    private static ImportCommand CreateCommand(string path)
        => ImportCommand.Create("claude-code-transcript", JsonSerializer.Serialize(new { path }), Clock.GetUtcNow());

    private ClaudeCodeTranscriptImportExecutor CreateSut() => new(
        new ClaudeCodeTranscriptAdapter(MockLogger<ClaudeCodeTranscriptAdapter>.GetSuccessful().Object),
        new PricingEngine(Context),
        new AgentRunUpserter(Context),
        Clock,
        MockLogger<ClaudeCodeTranscriptImportExecutor>.GetSuccessful().Object);

    private static string CreateTempTranscriptDir(params string[] fixtures)
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        foreach (string fixture in fixtures)
            File.Copy(fixture, Path.Combine(dir, Path.GetFileName(fixture)));
        return dir;
    }
}
