using TokenBurn.Processor.Persistence;
using TokenBurn.Testing.Common.Bases;

namespace TokenBurn.Processor.Tests.Bases;

public abstract class TelemetryHandlerTestBase : HandlerTestBase<TelemetryDbContext>
{
    protected TelemetryHandlerTestBase() : base("telemetry", TelemetryDbMigration.RunAsync) { }
}
