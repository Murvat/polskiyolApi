using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using PolskiYol.Features;
using PolskiYol.Features.Materials;
using PolskiYol.Services;
using Xunit;

namespace PolskiYol.Tests.Unit;

public sealed class FeatureServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AssignmentReusesConversationAndKeepsPreviousHistory()
    {
        var store = new AssignmentFake();
        var service = new StudentAssignmentService(store, new UpperInvariantLookupNormalizer());

        Assert.Equal(StudentAssignmentResult.StudentMissing, await service.AssignAsync("no@example.test", "first@example.test", default));
        Assert.Equal(StudentAssignmentResult.TeacherMissing, await service.AssignAsync("student@example.test", "no@example.test", default));
        Assert.Equal(StudentAssignmentResult.Assigned, await service.AssignAsync("student@example.test", "first@example.test", default));
        Assert.Equal(StudentAssignmentResult.Assigned, await service.AssignAsync("student@example.test", "FIRST@example.test", default));
        Assert.Single(store.Conversations);
        Assert.Equal(StudentAssignmentResult.Assigned, await service.AssignAsync("student@example.test", "second@example.test", default));
        Assert.Equal(3, store.TeacherId);
        Assert.Equal(2, store.Conversations.Count);
    }

    [Fact]
    public void ProfileAccessFollowsTheAssignedTeacherAndIdentity()
    {
        var teacher = new Teacher { Id = 2, UserId = Guid.NewGuid() };
        var student = new Student { Id = 1, UserId = Guid.NewGuid(), TeacherId = teacher.Id };

        Assert.Single(ProfileAccess.AssignedTeacher(new[] { teacher }.AsQueryable(), student.TeacherId, teacher.UserId));
        Assert.Single(ProfileAccess.AssignedStudent(new[] { student }.AsQueryable(), teacher.Id, student.UserId));
        Assert.True(ProfileAccess.IsOwnProfile(Principal("Student"), "Student", student.UserId, student.UserId));
        Assert.False(ProfileAccess.IsOwnProfile(Principal("Student"), "Teacher", teacher.UserId, student.UserId));
        student.TeacherId = 3;
        Assert.Empty(ProfileAccess.AssignedStudent(new[] { student }.AsQueryable(), teacher.Id, student.UserId));
    }

    [Fact]
    public void MessagingUsesCurrentMembershipAndAttributesSenders()
    {
        var teacher = new Teacher { Id = 2, UserId = Guid.NewGuid() };
        var student = new Student { Id = 1, UserId = Guid.NewGuid(), TeacherId = teacher.Id };
        var conversation = new Conversation { Id = 5, TeacherId = teacher.Id, Teacher = teacher, Student = student };
        var conversations = new[] { conversation }.AsQueryable();

        Assert.Single(MessagingService.GetAccessibleConversations(student.UserId, Principal("Student"), conversations));
        Assert.Single(MessagingService.GetAccessibleConversations(teacher.UserId, Principal("Teacher"), conversations));
        Assert.Empty(MessagingService.GetAccessibleConversations(Guid.NewGuid(), Principal("Teacher"), conversations));

        Assert.Null(MessagingService.CreateMessage(5, "   ", Principal("Teacher"), Now));
        var message = MessagingService.CreateMessage(5, " Hello ", Principal("Student"), Now);
        Assert.NotNull(message);
        Assert.Equal("Hello", message.Content);
        Assert.Equal(MessageSenderType.Student, message.SenderType);
        Assert.Equal(MessageSenderType.Teacher, MessagingService.CreateMessage(5, "Hi", Principal("Teacher"), Now)!.SenderType);

        student.TeacherId = 3;
        Assert.Empty(MessagingService.GetAccessibleConversations(teacher.UserId, Principal("Teacher"), conversations));
        Assert.Empty(MessagingService.GetAccessibleConversations(student.UserId, Principal("Student"), conversations));
    }

    [Fact]
    public void FeedCreationAndVisibilityFollowTeacherOwnership()
    {
        var teacher = new Teacher { Id = 2, UserId = Guid.NewGuid() };
        var student = new Student { Id = 1, UserId = Guid.NewGuid() };
        teacher.Students.Add(student);
        var request = new FeedPostAddRequest { Title = "Title", Content = new string('a', 4000) };
        Assert.True(Valid(request));

        var post = FeedPostService.CreatePost(request, teacher.Id, Now);
        post.Teacher = teacher;
        var posts = new[] { post }.AsQueryable();

        Assert.Equal(teacher.Id, post.TeacherId);
        Assert.Equal(Now, post.CreatedAt);
        Assert.Single(FeedPostService.GetAccessiblePosts(teacher.UserId, Principal("Teacher"), posts));
        Assert.Single(FeedPostService.GetAccessiblePosts(student.UserId, Principal("Student"), posts));
        Assert.Empty(FeedPostService.GetAccessiblePosts(Guid.NewGuid(), Principal("Teacher"), posts));
        request.Content += "a";
        Assert.False(Valid(request));
    }

    [Fact]
    public void CursorHeaderUsesLastReturnedIdAndOnlyAppearsWhenMoreItemsExist()
    {
        var page = new List<int> { 5, 4, 3 };
        var context = new DefaultHttpContext();
        CursorPage.SetNextCursor(page, 2, id => id, context.Response);
        Assert.Equal(new[] { 5, 4 }, page);
        Assert.Equal("4", context.Response.Headers["X-Next-Before-Id"].ToString());

        var last = new DefaultHttpContext();
        CursorPage.SetNextCursor(page, 2, id => id, last.Response);
        Assert.False(last.Response.Headers.ContainsKey("X-Next-Before-Id"));
    }

    [Theory]
    [InlineData(10 * 1024 * 1024, true)]
    [InlineData(10 * 1024 * 1024 + 1, false)]
    public void PdfUploadLimitIsValidatedAtTheBoundary(int length, bool valid)
    {
        var file = new FormFile(Stream.Null, 0, length, "file", "file.pdf");
        Assert.Equal(valid, Valid(new MaterialUploadRequest { File = file }));
    }

    [Fact]
    public async Task MaterialServicesValidateSignaturesAndCurrentAccess()
    {
        using var pdf = new MemoryStream("%PDF-1.5"u8.ToArray());
        var materialFile = new FormFile(pdf, 0, pdf.Length, "file", @"C:\fakepath\lesson.pdf");
        Assert.Equal(("lesson.pdf", (string?)null), await MaterialService.ValidatePdfAsync(materialFile, default));

        using var invalid = new MemoryStream("not a PDF"u8.ToArray());
        Assert.NotNull((await MaterialService.ValidatePdfAsync(
            new FormFile(invalid, 0, invalid.Length, "file", "lesson.pdf"), default)).Error);

        using var jpeg = new MemoryStream([0xff, 0xd8, 0xff, 0x00]);
        var cover = await MaterialService.ValidateCoverAsync(new FormFile(jpeg, 0, jpeg.Length, "file", "cover.jpeg"), default);
        Assert.Equal("image/jpeg", cover.ContentType);
        Assert.Null(cover.Error);
        Assert.NotNull((await MaterialService.ValidateCoverAsync(
            new FormFile(Stream.Null, 0, 5 * 1024 * 1024 + 1, "file", "cover.jpeg"), default)).Error);

        var teacher = new Teacher { Id = 2, UserId = Guid.NewGuid() };
        var student = new Student { UserId = Guid.NewGuid(), TeacherId = teacher.Id };
        var items = new[] { new Material { Teacher = teacher, TeacherId = teacher.Id } }.AsQueryable();
        var students = new[] { student }.AsQueryable();
        Assert.Single(MaterialService.GetAccessibleMaterials(student.UserId, Principal("Student"), items, students));
        Assert.Empty(MaterialService.GetAccessibleMaterials(Guid.NewGuid(), Principal("Student"), items, students));
    }

    [Theory]
    [InlineData(503, 2, 3)]
    [InlineData(503, 3, 3)]
    [InlineData(403, 3, 1)]
    public async Task MaterialStorageCompensatesFailedWritesAndBoundsRetries(int status, int failures, int expectedCalls)
    {
        var cleaned = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => MaterialService.SaveWithCleanupAsync(
            () => Task.FromException(new InvalidOperationException()), () =>
            {
                cleaned = true;
                return Task.CompletedTask;
            }));
        Assert.True(cleaned);

        using var cleanup = new MaterialBlobCleanup(NullLogger<MaterialBlobCleanup>.Instance);
        var calls = 0;
        await cleanup.RetryAsync(
            _ => ++calls <= failures ? Task.FromException(new RequestFailedException(status, "fake")) : Task.CompletedTask,
            (_, _) => Task.CompletedTask, default);
        Assert.Equal(expectedCalls, calls);
    }

    [Fact]
    public void LessonsUseMondayBoundariesAndOnlyAssignedStudents()
    {
        var (start, end) = LessonService.GetWeek(Now);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(start.AddDays(7), end);

        var students = new[] { new Student { Id = 1, TeacherId = 2 }, new Student { Id = 3, TeacherId = 4 } };
        Assert.Equal(1, Assert.Single(LessonService.EligibleStudents(students.AsQueryable(), [1, 3], 2)).Id);

        var request = new LessonAddRequest { StartsAt = Now, Topic = "Topic", DurationMinutes = 480, StudentIds = [1] };
        Assert.True(Valid(request));
        request.DurationMinutes = 481;
        Assert.False(Valid(request));
    }

    [Fact]
    public void PaymentsValidatePrecisionAndCalculateStatusAndTotals()
    {
        var today = DateOnly.FromDateTime(Now);
        var request = new PaymentAddRequest { StudentId = 1, Year = 2026, Month = 11, DueDate = today, ExpectedAmount = 1.001m };
        Assert.False(Valid(request));
        request.ExpectedAmount = 1.01m;
        Assert.True(Valid(request));
        Assert.False(PaymentService.ValidPeriod(2026, 13));

        var paid = new Payment { StudentId = 1, Year = 2026, Month = 11, DueDate = today.AddDays(-1), ExpectedAmount = 10m };
        var overdue = new Payment { StudentId = 2, Year = 2026, Month = 11, DueDate = today.AddDays(-1), ExpectedAmount = 20m };
        var due = new Payment { StudentId = 1, Year = 2026, Month = 12, DueDate = today, ExpectedAmount = 30m };
        var payments = new[] { paid, overdue, due }.AsQueryable();

        Assert.Same(paid, Assert.Single(PaymentService.ForMonth(payments, 1, 2026, 11)));
        Assert.True(PaymentService.MarkPaid(paid, Now));
        Assert.False(PaymentService.MarkPaid(paid, Now.AddMinutes(1)));
        Assert.Equal(Now, paid.PaidAt);
        Assert.Equal(PaymentStatus.Delayed, PaymentResponse.GetStatus(overdue, today));
        Assert.Equal(PaymentStatus.Due, PaymentResponse.GetStatus(due, today));

        var summary = Assert.Single(PaymentService.Summaries(payments, today));
        Assert.Equal(60m, summary.TotalExpected);
        Assert.Equal(10m, summary.TotalPaid);
        Assert.Equal(20m, summary.TotalDelayed);
        Assert.Equal((1, 1, 1), (summary.PaidCount, summary.DueCount, summary.DelayedCount));
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "unit"));

    private static bool Valid(object request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);

    private sealed class AssignmentFake : IStudentAssignmentStore
    {
        public HashSet<(int, int)> Conversations { get; } = [];
        public int? TeacherId { get; private set; }

        public Task<int?> FindStudentAsync(string? name, CancellationToken ct) =>
            Task.FromResult<int?>(name == "STUDENT@EXAMPLE.TEST" ? 1 : null);

        public Task<int?> FindTeacherAsync(string? name, CancellationToken ct) =>
            Task.FromResult<int?>(name switch { "FIRST@EXAMPLE.TEST" => 2, "SECOND@EXAMPLE.TEST" => 3, _ => null });

        public Task<bool> AssignAsync(int studentId, int teacherId, CancellationToken ct)
        {
            TeacherId = teacherId;
            Conversations.Add((studentId, teacherId));
            return Task.FromResult(true);
        }
    }
}
