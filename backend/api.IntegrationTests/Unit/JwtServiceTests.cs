using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using PolskiYol.Configuration;
using PolskiYol.Services;
using Xunit;

public sealed class JwtServiceTests
{
    private readonly JwtService _service = new(Options.Create(new JwtOptions
    {
        Issuer = "test",
        Audience = "test",
        SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        AccessTokenExpirationMinutes = 5
    }));

    [Fact]
    public void TokenAndResultExpireByTheSessionDeadline()
    {
        var deadline = DateTime.UtcNow.AddMinutes(1);
        var result = _service.GenerateAccessToken(new ApplicationUser(), ["Student"], deadline);

        Assert.NotNull(result);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(result.ExpiresAtUtc, token.ValidTo);
        Assert.InRange(token.ValidTo, deadline.AddSeconds(-1), deadline);
    }

    [Fact]
    public void ExpiredSessionDoesNotReceiveAnAccessToken()
    {
        var result = _service.GenerateAccessToken(
            new ApplicationUser(), ["Student"], DateTime.UtcNow.AddSeconds(-1));

        Assert.Null(result);
    }
}
