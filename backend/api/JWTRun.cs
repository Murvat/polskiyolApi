
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// ==========================
// PROGRAM.CS
// ==========================

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services
    .AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddControllers();

var app = builder.Build();

await IdentitySeeder.SeedRolesAsync(app.Services);

app.MapControllers();

app.Run();


// ==========================
// APPLICATION USER
// ==========================

public class ApplicationUser : IdentityUser<Guid>
{
    public Teacher? TeacherProfile { get; set; }
    public Student? StudentProfile { get; set; }
}


// ==========================
// TEACHER
// ==========================

public class Teacher
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;
    public string Surname { get; set; } = null!;

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;
}


// ==========================
// STUDENT
// ==========================

public class Student
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;
    public string Surname { get; set; } = null!;

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;
}


// ==========================
// DATABASE CONTEXT
// ==========================

public class AppDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.HasOne(t => t.User)
                .WithOne(u => u.TeacherProfile)
                .HasForeignKey<Teacher>(t => t.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.HasOne(s => s.User)
                .WithOne(u => u.StudentProfile)
                .HasForeignKey<Student>(s => s.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}


// ==========================
// IDENTITY ROLE SEEDER
// ==========================

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(
        IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        string[] roles = { "Student", "Teacher", "Admin" };

        foreach (var roleName in roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(
                new IdentityRole<Guid>(roleName)
            );

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(e => e.Description)
                );

                throw new InvalidOperationException(
                    $"Could not create role '{roleName}': {errors}"
                );
            }
        }
    }
}


// ==========================
// REGISTRATION DTOs
// ==========================

public class RegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class StudentRegisterDto : RegisterDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Surname { get; set; } = string.Empty;
}

public class TeacherRegisterDto : RegisterDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Surname { get; set; } = string.Empty;
}


// ==========================
// AUTH CONTROLLER
// ==========================

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtService _jwtService;

    public AuthController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IJwtService jwtService)
    {
        _db = db;
        _userManager = userManager;
        _jwtService = jwtService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginDto dto)
    {
        // 1. Find user by email
        var user = await _userManager.FindByEmailAsync(
            dto.Email.Trim()
        );

        if (user is null)
        {
            return Unauthorized("Invalid email or password.");
        }

        // 2. Check whether the account is locked
        if (await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized("Invalid email or password.");
        }

        // 3. Verify password
        var passwordValid = await _userManager.CheckPasswordAsync(
            user,
            dto.Password
        );

        if (!passwordValid)
        {
            return Unauthorized("Invalid email or password.");
        }

        // 4. Retrieve roles
        var roles = await _userManager.GetRolesAsync(user);

        // 5. Generate JWT
        var jwt = _jwtService.GenerateToken(user, roles);

        // 6. Build response
        var response = new AuthResponse(
            jwt.Token,
            jwt.ExpiresAtUtc
        );

        return Ok(response);
    }

    // ==========================
    // STUDENT REGISTRATION
    // ==========================



    [AllowAnonymous]
    [HttpPost("student")]
    public async Task<IActionResult> RegisterStudent(
        StudentRegisterDto dto,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken
            );

        var email = dto.Email.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await _userManager.CreateAsync(
            user,
            dto.Password
        );

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                Errors = result.Errors.Select(e => new
                {
                    e.Code,
                    e.Description
                })
            });
        }

        var roleResult = await _userManager.AddToRoleAsync(
            user,
            "Student"
        );

        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to assign Student role."
            );
        }

        var student = new Student
        {
            Name = dto.Name.Trim(),
            Surname = dto.Surname.Trim(),
            UserId = user.Id
        };

        _db.Students.Add(student);

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            UserId = user.Id,
            StudentId = student.Id
        });
    }

    // ==========================
    // TEACHER REGISTRATION
    // ==========================

    [AllowAnonymous]
    [HttpPost("teacher")]
    public async Task<IActionResult> RegisterTeacher(
        TeacherRegisterDto dto,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken
            );

        var email = dto.Email.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await _userManager.CreateAsync(
            user,
            dto.Password
        );

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                Errors = result.Errors.Select(e => new
                {
                    e.Code,
                    e.Description
                })
            });
        }

        var roleResult = await _userManager.AddToRoleAsync(
            user,
            "Teacher"
        );

        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to assign Teacher role."
            );
        }

        var teacher = new Teacher
        {
            Name = dto.Name.Trim(),
            Surname = dto.Surname.Trim(),
            UserId = user.Id
        };

        _db.Teachers.Add(teacher);

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            UserId = user.Id,
            TeacherId = teacher.Id
        });
    }
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenExpirationMinutes { get; set; }
    public int RefreshTokenExpirationDays { get; set; }
    public int AbsoluteSessionExpirationDays { get; set; }
}

// {
//     "Jwt": {
//         "Issuer": "PolskiYol",
//     "Audience": "PolskiYol.Api",
//     "AccessTokenExpirationMinutes": 15,
//     "RefreshTokenExpirationDays": 7,
//     "AbsoluteSessionExpirationDays": 30
//     }
// }
