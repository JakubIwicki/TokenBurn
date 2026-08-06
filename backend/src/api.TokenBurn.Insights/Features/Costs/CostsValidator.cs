using FluentValidation;

namespace Api.TokenBurn.Insights.Features.Costs;

public sealed class CostsValidator : AbstractValidator<CostsQuery>
{
    public CostsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.GroupBy).Must(g => g is null || g is "day" or "model" or "persona")
            .WithMessage("GroupBy must be one of 'day', 'model', 'persona'.");
    }
}
