using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using PolskiYol.Configuration;
using PolskiYol.Services;
using Xunit;

namespace PolskiYol.Tests.Unit;

public sealed class JwtServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly JwtService _service = new(Options.Create(new JwtOptions
    {
        Issuer = "unit-issuer", Audience = "unit-audience",
        SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        AccessTokenExpirationMinutes = 5
    }), new ManualClock(Now));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void ExpirationAndClaimsMatchTheSessionAndConfiguration(int sessionMinutes)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "person@example.test" };
        var result = _service.GenerateAccessToken(user, ["Student", "Teacher"], Now.AddMinutes(sessionMinutes).UtcDateTime);
        Assert.NotNull(result);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal("unit-issuer", token.Issuer);
        Assert.Equal("unit-audience", Assert.Single(token.Audiences));
        Assert.Equal(user.Id.ToString(), token.Subject);
        Assert.Equal(new[] { "Student", "Teacher" }, token.Claims.Where(c => c.Type == "role").Select(c => c.Value));
        Assert.Equal(Now.AddMinutes(Math.Min(sessionMinutes, 5)).UtcDateTime, token.ValidTo);
        Assert.Equal(token.ValidTo, result.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ExpiredSessionDoesNotReceiveAnAccessToken(int seconds)
    {
        var result = _service.GenerateAccessToken(new ApplicationUser(), ["Student"], Now.AddSeconds(seconds).UtcDateTime);
        Assert.Null(result);
    }
}
