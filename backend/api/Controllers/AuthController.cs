
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PolskiYol.Services;
using PolskiYol.DTOs;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RefreshTokenService _refreshTokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthenticationService _authentication;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        AuthenticationService authentication,
        IJwtService jwtService,
        RefreshTokenService refreshTokenService,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _authentication = authentication;
        _jwtService = jwtService;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-registration")]
    [HttpPost("register/student")]
    public async Task<IActionResult> RegisterStudent([FromBody] RegisterDto dto, CancellationToken ct)
    {
        var result = await _authentication.RegisterAsync(dto, RegistrationRole.Student, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created) : BadRequest(result.Errors);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-registration")]
    [HttpPost("register/teacher")]
    public async Task<IActionResult> RegisterTeacher([FromBody] RegisterDto dto, CancellationToken ct)
    {
        var result = await _authentication.RegisterAsync(dto, RegistrationRole.Teacher, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created) : BadRequest(result.Errors);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!Request.Cookies.TryGetValue(
            "refreshToken",
            out var token))
        {
            return Unauthorized();
        }

        var result = await _refreshTokenService.RotateAsync(
            token,
            cancellationToken);

        if (result is null)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(
            result.User);

        var accessToken = _jwtService.GenerateAccessToken(
            result.User,
            roles,
            result.SessionExpiresAtUtc);

        if (accessToken is null)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized();
        }

        SetRefreshTokenCookie(
            result.Token,
            result.ExpiresAtUtc);

        var response = new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc);

        return Ok(response);
    }
    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            "refreshToken",
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth"
            });
    }
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
    [FromBody] LoginDto dto,
    CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var (user, lockedOut) = await _authentication.CheckLoginAsync(dto.Email, dto.Password);
        if (user is null)
        {
            if (lockedOut)
                _logger.LogWarning("Login rejected due to account lockout. TraceId {TraceId}", HttpContext.TraceIdentifier);

            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        var refreshToken = await _refreshTokenService.CreateAsync(user, cancellationToken);
        var accessToken = _jwtService.GenerateAccessToken(user, roles, refreshToken.SessionExpiresAtUtc);

        if (accessToken is null)
            return Unauthorized();

        SetRefreshTokenCookie(
            refreshToken.Token,
            refreshToken.ExpiresAtUtc);

        var response = new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAtUtc);

        return Ok(response);
    }
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
    CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (Request.Cookies.TryGetValue(
            "refreshToken",
            out var token))
        {
            await _refreshTokenService.RevokeAsync(
                token,
                cancellationToken);
        }

        DeleteRefreshTokenCookie();

        return NoContent();
    }
    private void SetRefreshTokenCookie(
        string token,
        DateTime expiresAtUtc)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(expiresAtUtc),
            Path = "/api/auth"
        };

        Response.Cookies.Append(
            "refreshToken",
            token,
            cookieOptions);
    }
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
            return Unauthorized();

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new
        {
            user.Id,
            user.Email,
            Roles = roles
        });
    }
}
