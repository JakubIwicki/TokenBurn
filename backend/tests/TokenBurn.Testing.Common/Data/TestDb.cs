using Microsoft.EntityFrameworkCore;

namespace TokenBurn.Testing.Common.Data;

public sealed class TestDb(DbContext db)
{
    public void Store<T>(T entity) where T : class
    {
        db.Set<T>().Add(entity);
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public void StoreAll<T>(params T[] entities) where T : class
    {
        db.Set<T>().AddRange(entities);
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public void Remove<T>(T entity) where T : class
    {
        db.Set<T>().Remove(entity);
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    // Persists mutations made on a tracked aggregate (loaded via Find/FindFresh)
    // and clears the tracker so the next read comes from a cold context.
    public void SaveChanges()
    {
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public T? Find<T>(object id) where T : class => db.Set<T>().Find(id);

    public T? FindFresh<T>(object id) where T : class
    {
        db.ChangeTracker.Clear();
        return db.Set<T>().Find(id);
    }

    public IQueryable<T> Query<T>() where T : class => db.Set<T>();
}
