using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PolskiYol.Data;
using PolskiYol.Features.Realtime;
using PolskiYol.Tests.Infrastructure;
using Xunit;

namespace PolskiYol.Tests.Http;

public sealed partial class OfflineApiTests
{
    [Fact]
    public async Task MessagingPublishesOnlyToJoinedParticipantsAndRestRecoversMissedMessages()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        var conversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
        var messages = $"/api/conversations/{conversation.Id}/messages";

        await using var student = ConnectHub(host, classroom.Student);
        await using var teacher = ConnectHub(host, classroom.Teacher);
        await using var outsider = ConnectHub(host, classroom.OtherTeacher);
        var studentEvents = Listen<MessageResponse>(student, "MessageCreated");
        var teacherEvents = Listen<MessageResponse>(teacher, "MessageCreated");
        var outsiderEvents = Listen<MessageResponse>(outsider, "MessageCreated");
        await Task.WhenAll(student.StartAsync(), teacher.StartAsync(), outsider.StartAsync());

        await Assert.ThrowsAsync<HubException>(() => outsider.InvokeAsync("JoinConversation", conversation.Id));
        await student.InvokeAsync("JoinConversation", conversation.Id);
        await teacher.InvokeAsync("JoinConversation", conversation.Id);

        using var first = await classroom.Student.PostAsJsonAsync(messages, new { content = "Live" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var message = await Read<MessageResponse>(first);
        Assert.Equal(message.Id, (await Next(studentEvents)).Id);
        Assert.Equal(message.Id, (await Next(teacherEvents)).Id);
        await ExpectNoEvent(outsiderEvents);

        await teacher.InvokeAsync("LeaveConversation", conversation.Id);
        await teacher.StopAsync();
        using var missed = await classroom.Student.PostAsJsonAsync(messages, new { content = "Missed" });
        Assert.Equal(HttpStatusCode.Created, missed.StatusCode);
        await teacher.StartAsync();
        await teacher.InvokeAsync("JoinConversation", conversation.Id);
        var history = await classroom.Teacher.GetFromJsonAsync<MessageResponse[]>(messages);
        Assert.Contains(history!, item => item.Content == "Missed");

        using var resumed = await classroom.Student.PostAsJsonAsync(messages, new { content = "Resumed" });
        Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        Assert.Equal("Resumed", (await Next(teacherEvents)).Content);
    }

    [Fact]
    public async Task FeedAndMaterialEventsFollowTheCurrentAssignmentOnAnOpenConnection()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        await using var student = ConnectHub(host, classroom.Student);
        await using var outside = ConnectHub(host, classroom.OtherTeacher);
        var posts = Listen<ResourceChanged>(student, "FeedPostCreated");
        var otherPosts = Listen<ResourceChanged>(outside, "FeedPostCreated");
        var postDeletes = Listen<ResourceChanged>(student, "FeedPostDeleted");
        var materials = Listen<ResourceChanged>(student, "MaterialCreated");
        var covers = Listen<ResourceChanged>(student, "MaterialCoverUpdated");
        var materialDeletes = Listen<ResourceChanged>(student, "MaterialDeleted");
        var assignment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = student.On("TeacherAssignmentChanged", () => { assignment.TrySetResult(); });
        await Task.WhenAll(student.StartAsync(), outside.StartAsync());

        var postId = await CreateRealtimePost(classroom.Teacher, "Before reassignment");
        Assert.Equal(postId, (await Next(posts)).Id);
        await ExpectNoEvent(otherPosts);
        using var deletedPost = await classroom.Teacher.DeleteAsync($"/api/feed-posts/{postId}");
        Assert.Equal(HttpStatusCode.NoContent, deletedPost.StatusCode);
        Assert.Equal(postId, (await Next(postDeletes)).Id);

        using var uploaded = await Upload(classroom.Teacher, "/api/materials", "lesson.pdf", "%PDF-1.5"u8.ToArray());
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var material = await Read<MaterialResponse>(uploaded);
        Assert.Equal(material.Id, (await Next(materials)).Id);

        var materialPath = $"/api/materials/{material.Id}";
        using var cover = await Upload(classroom.Teacher, materialPath + "/cover", "cover.jpg", [0xff, 0xd8, 0xff]);
        Assert.Equal(HttpStatusCode.NoContent, cover.StatusCode);
        Assert.Equal(material.Id, (await Next(covers)).Id);
        using var deletedMaterial = await classroom.Teacher.DeleteAsync(materialPath);
        Assert.Equal(HttpStatusCode.NoContent, deletedMaterial.StatusCode);
        Assert.Equal(material.Id, (await Next(materialDeletes)).Id);

        await classroom.Assign(Classroom.OtherTeacherEmail);
        await assignment.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await CreateRealtimePost(classroom.Teacher, "Old classroom");
        await ExpectNoEvent(posts);
        var newPost = await CreateRealtimePost(classroom.OtherTeacher, "New classroom");
        Assert.Equal(newPost, (await Next(posts)).Id);
        Assert.Equal(newPost, (await Next(otherPosts)).Id);
    }

    [Fact]
    public async Task FailedWritesDoNotPublishAndTheHubRequiresAuthentication()
    {
        await using var host = new MemoryApiHost();
        using var classroom = await Classroom.Create(host);
        using var anonymous = host.Connect();
        using var negotiate = await anonymous.PostAsync("/hubs/messages/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);

        await using var student = ConnectHub(host, classroom.Student);
        var events = Listen<MessageResponse>(student, "MessageCreated");
        await student.StartAsync();
        var conversation = Assert.Single((await classroom.Student.GetFromJsonAsync<ConversationResponse[]>("/api/conversations"))!);
        await student.InvokeAsync("JoinConversation", conversation.Id);

        var data = Assert.IsType<MemoryApiData>(host.Services.GetRequiredService<IApiData>());
        data.FailNextSave = true;
        using var failed = await classroom.Student.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new { content = "Failed" });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await ExpectNoEvent(events);
    }

    private static HubConnection ConnectHub(MemoryApiHost host, HttpClient authenticated)
    {
        var token = authenticated.DefaultRequestHeaders.Authorization?.Parameter;
        return new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost" + MessagingHub.Route), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .Build();
    }

    private static ChannelReader<T> Listen<T>(HubConnection connection, string eventName)
    {
        var events = Channel.CreateUnbounded<T>();
        connection.On<T>(eventName, value => { events.Writer.TryWrite(value); });
        return events.Reader;
    }

    private static async Task<T> Next<T>(ChannelReader<T> events)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await events.ReadAsync(timeout.Token);
    }

    private static async Task ExpectNoEvent<T>(ChannelReader<T> events)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await events.ReadAsync(timeout.Token); });
    }

    private static async Task<int> CreateRealtimePost(HttpClient teacher, string title)
    {
        using var result = await teacher.PostAsJsonAsync("/api/feed-posts", new { title, content = "Content" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await Read<FeedPostResponse>(result)).Id;
    }
}
