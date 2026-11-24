using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;

public sealed record AccessTokenResult(string token, DateTimeOffset ExpiresAtUtc);

public interface IJwtService
{
    AccessTokenResult GetAccessTokenResult(
ApplicationUser user,
IList<string> roles
    );



}

public class JwtService : IJwtService
{
    private readonly JwtOptions _jwtOptions;

    public JwtService(IOptions<JwtOptions> options)
    {
        _jwtOptions = options.Value;
    }

    public AccessTokenResult GenerateToken(
        ApplicationUser user,
        IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()
            ),

            new Claim(
                JwtRegisteredClaimNames.UniqueName,
                user.UserName ?? string.Empty
            )
        };

        foreach (var role in roles)
        {
            claims.Add(
                new Claim(ClaimTypes.Role, role)
            );
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(
            _jwtOptions.AccessTokenExpirationMinutes
        );

        // TODO: Create signing key.
        var keyBytes = Convert.FromBase64String(
    _jwtOptions.SigningKey
);

        var securitykey = new SymmetricSecurityKey(keyBytes);
        // TODO: Create signing credentials.
        var credentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256
        );
        // TODO: Create JWT.
        var jwt = new JwtSecurityToken(
 issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials
        );
        // TODO: Serialize JWT.
        var handler = new JwtTokenHandler();
        var token = handler.WriteToken(jwt);
        return AccessTokenResult(
token, expiresAt
        );
        // TODO: Return AccessTokenResult.

        throw new NotImplementedException();
    }
}



public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc
);


public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Identity user relationship
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    // Token security
    public string TokenHash { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public string? SecurityStamp { get; set; }

    // Expiration
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }

    // Rotation and revocation
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    // Optimistic concurrency
    public byte[] RowVersion { get; set; } = null!;
}


public static class RefreshTokenGenerator
{
    public static string GenerateToken()
    {
        bytes[] randomBytes = RandomNumberGenerator.GetBytes(64);
        return WebEncoders.Base64UrlEncode(randomBytes);
    }

    public static string HashToken(string token)
    {
        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);

        byte[] hashBytes = SHA256.HashData(tokenBytes);

        return Convert.ToHexString(hashBytes);
    }
}

public record RefreshtokenResult(
string Token, DateTimeOffset expiresAt
);
public record RefreshTokenRotationResult(

ApplicationUser user,
string token,
DateTimeOffset ExpiresAt
);


[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";
    private const string RefreshCookiePath = "/api/auth";

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;
    private readonly RefreshTokenService _refreshTokenService;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService,
        RefreshTokenService refreshTokenService)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _refreshTokenService = refreshTokenService;
    }

    // ======================================
    // LOGIN
    // ======================================

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginDto dto,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(
            dto.Email.Trim());

        if (user is null)
        {
            return Unauthorized();
        }

        var signInResult =
            await _signInManager.CheckPasswordSignInAsync(
                user,
                dto.Password,
                lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        // Create signed JWT
        var accessToken = _jwtService.GenerateToken(
            user,
            roles);

        // Create database-backed refresh session
        var refreshToken =
            await _refreshTokenService.CreateAsync(
                user,
                cancellationToken);

        // Send refresh token in HttpOnly cookie
        SetRefreshTokenCookie(
            refreshToken.Token,
            refreshToken.ExpiresAtUtc);

        // Only access token goes into JSON
        return Ok(new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc));
    }

    // ======================================
    // REFRESH
    // ======================================

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        var rawToken = Request.Cookies[RefreshCookieName];

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            DeleteRefreshTokenCookie();
            return Unauthorized();
        }

        var rotation =
            await _refreshTokenService.RotateAsync(
                rawToken,
                cancellationToken);

        if (rotation is null)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized();
        }

        // Retrieve current Identity roles
        var roles = await _userManager.GetRolesAsync(
            rotation.User);

        // Generate new JWT
        var accessToken = _jwtService.GenerateToken(
            rotation.User,
            roles);

        // Replace old cookie with new refresh token
        SetRefreshTokenCookie(
            rotation.Token,
            rotation.ExpiresAtUtc);

        return Ok(new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc));
    }

    // ======================================
    // LOGOUT
    // ======================================

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        var rawToken = Request.Cookies[RefreshCookieName];

        await _refreshTokenService.RevokeAsync(
            rawToken,
            cancellationToken);

        DeleteRefreshTokenCookie();

        return NoContent();
    }

    // ======================================
    // CURRENT USER
    // ======================================

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>>
        GetCurrentUser()
    {
        var userIdClaim = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(
            userId.ToString());

        if (user is null)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new CurrentUserResponse(
            user.Id,
            user.Email,
            roles));
    }

    // ======================================
    // REFRESH COOKIE HELPERS
    // ======================================

    private void SetRefreshTokenCookie(
        string rawToken,
        DateTimeOffset expiresAtUtc)
    {
        Response.Cookies.Append(
            RefreshCookieName,
            rawToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath,
                Expires = expiresAtUtc,
                IsEssential = true
            });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = RefreshCookiePath
            });
    }
}

