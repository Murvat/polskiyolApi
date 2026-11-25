using PolskiYol.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/lessons")]
public class LessonsController : ControllerBase
{
    private readonly AppDbContext _context;

    public LessonsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<LessonResponse>>> GetLessons(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var (weekStart, weekEnd) = LessonService.GetWeek(DateTime.Today);

        var lessons = await GetAccessibleLessons(currentUserId)
            .Where(l => l.StartsAt >= weekStart && l.StartsAt < weekEnd)
            .OrderBy(l => l.StartsAt)
            .ToListAsync(cancellationToken);

        var response = lessons
            .Select(LessonResponse.FromLesson)
            .ToList();

        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LessonResponse>> GetLesson(
        int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var lesson = await GetAccessibleLessons(currentUserId)
            .SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (lesson is null)
            return NotFound();

        return Ok(LessonResponse.FromLesson(lesson));
    }

    [Authorize(Roles = "Teacher")]
    [HttpPost]
    public async Task<ActionResult<LessonResponse>> CreateLesson(
        [FromBody] LessonAddRequest lesson,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var teacher = await _context.Teachers
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.UserId == currentUserId,
                cancellationToken);

        if (teacher is null)
            return Forbid();

        if (lesson.StudentIds.Any(id => id <= 0))
            return BadRequest("Student IDs must be positive.");

        var studentIds = lesson.StudentIds.Distinct().ToList();

        var students = await LessonService.EligibleStudents(_context.Students.AsTracking(), studentIds, teacher.Id)
            .ToListAsync(cancellationToken);

        if (students.Count != studentIds.Count)
            return BadRequest(
                "One or more selected students are invalid or not assigned to you.");

        var newLesson = lesson.ToLesson(teacher.Id);

        newLesson.Students = students;

        _context.Lessons.Add(newLesson);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetLesson),
            new { id = newLesson.Id },
            LessonResponse.FromLesson(newLesson));
    }

    private IQueryable<Lesson> GetAccessibleLessons(Guid currentUserId) =>
        LessonService.GetAccessibleLessons(currentUserId, User, _context.Lessons.AsNoTracking());
}