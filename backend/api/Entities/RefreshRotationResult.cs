namespace PolskiYol.Models;

public sealed record RefreshTokenRotationResult(
    ApplicationUser User,
    string Token,
    DateTime ExpiresAtUtc,
    DateTime SessionExpiresAtUtc
);