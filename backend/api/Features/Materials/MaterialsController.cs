using PolskiYol.Features.Realtime;
using PolskiYol.Data;
using System.ComponentModel.DataAnnotations;
using PolskiYol.Features;
using PolskiYol.Features.Materials;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

[Authorize(Roles = "Teacher,Student")]
[ApiController]
[Route("api/materials")]
public class MaterialsController : ControllerBase
{

    private readonly IApiData _data;
    private readonly RealtimeNotifications _notifications;
    private readonly IMaterialBlobStore _blobs;
    private readonly ILogger<MaterialsController> _logger;

    public MaterialsController(
        IApiData data,
        RealtimeNotifications notifications,
        IMaterialBlobStore blobs,
        ILogger<MaterialsController> logger)
    {
        _data = data;
        _notifications = notifications;
        _blobs = blobs;
        _logger = logger;
    }

    // GET /api/materials
    [HttpGet]
    public async Task<ActionResult<List<MaterialResponse>>> GetMaterials(
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery, Range(1, int.MaxValue)] int? beforeId = null,
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out Guid currentUserId))
            return Unauthorized();

        var materials = await GetAccessibleMaterials(currentUserId)
            .Where(m => !beforeId.HasValue || m.Id < beforeId.Value)
            .OrderByDescending(m => m.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        CursorPage.SetNextCursor(materials, limit, m => m.Id, Response);

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

        var teacher = await _data.Query<Teacher>()
            .SingleOrDefaultAsync(t => t.UserId == currentUserId, ct);

        if (teacher is null)
            return Forbid();

        var file = request.File;

        var (fileName, error) = await MaterialService.ValidatePdfAsync(file, ct);
        if (error is not null)
            return BadRequest(error);

        var storageKey = $"pdf/{Guid.NewGuid():N}.pdf";

        var material = new Material
        {
            TeacherId = teacher.Id,
            FileName = fileName!,
            Description = request.Description,
            FileStorageKey = storageKey
        };

        await MaterialService.SaveWithCleanupAsync(async () =>
        {
            await using (var stream = file.OpenReadStream())
            {
                await _blobs.UploadAsync(storageKey, stream, "application/pdf", ct);
            }

            _data.Add(material);
            await _data.SaveChangesAsync(ct);
        }, () => _blobs.DeleteOrQueueAsync(storageKey));

        await _notifications.MaterialCreatedAsync(material.TeacherId, material.Id);

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

        var material = await _data.Query<Material>(track: true)
            .SingleOrDefaultAsync(m =>
                m.Id == id &&
                m.Teacher.UserId == currentUserId, ct);

        if (material == null)
            return NotFound();

        var file = request.File;

        var (extension, contentType, error) = await MaterialService.ValidateCoverAsync(file, ct);
        if (error is not null)
            return BadRequest(error);

        var storageKey = $"covers/{Guid.NewGuid():N}{extension}";

        var previousCoverKey = material.CoverStorageKey;

        await MaterialService.SaveWithCleanupAsync(async () =>
        {
            await using (var stream = file!.OpenReadStream())
            {
                await _blobs.UploadAsync(storageKey, stream, contentType!, ct);
            }

            material.CoverStorageKey = storageKey;
            await _data.SaveChangesAsync(ct);
        }, () => _blobs.DeleteOrQueueAsync(storageKey));

        await _notifications.MaterialCoverUpdatedAsync(material.TeacherId, material.Id);
        await _blobs.DeleteOrQueueAsync(previousCoverKey);

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

        try
        {
            var stream = await MaterialService.OpenDownloadAsync(material?.FileStorageKey, _blobs.OpenAsync, ct);
            if (stream is null)
                return NotFound();

            return File(stream, "application/pdf", material!.FileName);
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

    [HttpGet("{id:int}/cover")]
    public async Task<IActionResult> GetCover(int id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var currentUserId))
            return Unauthorized();

        var storageKey = await GetAccessibleMaterials(currentUserId)
            .Where(m => m.Id == id)
            .Select(m => m.CoverStorageKey)
            .SingleOrDefaultAsync(ct);

        try
        {
            var stream = await MaterialService.OpenDownloadAsync(storageKey, _blobs.OpenAsync, ct);
            if (stream is null)
                return NotFound();

            var contentType = storageKey!.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(stream, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogError(ex, "Cover blob missing for material {MaterialId}", id);
            return Problem(detail: "The stored cover is unavailable.", statusCode: 500);
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

        var material = await _data.Query<Material>(track: true)
            .SingleOrDefaultAsync(m =>
                m.Id == id &&
                m.Teacher.UserId == currentUserId, ct);

        if (material == null)
            return NotFound();

        _data.Remove(material);
        await _data.SaveChangesAsync(ct);
        await _notifications.MaterialDeletedAsync(material.TeacherId, material.Id);

        await _blobs.DeleteOrQueueAsync(material.FileStorageKey);
        await _blobs.DeleteOrQueueAsync(material.CoverStorageKey);

        return NoContent();
    }

    private IQueryable<Material> GetAccessibleMaterials(Guid currentUserId) =>
        MaterialService.GetAccessibleMaterials(currentUserId, User, _data.Query<Material>(), _data.Query<Student>());
}
