using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: WebApplicationFactoryContentRoot(
    "api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "../../../../api", "Program.cs", "0")]

public sealed class ApiFixture : IAsyncLifetime
{
    public const string Password = "Integration_2026!";
    public const string AdminEmail = "admin@example.test";

    private readonly PaymentInsertGate _paymentGate = new();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
        ?? throw new InvalidOperationException("Configure ConnectionStrings__DefaultConnection for integration tests.");

    private readonly string _apiDatabaseName = $"PolskiYolApi_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await using (var database = CreateDatabase(_apiDatabaseName))
        {
            await database.Database.MigrateAsync();
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__DefaultConnection", database.Database.GetConnectionString());
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTests");
            builder.ConfigureServices(services =>
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(_paymentGate)));
        });

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = new ApplicationUser { UserName = AdminEmail, Email = AdminEmail };

        Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "Admin")).Succeeded);
    }

    public HttpClient CreateClient(bool handleCookies = true) =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    public AppDbContext CreateDatabase(string? temporaryName = null)
    {
        var connection = new SqlConnectionStringBuilder(_connectionString);
        if (temporaryName is not null)
            connection.InitialCatalog = temporaryName;

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options);
    }

    public void SynchronizeNextTwoPaymentInserts() => _paymentGate.Arm();

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        if (_factory is not null)
            await _factory.DisposeAsync();

        await using var database = CreateDatabase(_apiDatabaseName);
        await database.Database.EnsureDeletedAsync();
    }

    private sealed class PaymentInsertGate : SaveChangesInterceptor
    {
        private TaskCompletionSource? _ready;
        private int _arrivals;

        public void Arm()
        {
            _arrivals = 0;
            _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var ready = _ready;
            if (ready is null || eventData.Context is null ||
                !eventData.Context.ChangeTracker.Entries<Payment>().Any(e => e.State == EntityState.Added))
                return result;

            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _ready = null;
                ready.SetResult();
            }

            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
