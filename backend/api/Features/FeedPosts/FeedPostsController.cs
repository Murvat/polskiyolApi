using PolskiYol.Data;
using System.ComponentModel.DataAnnotations;
using PolskiYol.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/feed-posts")]
public class FeedPostsController : ControllerBase
{
    private readonly IApiData _data;

    public FeedPostsController(IApiData data)
    {
        _data = data;
    }

    // GET /api/feed-posts
    [HttpGet]
    public async Task<ActionResult<List<FeedPostResponse>>> GetPosts(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery, Range(1, int.MaxValue)] int? beforeId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var posts = await GetAccessiblePosts(currentUserId)
            .Where(p => !beforeId.HasValue || p.Id < beforeId.Value)
            .OrderByDescending(p => p.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        CursorPage.SetNextCursor(posts, limit, p => p.Id, Response);

        var response = posts
            .Select(FeedPostResponse.FromFeedPost)
            .ToList();

        return Ok(response);
    }

    // GET /api/feed-posts/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<FeedPostResponse>> GetPost(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var post = await GetAccessiblePosts(currentUserId)
            .SingleOrDefaultAsync(
                p => p.Id == id,
                cancellationToken);

        if (post is null)
            return NotFound();

        return Ok(FeedPostResponse.FromFeedPost(post));
    }

    // POST /api/feed-posts
    [Authorize(Roles = "Teacher")]
    [HttpPost]
    public async Task<ActionResult<FeedPostResponse>> CreatePost(
        [FromBody] FeedPostAddRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var teacher = await _data.Query<Teacher>()
            .SingleOrDefaultAsync(
                t => t.UserId == currentUserId,
                cancellationToken);

        if (teacher is null)
            return Forbid();

        var post = FeedPostService.CreatePost(request, teacher.Id, DateTime.UtcNow);

        _data.Add(post);

        await _data.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetPost),
            new { id = post.Id },
            FeedPostResponse.FromFeedPost(post));
    }

    // DELETE /api/feed-posts/{id}
    [Authorize(Roles = "Teacher")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeletePost(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var post = await FeedPostService.GetAccessiblePosts(currentUserId, User, _data.Query<FeedPost>(track: true))
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (post is null)
            return NotFound();

        _data.Remove(post);

        await _data.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private IQueryable<FeedPost> GetAccessiblePosts(Guid currentUserId) =>
        FeedPostService.GetAccessiblePosts(currentUserId, User, _data.Query<FeedPost>());
}