using System.Security.Claims;

namespace PolskiYol.Features;

public static class FeedPostService
{
    public static IQueryable<FeedPost> GetAccessiblePosts(
        Guid currentUserId, ClaimsPrincipal principal, IQueryable<FeedPost> source)
    {
        var query = source;

        if (principal.IsInRole("Teacher"))
        {
            return query.Where(
                p => p.Teacher.UserId == currentUserId);
        }

        if (principal.IsInRole("Student"))
        {
            return query.Where(p =>
                p.Teacher.Students.Any(
                    s => s.UserId == currentUserId));
        }

        return query.Where(p => false);
    }

    public static FeedPost CreatePost(FeedPostAddRequest request, int teacherId, DateTime utcNow) => new()
    {
        Title = request.Title,
        Content = request.Content,
        TeacherId = teacherId,
        CreatedAt = utcNow
    };
}
