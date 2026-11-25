using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PolskiYol.Data;
using PolskiYol.Features.Materials;
using PolskiYol.Services;

namespace PolskiYol.Tests.Infrastructure;

internal sealed class MemoryApiHost : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _configuration = new()
    {
        ["ConnectionStrings:DefaultConnection"] = null,
        ["AzureStorage:ServiceUri"] = null,
        ["Jwt:Issuer"] = "http-test",
        ["Jwt:Audience"] = "http-test",
        ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Jwt:AccessTokenExpirationMinutes"] = "5",
        ["Jwt:RefreshTokenExpirationDays"] = "1",
        ["Jwt:AbsoluteSessionExpirationDays"] = "2"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("OfflineHttpTests");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_configuration));
        builder.ConfigureTestServices(services =>
        {
            var data = new MemoryApiData();
            var accounts = new MemoryAccounts(data);
            accounts.Seed("admin@example.test", "Admin");

            services.RemoveAll<AppDbContext>();
            services.RemoveAll<Azure.Storage.Blobs.BlobContainerClient>();
            services.RemoveAll<IApiData>();
            services.RemoveAll<IAccountAccess>();
            services.RemoveAll<IRegistrationStore>();
            services.RemoveAll<IIdentityInitializer>();
            services.RemoveAll<IRefreshTokenStore>();
            services.RemoveAll<IStudentAssignmentStore>();
            services.RemoveAll<IMaterialBlobStore>();
            services.AddSingleton<IApiData>(data);
            services.AddSingleton<IAccountAccess>(accounts);
            services.AddSingleton<IRegistrationStore>(accounts);
            services.AddSingleton<IIdentityInitializer>(accounts);
            services.AddSingleton<IRefreshTokenStore>(new MemoryRefreshTokens(accounts));
            services.AddSingleton<IStudentAssignmentStore>(new MemoryAssignments(data));
            services.AddSingleton<IMaterialBlobStore, MemoryBlobs>();
        });
    }

    public HttpClient Connect() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
