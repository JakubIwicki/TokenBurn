using FluentValidation;

namespace Api.TokenBurn.Insights.Features.Runs;

public sealed class RunsValidator : AbstractValidator<RunsQuery>
{
    public RunsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null);
    }
}

public sealed class RunDetailValidator : AbstractValidator<RunDetailQuery>
{
}
