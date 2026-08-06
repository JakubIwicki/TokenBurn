using FluentValidation;

namespace Api.TokenBurn.Insights.Features.Ask;

public sealed class AskValidator : AbstractValidator<AskQuery>
{
    public AskValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null);
    }
}
