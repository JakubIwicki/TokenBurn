using FluentValidation;

namespace Api.TokenBurn.Insights.Features.Search;

public sealed class SearchValidator : AbstractValidator<SearchQuery>
{
    public SearchValidator()
    {
        RuleFor(x => x.Q).NotEmpty().WithMessage("q is required.");
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Mode).Must(BeSupportedMode)
            .When(x => x.Mode is not null)
            .WithMessage("mode must be 'keyword' (hybrid arrives in Phase 5).");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null);
    }

    private static bool BeSupportedMode(string? mode)
        => mode is null || mode == "keyword";
}
