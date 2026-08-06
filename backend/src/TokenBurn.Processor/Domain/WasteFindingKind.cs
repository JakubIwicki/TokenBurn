namespace TokenBurn.Processor.Domain;

/// <summary>
///     Owned by the WasteFinding aggregate (Slice C) and persisted by string name via
///     <c>HasConversion&lt;string&gt;</c> with a CHECK constraint on those names, so renumbering
///     these explicit values does NOT remap stored rows. The explicit values remain good hygiene:
///     a stable in-code ordering for switches and comparisons.
/// </summary>
public enum WasteFindingKind
{
    ContextReplay = 0,
    Loop = 1,
    CostThreshold = 2
}

public enum WasteFindingSeverity
{
    Minor = 0,
    Major = 1,
    Critical = 2
}
