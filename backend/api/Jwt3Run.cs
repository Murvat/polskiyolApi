public sealed class RefreshTokenService
{
    private readonly AppDbContext _context;
    private readonly JwtOptions _options;

    public RefreshTokenService(
        AppDbContext context,
        IOptions<JwtOptions> options)
    {
        _context = context;
        _options = options.Value;
    }


    public async Task<RefreshTokenResult> CreateAsync(
       ApplicationUser user,
       CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // 1. Generate raw refresh token
        string token = RefreshTokenGenerator.GenerateToken();

        // 2. Hash the token
        string hashToken = RefreshTokenGenerator.HashToken(token);

        // 3. Calculate expiration
        var now = DateTimeOffset.UtcNow;

        var absoluteExpiresAtUtc = now.AddDays(
            _options.AbsoluteSessionExpirationDays
        );

        var expiresAtUtc = now.AddDays(
            _options.RefreshTokenExpirationDays
        );

        // Never exceed absolute session expiration
        if (expiresAtUtc > absoluteExpiresAtUtc)
        {
            expiresAtUtc = absoluteExpiresAtUtc;
        }

        // 4. Create database entity
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,

            TokenHash = hashToken,

            FamilyId = Guid.NewGuid(),

            SecurityStamp = user.SecurityStamp,

            CreatedAtUtc = now,

            ExpiresAtUtc = expiresAtUtc,

            AbsoluteExpiresAtUtc = absoluteExpiresAtUtc
        };

        // 5. Save token hash in SQL Server
        _context.RefreshTokens.Add(refreshToken);

        await _context.SaveChangesAsync(cancellationToken);

        // 6. Return raw token to the caller
        return new RefreshTokenResult(
            token,
            expiresAtUtc
        );
    }



    // Inside RefreshTokenService

    public async Task<RefreshTokenRotationResult?> RotateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate input
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        // 2. Hash incoming token
        var tokenHash = RefreshTokenGenerator.HashToken(rawToken);

        // 3. Find token and its user
        var existingToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .SingleOrDefaultAsync(
                rt => rt.TokenHash == tokenHash,
                cancellationToken
            );

        if (existingToken is null)
        {
            return null;
        }

        var familyId = existingToken.FamilyId;

        // 4. Detect reuse of a rotated token
        if (existingToken.ReplacedByTokenHash is not null)
        {
            await RevokeFamilyAsync(familyId, cancellationToken);
            return null;
        }

        // 5. Check revocation
        if (existingToken.RevokedAtUtc is not null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        // 6. Check expiration
        if (existingToken.ExpiresAtUtc <= now ||
            existingToken.AbsoluteExpiresAtUtc <= now)
        {
            return null;
        }

        // 7. Validate Identity security stamp
        if (string.IsNullOrEmpty(existingToken.SecurityStamp) ||
            !string.Equals(
                existingToken.SecurityStamp,
                existingToken.User.SecurityStamp,
                StringComparison.Ordinal))
        {
            await RevokeFamilyAsync(familyId, cancellationToken);
            return null;
        }

        // 8. Generate replacement token
        var newRawToken = RefreshTokenGenerator.GenerateToken();

        var newTokenHash = RefreshTokenGenerator.HashToken(
            newRawToken
        );

        // 9. Calculate replacement expiration
        var newExpiresAtUtc = now.AddDays(
            _options.RefreshTokenExpirationDays
        );

        if (newExpiresAtUtc > existingToken.AbsoluteExpiresAtUtc)
        {
            newExpiresAtUtc = existingToken.AbsoluteExpiresAtUtc;
        }

        // 10. Create replacement entity
        var replacement = new RefreshToken
        {
            UserId = existingToken.UserId,
            TokenHash = newTokenHash,
            FamilyId = existingToken.FamilyId,
            SecurityStamp = existingToken.SecurityStamp,

            CreatedAtUtc = now,
            ExpiresAtUtc = newExpiresAtUtc,
            AbsoluteExpiresAtUtc =
                existingToken.AbsoluteExpiresAtUtc
        };

        // 11. Begin transaction
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                cancellationToken
            );

        try
        {
            // 12. Invalidate previous token
            existingToken.RevokedAtUtc = now;
            existingToken.ReplacedByTokenHash = newTokenHash;

            // 13. Track replacement
            _context.RefreshTokens.Add(replacement);

            // 14. Save both changes atomically
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request modified the same token.
            await transaction.RollbackAsync(cancellationToken);

            // Discard stale tracked entities.
            _context.ChangeTracker.Clear();

            // Fail closed: invalidate the token family.
            await RevokeFamilyAsync(familyId, cancellationToken);

            return null;
        }

        // 15. Return the new raw token
        return new RefreshTokenRotationResult(
            existingToken.User,
            newRawToken,
            newExpiresAtUtc
        );
    }


    // =================================
    // REVOKE TOKEN FAMILY
    // =================================

    public async Task RevokeAsync(
        string? rawToken,
        CancellationToken cancellationToken = default)
    {
        // 1. Handle a missing refresh token
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        // 2. Hash the incoming token
        var tokenHash = RefreshTokenGenerator.HashToken(rawToken);

        // 3. Find the token's session family
        var familyId = await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.TokenHash == tokenHash)
            .Select(rt => (Guid?)rt.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        // 4. Unknown token: nothing to revoke
        if (familyId is null)
        {
            return;
        }

        // 5. Revoke the entire login session
        await RevokeFamilyAsync(
            familyId.Value,
            cancellationToken
        );
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await _context.RefreshTokens
            .Where(rt =>
                rt.FamilyId == familyId &&
                rt.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    rt => rt.RevokedAtUtc,
                    (DateTimeOffset?)now
                ),
                cancellationToken
            );
    }
}
