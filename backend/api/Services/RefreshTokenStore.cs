using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolskiYol.Models;

namespace PolskiYol.Services;

public interface IRefreshTokenStore
{
    Task<IDbContextTransaction> BeginRotationAsync(CancellationToken ct);
    Task<RefreshToken?> FindAsync(string hash, CancellationToken ct);
    Task<Guid?> FindFamilyAsync(string hash, CancellationToken ct);
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct);
    Task SaveAsync(RefreshToken token, CancellationToken ct);
    void ClearTracking();
}

public sealed class RefreshTokenStore(AppDbContext context) : IRefreshTokenStore
{
    public Task<IDbContextTransaction> BeginRotationAsync(CancellationToken ct) =>
        context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

    public Task<RefreshToken?> FindAsync(string hash, CancellationToken ct) =>
        context.RefreshTokens.Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, ct);

    public Task<Guid?> FindFamilyAsync(string hash, CancellationToken ct) =>
        context.RefreshTokens.Where(token => token.TokenHash == hash)
            .Select(token => (Guid?)token.FamilyId).SingleOrDefaultAsync(ct);

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct) =>
        context.RefreshTokens.Where(token => token.FamilyId == familyId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAtUtc, revokedAtUtc), ct);

    public async Task SaveAsync(RefreshToken token, CancellationToken ct)
    {
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync(ct);
    }

    public void ClearTracking() => context.ChangeTracker.Clear();
}
