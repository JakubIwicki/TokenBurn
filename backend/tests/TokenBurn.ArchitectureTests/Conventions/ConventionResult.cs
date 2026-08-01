namespace TokenBurn.ArchitectureTests.Conventions;

public readonly record struct ConventionResult(
    List<string> Violations,
    int ScannedCount,
    List<string>? ScannedAssemblyNames = null,
    List<string>? LoadFailures = null);
