namespace TokenBurn.Common.Primitives;

public abstract class BaseEntity<TKey>
{
    public TKey Id { get; protected init; } = default!;
}
