namespace TokenBurn.Common;

public abstract class BaseEntity<TKey>
{
    public TKey Id { get; protected init; } = default!;
}
