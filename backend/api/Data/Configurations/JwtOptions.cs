namespace PolskiYol.Configuration;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public int AbsoluteSessionExpirationDays { get; set; }
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenExpirationMinutes { get; set; }
    public int RefreshTokenExpirationDays { get; set; }
}