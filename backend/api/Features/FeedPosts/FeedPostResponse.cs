public class FeedPostResponse
{
    public int Id { get; set; }
    public int TeacherId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    public string? PostImageKey { get; set; }
    public DateTime CreatedAt { get; set; }

    public static FeedPostResponse FromFeedPost(FeedPost post)
    {
        return new FeedPostResponse
        {
            Id = post.Id,
            TeacherId = post.TeacherId,
            Title = post.Title,
            Content = post.Content,
            PostImageKey = post.PostImageKey,
            CreatedAt = post.CreatedAt
        };
    }
}
