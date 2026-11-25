using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using PolskiYol.DTOs;
using PolskiYol.Tests.Infrastructure;
using Xunit;

namespace PolskiYol.Tests.Http;

public sealed partial class OfflineApiTests
{
    [Fact]
    public async Task RegistrationLoginRefreshLogoutAndProfilesUseTheHttpContract()
    {
        await using var host = new MemoryApiHost();
        using var anonymous = host.Connect();
        using var denied = await anonymous.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var invalid = await anonymous.PostAsJsonAsync("/api/auth/register/student", new { email = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, "Email", StringComparison.OrdinalIgnoreCase));

        using var student = host.Connect();
        await Register(student, "student", "student@example.test");
        using var badPassword = await student.PostAsJsonAsync("/api/auth/login", new { email = "student@example.test", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);

        using var login = await student.PostAsJsonAsync("/api/auth/login", new { email = "student@example.test", password = MemoryAccounts.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var auth = await Read<AuthResponse>(login);
        student.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var profile = await student.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Contains("student@example.test", await profile.Content.ReadAsStringAsync());
        var studentProfile = await student.GetFromJsonAsync<StudentResponse>("/api/students/me");
        Assert.NotNull(studentProfile);
        Assert.Null(studentProfile.TeacherId);

        using var refresh = await student.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.NotEqual(cookie, Assert.Single(refresh.Headers.GetValues("Set-Cookie")));
        using var logout = await student.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await student.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task LoginReturns429AfterTheConfiguredLimit()
    {
        await using var host = new MemoryApiHost();
        using var client = host.Connect();

        for (var i = 0; i < 10; i++)
        {
            using var denied = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@example.test", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }

        using var throttled = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@example.test", password = "wrong" });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotNull(throttled.Headers.RetryAfter);
    }

    [Fact]
    public async Task AssignmentAndMessagingEnforceCurrentAccessAndReturnCursors()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);

        using var rejected = await classroom.Student.PutAsJsonAsync("/api/students/teacher-assignment",
            new { studentEmail = Classroom.StudentEmail, teacherEmail = Classroom.TeacherEmail });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        var assigned = await classroom.Student.GetFromJsonAsync<StudentResponse>("/api/students/me");
        Assert.NotNull(assigned);
        var teacher = await classroom.Student.GetFromJsonAsync<TeacherResponse>($"/api/teachers/{assigned.TeacherId}");
        Assert.NotNull(teacher);
        using var teacherRead = await classroom.Teacher.GetAsync($"/api/students/{assigned.Id}");
        Assert.Equal(HttpStatusCode.OK, teacherRead.StatusCode);
        using var outsiderRead = await classroom.OtherTeacher.GetAsync($"/api/students/{assigned.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, outsiderRead.StatusCode);

        var conversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
        var messages = $"/api/conversations/{conversation.Id}/messages";
        using var denied = await classroom.OtherTeacher.GetAsync(messages);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        foreach (var content in new[] { "First", "Second", "Third" })
        {
            using var sent = await classroom.Student.PostAsJsonAsync(messages, new { content });
            Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        }

        using var latest = await classroom.Teacher.GetAsync(messages + "?limit=2");
        Assert.Equal(new[] { "Second", "Third" }, (await Read<MessageResponse[]>(latest)).Select(m => m.Content));
        var cursor = Assert.Single(latest.Headers.GetValues("X-Next-Before-Id"));
        var older = await classroom.Teacher.GetFromJsonAsync<MessageResponse[]>(messages + $"?limit=2&beforeId={cursor}");
        Assert.Equal("First", Assert.Single(older!).Content);

        await classroom.Assign(Classroom.OtherTeacherEmail);
        using var old = await classroom.Student.GetAsync(messages);
        using var oldSend = await classroom.Teacher.PostAsJsonAsync(messages, new { content = "After reassignment" });
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldSend.StatusCode);
        Assert.Single((await classroom.OtherTeacher.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
    }

    [Fact]
    public async Task FeedEnforcesOwnershipValidationAndPaging()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);

        using var invalid = await classroom.Teacher.PostAsJsonAsync("/api/feed-posts", new { title = "Title", content = new string('a', 4001) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        foreach (var title in new[] { "First", "Second" })
        {
            using var created = await classroom.Teacher.PostAsJsonAsync("/api/feed-posts", new { title, content = "Content" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var page = await classroom.Student.GetAsync("/api/feed-posts?limit=1");
        var latest = Assert.Single(await Read<FeedPostResponse[]>(page));
        Assert.Equal("Second", latest.Title);
        var cursor = Assert.Single(page.Headers.GetValues("X-Next-Before-Id"));
        var older = await classroom.Student.GetFromJsonAsync<FeedPostResponse[]>($"/api/feed-posts?beforeId={cursor}");
        Assert.Equal("First", Assert.Single(older!).Title);

        using var denied = await classroom.OtherTeacher.DeleteAsync($"/api/feed-posts/{latest.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deleted = await classroom.Teacher.DeleteAsync($"/api/feed-posts/{latest.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task MaterialsValidateUploadsAndServeOnlyAuthorizedPdfAndCoverDownloads()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        using var invalid = await Upload(classroom.Teacher, "/api/materials", "bad.pdf", "bad-data"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var pdf = "%PDF-1.5 fixture data"u8.ToArray();
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        using var created = await Upload(classroom.Teacher, "/api/materials", "lesson.pdf", pdf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var material = await Read<MaterialResponse>(created);
        Assert.False(material.HasCover);
        using var second = await Upload(classroom.Teacher, "/api/materials", "second.pdf", pdf);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var path = $"/api/materials/{material.Id}";
        using var cover = await Upload(classroom.Teacher, path + "/cover", "cover.png", png);
        Assert.Equal(HttpStatusCode.NoContent, cover.StatusCode);
        Assert.True((await classroom.Student.GetFromJsonAsync<MaterialResponse>(path))!.HasCover);
        using var page = await classroom.Student.GetAsync("/api/materials?limit=1");
        Assert.Single(await Read<MaterialResponse[]>(page));
        Assert.Single(page.Headers.GetValues("X-Next-Before-Id"));

        using var download = await classroom.Student.GetAsync(path + "/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
        using var image = await classroom.Student.GetAsync(path + "/cover");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());

        using var denied = await classroom.OtherTeacher.GetAsync(path + "/download");
        using var deniedCover = await classroom.OtherTeacher.GetAsync(path + "/cover");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedCover.StatusCode);
    }

    [Fact]
    public async Task LessonsAndPaymentsExerciseAccessAndPaymentConflict()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        var student = (await classroom.Student.GetFromJsonAsync<StudentResponse>("/api/students/me"))!;
        var lesson = new { durationMinutes = 45, topic = "Topic", startsAt = DateTime.Now, studentIds = new[] { student.Id } };

        using var badTeacher = await classroom.OtherTeacher.PostAsJsonAsync("/api/lessons", lesson);
        Assert.Equal(HttpStatusCode.BadRequest, badTeacher.StatusCode);
        using var createdLesson = await classroom.Teacher.PostAsJsonAsync("/api/lessons", lesson);
        Assert.Equal(HttpStatusCode.Created, createdLesson.StatusCode);
        var item = await Read<LessonResponse>(createdLesson);
        var lessonUrl = $"/api/lessons/{item.Id}";
        using var allowed = await classroom.Student.GetAsync(lessonUrl);
        using var denied = await classroom.OtherTeacher.GetAsync(lessonUrl);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var payment = new { studentId = student.Id, year = 2026, month = 11, expectedAmount = 50.25m, dueDate = "2026-11-30" };
        using var created = await classroom.Teacher.PostAsJsonAsync("/api/payments", payment);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var paymentItem = await Read<PaymentResponse>(created);
        using var duplicate = await classroom.Teacher.PostAsJsonAsync("/api/payments", payment);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var paid = await classroom.Teacher.PatchAsync($"/api/payments/{paymentItem.Id}/paid", null);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);
        var summary = await classroom.Student.GetFromJsonAsync<PaymentSummaryResponse>("/api/payments/summary?year=2026&month=11");
        Assert.NotNull(summary);
        Assert.Equal(50.25m, summary.TotalPaid);
        Assert.Equal(1, summary.PaidCount);
    }

    [Fact]
    public async Task UnexpectedFailuresReturnSanitizedProblemDetails()
    {
        await using var host = new MemoryApiHost();
        using var client = host.Connect();
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "throw@example.test", password = "irrelevant" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("traceId", body);
        Assert.DoesNotContain(MemoryAccounts.PrivateFailure, body);
    }

    [Fact]
    public async Task DuplicateRegistrationKeepsTheOriginalAccount()
    {
        await using var host = new MemoryApiHost();
        using var client = host.Connect();
        const string email = "student@example.test";
        await Register(client, "student", email);
        await Login(client, email);
        var before = await client.GetFromJsonAsync<StudentResponse>("/api/students/me");

        using var duplicate = await client.PostAsJsonAsync("/api/auth/register/student", new
        {
            email, password = "Different-password-2", firstName = "Other", lastName = "Name", phoneNumber = "5550101"
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var rejected = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Different-password-2" });
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        await Login(client, email);
        var after = await client.GetFromJsonAsync<StudentResponse>("/api/students/me");
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.FirstName, after.FirstName);
    }

    [Fact]
    public async Task AssignmentValidatesProfilesAndLeavesTheCurrentTeacherInPlaceAfterRejection()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        using var admin = host.Connect();
        await Login(admin, "admin@example.test");
        var before = await classroom.Student.GetFromJsonAsync<StudentResponse>("/api/students/me");
        Assert.NotNull(before);
        Assert.NotNull(before.TeacherId);

        using var invalid = await admin.PutAsJsonAsync("/api/students/teacher-assignment",
            new { studentEmail = "bad-email", teacherEmail = Classroom.TeacherEmail });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missingStudent = await admin.PutAsJsonAsync("/api/students/teacher-assignment",
            new { studentEmail = "missing@example.test", teacherEmail = Classroom.TeacherEmail });
        Assert.Equal(HttpStatusCode.NotFound, missingStudent.StatusCode);
        using var missingTeacher = await admin.PutAsJsonAsync("/api/students/teacher-assignment",
            new { studentEmail = Classroom.StudentEmail, teacherEmail = "missing@example.test" });
        Assert.Equal(HttpStatusCode.NotFound, missingTeacher.StatusCode);

        var after = await classroom.Student.GetFromJsonAsync<StudentResponse>("/api/students/me");
        Assert.Equal(before.TeacherId, after!.TeacherId);
    }

    [Theory]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=101", "limit")]
    [InlineData("?beforeId=0", "beforeId")]
    [InlineData("?beforeId=invalid", "beforeId")]
    public async Task ListEndpointsRejectInvalidPagingParameters(string query, string field)
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        var conversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);

        foreach (var path in new[] { "/api/feed-posts", "/api/materials", $"/api/conversations/{conversation.Id}/messages" })
        {
            using var response = await classroom.Student.GetAsync(path + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var details = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.NotNull(details);
            Assert.Contains(details.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task MessageLookupRespectsTheConversationAndHistoryReturnsAfterReassignmentBack()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        var oldConversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
        var oldMessages = $"/api/conversations/{oldConversation.Id}/messages";

        using var empty = await classroom.Student.PostAsJsonAsync(oldMessages, new { content = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var sent = await classroom.Student.PostAsJsonAsync(oldMessages, new { content = "Saved history" });
        var message = await Read<MessageResponse>(sent);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        await classroom.Assign(Classroom.OtherTeacherEmail);
        var currentConversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
        using var wrongConversation = await classroom.Student.GetAsync($"/api/conversations/{currentConversation.Id}/messages/{message.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongConversation.StatusCode);
        using var oldHistory = await classroom.Student.GetAsync(oldMessages);
        Assert.Equal(HttpStatusCode.NotFound, oldHistory.StatusCode);

        await classroom.Assign(Classroom.TeacherEmail);
        var restored = await classroom.Student.GetFromJsonAsync<MessageResponse>(oldMessages + $"/{message.Id}");
        Assert.NotNull(restored);
        Assert.Equal("Saved history", restored.Content);
    }

    [Fact]
    public async Task StudentMutationsReturnForbiddenAndInvalidTeacherPaymentReturnsFieldError()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        var student = (await classroom.Student.GetFromJsonAsync<StudentResponse>("/api/students/me"))!;
        var payment = new { studentId = student.Id, year = 2026, month = 11, expectedAmount = 10.001m, dueDate = "2026-11-30" };

        using var createPayment = await classroom.Student.PostAsJsonAsync("/api/payments", payment);
        using var createLesson = await classroom.Student.PostAsJsonAsync("/api/lessons", new { });
        using var createPost = await classroom.Student.PostAsJsonAsync("/api/feed-posts", new { });
        using var createMaterial = await Upload(classroom.Student, "/api/materials", "lesson.pdf", "%PDF-fixture"u8.ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, createPayment.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createLesson.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createPost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createMaterial.StatusCode);

        using var invalidPayment = await classroom.Teacher.PostAsJsonAsync("/api/payments", payment);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPayment.StatusCode);
        var details = await invalidPayment.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(details);
        Assert.Contains(details.Errors.Keys, key => string.Equals(key, "ExpectedAmount", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string path, string filename, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", filename);
        return await client.PostAsync(path, content);
    }

    private static async Task Register(HttpClient client, string role, string email)
    {
        using var response = await client.PostAsJsonAsync($"/api/auth/register/{role}", new
        {
            email, password = MemoryAccounts.Password, firstName = "Test", lastName = role, phoneNumber = "5550100"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task Login(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = MemoryAccounts.Password });
        var auth = await Read<AuthResponse>(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private sealed class Classroom(MemoryApiHost host) : IDisposable
    {
        public const string StudentEmail = "student@example.test";
        public const string TeacherEmail = "teacher@example.test";
        public const string OtherTeacherEmail = "other@example.test";

        public HttpClient Student { get; } = host.Connect();
        public HttpClient Teacher { get; } = host.Connect();
        public HttpClient OtherTeacher { get; } = host.Connect();
        private HttpClient Admin { get; } = host.Connect();

        public static async Task<Classroom> Create(MemoryApiHost host)
        {
            var classroom = new Classroom(host);
            try
            {
                await Register(classroom.Student, "student", StudentEmail);
                await Register(classroom.Teacher, "teacher", TeacherEmail);
                await Register(classroom.OtherTeacher, "teacher", OtherTeacherEmail);
                await Login(classroom.Student, StudentEmail);
                await Login(classroom.Teacher, TeacherEmail);
                await Login(classroom.OtherTeacher, OtherTeacherEmail);
                await Login(classroom.Admin, "admin@example.test");
                await classroom.Assign(TeacherEmail);
                return classroom;
            }
            catch
            {
                classroom.Dispose();
                throw;
            }
        }

        public async Task Assign(string teacherEmail)
        {
            using var response = await Admin.PutAsJsonAsync("/api/students/teacher-assignment",
                new { studentEmail = StudentEmail, teacherEmail });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public void Dispose()
        {
            Student.Dispose();
            Teacher.Dispose();
            OtherTeacher.Dispose();
            Admin.Dispose();
        }
    }
}
