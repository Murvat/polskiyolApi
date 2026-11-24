
public class Conversation
{
    public int Id { get; set; }
    public Student Student { get; set; }
    public Teacher Teacher { get; set; }

    public int StudentId { get; set; }

    public int TeacherId { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
}