using System.ComponentModel.DataAnnotations;

public class StudentTeacherAssignmentRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string StudentEmail { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string TeacherEmail { get; set; } = string.Empty;
}
