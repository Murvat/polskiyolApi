using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize]
[ApiController]
[Route("api/students")]
public class StudentsController : ControllerBase
{
    private readonly AppDbContext _context;

    public StudentsController(AppDbContext context)
    {
        _context = context;
    }

    [Authorize(Roles = "Student")]
    [HttpGet("me")]
    public async Task<ActionResult<StudentResponse>> GetCurrentStudent(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var currentUserId))
            return Unauthorized();

        var student = await _context.Students
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.UserId == currentUserId, cancellationToken);

        if (student is null)
            return NotFound();

        return Ok(StudentResponse.FromStudent(student));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("teacher-assignment")]
    public async Task<IActionResult> AssignTeacher(
        [FromBody] StudentTeacherAssignmentRequest request,
        [FromServices] ILookupNormalizer normalizer,
        CancellationToken cancellationToken)
    {
        // Registration uses the email address as the unique Identity user name.
        var studentUserName = normalizer.NormalizeName(request.StudentEmail);
        var teacherUserName = normalizer.NormalizeName(request.TeacherEmail);

        var studentId = await _context.Students
            .Where(s => s.User.NormalizedUserName == studentUserName)
            .Select(s => (int?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (studentId is not int assignedStudentId)
            return NotFound("Student profile was not found.");

        var teacherId = await _context.Teachers
            .Where(t => t.User.NormalizedUserName == teacherUserName)
            .Select(t => (int?)t.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (teacherId is not int assignedTeacherId)
            return NotFound("Teacher profile was not found.");

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // Update first so assignment requests for the same student serialize
        // before they check whether the conversation already exists.
        var updated = await _context.Students
            .Where(s => s.Id == assignedStudentId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.TeacherId, (int?)assignedTeacherId),
                cancellationToken);

        if (updated == 0)
            return NotFound("Student profile was not found.");

        var conversationExists = await _context.Conversations.AnyAsync(
            c => c.StudentId == assignedStudentId && c.TeacherId == assignedTeacherId,
            cancellationToken);

        if (!conversationExists)
        {
            _context.Conversations.Add(new Conversation
            {
                StudentId = assignedStudentId,
                TeacherId = assignedTeacherId
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "Teacher")]
    [HttpGet]
    public async Task<ActionResult<List<StudentResponse>>> GetStudents()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
        {
            return Unauthorized();
        }

        var students = await _context.Students
            .AsNoTracking()
            .Where(s =>
                s.Teacher != null &&
                s.Teacher.UserId == currentUserId)
            .ToListAsync();

        var response = new List<StudentResponse>();

        foreach (var student in students)
        {
            response.Add(StudentResponse.FromStudent(student));
        }

        return Ok(response);
    }

    [Authorize(Roles = "Teacher,Student")]
    [HttpGet("{id}")]
    public async Task<ActionResult<StudentResponse>> GetStudent(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
        {
            return Unauthorized();
        }

        var student = await _context.Students
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id);

        if (student is null)
        {
            return NotFound();
        }

        if (User.IsInRole("Teacher"))
        {
            bool isAssignedTeacher = await _context.Teachers
                .AnyAsync(t =>
                    t.Id == student.TeacherId &&
                    t.UserId == currentUserId);

            if (!isAssignedTeacher)
            {
                return Forbid();
            }
        }
        else if (User.IsInRole("Student"))
        {
            if (student.UserId != currentUserId)
            {
                return Forbid();
            }
        }
        else
        {
            return Forbid();
        }

        return Ok(StudentResponse.FromStudent(student));
    }
}