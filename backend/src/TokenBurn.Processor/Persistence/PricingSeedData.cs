namespace TokenBurn.Processor.Persistence;

internal static class PricingSeedData
{
    internal sealed record ModelPriceSeed(string Slug, string Service, decimal InputPerMtok, decimal CacheReadPerMtok, decimal CacheWritePerMtok, decimal OutputPerMtok, int? ContextWindow);
    internal sealed record ModelAliasSeed(string Alias, string Service, string Slug);

    // All prices are open-ended from the -infinity sentinel with a NULL effective_to; the
    // seeder writes the sentinel literal directly into SQL because -infinity must never
    // round-trip through a DateTimeOffset parameter.
    internal static readonly ModelPriceSeed[] Prices =
    [
        new("deepseek-v4-flash", "deepseek", 0.14m, 0.0028m, 0m, 0.28m, 1048576),
        new("deepseek/deepseek-v4-flash-0731", "openrouter-deepinfra", 0.09m, 0.018m, 0m, 0.18m, 1048576),
        new("openai/gpt-5.6-luna", "openrouter-flex", 0.05m, 0.005m, 0m, 0.3m, 1050000),
        new("xiaomi/mimo-v2.5-pro", "openrouter", 0.435m, 0.0036m, 0m, 0.87m, null),
        new("xiaomi/mimo-v2.5", "openrouter", 0.14m, 0.0028m, 0m, 0.28m, null)
    ];

    internal static readonly ModelAliasSeed[] Aliases =
    [
        new("strong", "deepseek", "deepseek-v4-flash"),
        new("fast", "deepseek", "deepseek-v4-flash"),
        new("flash", "deepseek", "deepseek-v4-flash"),
        new("v4-flash", "deepseek", "deepseek-v4-flash"),
        new("pro", "deepseek", "deepseek-v4-flash"),
        new("v4-pro", "deepseek", "deepseek-v4-flash"),
        new("deepinfra", "openrouter-deepinfra", "deepseek/deepseek-v4-flash-0731"),
        new("flash-deepinfra", "openrouter-deepinfra", "deepseek/deepseek-v4-flash-0731"),
        new("luna", "openrouter-flex", "openai/gpt-5.6-luna"),
        new("mimo", "openrouter", "xiaomi/mimo-v2.5-pro"),
        new("mimo-fast", "openrouter", "xiaomi/mimo-v2.5")
    ];
}
