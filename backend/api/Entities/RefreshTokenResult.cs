namespace PolskiYol.Models;

public sealed record RefreshTokenResult(
    string Token,
    DateTime ExpiresAtUtc
);