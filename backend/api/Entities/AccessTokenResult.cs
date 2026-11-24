namespace PolskiYol.Models;

public sealed record AccessTokenResult(
    string Token,
    DateTime ExpiresAtUtc
);