namespace TokenBurn.Contracts;

/// <summary>
///     Transport pricing-status vocabulary carried on <see cref="PricedRun" />.
///     Distinct from the domain's <c>PricingStatus</c> (which owns persistence
///     state) just as <see cref="RunStatus" /> is split — Contracts is the
///     topic-chain vocabulary.
/// </summary>
public enum PricingStatus
{
    Quarantined = 0,
    Priced = 1,
    Unpriceable = 2
}
