using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using PolskiYol.Data;
using PolskiYol.Features.Materials;
using PolskiYol.Services;

namespace PolskiYol.Tests.Infrastructure;

internal sealed class MemoryAccounts(MemoryApiData data) : IAccountAccess, IRegistrationStore, IIdentityInitializer
{
    public const string Password = "Test-phase-two-1";
    public const string PrivateFailure = "private fixture error details";
    private readonly Dictionary<string, (ApplicationUser User, string Password, string Role)> _users =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _password;
    private string? _role;
    private Action? _commitRegistration;

    public Task InitializeAsync() => Task.CompletedTask;

    public ApplicationUser Seed(string email, string role)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email };
        _users.Add(email, (user, Password, role));
        return user;
    }

    public Task<ApplicationUser?> FindAsync(string email)
    {
        if (email == "throw@example.test")
            throw new InvalidOperationException(PrivateFailure);

        return Task.FromResult(_users.TryGetValue(email, out var entry) ? entry.User : null);
    }

    public Task<ApplicationUser?> FindByIdAsync(string id) =>
        Task.FromResult(_users.Values.Select(entry => entry.User).SingleOrDefault(user => user.Id.ToString() == id));

    public Task<SignInResult> CheckPasswordAsync(ApplicationUser user, string password) =>
        Task.FromResult(_users[user.Email!].Password == password ? SignInResult.Success : SignInResult.Failed);

    public Task<bool> CanRefreshAsync(ApplicationUser user) => Task.FromResult(true);
    public Task<string> GetSecurityStampAsync(ApplicationUser user) => Task.FromResult("test-stamp");
    public Task<IList<string>> GetRolesAsync(ApplicationUser user) =>
        Task.FromResult<IList<string>>([_users[user.Email!].Role]);

    public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) =>
        Task.FromResult<IDbContextTransaction>(new MemoryTransaction(() => _commitRegistration!()));

    public Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
    {
        _password = password;
        return Task.FromResult(_users.ContainsKey(user.Email!)
            ? IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail" })
            : IdentityResult.Success);
    }

    public Task<IdentityResult> AddRoleAsync(ApplicationUser user, string role)
    {
        _role = role;
        return Task.FromResult(IdentityResult.Success);
    }

    public Task SaveProfileAsync(ApplicationUser user, RegisterDto profile, RegistrationRole role, CancellationToken ct)
    {
        var password = _password!;
        var roleName = _role!;
        _commitRegistration = () =>
        {
            _users.Add(user.Email!, (user, password, roleName));
            if (role == RegistrationRole.Student)
                data.Add(new Student
                {
                    User = user, UserId = user.Id, FirstName = profile.FirstName,
                    LastName = profile.LastName, PhoneNumber = profile.PhoneNumber
                });
            else
                data.Add(new Teacher
                {
                    User = user, UserId = user.Id, FirstName = profile.FirstName,
                    LastName = profile.LastName, PhoneNumber = profile.PhoneNumber
                });
        };
        return Task.CompletedTask;
    }
}

internal sealed class MemoryAssignments(MemoryApiData data) : IStudentAssignmentStore
{
    public Task<int?> FindStudentAsync(string? userName, CancellationToken ct) =>
        Task.FromResult(data.Rows<Student>().Where(s => Match(s.User, userName)).Select(s => (int?)s.Id).SingleOrDefault());

    public Task<int?> FindTeacherAsync(string? userName, CancellationToken ct) =>
        Task.FromResult(data.Rows<Teacher>().Where(t => Match(t.User, userName)).Select(t => (int?)t.Id).SingleOrDefault());

    public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) =>
        Task.FromResult<IDbContextTransaction>(new MemoryTransaction());

    public Task<bool> UpdateTeacherAsync(int studentId, int teacherId, CancellationToken ct)
    {
        var student = data.Rows<Student>().Single(s => s.Id == studentId);
        var teacher = data.Rows<Teacher>().Single(t => t.Id == teacherId);
        student.Teacher?.Students.Remove(student);
        student.Teacher = teacher;
        student.TeacherId = teacher.Id;
        teacher.Students.Add(student);
        return Task.FromResult(true);
    }

    public Task<bool> ConversationExistsAsync(int studentId, int teacherId, CancellationToken ct) =>
        Task.FromResult(data.Rows<Conversation>().Any(c => c.StudentId == studentId && c.TeacherId == teacherId));

    public Task CreateConversationAsync(int studentId, int teacherId, CancellationToken ct)
    {
        data.Add(new Conversation { StudentId = studentId, TeacherId = teacherId });
        return Task.CompletedTask;
    }

    private static bool Match(ApplicationUser user, string? normalized) =>
        string.Equals(user.UserName, normalized, StringComparison.OrdinalIgnoreCase);
}

internal sealed class MemoryRefreshTokens(MemoryAccounts accounts) : IRefreshTokenStore
{
    private readonly List<RefreshToken> _tokens = [];

    public Task<IDbContextTransaction> BeginRotationAsync(CancellationToken ct) =>
        Task.FromResult<IDbContextTransaction>(new MemoryTransaction());

    public Task<RefreshToken?> FindAsync(string hash, CancellationToken ct) =>
        Task.FromResult(_tokens.SingleOrDefault(token => token.TokenHash == hash));

    public Task<Guid?> FindFamilyAsync(string hash, CancellationToken ct) =>
        Task.FromResult(_tokens.SingleOrDefault(token => token.TokenHash == hash)?.FamilyId);

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct)
    {
        var active = _tokens.Where(token => token.FamilyId == familyId && token.RevokedAtUtc is null).ToArray();
        foreach (var token in active)
            token.RevokedAtUtc = revokedAtUtc;

        return Task.FromResult(active.Length);
    }

    public async Task SaveAsync(RefreshToken token, CancellationToken ct)
    {
        token.User = (await accounts.FindByIdAsync(token.UserId.ToString()))!;
        _tokens.Add(token);
    }

    public void ClearTracking() { }
}

internal sealed class MemoryBlobs : IMaterialBlobStore
{
    private readonly Dictionary<string, byte[]> _items = [];

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        _items.Add(key, copy.ToArray());
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream?>(_items.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteOrQueueAsync(string? key)
    {
        if (key is not null)
            _items.Remove(key);

        return Task.CompletedTask;
    }
}

internal sealed class MemoryTransaction(Action? onCommit = null) : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();
    public void Commit() => onCommit?.Invoke();
    public Task CommitAsync(CancellationToken ct = default) { Commit(); return Task.CompletedTask; }
    public void Rollback() { }
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
