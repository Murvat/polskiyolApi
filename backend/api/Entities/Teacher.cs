public class Teacher
{
    public int Id { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ICollection<Student> Students { get; set; } = [];
    public ICollection<Lesson> Lessons { get; set; } = [];
    public ICollection<Material> Materials { get; set; } = [];
    public ICollection<FeedPost> FeedPosts { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    public ICollection<Conversation> Conversations { get; set; } = [];
}