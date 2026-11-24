public class FeedPost
{
    public int Id { get; set; }
    public Teacher Teacher { get; set; }
    public int TeacherId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string? PostImageKey { get; set; }
    public DateTime CreatedAt { get; set; }

}