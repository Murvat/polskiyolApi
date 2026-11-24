
public class TeacherResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public static TeacherResponse FromTeacher(Teacher teacher)
    {
        return new TeacherResponse
        {
            Id = teacher.Id,
            FirstName = teacher.FirstName,
            LastName = teacher.LastName,
            PhoneNumber = teacher.PhoneNumber
        };
    }
}
