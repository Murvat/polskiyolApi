using Microsoft.EntityFrameworkCore;

namespace PolskiYol.Data;

public interface IApiData
{
    IQueryable<TEntity> Query<TEntity>(bool track = false) where TEntity : class;
    void Add<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class ApiData(AppDbContext context) : IApiData
{
    public IQueryable<TEntity> Query<TEntity>(bool track = false) where TEntity : class =>
        track ? context.Set<TEntity>().AsTracking() : context.Set<TEntity>().AsNoTracking();

    public void Add<TEntity>(TEntity entity) where TEntity : class => context.Add(entity);
    public void Remove<TEntity>(TEntity entity) where TEntity : class => context.Remove(entity);
    public Task<int> SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
