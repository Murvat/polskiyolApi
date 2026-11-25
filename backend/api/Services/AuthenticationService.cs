using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PolskiYol.DTOs;

namespace PolskiYol.Services;

public enum RegistrationRole { Student, Teacher }

public interface IRegistrationStore
{
    Task<IDbContextTransaction> BeginAsync(CancellationToken ct);
    Task<IdentityResult> CreateAsync(ApplicationUser user, string password);
    Task<IdentityResult> AddRoleAsync(ApplicationUser user, string role);
    Task SaveProfileAsync(ApplicationUser user, RegisterDto profile, RegistrationRole role, CancellationToken ct);
}

public sealed class RegistrationStore(AppDbContext context, UserManager<ApplicationUser> users) : IRegistrationStore
{
    public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) =>
        context.Database.BeginTransactionAsync(ct);

    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password) =>
        users.CreateAsync(user, password);

    public Task<IdentityResult> AddRoleAsync(ApplicationUser user, string role) => users.AddToRoleAsync(user, role);

    public async Task SaveProfileAsync(ApplicationUser user, RegisterDto profile, RegistrationRole role, CancellationToken ct)
    {
        if (role == RegistrationRole.Student)
        {
            context.Students.Add(new Student
            {
                UserId = user.Id, FirstName = profile.FirstName, LastName = profile.LastName,
                PhoneNumber = profile.PhoneNumber
            });
        }
        else
        {
            context.Teachers.Add(new Teacher
            {
                UserId = user.Id, FirstName = profile.FirstName, LastName = profile.LastName,
                PhoneNumber = profile.PhoneNumber
            });
        }

        await context.SaveChangesAsync(ct);
    }
}

public sealed class AuthenticationService(IAccountAccess accounts, IRegistrationStore registration)
{
    public async Task<IdentityResult> RegisterAsync(RegisterDto profile, RegistrationRole role, CancellationToken ct)
    {
        await using var transaction = await registration.BeginAsync(ct);
        var user = new ApplicationUser { UserName = profile.Email, Email = profile.Email };
        var result = await registration.CreateAsync(user, profile.Password);
        if (!result.Succeeded)
            return result;

        result = await registration.AddRoleAsync(user, role.ToString());
        if (!result.Succeeded)
            return result;

        await registration.SaveProfileAsync(user, profile, role, ct);
        await transaction.CommitAsync(ct);
        return IdentityResult.Success;
    }

    public async Task<(ApplicationUser? User, bool LockedOut)> CheckLoginAsync(string email, string password)
    {
        var user = await accounts.FindAsync(email);
        if (user is null)
            return (null, false);

        var result = await accounts.CheckPasswordAsync(user, password);
        return (result.Succeeded ? user : null, result.IsLockedOut);
    }
}
