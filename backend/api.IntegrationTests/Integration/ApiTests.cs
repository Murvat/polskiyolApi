using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PolskiYol.DTOs;
using Xunit;

public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationsProduceTheCurrentConversationSchema(bool upgrade)
    {
        await using var database = fixture.CreateDatabase($"PolskiYolMigration_{Guid.NewGuid():N}");

        try
        {
            if (upgrade)
            {
                await database.Database.GetService<IMigrator>()
                    .MigrateAsync("20261116202501_AddAbsoluteSessionExpiration");
                Assert.True(await HasLegacyTeacherColumn(database));
            }

            await database.Database.MigrateAsync();

            Assert.Empty(await database.Database.GetPendingMigrationsAsync());
            Assert.False(await HasLegacyTeacherColumn(database));
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task OnlyAdminsCanAssignAndOnlyCurrentParticipantsCanUseTheConversation()
    {
        using var admin = fixture.CreateClient();
        using var student = fixture.CreateClient();
        using var teacher = fixture.CreateClient();
        using var nextTeacher = fixture.CreateClient();

        await SignIn(admin, ApiFixture.AdminEmail);
        var studentEmail = await RegisterAndSignIn(student, "student");
        var teacherEmail = await RegisterAndSignIn(teacher, "teacher");
        var nextTeacherEmail = await RegisterAndSignIn(nextTeacher, "teacher");

        Assert.Null((await GetStudent(student)).TeacherId);
        Assert.Empty(await GetConversations(student));

        using var forbidden = await student.PutAsJsonAsync(
            "/api/students/teacher-assignment", new { studentEmail, teacherEmail });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await AssignTeacher(admin, studentEmail, teacherEmail);
        await AssignTeacher(admin, studentEmail, teacherEmail);
        var conversation = Assert.Single(await GetConversations(student));
        Assert.Equal(conversation.TeacherId, (await GetStudent(student)).TeacherId);
        Assert.Equal(conversation.Id, Assert.Single(await GetConversations(teacher)).Id);
        Assert.Empty(await GetConversations(nextTeacher));

        var messagesUrl = $"/api/conversations/{conversation.Id}/messages";
        using var sent = await student.PostAsJsonAsync(messagesUrl, new { content = "Hello" });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Single((await teacher.GetFromJsonAsync<MessageResponse[]>(messagesUrl))!);
        using var unrelatedRead = await nextTeacher.GetAsync(messagesUrl);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedRead.StatusCode);

        await AssignTeacher(admin, studentEmail, nextTeacherEmail);

        Assert.Empty(await GetConversations(teacher));
        Assert.NotEqual(conversation.Id, Assert.Single(await GetConversations(student)).Id);
        Assert.Single(await GetConversations(nextTeacher));
        using var oldRead = await student.GetAsync(messagesUrl);
        using var oldWrite = await teacher.PostAsJsonAsync(messagesUrl, new { content = "Old conversation" });
        Assert.Equal(HttpStatusCode.NotFound, oldRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldWrite.StatusCode);

        await AssignTeacher(admin, studentEmail, teacherEmail);
        Assert.Equal(conversation.Id, Assert.Single(await GetConversations(student)).Id);
        Assert.Single((await student.GetFromJsonAsync<MessageResponse[]>(messagesUrl))!);
    }

    [Fact]
    public async Task ConcurrentMonthlyPaymentsReturnCreatedAndConflict()
    {
        using var admin = fixture.CreateClient();
        using var student = fixture.CreateClient();
        using var teacher = fixture.CreateClient();
        await SignIn(admin, ApiFixture.AdminEmail);
        var studentEmail = await RegisterAndSignIn(student, "student");
        var teacherEmail = await RegisterAndSignIn(teacher, "teacher");
        await AssignTeacher(admin, studentEmail, teacherEmail);
        var studentId = (await GetStudent(student)).Id;

        var payment = new
        {
            studentId,
            year = 2026,
            month = 11,
            expectedAmount = 100.25m,
            dueDate = new DateOnly(2026, 11, 30)
        };

        fixture.SynchronizeNextTwoPaymentInserts();
        var results = await Task.WhenAll(
            teacher.PostAsJsonAsync("/api/payments", payment),
            teacher.PostAsJsonAsync("/api/payments", payment));
        using var first = results[0];
        using var second = results[1];

        Assert.Contains(HttpStatusCode.Created, new[] { first.StatusCode, second.StatusCode });
        Assert.Contains(HttpStatusCode.Conflict, new[] { first.StatusCode, second.StatusCode });
        using var list = await teacher.GetAsync($"/api/payments?year=2026&month=11&studentId={studentId}");
        list.EnsureSuccessStatusCode();
        Assert.Single((await list.Content.ReadFromJsonAsync<PaymentResponse[]>())!);
    }

    [Fact]
    public async Task RefreshRotatesTheCookieAndReuseRevokesTheReplacement()
    {
        using var client = fixture.CreateClient(handleCookies: false);
        var email = await Register(client, "student");
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFixture.Password });
        login.EnsureSuccessStatusCode();

        var original = RefreshCookie(login);
        using var rotated = await Refresh(client, original);
        rotated.EnsureSuccessStatusCode();
        var replacement = RefreshCookie(rotated);
        Assert.NotEqual(original, replacement);

        using var replay = await Refresh(client, original);
        using var revoked = await Refresh(client, replacement);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    private static Task<bool> HasLegacyTeacherColumn(AppDbContext database) =>
        database.Database.SqlQueryRaw<bool>(
            "SELECT CAST(CASE WHEN COL_LENGTH('Conversations', 'TeacherId1') IS NULL THEN 0 ELSE 1 END AS bit) AS [Value]")
            .SingleAsync();

    private static async Task<string> Register(HttpClient client, string role)
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsJsonAsync($"/api/auth/register/{role}", new
        {
            email,
            password = ApiFixture.Password,
            firstName = "Integration",
            lastName = "Test",
            phoneNumber = "5550100"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private static async Task<string> RegisterAndSignIn(HttpClient client, string role)
    {
        var email = await Register(client, role);
        await SignIn(client, email);
        return email;
    }

    private static async Task SignIn(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFixture.Password });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    private static async Task AssignTeacher(HttpClient admin, string studentEmail, string teacherEmail)
    {
        using var response = await admin.PutAsJsonAsync("/api/students/teacher-assignment", new { studentEmail, teacherEmail });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<StudentResponse> GetStudent(HttpClient student) =>
        (await student.GetFromJsonAsync<StudentResponse>("/api/students/me"))!;

    private static async Task<ConversationResponse[]> GetConversations(HttpClient client) =>
        (await client.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!;

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("refreshToken=", StringComparison.Ordinal))
            .Split(';', 2)[0];

    private static async Task<HttpResponseMessage> Refresh(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
