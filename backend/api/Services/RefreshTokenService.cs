using Microsoft.Extensions.Options;
using PolskiYol.Configuration;
using PolskiYol.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace PolskiYol.Services;

public sealed class RefreshTokenService
{
    private readonly IAccountAccess _accounts;
    private readonly IRefreshTokenStore _store;
    private readonly TimeProvider _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        IRefreshTokenStore store, IOptions<JwtOptions> jwtOptions,
        IAccountAccess accounts, TimeProvider clock,
        ILogger<RefreshTokenService> logger)
    {
        _store = store;
        _jwtOptions = jwtOptions.Value;
        _accounts = accounts;
        _clock = clock;
        _logger = logger;
    }

    public async Task<RefreshTokenResult> CreateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var token = RefreshTokenGenerator.GenerateToken();

        var tokenHash =
            RefreshTokenGenerator.HashToken(token);

        var securityStamp =
            await _accounts.GetSecurityStampAsync(user);

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

        await _store.SaveAsync(refreshToken, cancellationToken);

        return new RefreshTokenResult(token, expiresAt, sessionExpiresAt);
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
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                or DbUpdateException { InnerException: SqlException { Number: 1205 } }
                or SqlException { Number: 1205 })
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(
                        "Refresh token rotation exhausted {Attempts} attempts. TraceId {TraceId}",
                        attempt, Activity.Current?.TraceId);
                    throw;
                }

                _store.ClearTracking();
                await Task.Delay(Random.Shared.Next(30, 101) * attempt, cancellationToken);
            }
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

        var familyId = await _store.FindFamilyAsync(tokenHash, cancellationToken);

        if (familyId is null)
            return;

        var revoked = await _store.RevokeFamilyAsync(familyId.Value, _clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (revoked > 0)
            _logger.LogInformation("Refresh token family revoked on logout. TraceId {TraceId}", Activity.Current?.TraceId);
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

        await using var transaction = await _store.BeginRotationAsync(cancellationToken);
        var existingToken = await _store.FindAsync(tokenHash, cancellationToken);

        // 1. Check whether the token exists

        if (existingToken is null)
            return null;

        var now = _clock.GetUtcNow().UtcDateTime;

        // 2. Detect refresh token reuse

        if (existingToken.RevokedAtUtc is not null)
        {
            if (existingToken.ReplacedByTokenId is not null)
            {
                var revoked = await _store.RevokeFamilyAsync(
                    existingToken.FamilyId,
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                _logger.LogWarning(
                    "Refresh token reuse detected. Revoked {TokenCount} tokens. TraceId {TraceId}",
                    revoked, Activity.Current?.TraceId);
            }

            return null;
        }

        // 3. Check token expiration

        if (existingToken.SessionExpiresAtUtc <= now)
        {
            await _store.RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Expired refresh token family revoked. TraceId {TraceId}", Activity.Current?.TraceId);

            return null;
        }

        if (existingToken.ExpiresAtUtc <= now)
            return null;
        // 4. Check account eligibility

        if (!await _accounts.CanRefreshAsync(existingToken.User))
        {
            await _store.RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Refresh token family revoked because the account is ineligible. TraceId {TraceId}",
                Activity.Current?.TraceId);

            return null;
        }

        // 5. Validate the security stamp

        var currentSecurityStamp =
            await _accounts.GetSecurityStampAsync(
                existingToken.User);

        if (!string.Equals(
                existingToken.SecurityStamp,
                currentSecurityStamp,
                StringComparison.Ordinal))
        {
            await _store.RevokeFamilyAsync(
                existingToken.FamilyId,
                now,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Refresh token family revoked after a security stamp change. TraceId {TraceId}",
                Activity.Current?.TraceId);

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

        await _store.SaveAsync(replacementToken, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new RefreshTokenRotationResult(
            existingToken.User,
            newToken,
            expiresAt,
            existingToken.SessionExpiresAtUtc);
    }
}
