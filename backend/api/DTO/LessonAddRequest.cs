using System.ComponentModel.DataAnnotations;

public class LessonAddRequest
{
    [Range(1, 480)]
    public int DurationMinutes { get; set; }

    [Required]
    [StringLength(200)]
    public string Topic { get; set; } = string.Empty;

    [Required]
    public DateTime? StartsAt { get; set; }

    [Required]
    [MinLength(1)]
    public List<int> StudentIds { get; set; } = [];

    public Lesson ToLesson(int teacherId)
    {
        return new Lesson
        {
            DurationMinutes = DurationMinutes,
            Topic = Topic,
            StartsAt = StartsAt!.Value,
            TeacherId = teacherId
        };
    }
}