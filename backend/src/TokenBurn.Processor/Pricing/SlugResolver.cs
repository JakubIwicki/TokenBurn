namespace TokenBurn.Processor.Pricing;

public static class SlugResolver
{
    private const string OneMinuteSuffix = "[1m]";

    public static string Resolve(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return "";
        return slug.EndsWith(OneMinuteSuffix, StringComparison.Ordinal)
            ? slug[..^OneMinuteSuffix.Length]
            : slug;
    }
}
