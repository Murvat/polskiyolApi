using System.Security.Claims;

namespace PolskiYol.Services;

public static class ProfileAccess
{
    public static IQueryable<Teacher> AssignedTeacher(IQueryable<Teacher> teachers, int? teacherId, Guid userId) =>
        teachers.Where(teacher => teacher.Id == teacherId && teacher.UserId == userId);

    public static IQueryable<Student> AssignedStudent(IQueryable<Student> students, int teacherId, Guid userId) =>
        students.Where(student => student.UserId == userId && student.TeacherId == teacherId);

    public static bool IsOwnProfile(ClaimsPrincipal principal, string role, Guid profileUserId, Guid userId) =>
        principal.IsInRole(role) && profileUserId == userId;
}
