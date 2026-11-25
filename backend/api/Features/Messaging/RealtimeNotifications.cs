using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PolskiYol.Data;

namespace PolskiYol.Features.Realtime;

public sealed class RealtimeNotifications(
    IApiData data, IHubContext<MessagingHub, IRealtimeClient> hub, ILogger<RealtimeNotifications> logger)
{
    public Task MessageCreatedAsync(MessageResponse message) => PublishAsync(nameof(IRealtimeClient.MessageCreated), message.Id, async () =>
    {
        var audience = await data.Query<Conversation>()
            .Where(c => c.Id == message.ConversationId && c.Student.TeacherId == c.TeacherId)
            .Select(c => new { Student = c.Student.UserId, Teacher = c.Teacher.UserId })
            .SingleOrDefaultAsync();

        if (audience is null)
            return;

        await hub.Clients.Groups([
            MessagingHub.ConversationGroup(message.ConversationId, audience.Student),
            MessagingHub.ConversationGroup(message.ConversationId, audience.Teacher)
        ]).MessageCreated(message);
    });

    public Task FeedPostCreatedAsync(int teacherId, int postId) =>
        NotifyClassroomAsync(teacherId, postId, nameof(IRealtimeClient.FeedPostCreated), (clients, change) => clients.FeedPostCreated(change));

    public Task FeedPostDeletedAsync(int teacherId, int postId) =>
        NotifyClassroomAsync(teacherId, postId, nameof(IRealtimeClient.FeedPostDeleted), (clients, change) => clients.FeedPostDeleted(change));

    public Task MaterialCreatedAsync(int teacherId, int materialId) =>
        NotifyClassroomAsync(teacherId, materialId, nameof(IRealtimeClient.MaterialCreated), (clients, change) => clients.MaterialCreated(change));

    public Task MaterialCoverUpdatedAsync(int teacherId, int materialId) =>
        NotifyClassroomAsync(teacherId, materialId, nameof(IRealtimeClient.MaterialCoverUpdated), (clients, change) => clients.MaterialCoverUpdated(change));

    public Task MaterialDeletedAsync(int teacherId, int materialId) =>
        NotifyClassroomAsync(teacherId, materialId, nameof(IRealtimeClient.MaterialDeleted), (clients, change) => clients.MaterialDeleted(change));

    public Task TeacherAssignmentChangedAsync(int studentId) => PublishAsync(nameof(IRealtimeClient.TeacherAssignmentChanged), studentId, async () =>
    {
        var userId = await data.Query<Student>().Where(student => student.Id == studentId)
            .Select(student => (Guid?)student.UserId).SingleOrDefaultAsync();
        if (userId.HasValue)
            await hub.Clients.User(userId.Value.ToString("D")).TeacherAssignmentChanged();
    });

    private Task NotifyClassroomAsync(
        int teacherId, int id, string eventName, Func<IRealtimeClient, ResourceChanged, Task> send) =>
        PublishAsync(eventName, id, async () =>
        {
            var audience = await data.Query<Teacher>().Where(teacher => teacher.Id == teacherId).Select(teacher => teacher.UserId)
                .Concat(data.Query<Student>().Where(student => student.TeacherId == teacherId).Select(student => student.UserId))
                .Distinct().ToListAsync();

            if (audience.Count > 0)
                await send(hub.Clients.Users(audience.Select(userId => userId.ToString("D")).ToArray()), new ResourceChanged(id));
        });

    private async Task PublishAsync(string eventName, int resourceId, Func<Task> publish)
    {
        try
        {
            await publish();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not publish {RealtimeEvent} for resource {ResourceId}.", eventName, resourceId);
        }
    }
}
