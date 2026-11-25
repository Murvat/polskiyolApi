using System.Security.Claims;

namespace PolskiYol.Features;

public static class MaterialService
{
    public static IQueryable<Material> GetAccessibleMaterials(
        Guid currentUserId, ClaimsPrincipal principal, IQueryable<Material> source, IQueryable<Student> students)
    {
        var query = source;

        if (principal.IsInRole("Teacher"))
        {
            return query.Where(m =>
                m.Teacher.UserId == currentUserId);
        }

        if (principal.IsInRole("Student"))
        {
            return query.Where(m => students.Any(s =>
                s.UserId == currentUserId &&
                s.TeacherId == m.TeacherId));
        }

        return query.Where(m => false);
    }

    public static async Task<(string? FileName, string? Error)> ValidatePdfAsync(IFormFile file, CancellationToken ct)
    {
        var fileName = Path.GetFileName(
            file.FileName.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            !fileName.EndsWith(".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Invalid PDF filename.");
        }

        if (!await HasSignatureAsync(
                file, [0x25, 0x50, 0x44, 0x46, 0x2D], ct))
        {
            return (null, "File is not a recognized PDF.");
        }
        return (fileName, null);
    }

    public static async Task<(string? Extension, string? ContentType, string? Error)> ValidateCoverAsync(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return (null, null, "A cover image is required.");

        if (file.Length > 5 * 1024 * 1024)
            return (null, null, "Cover exceeds the 5 MB limit.");

        var extension = Path.GetExtension(file.FileName)
            .ToLowerInvariant();

        var validImage = extension switch
        {
            ".png" => await HasSignatureAsync(
                file, [0x89, 0x50, 0x4E, 0x47,
                       0x0D, 0x0A, 0x1A, 0x0A], ct),

            ".jpg" or ".jpeg" => await HasSignatureAsync(
                file, [0xFF, 0xD8, 0xFF], ct),

            _ => false
        };

        if (!validImage)
            return (null, null, "Only PNG and JPEG images are supported.");

        var contentType = extension == ".png"
            ? "image/png"
            : "image/jpeg";
        return (extension, contentType, null);
    }

    public static async Task SaveWithCleanupAsync(Func<Task> save, Func<Task> cleanup)
    {
        try
        {
            await save();
        }
        catch
        {
            await cleanup();
            throw;
        }
    }

    private static async Task<bool> HasSignatureAsync(
        IFormFile file,
        byte[] signature,
        CancellationToken ct)
    {
        if (file.Length < signature.Length)
            return false;

        await using var stream = file.OpenReadStream();

        var header = new byte[signature.Length];
        await stream.ReadExactlyAsync(header, ct);

        return header.SequenceEqual(signature);
    }
}
