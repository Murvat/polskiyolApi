using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PolskiYol.Configuration;
using PolskiYol.Services;
using Xunit;

namespace PolskiYol.Tests.Unit;

public sealed class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RegistrationRole.Student)]
    [InlineData(RegistrationRole.Teacher)]
    public async Task RegistrationCommitsAccountRoleAndProfile(RegistrationRole role)
    {
        var store = new RegistrationFake();
        var service = new AuthenticationService(new AccountFake(), store);

        Assert.True((await service.RegisterAsync(Profile(), role, default)).Succeeded);
        Assert.Equal(("user@example.test", role.ToString(), role), Assert.Single(store.Registrations));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IdentityFailureLeavesNoPartialRegistration(bool failCreate)
    {
        var store = new RegistrationFake { FailCreate = failCreate, FailRole = !failCreate };
        var service = new AuthenticationService(new AccountFake(), store);

        Assert.False((await service.RegisterAsync(Profile(), RegistrationRole.Student, default)).Succeeded);
        Assert.Empty(store.Registrations);
        Assert.False(store.ProfileWritten);
    }

    [Fact]
    public async Task ProfileWriteFailureDoesNotCommitRegistration()
    {
        var store = new RegistrationFake { FailProfile = true };
        var service = new AuthenticationService(new AccountFake(), store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAsync(Profile(), RegistrationRole.Student, default));
        Assert.Empty(store.Registrations);
    }

    [Fact]
    public async Task LoginReportsGenericFailureAndIdentifiesLockoutForLogging()
    {
        var accounts = new AccountFake();
        var service = new AuthenticationService(accounts, new RegistrationFake());
        Assert.Equal(((ApplicationUser?)null, false), await service.CheckLoginAsync("unknown@example.test", "password"));

        accounts.PasswordResult = SignInResult.Failed;
        Assert.Equal(((ApplicationUser?)null, false), await service.CheckLoginAsync(accounts.User.Email!, "wrong"));
        accounts.PasswordResult = SignInResult.TwoFactorRequired;
        Assert.Equal(((ApplicationUser?)null, false), await service.CheckLoginAsync(accounts.User.Email!, "password"));
        accounts.PasswordResult = SignInResult.LockedOut;
        Assert.Equal(((ApplicationUser?)null, true), await service.CheckLoginAsync(accounts.User.Email!, "password"));
        accounts.PasswordResult = SignInResult.Success;
        Assert.Same(accounts.User, (await service.CheckLoginAsync(accounts.User.Email!, "password")).User);
    }

    [Fact]
    public async Task RotationStoresHashesAndCapsSlidingExpiryAtSessionDeadline()
    {
        var store = new TokenFake();
        var accounts = new AccountFake();
        var clock = new ManualClock(Now);
        var service = RefreshService(store, accounts, clock);
        var token = await service.CreateAsync(accounts.User);

        var saved = Assert.Single(store.Tokens);
        Assert.Equal(86, token.Token.Length);
        Assert.Equal(64, saved.TokenHash.Length);
        Assert.Equal(RefreshTokenGenerator.HashToken(token.Token), saved.TokenHash);
        Assert.NotEqual(token.Token, saved.TokenHash);
        Assert.Equal(Now.AddDays(2).UtcDateTime, token.ExpiresAtUtc);
        Assert.Equal(Now.AddDays(3).UtcDateTime, token.SessionExpiresAtUtc);

        clock.UtcNow = Now.AddDays(1).AddHours(12);
        var replacement = await service.RotateAsync(token.Token);
        Assert.NotNull(replacement);
        Assert.NotEqual(token.Token, replacement.Token);
        Assert.Equal(token.SessionExpiresAtUtc, replacement.ExpiresAtUtc);
        Assert.Null(await service.RotateAsync(token.Token));
        Assert.All(store.Tokens, item => Assert.NotNull(item.RevokedAtUtc));
        Assert.Null(await service.RotateAsync(replacement.Token));
    }

    [Theory]
    [InlineData("sliding")]
    [InlineData("absolute")]
    [InlineData("stamp")]
    [InlineData("eligibility")]
    [InlineData("logout")]
    public async Task RefreshRejectsInvalidSessions(string reason)
    {
        var store = new TokenFake();
        var accounts = new AccountFake();
        var clock = new ManualClock(Now);
        var service = RefreshService(store, accounts, clock);
        var token = await service.CreateAsync(accounts.User);

        switch (reason)
        {
            case "sliding": clock.UtcNow = Now.AddDays(2); break;
            case "absolute": clock.UtcNow = Now.AddDays(3); break;
            case "stamp": accounts.Stamp = "changed"; break;
            case "eligibility": accounts.CanRefresh = false; break;
            case "logout": await service.RevokeAsync(token.Token); break;
        }

        Assert.Null(await service.RotateAsync(token.Token));
        Assert.Equal(reason != "sliding", Assert.Single(store.Tokens).RevokedAtUtc.HasValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RotationRetriesConcurrencyFailuresWithoutLeavingPartialWrites(int failures)
    {
        var store = new TokenFake();
        var accounts = new AccountFake();
        var service = RefreshService(store, accounts, new ManualClock(Now));
        var token = await service.CreateAsync(accounts.User);
        store.FailuresRemaining = failures;

        if (failures == 3)
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.RotateAsync(token.Token));
            Assert.Null(Assert.Single(store.Tokens).RevokedAtUtc);
            Assert.Equal(2, store.TrackingResets);
        }
        else
        {
            Assert.NotNull(await service.RotateAsync(token.Token));
            Assert.Equal(2, store.Tokens.Count);
            Assert.Equal(1, store.TrackingResets);
        }
    }

    private static RegisterDto Profile() => new()
    {
        Email = "user@example.test", Password = "test-password1", FirstName = "First",
        LastName = "Last", PhoneNumber = "5550100"
    };

    private static RefreshTokenService RefreshService(TokenFake store, AccountFake accounts, TimeProvider clock) =>
        new(store, Options.Create(new JwtOptions
        {
            RefreshTokenExpirationDays = 2, AbsoluteSessionExpirationDays = 3
        }), accounts, clock, NullLogger<RefreshTokenService>.Instance);

    private sealed class AccountFake : IAccountAccess
    {
        public ApplicationUser User { get; } = new() { Id = Guid.NewGuid(), Email = "user@example.test" };
        public SignInResult PasswordResult { get; set; } = SignInResult.Success;
        public string Stamp { get; set; } = "stamp";
        public bool CanRefresh { get; set; } = true;
        public Task<ApplicationUser?> FindAsync(string email) => Task.FromResult(User.Email == email ? User : null);
        public Task<SignInResult> CheckPasswordAsync(ApplicationUser user, string password) => Task.FromResult(PasswordResult);
        public Task<bool> CanRefreshAsync(ApplicationUser user) => Task.FromResult(CanRefresh);
        public Task<string> GetSecurityStampAsync(ApplicationUser user) => Task.FromResult(Stamp);
        public Task<IList<string>> GetRolesAsync(ApplicationUser user) => Task.FromResult<IList<string>>(["Student"]);
    }

    private sealed class RegistrationFake : IRegistrationStore
    {
        public bool FailCreate { get; init; }
        public bool FailRole { get; init; }
        public bool FailProfile { get; init; }
        public bool ProfileWritten { get; private set; }
        public List<(string?, string?, RegistrationRole)> Registrations { get; } = [];
        private string? _email;
        private string? _role;
        private RegistrationRole _profile;

        public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) =>
            Task.FromResult<IDbContextTransaction>(new FakeTransaction(() => Registrations.Add((_email, _role, _profile))));

        public Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            _email = user.Email;
            return Task.FromResult(FailCreate ? IdentityResult.Failed(new IdentityError()) : IdentityResult.Success);
        }

        public Task<IdentityResult> AddRoleAsync(ApplicationUser user, string role)
        {
            _role = role;
            return Task.FromResult(FailRole ? IdentityResult.Failed(new IdentityError()) : IdentityResult.Success);
        }

        public Task SaveProfileAsync(ApplicationUser user, RegisterDto profile, RegistrationRole role, CancellationToken ct)
        {
            if (FailProfile)
                throw new InvalidOperationException("Write failed.");

            ProfileWritten = true;
            _profile = role;
            return Task.CompletedTask;
        }
    }

    private sealed class TokenFake : IRefreshTokenStore
    {
        public List<RefreshToken> Tokens { get; } = [];
        public int FailuresRemaining { get; set; }
        public int TrackingResets { get; private set; }

        public Task<IDbContextTransaction> BeginRotationAsync(CancellationToken ct)
        {
            var snapshot = Tokens.Select(t => (Token: t, t.RevokedAtUtc, t.ReplacedByTokenId)).ToArray();
            return Task.FromResult<IDbContextTransaction>(new FakeTransaction(() => { }, () =>
            {
                Tokens.Clear();
                foreach (var saved in snapshot)
                {
                    saved.Token.RevokedAtUtc = saved.RevokedAtUtc;
                    saved.Token.ReplacedByTokenId = saved.ReplacedByTokenId;
                    Tokens.Add(saved.Token);
                }
            }));
        }

        public Task<RefreshToken?> FindAsync(string hash, CancellationToken ct)
        {
            var item = Tokens.SingleOrDefault(t => t.TokenHash == hash);
            if (item is not null)
                item.User = new ApplicationUser { Id = item.UserId };

            return Task.FromResult(item);
        }

        public Task<Guid?> FindFamilyAsync(string hash, CancellationToken ct) =>
            Task.FromResult(Tokens.SingleOrDefault(t => t.TokenHash == hash)?.FamilyId);

        public Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct)
        {
            var active = Tokens.Where(t => t.FamilyId == familyId && t.RevokedAtUtc is null).ToArray();
            foreach (var token in active)
                token.RevokedAtUtc = revokedAtUtc;

            return Task.FromResult(active.Length);
        }

        public Task SaveAsync(RefreshToken token, CancellationToken ct)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new DbUpdateConcurrencyException();
            }

            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public void ClearTracking() => TrackingResets++;
    }

    private sealed class FakeTransaction(Action commit, Action? rollback = null) : IDbContextTransaction
    {
        private bool _committed;
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { commit(); _committed = true; }
        public Task CommitAsync(CancellationToken ct = default) { Commit(); return Task.CompletedTask; }
        public void Rollback() => rollback?.Invoke();
        public Task RollbackAsync(CancellationToken ct = default) { Rollback(); return Task.CompletedTask; }
        public void Dispose() { if (!_committed) Rollback(); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

internal sealed class ManualClock(DateTimeOffset value) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = value;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
