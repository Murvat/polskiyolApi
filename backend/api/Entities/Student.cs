public class Student
{
    public int Id { get; set; }

    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    public ICollection<Lesson> Lessons { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}