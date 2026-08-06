namespace Api.TokenBurn.Insights.Features.Ask;

/// <summary>
///     Thrown when a principal exceeds the per-hour ask budget. The endpoint maps this to
///     HTTP 429; the message carries no prompt or context text.
/// </summary>
public sealed class AskBudgetExceededException : Exception
{
    public AskBudgetExceededException()
        : base("The per-principal ask budget is exhausted; retry later.")
    {
    }
}
