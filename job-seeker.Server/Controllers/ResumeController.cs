using Azure;
using job_seeker.Server.Models;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace job_seeker.Server.Controllers;

[ApiController]
[Route("resume")]
public class ResumeController : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".pdf", ".md", ".txt", ".html", ".docx"];
    private const long MaxFileSizeBytes = 5L * 1024 * 1024;
    private static readonly TimeSpan DownloadLinkValidFor = TimeSpan.FromHours(1);

    private readonly IResumeStorage _resumeStorage;
    private readonly IResumeBlobStorage _resumeBlobStorage;

    public ResumeController(IResumeStorage resumeStorage, IResumeBlobStorage resumeBlobStorage)
    {
        _resumeStorage = resumeStorage;
        _resumeBlobStorage = resumeBlobStorage;
    }

    private string? GetCustomerId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [Authorize]
    [HttpGet("get-resumes")]
    public async Task<IActionResult> GetResumes()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var rows = await _resumeStorage.GetByCustomerIdAsync(customerId);

        var items = await Task.WhenAll(rows.Select(async row =>
        {
            var url = await _resumeBlobStorage.GenerateDownloadUrlAsync(customerId, row.FileName, DownloadLinkValidFor);
            return url == null ? null : ResumeResponseItem.From(row, url.ToString());
        }));

        return Ok(items.Where(i => i != null));
    }

    [Authorize]
    [HttpPost("add")]
    public async Task<IActionResult> Add([FromForm] AddResumeRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (request.File.Length == 0)
            return BadRequest(new { error = "File is empty." });

        if (request.File.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "File exceeds the 5 MB size limit." });

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return BadRequest(new { error = $"Unsupported file type. Allowed: {string.Join(", ", AllowedExtensions)}" });

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var resolvedName = string.IsNullOrWhiteSpace(request.Name) ? request.File.FileName : request.Name.Trim();

        var existing = await _resumeStorage.GetByCustomerIdAsync(customerId);
        if (existing.Any(r =>
                string.Equals(r.Name, resolvedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.FileName, request.File.FileName, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = "A resume with that name or file name already exists." });

        var resumeId = Guid.NewGuid().ToString();

        try
        {
            using var stream = request.File.OpenReadStream();
            await _resumeBlobStorage.UploadAsync(customerId, request.File.FileName, stream, request.File.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return Conflict(new { error = "A resume with that file name already exists." });
        }
        catch (Exception ex)
        {
            return Conflict(new { error = $"Failed to upload resume: {ex.Message}" });
        }

        try
        {
            await _resumeStorage.CreateAsync(customerId, resumeId, resolvedName, request.File.FileName, request.AssociatedCriteriaId);
        }
        catch (Exception ex)
        {
            // Roll back the blob so we don't leave an orphan with no table row.
            await _resumeBlobStorage.DeleteAsync(customerId, request.File.FileName);
            return Conflict(new { error = $"Failed to save resume: {ex.Message}" });
        }

        return Ok();
    }

    [Authorize]
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteResumeRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var row = await _resumeStorage.GetByIdAsync(customerId, request.ResumeId);
        if (row == null)
            return NotFound();

        try
        {
            // Idempotent: a missing blob is treated as already-deleted.
            await _resumeBlobStorage.DeleteAsync(customerId, row.FileName);
        }
        catch (Exception ex)
        {
            return Conflict(new { error = $"Failed to delete resume: {ex.Message}" });
        }

        try
        {
            await _resumeStorage.DeleteAsync(customerId, request.ResumeId);
        }
        catch (Exception ex)
        {
            return Conflict(new { error = $"Failed to delete resume: {ex.Message}" });
        }

        return Ok();
    }
}
