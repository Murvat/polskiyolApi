using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PolskiYol.Data;
using PolskiYol.Features;

namespace PolskiYol.Features.Realtime;

[Authorize(Roles = "Teacher,Student")]
public sealed class MessagingHub(IApiData data) : Hub<IRealtimeClient>
{
    public const string Route = "/hubs/messages";

    public async Task JoinConversation(int conversationId)
    {
        var principal = Context.User;
        if (principal is null || !Guid.TryParse(Context.UserIdentifier, out var userId))
            throw new HubException("Conversation is unavailable.");

        var accessible = await MessagingService.GetAccessibleConversations(userId, principal, data.Query<Conversation>())
            .AnyAsync(conversation => conversation.Id == conversationId, Context.ConnectionAborted);
        if (!accessible)
            throw new HubException("Conversation is unavailable.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId, userId), Context.ConnectionAborted);
    }

    public Task LeaveConversation(int conversationId) =>
        Guid.TryParse(Context.UserIdentifier, out var userId)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId, userId), Context.ConnectionAborted)
            : Task.CompletedTask;

    internal static string ConversationGroup(int conversationId, Guid userId) => $"conversation:{conversationId}:{userId:D}";
}

public interface IRealtimeClient
{
    Task MessageCreated(MessageResponse message);
    Task FeedPostCreated(ResourceChanged change);
    Task FeedPostDeleted(ResourceChanged change);
    Task MaterialCreated(ResourceChanged change);
    Task MaterialCoverUpdated(ResourceChanged change);
    Task MaterialDeleted(ResourceChanged change);
    Task TeacherAssignmentChanged();
}

public sealed record ResourceChanged(int Id);
