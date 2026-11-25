using System.Security.Claims;

namespace PolskiYol.Features;

public static class LessonService
{
    public static IQueryable<Lesson> GetAccessibleLessons(Guid currentUserId, ClaimsPrincipal principal, IQueryable<Lesson> source)
    {
        var query = source;

        if (principal.IsInRole("Teacher"))
            return query.Where(l => l.Teacher.UserId == currentUserId);

        if (principal.IsInRole("Student"))
            return query.Where(l =>
                l.Students.Any(s => s.UserId == currentUserId && s.TeacherId == l.TeacherId));

        return query.Where(l => false);
    }

    public static (DateTime Start, DateTime End) GetWeek(DateTime today)
    {
        var start = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (start, start.AddDays(7));
    }

    public static IQueryable<Student> EligibleStudents(IQueryable<Student> students, List<int> ids, int teacherId) =>
        students.Where(student => ids.Contains(student.Id) && student.TeacherId == teacherId);
}
