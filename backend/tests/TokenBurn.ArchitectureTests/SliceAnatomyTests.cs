using TokenBurn.ArchitectureTests.Conventions;

namespace TokenBurn.ArchitectureTests;

public sealed class SliceAnatomyTests
{
    [Fact]
    public void AllFeatureSlices_HaveCompleteAnatomy()
    {
        ConventionResult result = SliceAnatomyConvention.CollectSliceAnatomyViolations();

        result.ScannedAssemblyNames.Should().NotBeNull();
        result.ScannedAssemblyNames.Should().BeEquivalentTo(
            ["api.TokenBurn.Identity", "api.TokenBurn.Ingest", "api.TokenBurn.Insights"],
            "the convention must discover every API assembly by the api.TokenBurn. prefix — " +
            "a missing assembly means discovery dropped it, and an extra assembly means a new API project was added " +
            "(bump this assertion deliberately)");

        result.LoadFailures.Should().BeEmpty(
            "a type-load failure means the convention scanned fewer types than the real assembly contains; " +
            "build output may be missing a transitive dependency DLL");

        result.Violations.Should().BeEmpty(
            $"all feature slices must have Endpoint (with Map*), a message type (*Command|*Query|*Request), " +
            $"and Handler (with HandleAsync).\n" +
            $"Scanned {result.ScannedCount} handler(s).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");

        // ScannedCount is deliberately not asserted to be > 0: no .Features.
        // namespaces exist yet — the first slices land in Phase 1. The per-slice
        // rules bind from Phase 1 when the first .Features. namespaces land, and
        // the exact-assembly guard above keeps API-project additions loud in the
        // meantime.
    }
}
