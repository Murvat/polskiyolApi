using Microsoft.Extensions.Options;
using PolskiYol.Configuration;
using PolskiYol.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using System.Data;

namespace PolskiYol.Services;

public sealed class RefreshTokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _context;
    private readonly JwtOptions _jwtOptions;

    public RefreshTokenService(
        AppDbContext context,
        IOptions<JwtOptions> jwtOptions,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<RefreshTokenResult> CreateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var token = RefreshTokenGenerator.GenerateToken();

        var tokenHash =
            RefreshTokenGenerator.HashToken(token);

        var securityStamp =
            await _userManager.GetSecurityStampAsync(user);

        var sessionExpiresAt = now.AddDays(
            _jwtOptions.AbsoluteSessionExpirationDays);

        var expiresAt = CalculateExpiration(
            now,
            sessionExpiresAt);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            SecurityStamp = securityStamp,
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            SessionExpiresAtUtc = sessionExpiresAt
        };

        _context.RefreshTokens.Add(refreshToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenResult(
            token,
            expiresAt);
    }
    private DateTime CalculateExpiration(
    DateTime now,
    DateTime sessionExpiresAt)
    {
        var slidingExpiration = now.AddDays(
            _jwtOptions.RefreshTokenExpirationDays);

        return slidingExpiration < sessionExpiresAt
            ? slidingExpiration
            : sessionExpiresAt;
    }
    public async Task<RefreshTokenRotationResult?> RotateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await RotateCoreAsync(
                    token,
                    cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
                when (attempt < maxAttempts)
            {
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException
                { Number: 1205 } &&
                      attempt < maxAttempts)
            {
                _context.ChangeTracker.Clear();
            }
            catch (SqlException ex)
                when (ex.Number == 1205 &&
                      attempt < maxAttempts)
            {
                _context.ChangeTracker.Clear();
            }

            await Task.Delay(
                Random.Shared.Next(30, 101) * attempt,
                cancellationToken);
        }

        throw new InvalidOperationException(
            "Unexpected refresh token rotation state.");
    }

    public async Task RevokeAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length != 86)
            return;

        var tokenHash =
            RefreshTokenGenerator.HashToken(token);

        var familyId = await _context.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);

        if (familyId is null)
            return;

        await RevokeFamilyAsync(
            familyId.Value,
            DateTime.UtcNow,
            cancellationToken);
    }

    private async Task<RefreshTokenRotationResult?> RotateCoreAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length != 86)
            return null;

        var tokenHash =
            RefreshTokenGenerator.HashToken(token);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var existingToken = await _context.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(
                t => t.TokenHash == tokenHash,
                cancellationToken);

        // 1. Check whether the token exists

        if (existingToken is null)
            return null;

        var now = DateTime.UtcNow;

        // 2. Detect refresh token reuse

        if (existingToken.RevokedAtUtc is not null)
        {
            if (existingToken.ReplacedByTokenId is not null)
            {
                await RevokeFamilyAsync(
                    existingToken.FamilyId,
                    now,
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);
            }

            return null;
        }

        // 3. Check token expiration

        if (existingToken.SessionExpiresAtUtc <= now)
        {
            await RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        if (existingToken.ExpiresAtUtc <= now)
            return null;
        // 4. Check account eligibility

        if (!await CanRefreshAsync(existingToken.User))
        {
            await RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return null;
        }

        // 5. Validate the security stamp

        var currentSecurityStamp =
            await _userManager.GetSecurityStampAsync(
                existingToken.User);

        if (!string.Equals(
                existingToken.SecurityStamp,
                currentSecurityStamp,
                StringComparison.Ordinal))
        {
            await RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return null;
        }

        // 6. Generate the replacement token

        var newToken =
            RefreshTokenGenerator.GenerateToken();

        var expiresAt = CalculateExpiration(
            now,
            existingToken.SessionExpiresAtUtc);
        var replacementToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            FamilyId = existingToken.FamilyId,
            TokenHash = RefreshTokenGenerator.HashToken(newToken),
            SecurityStamp = currentSecurityStamp,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            SessionExpiresAtUtc = existingToken.SessionExpiresAtUtc
        };
        // 7. Revoke the original token

        existingToken.RevokedAtUtc = now;
        existingToken.ReplacedByTokenId = replacementToken.Id;

        _context.RefreshTokens.Add(replacementToken);

        // 8. Persist changes and commit the transaction

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotationResult(
            existingToken.User,
            newToken,
            expiresAt);
    }

    private async Task<bool> CanRefreshAsync(
        ApplicationUser user)
    {
        if (!await _signInManager.CanSignInAsync(user))
            return false;

        if (_userManager.SupportsUserLockout &&
            await _userManager.IsLockedOutAsync(user))
            return false;

        if (await _userManager.GetTwoFactorEnabledAsync(user))
            return false;

        return true;
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        await _context.RefreshTokens
            .Where(t =>
                t.FamilyId == familyId &&
                t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    t => t.RevokedAtUtc,
                    revokedAtUtc),
                cancellationToken);
    }
}