using PolskiYol.Features.Realtime;
using PolskiYol.Data;
using System.ComponentModel.DataAnnotations;
using PolskiYol.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/conversations/{conversationId:int}/messages")]
public class MessagesController : ControllerBase
{
    private readonly IApiData _data;
    private readonly RealtimeNotifications _notifications;

    public MessagesController(IApiData data, RealtimeNotifications notifications)
    {
        _data = data;
        _notifications = notifications;
    }

    // GET /api/conversations/1/messages
    [HttpGet]
    public async Task<ActionResult<List<MessageResponse>>> GetMessages(
        int conversationId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery, Range(1, int.MaxValue)] int? beforeId = null,
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var conversations = GetAccessibleConversations(currentUserId)
            .Where(c => c.Id == conversationId);

        if (!await conversations.AnyAsync(ct))
            return NotFound();

        var messages = await _data.Query<Message>()
            .Where(m =>
                m.ConversationId == conversationId &&
                (!beforeId.HasValue || m.Id < beforeId.Value) &&
                conversations.Any(c => c.Id == m.ConversationId))
            .OrderByDescending(m => m.Id)
            .Take(limit + 1)
            .Select(m => new MessageResponse
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                Content = m.Content,
                SentAt = m.SentAt,
                SenderType = m.SenderType
            })
            .ToListAsync(ct);

        CursorPage.SetNextCursor(messages, limit, m => m.Id, Response);
        messages.Reverse();

        return Ok(messages);
    }

    // GET /api/conversations/1/messages/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MessageResponse>> GetMessage(
        int conversationId,
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var conversations = GetAccessibleConversations(currentUserId);

        var message = await _data.Query<Message>()
            .SingleOrDefaultAsync(m =>
                m.Id == id &&
                m.ConversationId == conversationId &&
                conversations.Any(c => c.Id == m.ConversationId), ct);

        if (message is null)
            return NotFound();

        return Ok(MessageResponse.FromMessage(message));
    }

    // POST /api/conversations/1/messages
    [HttpPost]
    public async Task<ActionResult<MessageResponse>> SendMessage(
        int conversationId,
        [FromBody] MessageAddRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        if (!User.IsInRole("Teacher") && !User.IsInRole("Student"))
            return Forbid();

        bool isParticipant = await GetAccessibleConversations(currentUserId)
            .AnyAsync(c => c.Id == conversationId, ct);

        if (!isParticipant)
            return NotFound();

        var message = MessagingService.CreateMessage(conversationId, request.Content, User, DateTime.UtcNow);
        if (message is null)
            return BadRequest("Message cannot be empty.");

        _data.Add(message);

        await _data.SaveChangesAsync(ct);
        var response = MessageResponse.FromMessage(message);
        await _notifications.MessageCreatedAsync(response);

        return CreatedAtAction(
            nameof(GetMessage),
            new
            {
                conversationId,
                id = message.Id
            },
            response);
    }

    private IQueryable<Conversation> GetAccessibleConversations(Guid currentUserId) =>
        MessagingService.GetAccessibleConversations(currentUserId, User, _data.Query<Conversation>());
}