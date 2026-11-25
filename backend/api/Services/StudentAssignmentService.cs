using Microsoft.AspNetCore.Identity;
using PolskiYol.Features.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace PolskiYol.Services;

public enum StudentAssignmentResult { Assigned, StudentMissing, TeacherMissing }

public interface IStudentAssignmentStore
{
    Task<int?> FindStudentAsync(string? userName, CancellationToken ct);
    Task<int?> FindTeacherAsync(string? userName, CancellationToken ct);
    Task<IDbContextTransaction> BeginAsync(CancellationToken ct);
    Task<bool> UpdateTeacherAsync(int studentId, int teacherId, CancellationToken ct);
    Task<bool> ConversationExistsAsync(int studentId, int teacherId, CancellationToken ct);
    Task CreateConversationAsync(int studentId, int teacherId, CancellationToken ct);
}

public sealed class StudentAssignmentService(
    IStudentAssignmentStore store, ILookupNormalizer normalizer, RealtimeNotifications? notifications = null)
{
    public async Task<StudentAssignmentResult> AssignAsync(string studentEmail, string teacherEmail, CancellationToken ct)
    {
        var student = await store.FindStudentAsync(normalizer.NormalizeName(studentEmail), ct);
        if (student is null)
            return StudentAssignmentResult.StudentMissing;

        var teacher = await store.FindTeacherAsync(normalizer.NormalizeName(teacherEmail), ct);
        if (teacher is null)
            return StudentAssignmentResult.TeacherMissing;

        await using var transaction = await store.BeginAsync(ct);

        // Update first so requests for the same student serialize before looking up the conversation.
        if (!await store.UpdateTeacherAsync(student.Value, teacher.Value, ct))
            return StudentAssignmentResult.StudentMissing;

        if (!await store.ConversationExistsAsync(student.Value, teacher.Value, ct))
            await store.CreateConversationAsync(student.Value, teacher.Value, ct);

        await transaction.CommitAsync(ct);
        if (notifications is not null)
            await notifications.TeacherAssignmentChangedAsync(student.Value);

        return StudentAssignmentResult.Assigned;
    }
}

public sealed class StudentAssignmentStore(AppDbContext context) : IStudentAssignmentStore
{
    public Task<int?> FindStudentAsync(string? userName, CancellationToken ct) =>
        context.Students.Where(s => s.User.NormalizedUserName == userName).Select(s => (int?)s.Id).SingleOrDefaultAsync(ct);

    public Task<int?> FindTeacherAsync(string? userName, CancellationToken ct) =>
        context.Teachers.Where(t => t.User.NormalizedUserName == userName).Select(t => (int?)t.Id).SingleOrDefaultAsync(ct);

    public Task<IDbContextTransaction> BeginAsync(CancellationToken ct) =>
        context.Database.BeginTransactionAsync(ct);

    public async Task<bool> UpdateTeacherAsync(int studentId, int teacherId, CancellationToken ct) =>
        await context.Students.Where(student => student.Id == studentId)
            .ExecuteUpdateAsync(update => update.SetProperty(student => student.TeacherId, (int?)teacherId), ct) > 0;

    public Task<bool> ConversationExistsAsync(int studentId, int teacherId, CancellationToken ct) =>
        context.Conversations.AnyAsync(
            c => c.StudentId == studentId && c.TeacherId == teacherId, ct);

    public async Task CreateConversationAsync(int studentId, int teacherId, CancellationToken ct)
    {
        context.Conversations.Add(new Conversation { StudentId = studentId, TeacherId = teacherId });
        await context.SaveChangesAsync(ct);
    }
}
