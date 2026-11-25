using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ConversationsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/conversations
    [HttpGet]
    public async Task<ActionResult<List<ConversationResponse>>> GetConversations(
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var conversations = await GetAccessibleConversations(currentUserId)
            .Select(c => new ConversationResponse
            {
                Id = c.Id,
                StudentId = c.StudentId,
                TeacherId = c.TeacherId,
                StudentFirstName = c.Student.FirstName,
                StudentLastName = c.Student.LastName
            })
            .ToListAsync(ct);

        return Ok(conversations);
    }

    // GET /api/conversations/10
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConversationResponse>> GetConversation(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var conversation = await GetAccessibleConversations(currentUserId)
            .Where(c => c.Id == id)
            .Select(c => new ConversationResponse
            {
                Id = c.Id,
                StudentId = c.StudentId,
                TeacherId = c.TeacherId,
                StudentFirstName = c.Student.FirstName,
                StudentLastName = c.Student.LastName
            })
            .SingleOrDefaultAsync(ct);

        if (conversation is null)
            return NotFound();

        return Ok(conversation);
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