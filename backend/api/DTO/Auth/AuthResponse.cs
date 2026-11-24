namespace PolskiYol.DTOs;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc
);