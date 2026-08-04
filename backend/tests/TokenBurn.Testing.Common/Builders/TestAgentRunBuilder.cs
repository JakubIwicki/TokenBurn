using TokenBurn.Processor.Domain;
using TokenBurn.Testing.Common.Data;

namespace TokenBurn.Testing.Common.Builders;

public sealed class TestAgentRunBuilder
{
    private readonly TestDb _db;
    private string _sessionId = "session-1";
    private string _agentId = "agent-1";
    private string _source = "delegate-ledger";
    private string? _modelSlug;
    private RunStatus _status = RunStatus.Running;
    private DateTimeOffset? _endedAt;
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long? _inputTokens = 100;
    private long? _cacheReadTokens = 0;
    private long? _cacheWriteTokens = 100;
    private long? _outputTokens = 50;
    private decimal? _reportedCostUsd;

    private TestAgentRunBuilder(TestDb db) { _db = db; }

    public static TestAgentRunBuilder Init(TestDb db) => new(db);

    public TestAgentRunBuilder WithSessionId(string sessionId) { _sessionId = sessionId; return this; }
    public TestAgentRunBuilder WithAgentId(string agentId) { _agentId = agentId; return this; }
    public TestAgentRunBuilder WithModelSlug(string modelSlug) { _modelSlug = modelSlug; return this; }
    public TestAgentRunBuilder Running() { _status = RunStatus.Running; _endedAt = null; return this; }
    public TestAgentRunBuilder Completed(DateTimeOffset endedAt) { _status = RunStatus.Completed; _endedAt = endedAt; return this; }
    public TestAgentRunBuilder Failed() { _status = RunStatus.Failed; _endedAt = _now; return this; }
    public TestAgentRunBuilder WithStatus(RunStatus status) { _status = status; return this; }
    public TestAgentRunBuilder WithTime(DateTimeOffset now) { _now = now; return this; }
    public TestAgentRunBuilder WithInputTokens(long? inputTokens) { _inputTokens = inputTokens; return this; }
    public TestAgentRunBuilder WithCacheReadTokens(long? cacheReadTokens) { _cacheReadTokens = cacheReadTokens; return this; }
    public TestAgentRunBuilder WithCacheWriteTokens(long? cacheWriteTokens) { _cacheWriteTokens = cacheWriteTokens; return this; }
    public TestAgentRunBuilder WithOutputTokens(long? outputTokens) { _outputTokens = outputTokens; return this; }
    public TestAgentRunBuilder WithReportedCostUsd(decimal? reportedCostUsd) { _reportedCostUsd = reportedCostUsd; return this; }

    public AgentRun Build() => BuildInternal(storeToDb: true);

    // For tests that must persist through the production path (AgentRunUpserter) rather than
    // TestDb.Store; DB writes then go through exactly the SQL production executes.
    public AgentRun BuildWithoutDatabase() => BuildInternal(storeToDb: false);

    private AgentRun BuildInternal(bool storeToDb)
    {
        var run = AgentRun.Create(
            _sessionId, _agentId, _source, null, null, _modelSlug, _status,
            _status == RunStatus.Running ? null : _now, _endedAt, _inputTokens, _cacheReadTokens,
            _cacheWriteTokens, _outputTokens, _reportedCostUsd);
        if (storeToDb)
            _db.Store(run);
        return run;
    }
}
