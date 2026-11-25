using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PolskiYol.Services;

public enum StudentAssignmentResult { Assigned, StudentMissing, TeacherMissing }

public interface IStudentAssignmentStore
{
    Task<int?> FindStudentAsync(string? userName, CancellationToken ct);
    Task<int?> FindTeacherAsync(string? userName, CancellationToken ct);
    Task<bool> AssignAsync(int studentId, int teacherId, CancellationToken ct);
}

public sealed class StudentAssignmentService(IStudentAssignmentStore store, ILookupNormalizer normalizer)
{
    public async Task<StudentAssignmentResult> AssignAsync(string studentEmail, string teacherEmail, CancellationToken ct)
    {
        var student = await store.FindStudentAsync(normalizer.NormalizeName(studentEmail), ct);
        if (student is null)
            return StudentAssignmentResult.StudentMissing;

        var teacher = await store.FindTeacherAsync(normalizer.NormalizeName(teacherEmail), ct);
        if (teacher is null)
            return StudentAssignmentResult.TeacherMissing;

        return await store.AssignAsync(student.Value, teacher.Value, ct)
            ? StudentAssignmentResult.Assigned : StudentAssignmentResult.StudentMissing;
    }
}

public sealed class StudentAssignmentStore(AppDbContext context) : IStudentAssignmentStore
{
    public Task<int?> FindStudentAsync(string? userName, CancellationToken ct) =>
        context.Students.Where(s => s.User.NormalizedUserName == userName).Select(s => (int?)s.Id).SingleOrDefaultAsync(ct);

    public Task<int?> FindTeacherAsync(string? userName, CancellationToken ct) =>
        context.Teachers.Where(t => t.User.NormalizedUserName == userName).Select(t => (int?)t.Id).SingleOrDefaultAsync(ct);

    public async Task<bool> AssignAsync(int studentId, int teacherId, CancellationToken ct)
    {
        await using var transaction =
            await context.Database.BeginTransactionAsync(ct);

        // Update first so assignment requests for the same student serialize
        // before they check whether the conversation already exists.
        var updated = await context.Students
            .Where(s => s.Id == studentId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.TeacherId, (int?)teacherId),
                ct);

        if (updated == 0)
            return false;

        var conversationExists = await context.Conversations.AnyAsync(
            c => c.StudentId == studentId && c.TeacherId == teacherId,
            ct);

        if (!conversationExists)
        {
            context.Conversations.Add(new Conversation
            {
                StudentId = studentId,
                TeacherId = teacherId
            });

            await context.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}
