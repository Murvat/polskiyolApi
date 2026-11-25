using Microsoft.AspNetCore.Mvc;
using PolskiYol.Services;
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
        [FromServices] StudentAssignmentService assignment,
        CancellationToken cancellationToken)
    {
        var result = await assignment.AssignAsync(request.StudentEmail, request.TeacherEmail, cancellationToken);
        return result switch
        {
            StudentAssignmentResult.Assigned => NoContent(),
            StudentAssignmentResult.StudentMissing => NotFound("Student profile was not found."),
            _ => NotFound("Teacher profile was not found.")
        };
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
            bool isAssignedTeacher = await ProfileAccess.AssignedTeacher(_context.Teachers, student.TeacherId, currentUserId).AnyAsync();

            if (!isAssignedTeacher)
            {
                return Forbid();
            }
        }
        else if (User.IsInRole("Student"))
        {
            if (!ProfileAccess.IsOwnProfile(User, "Student", student.UserId, currentUserId))
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