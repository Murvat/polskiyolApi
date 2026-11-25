
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PolskiYol.Features.Materials;
using PolskiYol.Configuration;
using PolskiYol.Services;

var builder = WebApplication.CreateBuilder(args);

// Database connection string
string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

// Controllers and OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return ValueTask.CompletedTask;
    };

    AddAuthRateLimit(options, "auth-login", 10);
    AddAuthRateLimit(options, "auth-registration", 10);
    AddAuthRateLimit(options, "auth-refresh", 30);
});

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Entity Framework Core
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});
builder.Services.AddSingleton<IJwtService, JwtService>();
// ASP.NET Core Identity
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Azure Blob Storage
builder.Services.AddSingleton(sp =>
{
    var serviceUri = builder.Configuration["AzureStorage:ServiceUri"]
        ?? throw new InvalidOperationException(
            "AzureStorage:ServiceUri is missing.");

    var serviceClient = new BlobServiceClient(
        new Uri(serviceUri),
        new DefaultAzureCredential());

    return serviceClient.GetBlobContainerClient("materials");
});

builder.Services.AddSingleton<MaterialBlobCleanup>();
builder.Services.AddHostedService<MaterialBlobCleanup>(sp => sp.GetRequiredService<MaterialBlobCleanup>());

// JWT configuration and validation
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer),
        "JWT Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
        "JWT Audience is required.")
    .Validate(options => options.AccessTokenExpirationMinutes > 0,
        "JWT expiration must be greater than zero.")
        .Validate(
    options => options.RefreshTokenExpirationDays > 0,
    "Refresh token expiration must be greater than zero."
)
.Validate(
    options => options.AbsoluteSessionExpirationDays > 0,
    "Absolute session expiration must be greater than zero.")
.Validate(
    options => options.AbsoluteSessionExpirationDays
        >= options.RefreshTokenExpirationDays,
    "Absolute session lifetime must not be shorter than refresh token lifetime.")
.Validate(options =>
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(options.SigningKey).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }, "JWT signing key must contain at least 256 bits encoded in Base64.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountAccess, AccountAccess>();
builder.Services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<IRegistrationStore, RegistrationStore>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<IStudentAssignmentStore, StudentAssignmentStore>();
builder.Services.AddScoped<StudentAssignmentService>();
// Authentication schemes
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultForbidScheme =
        JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

// JWT Bearer configuration
builder.Services.AddOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwt = jwtOptionsAccessor.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateLifetime = true,
            RequireExpirationTime = true,

            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,

            IssuerSigningKey = new SymmetricSecurityKey(
                Convert.FromBase64String(jwt.SigningKey)),

            ValidAlgorithms = new[]
            {
                SecurityAlgorithms.HmacSha256
            },

            ClockSkew = TimeSpan.Zero,

            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        options.MapInboundClaims = true;
        options.IncludeErrorDetails = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userId, out var id) || id == Guid.Empty)
                {
                    context.Fail("The token must identify an application user.");
                }

                return Task.CompletedTask;
            }
        };
    });

// Build application
var app = builder.Build();

// Seed Identity roles
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    await IdentitySeeder.SeedRolesAsync(roleManager);
}

// Development environment
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var dbContext = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    // await DbSeeder.SeedAsync(dbContext);
}

// HTTP request pipeline
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(context => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: "An unexpected error occurred.",
        extensions: new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier
        }).ExecuteAsync(context));
});
app.UseHttpsRedirection();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void AddAuthRateLimit(RateLimiterOptions options, string policy, int permitLimit)
{
    options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
}

public partial class Program { }
