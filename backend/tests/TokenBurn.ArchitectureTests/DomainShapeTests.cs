using TokenBurn.ArchitectureTests.Conventions;

namespace TokenBurn.ArchitectureTests;

public sealed class DomainShapeTests
{
    [Fact]
    public void AllDomainEntities_HaveAggregateShape()
    {
        ConventionResult result = DomainShapeConvention.CollectDomainShapeViolations();

        result.ScannedAssemblyNames.Should().Contain(
            [
                "TokenBurn.Collector",
                "TokenBurn.Common",
                "TokenBurn.Contracts",
                "TokenBurn.Processor",
                "api.TokenBurn.Identity",
                "api.TokenBurn.Ingest",
                "api.TokenBurn.Insights"
            ],
            "the convention must discover every production assembly by the TokenBurn./api.TokenBurn. prefixes — " +
            "a missing assembly means discovery dropped it");

        result.LoadFailures.Should().BeEmpty(
            "a type-load failure means the convention scanned fewer types than the real assembly contains; " +
            "build output may be missing a transitive dependency DLL");

        result.ScannedCount.Should().BeGreaterThan(0,
            "vacuity guard — the convention must find at least one domain aggregate; " +
            "a zero scan means the domain-entity discovery is broken");

        result.ScannedCount.Should().Be(7,
            "the domain aggregates are AgentMessage, AgentRun, WasteFinding, Envelope, OutboxMessage, IdentityUser, and ImportCommand; " +
            "bump this deliberately when an aggregate is added");

        result.Violations.Should().BeEmpty(
            $"every domain aggregate must be sealed, expose a private parameterless constructor, " +
            $"and a public static factory returning the type.\n" +
            $"Scanned {result.ScannedCount} aggregate(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");
    }
}
