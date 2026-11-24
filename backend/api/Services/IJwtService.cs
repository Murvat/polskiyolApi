using PolskiYol.Models;

namespace PolskiYol.Services;

public interface IJwtService
{
    AccessTokenResult GenerateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles);
}