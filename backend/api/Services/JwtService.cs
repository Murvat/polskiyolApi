using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PolskiYol.Configuration;
using PolskiYol.Models;

namespace PolskiYol.Services;

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _jwtOptions;
    private readonly TimeProvider _clock;

    public JwtService(IOptions<JwtOptions> jwtOptions, TimeProvider? clock = null)
    {
        _jwtOptions = jwtOptions.Value;
        _clock = clock ?? TimeProvider.System;
    }

    public AccessTokenResult? GenerateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        DateTime sessionExpiresAtUtc)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var configuredExpiration = now.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);
        var deadline = configuredExpiration < sessionExpiresAtUtc ? configuredExpiration : sessionExpiresAtUtc;

        // JWT timestamps are encoded as whole seconds.
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(DateTime.SpecifyKind(deadline, DateTimeKind.Utc)).ToUnixTimeSeconds()).UtcDateTime;
        if (expiresAt <= now)
            return null;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

            new(JwtRegisteredClaimNames.UniqueName,
                user.UserName ?? user.Id.ToString()),

            new(JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        claims.AddRange(
            roles.Select(role => new Claim("role", role)));

        var signingKey = new SymmetricSecurityKey(
            Convert.FromBase64String(_jwtOptions.SigningKey));

        var signingCredentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: signingCredentials);

        var tokenHandler = new JwtSecurityTokenHandler();

        var accessToken = tokenHandler.WriteToken(token);

        return new AccessTokenResult(
            accessToken,
            expiresAt);
    }
}