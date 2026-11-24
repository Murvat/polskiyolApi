using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace PolskiYol.Services;

public static class RefreshTokenGenerator
{
    public static string GenerateToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64);

        return WebEncoders.Base64UrlEncode(randomBytes);
    }

    public static string HashToken(string token)
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);

        byte[] hashBytes = SHA256.HashData(tokenBytes);

        return Convert.ToHexString(hashBytes);
    }
}