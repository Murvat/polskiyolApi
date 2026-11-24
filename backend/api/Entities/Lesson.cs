public class Lesson
{
    public int Id { get; set; }
    public int DurationMinutes { get; set; }
    public string Topic { get; set; }
    public DateTime StartsAt { get; set; }
    public Teacher Teacher { get; set; }
    public int TeacherId { get; set; }
    public ICollection<Student> Students { get; set; } = [];

}