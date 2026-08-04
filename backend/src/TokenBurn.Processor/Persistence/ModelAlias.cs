namespace TokenBurn.Processor.Persistence;

public sealed class ModelAlias
{
    public string Alias { get; private set; } = null!;
    public string Service { get; private set; } = null!;
    public string Slug { get; private set; } = null!;

    private ModelAlias() { }

    public ModelAlias(string alias, string service, string slug)
    {
        Alias = alias;
        Service = service;
        Slug = slug;
    }
}
