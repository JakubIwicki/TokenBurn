using FluentValidation;

namespace Api.TokenBurn.Insights.Features.Findings;

public sealed class FindingsValidator : AbstractValidator<FindingsQuery>
{
    public FindingsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Kind).Must(BeSupportedKind)
            .When(x => x.Kind is not null)
            .WithMessage("kind must be one of 'ContextReplay', 'Loop', 'CostThreshold'.");
        RuleFor(x => x.Severity).Must(BeSupportedSeverity)
            .When(x => x.Severity is not null)
            .WithMessage("severity must be one of 'Minor', 'Major', 'Critical'.");
    }

    private static bool BeSupportedKind(string? kind)
        => kind is null || kind is "ContextReplay" or "Loop" or "CostThreshold";

    private static bool BeSupportedSeverity(string? severity)
        => severity is null || severity is "Minor" or "Major" or "Critical";
}
