using TokenBurn.ArchitectureTests.Conventions;

namespace TokenBurn.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void ProductionAssemblies_ReferenceOnlyAllowedKernelAssemblies()
    {
        // Discovery-driven guard: glob the build output the same way
        // ProductionAssemblies does, then require the convention to have
        // scanned exactly that set. A new internal src project present in the
        // build output but missing from the convention's AllowedReferences
        // matrix (and from the expected set below) makes this assertion fail
        // loudly — the matrix is never allowed to silently miss a project.
        var discoveredAssemblyNames = ProductionAssemblies.Discover()
            .Select(a => a.GetName().Name!)
            .ToList();

        discoveredAssemblyNames.Should().NotBeEmpty(
            "the discovery glob must match at least one production assembly — a zero-match glob makes the " +
            "discovery guard vacuous");

        ConventionResult result = DependencyDirectionConvention.CollectForbiddenDependencyDirections();

        result.ScannedAssemblyNames.Should().NotBeNull();
        result.ScannedAssemblyNames.Should().BeEquivalentTo(discoveredAssemblyNames,
            "the convention must scan exactly the production assemblies found in the build output — a new src " +
            "project present in the output but missing from AllowedReferences is caught here regardless of " +
            "whether the expected set below was updated");

        result.ScannedAssemblyNames.Should().BeEquivalentTo(
            ["api.TokenBurn.Identity", "api.TokenBurn.Ingest", "api.TokenBurn.Insights",
             "TokenBurn.Collector", "TokenBurn.Common", "TokenBurn.Contracts", "TokenBurn.Processor"],
            "the convention must scan exactly the 7 production assemblies — a missing assembly means discovery " +
            "dropped it, and a new src project must be added to the expected set AND its allowed-references row " +
            "in DependencyDirectionConvention (bump this assertion deliberately)");

        result.ScannedCount.Should().Be(7,
            "the convention must scan exactly 7 production assemblies; a count drop means an assembly escaped the scan");

        result.LoadFailures.Should().BeEmpty(
            "a load failure means the convention checked fewer assemblies than the real build contains; " +
            "build output may be missing a transitive dependency DLL");

        result.Violations.Should().BeEmpty(
            $"each production assembly may reference only its allow-listed kernel assemblies.\n" +
            $"Allowed: hosts → TokenBurn.Common, TokenBurn.Contracts; TokenBurn.Common / TokenBurn.Contracts → none.\n" +
            $"Scanned {result.ScannedCount} assembly(ies).\n" +
            $"Violations:\n{string.Join('\n', result.Violations)}");
    }
}
