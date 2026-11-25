using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/materials")]
public class MaterialsController : ControllerBase
{
    private const long MaxCoverBytes = 5 * 1024 * 1024;

    private readonly AppDbContext _context;
    private readonly BlobContainerClient _container;
    private readonly ILogger<MaterialsController> _logger;

    public MaterialsController(
        AppDbContext context,
        BlobContainerClient container,
        ILogger<MaterialsController> logger)
    {
        _context = context;
        _container = container;
        _logger = logger;
    }

    // GET /api/materials
    [HttpGet]
    public async Task<ActionResult<List<MaterialResponse>>> GetMaterials(
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var materials = await GetAccessibleMaterials(currentUserId)
            .ToListAsync(ct);

        return Ok(materials
            .Select(MaterialResponse.FromMaterial)
            .ToList());
    }

    // GET /api/materials/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaterialResponse>> GetMaterial(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var material = await GetAccessibleMaterials(currentUserId)
            .SingleOrDefaultAsync(m => m.Id == id, ct);

        if (material is null)
            return NotFound();

        return Ok(MaterialResponse.FromMaterial(material));
    }

    // POST /api/materials
    [Authorize(Roles = "Teacher")]
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaterialUploadRequest.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaterialUploadRequest.MaxRequestBytes)]
    public async Task<ActionResult<MaterialResponse>> UploadMaterial(
        [FromForm] MaterialUploadRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var teacher = await _context.Teachers
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.UserId == currentUserId, ct);

        if (teacher is null)
            return Forbid();

        var file = request.File;

        var fileName = Path.GetFileName(
            file.FileName.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            !fileName.EndsWith(".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid PDF filename.");
        }

        if (!await HasSignatureAsync(
                file, [0x25, 0x50, 0x44, 0x46, 0x2D], ct))
        {
            return BadRequest("File is not a recognized PDF.");
        }

        var storageKey = $"pdf/{Guid.NewGuid():N}.pdf";
        var blob = _container.GetBlobClient(storageKey);

        var material = new Material
        {
            TeacherId = teacher.Id,
            FileName = fileName,
            Description = request.Description,
            FileStorageKey = storageKey
        };

        try
        {
            await using (var stream = file.OpenReadStream())
            {
                await blob.UploadAsync(
                    stream,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = "application/pdf"
                        }
                    },
                    ct);
            }

            _context.Materials.Add(material);
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteBlobBestEffortAsync(storageKey);
            throw;
        }

        return CreatedAtAction(
            nameof(GetMaterial),
            new { id = material.Id },
            MaterialResponse.FromMaterial(material));
    }

    // POST /api/materials/5/cover
    [Authorize(Roles = "Teacher")]
    [HttpPost("{id:int}/cover")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadCover(
        int id,
        [FromForm] MaterialCoverUploadRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var material = await _context.Materials
            .AsTracking()
            .SingleOrDefaultAsync(m =>
                m.Id == id &&
                m.Teacher.UserId == currentUserId, ct);

        if (material == null)
            return NotFound();

        var file = request.File;

        if (file == null || file.Length == 0)
            return BadRequest("A cover image is required.");

        if (file.Length > MaxCoverBytes)
            return BadRequest("Cover exceeds the 5 MB limit.");

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
            return BadRequest("Only PNG and JPEG images are supported.");

        var contentType = extension == ".png"
            ? "image/png"
            : "image/jpeg";

        var storageKey = $"covers/{Guid.NewGuid():N}{extension}";
        var blob = _container.GetBlobClient(storageKey);

        var previousCoverKey = material.CoverStorageKey;

        try
        {
            await using (var stream = file.OpenReadStream())
            {
                await blob.UploadAsync(
                    stream,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders
                        {
                            ContentType = contentType
                        }
                    },
                    ct);
            }

            material.CoverStorageKey = storageKey;
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteBlobBestEffortAsync(storageKey);
            throw;
        }

        await DeleteBlobBestEffortAsync(previousCoverKey);

        return NoContent();
    }

    // GET /api/materials/5/download
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> DownloadMaterial(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var material = await GetAccessibleMaterials(currentUserId)
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.FileName,
                m.FileStorageKey
            })
            .FirstOrDefaultAsync(ct);

        if (material == null)
            return NotFound();

        var blob = _container.GetBlobClient(
            material.FileStorageKey);

        try
        {
            var download = await blob.DownloadStreamingAsync(
                cancellationToken: ct);

            return File(
                download.Value.Content,
                "application/pdf",
                material.FileName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogError(
                ex,
                "Blob missing for material {MaterialId}",
                id);

            return Problem(
                detail: "The stored file is unavailable.",
                statusCode: 500);
        }
    }

    // DELETE /api/materials/5
    [Authorize(Roles = "Teacher")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMaterial(
        int id,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var material = await _context.Materials
            .AsTracking()
            .SingleOrDefaultAsync(m =>
                m.Id == id &&
                m.Teacher.UserId == currentUserId, ct);

        if (material == null)
            return NotFound();

        _context.Materials.Remove(material);
        await _context.SaveChangesAsync(ct);

        await DeleteBlobBestEffortAsync(material.FileStorageKey);
        await DeleteBlobBestEffortAsync(material.CoverStorageKey);

        return NoContent();
    }

    private IQueryable<Material> GetAccessibleMaterials(
        Guid currentUserId)
    {
        var query = _context.Materials.AsNoTracking();

        if (User.IsInRole("Teacher"))
        {
            return query.Where(m =>
                m.Teacher.UserId == currentUserId);
        }

        if (User.IsInRole("Student"))
        {
            return query.Where(m => _context.Students.Any(s =>
                s.UserId == currentUserId &&
                s.TeacherId == m.TeacherId));
        }

        return query.Where(m => false);
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

    private async Task DeleteBlobBestEffortAsync(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return;

        try
        {
            await _container.GetBlobClient(storageKey)
                .DeleteIfExistsAsync(
                    cancellationToken: CancellationToken.None);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not delete blob {StorageKey}",
                storageKey);
        }
    }
}