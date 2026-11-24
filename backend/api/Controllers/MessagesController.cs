using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/conversations/{conversationId:int}/messages")]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _context;

    public MessagesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/conversations/1/messages
    [HttpGet]
    public async Task<ActionResult<List<MessageResponse>>> GetMessages(
        int conversationId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        if (limit < 1 || limit > 100)
            return BadRequest("Limit must be between 1 and 100.");

        var conversations = GetAccessibleConversations(currentUserId)
            .Where(c => c.Id == conversationId);

        if (!await conversations.AnyAsync(ct))
            return NotFound();

        var messages = await _context.Messages
            .AsNoTracking()
            .Where(m =>
                m.ConversationId == conversationId &&
                conversations.Any(c => c.Id == m.ConversationId))
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new MessageResponse
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                Content = m.Content,
                SentAt = m.SentAt,
                SenderType = m.SenderType
            })
            .ToListAsync(ct);

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

        var message = await _context.Messages
            .AsNoTracking()
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

        MessageSenderType senderType;

        if (User.IsInRole("Teacher"))
        {
            senderType = MessageSenderType.Teacher;
        }
        else if (User.IsInRole("Student"))
        {
            senderType = MessageSenderType.Student;
        }
        else
        {
            return Forbid();
        }

        bool isParticipant = await GetAccessibleConversations(currentUserId)
            .AnyAsync(c => c.Id == conversationId, ct);

        if (!isParticipant)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Message cannot be empty.");

        var message = new Message
        {
            ConversationId = conversationId,
            Content = request.Content.Trim(),
            SentAt = DateTime.UtcNow,
            SenderType = senderType
        };

        _context.Messages.Add(message);

        await _context.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetMessage),
            new
            {
                conversationId,
                id = message.Id
            },
            MessageResponse.FromMessage(message));
    }

    private IQueryable<Conversation> GetAccessibleConversations(
        Guid currentUserId)
    {
        var query = _context.Conversations
            .AsNoTracking()
            .Where(c => c.Student.TeacherId == c.TeacherId);

        if (User.IsInRole("Teacher"))
        {
            return query.Where(c =>
                c.Teacher.UserId == currentUserId);
        }

        if (User.IsInRole("Student"))
        {
            return query.Where(c =>
                c.Student.UserId == currentUserId);
        }

        return query.Where(c => false);
    }
}