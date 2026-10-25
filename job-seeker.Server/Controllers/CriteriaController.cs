using job_seeker.Server.Models;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace job_seeker.Server.Controllers;

[ApiController]
[Route("criteria")]
public class CriteriaController : ControllerBase
{
    private static readonly string[] AllowedJobTypes = ["fulltime", "parttime", "contract", "internship"];

    private readonly ICriteriaStorage _criteriaStorage;
    private readonly ICriteriaBlobStorage _criteriaBlobStorage;
    private readonly ILogger<CriteriaController> _logger;

    public CriteriaController(ICriteriaStorage criteriaStorage, ICriteriaBlobStorage criteriaBlobStorage, ILogger<CriteriaController> logger)
    {
        _criteriaStorage = criteriaStorage;
        _criteriaBlobStorage = criteriaBlobStorage;
        _logger = logger;
    }

    private string? GetCustomerId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [Authorize]
    [HttpGet("get-criteria")]
    public async Task<IActionResult> GetCriteria()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var rows = await _criteriaStorage.GetByCustomerIdAsync(customerId);

        var items = await Task.WhenAll(rows.Select(async row =>
        {
            var detail = await _criteriaBlobStorage.GetAsync(customerId, row.Id) ?? new CriteriaDetail();
            return CriteriaResponseItem.From(row.Id, row.Name, row.TimesScraped, row.CreatedDate, detail);
        }));

        return Ok(items);
    }

    [Authorize]
    [HttpPost("add")]
    public async Task<IActionResult> Add([FromBody] AddCriteriaRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!string.IsNullOrEmpty(request.JobType) && !AllowedJobTypes.Contains(request.JobType))
        {
            ModelState.AddModelError(nameof(request.JobType), $"jobType must be one of: {string.Join(", ", AllowedJobTypes)}.");
            return BadRequest(ModelState);
        }

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var existing = await _criteriaStorage.GetByCustomerIdAsync(customerId);
        if (existing.Any(c => string.Equals(c.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = "A criteria with that name already exists." });

        var criteriaId = Guid.NewGuid().ToString();

        try
        {
            await _criteriaBlobStorage.UpsertAsync(customerId, criteriaId, request.ToDetail());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save criteria");
            return Conflict(new { error = "Failed to save criteria. Please try again." });
        }

        try
        {
            await _criteriaStorage.CreateAsync(customerId, criteriaId, request.Name);
        }
        catch (Exception ex)
        {
            // Roll back the blob so we don't leave an orphan with no display name.
            await _criteriaBlobStorage.DeleteAsync(customerId, criteriaId);
            _logger.LogError(ex, "Failed to save criteria");
            return Conflict(new { error = "Failed to save criteria. Please try again." });
        }

        return Ok();
    }

    [Authorize]
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteCriteriaRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        try
        {
            // Idempotent: 404 is treated as already-deleted inside CriteriaBlobStorage.DeleteAsync.
            await _criteriaBlobStorage.DeleteAsync(customerId, request.CriteriaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete criteria");
            return Conflict(new { error = "Failed to delete criteria. Please try again." });
        }

        try
        {
            await _criteriaStorage.DeleteAsync(customerId, request.CriteriaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete criteria");
            return Conflict(new { error = "Failed to delete criteria. Please try again." });
        }

        return Ok();
    }
}
