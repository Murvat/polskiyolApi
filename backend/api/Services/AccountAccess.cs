using Microsoft.AspNetCore.Identity;

namespace PolskiYol.Services;

public interface IAccountAccess
{
    Task<ApplicationUser?> FindAsync(string email);
    Task<SignInResult> CheckPasswordAsync(ApplicationUser user, string password);
    Task<bool> CanRefreshAsync(ApplicationUser user);
    Task<string> GetSecurityStampAsync(ApplicationUser user);
    Task<IList<string>> GetRolesAsync(ApplicationUser user);
}

public sealed class AccountAccess(
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : IAccountAccess
{
    public Task<ApplicationUser?> FindAsync(string email) => users.FindByEmailAsync(email);

    public async Task<SignInResult> CheckPasswordAsync(ApplicationUser user, string password)
    {
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded && await users.GetTwoFactorEnabledAsync(user))
            return SignInResult.TwoFactorRequired;

        return result;
    }

    public async Task<bool> CanRefreshAsync(ApplicationUser user) =>
        await signIn.CanSignInAsync(user) &&
        (!users.SupportsUserLockout || !await users.IsLockedOutAsync(user)) &&
        !await users.GetTwoFactorEnabledAsync(user);

    public Task<string> GetSecurityStampAsync(ApplicationUser user) => users.GetSecurityStampAsync(user);
    public Task<IList<string>> GetRolesAsync(ApplicationUser user) => users.GetRolesAsync(user);
}
