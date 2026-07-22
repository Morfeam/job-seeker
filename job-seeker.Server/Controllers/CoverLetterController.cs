using job_seeker.Server.Models;
using job_seeker.Server.Operations.Ai;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace job_seeker.Server.Controllers;

[ApiController]
[Route("cv")]
public class CoverLetterController : ControllerBase
{
    private static readonly string[] AllowedStyles = ["None", "Prime", "All"];
    private static readonly TimeSpan DownloadLinkValidFor = TimeSpan.FromHours(1);
    private const int MaxAllStyleExamples = 5;

    // Non-admin customers may generate this many cover letters per UTC calendar day.
    private const int MaxDailyCoverLetters = 3;

    private readonly ICoverLetterStorage _coverLetterStorage;
    private readonly ICoverLetterBlobStorage _coverLetterBlobStorage;
    private readonly IResumeStorage _resumeStorage;
    private readonly IResumeBlobStorage _resumeBlobStorage;
    private readonly IJobResultStorage _jobResultStorage;
    private readonly ICustomerStorage _customerStorage;
    private readonly IAnthropicClient _anthropicClient;

    public CoverLetterController(
        ICoverLetterStorage coverLetterStorage,
        ICoverLetterBlobStorage coverLetterBlobStorage,
        IResumeStorage resumeStorage,
        IResumeBlobStorage resumeBlobStorage,
        IJobResultStorage jobResultStorage,
        ICustomerStorage customerStorage,
        IAnthropicClient anthropicClient)
    {
        _coverLetterStorage = coverLetterStorage;
        _coverLetterBlobStorage = coverLetterBlobStorage;
        _resumeStorage = resumeStorage;
        _resumeBlobStorage = resumeBlobStorage;
        _jobResultStorage = jobResultStorage;
        _customerStorage = customerStorage;
        _anthropicClient = anthropicClient;
    }

    private string? GetCustomerId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [Authorize]
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateCoverLetterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!AllowedStyles.Contains(request.CvEnrichmentStyle, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"cvEnrichmentStyle must be one of: {string.Join(", ", AllowedStyles)}." });

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        if (!await _customerStorage.HasCoverLetterQuotaRemainingAsync(customerId, MaxDailyCoverLetters))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"You've reached your daily limit of {MaxDailyCoverLetters} cover letters. Please check back tomorrow." });

        if (!Guid.TryParse(request.ResumeId, out _))
            return BadRequest(new { error = "resumeId must be a valid guid." });

        var resume = await _resumeStorage.GetByIdAsync(customerId, request.ResumeId);
        if (resume == null)
            return NotFound(new { error = "Resume not found." });

        if (Path.GetExtension(resume.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "This resume is a .docx file, which isn't supported for AI cover-letter generation. Re-upload it as .pdf, .txt, .md, or .html to use it here." });

        var job = await _jobResultStorage.GetJobAsync(customerId, request.JobBlobFileName);
        if (job == null)
            return NotFound(new { error = "Job not found." });

        var existing = await _coverLetterStorage.GetByCustomerIdAsync(customerId);

        var styleExamples = new List<string>();
        if (string.Equals(request.CvEnrichmentStyle, "Prime", StringComparison.OrdinalIgnoreCase))
        {
            var primeRow = existing.FirstOrDefault(c => c.IsPrime);
            if (primeRow == null)
                return NotFound(new { error = "You don't have a Prime cover letter yet. Generate one with style \"None\" first, then mark it as Prime." });

            var primeText = await _coverLetterBlobStorage.DownloadTextAsync(customerId, primeRow.Id);
            if (primeText != null)
                styleExamples.Add(primeText);
        }
        else if (string.Equals(request.CvEnrichmentStyle, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Count == 0)
                return NotFound(new { error = "You don't have any cover letters yet." });

            var recent = existing.OrderByDescending(c => c.CreatedDate ?? DateTimeOffset.MinValue).Take(MaxAllStyleExamples);
            var texts = await Task.WhenAll(recent.Select(c => _coverLetterBlobStorage.DownloadTextAsync(customerId, c.Id)));
            styleExamples.AddRange(texts.Where(t => t != null)!);
        }

        var resumeDownload = await _resumeBlobStorage.DownloadAsync(customerId, resume.FileName);
        if (resumeDownload == null)
            return Conflict(new { error = "Resume file could not be found in storage." });

        string letterText;
        try
        {
            letterText = await _anthropicClient.GenerateCoverLetterAsync(
                job.Title ?? string.Empty,
                job.Company ?? string.Empty,
                job.Description ?? string.Empty,
                resume.FileName,
                resumeDownload.Value.Content,
                styleExamples);
        }
        catch (Exception ex)
        {
            return Conflict(new { error = $"Failed to generate cover letter: {ex.Message}" });
        }

        var coverLetterId = Guid.NewGuid().ToString();
        var coverLetterName = $"Cover letter {existing.Count + 1} for {job.Title}";

        try
        {
            await _coverLetterBlobStorage.UploadAsync(customerId, coverLetterId, letterText);
        }
        catch (Exception ex)
        {
            return Conflict(new { error = $"Failed to save cover letter: {ex.Message}" });
        }

        try
        {
            await _coverLetterStorage.CreateAsync(customerId, coverLetterId, coverLetterName, resume.Id, job.Id ?? string.Empty);
        }
        catch (Exception ex)
        {
            // Roll back the blob so we don't leave an orphan with no table row.
            await _coverLetterBlobStorage.DeleteAsync(customerId, coverLetterId);
            return Conflict(new { error = $"Failed to save cover letter: {ex.Message}" });
        }

        await _customerStorage.RecordCoverLetterAsync(customerId);

        return Ok(new GenerateCoverLetterResponse
        {
            ResumeId = resume.Id,
            JobBlobFileName = request.JobBlobFileName,
            CoverLetterId = coverLetterId,
            CoverLetterName = coverLetterName,
        });
    }

    [Authorize]
    [HttpGet("cover-letters")]
    public async Task<IActionResult> GetCoverLetters()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var rows = await _coverLetterStorage.GetRecentByCustomerIdAsync(customerId, 3);

        var items = await Task.WhenAll(rows.Select(async row =>
        {
            var resume = await _resumeStorage.GetByIdAsync(customerId, row.ResumeId);
            var url = await _coverLetterBlobStorage.GenerateDownloadUrlAsync(customerId, row.Id, DownloadLinkValidFor);
            return url == null ? null : CoverLetterResponseItem.From(row, resume?.Name ?? string.Empty, url.ToString());
        }));

        return Ok(items.Where(i => i != null));
    }

    [Authorize]
    [HttpGet("latest-for-job")]
    public async Task<IActionResult> GetLatestForJob([FromQuery] string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest(new { error = "jobId is required." });

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var row = await _coverLetterStorage.GetLatestForJobAsync(customerId, jobId);
        if (row == null)
            return NotFound();

        var resume = await _resumeStorage.GetByIdAsync(customerId, row.ResumeId);
        var url = await _coverLetterBlobStorage.GenerateDownloadUrlAsync(customerId, row.Id, DownloadLinkValidFor);
        if (url == null)
            return NotFound();

        return Ok(CoverLetterResponseItem.From(row, resume?.Name ?? string.Empty, url.ToString()));
    }

    [Authorize]
    [HttpGet("cover-letter-text/{coverLetterId}")]
    public async Task<IActionResult> GetCoverLetterText(string coverLetterId)
    {
        if (string.IsNullOrWhiteSpace(coverLetterId))
            return BadRequest(new { error = "coverLetterId is required." });

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        // Confirm ownership before serving the content.
        var row = await _coverLetterStorage.GetByIdAsync(customerId, coverLetterId);
        if (row == null)
            return NotFound();

        var text = await _coverLetterBlobStorage.DownloadTextAsync(customerId, coverLetterId);
        if (text == null)
            return NotFound();

        return Ok(new { text });
    }

    [Authorize]
    [HttpPost("set-prime")]
    public async Task<IActionResult> SetPrime([FromBody] SetPrimeCoverLetterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var row = await _coverLetterStorage.GetByIdAsync(customerId, request.CoverLetterId);
        if (row == null)
            return NotFound();

        await _coverLetterStorage.SetPrimeAsync(customerId, request.CoverLetterId);

        return Ok();
    }
}
