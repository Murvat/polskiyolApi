public class LessonResponse
{
    public int Id { get; set; }

    public string Topic { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public DateTime StartsAt { get; set; }
    public int TeacherId { get; set; }

    public static LessonResponse FromLesson(Lesson lesson)
    {
        return new LessonResponse
        {
            Id = lesson.Id,
            Topic = lesson.Topic,
            StartsAt = lesson.StartsAt,
            TeacherId = lesson.TeacherId,
            DurationMinutes = lesson.DurationMinutes
        };
    }
}