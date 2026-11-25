using Microsoft.AspNetCore.Identity;

namespace PolskiYol.Data;

public interface IIdentityInitializer
{
    Task InitializeAsync();
}

public sealed class IdentityInitializer(RoleManager<IdentityRole<Guid>> roles) : IIdentityInitializer
{
    public Task InitializeAsync() => IdentitySeeder.SeedRolesAsync(roles);
}
