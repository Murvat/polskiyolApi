using PolskiYol.Data;
using PolskiYol.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/[controller]")]
public class TeachersController : ControllerBase
{
    private readonly IApiData _data;

    public TeachersController(IApiData data)
    {
        _data = data;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TeacherResponse>> GetTeacher(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
        {
            return Unauthorized();
        }

        var teacher = await _data.Query<Teacher>()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (teacher is null)
        {
            return NotFound();
        }

        if (User.IsInRole("Teacher"))
        {
            if (!ProfileAccess.IsOwnProfile(User, "Teacher", teacher.UserId, currentUserId))
            {
                return Forbid();
            }
        }
        else if (User.IsInRole("Student"))
        {
            bool isAssignedStudent = await ProfileAccess.AssignedStudent(_data.Query<Student>(), id, currentUserId).AnyAsync();

            if (!isAssignedStudent)
            {
                return Forbid();
            }
        }
        else
        {
            return Forbid();
        }

        return Ok(TeacherResponse.FromTeacher(teacher));
    }
}