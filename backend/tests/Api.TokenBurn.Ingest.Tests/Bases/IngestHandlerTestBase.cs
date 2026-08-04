using Api.TokenBurn.Ingest;
using TokenBurn.Testing.Common.Bases;

namespace Api.TokenBurn.Ingest.Tests.Bases;

public abstract class IngestHandlerTestBase : HandlerTestBase<IngestDbContext>
{
    protected IngestHandlerTestBase() : base("ingest", IngestDbMigration.RunAsync) { }
}
