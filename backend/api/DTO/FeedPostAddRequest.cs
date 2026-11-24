using System.ComponentModel.DataAnnotations;

public class FeedPostAddRequest
{
    [Required]
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(5000)]
    public string Content { get; set; } = string.Empty;

    public static FeedPost ToFeedPost(
        FeedPostAddRequest request,
        int teacherId)
    {
        return new FeedPost
        {
            Title = request.Title,
            Content = request.Content,
            TeacherId = teacherId,
            CreatedAt = DateTime.UtcNow
        };
    }
}