using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using job_seeker.Server.Models;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace job_seeker.Server.Controllers;

[ApiController]
[Route("job-seeker")]
public class JobSeekerController : ControllerBase
{
    // Name of the container inside the Container App Job whose args we override.
    private const string ScraperContainerName = "job-seeker";

    // Most recent jobs returned by GET /get-jobs.
    private const int MaxJobs = 100;

    // Non-admin customers may trigger this many scrapes per UTC calendar day.
    private const int MaxDailyScrapes = 3;

    private readonly ICriteriaStorage _criteriaStorage;
    private readonly ICriteriaBlobStorage _criteriaBlobStorage;
    private readonly IJobLinkStorage _jobLinkStorage;
    private readonly IJobResultStorage _jobResultStorage;
    private readonly ICustomerStorage _customerStorage;
    private readonly ArmClient _armClient;
    private readonly IConfiguration _config;

    public JobSeekerController(
        ICriteriaStorage criteriaStorage,
        ICriteriaBlobStorage criteriaBlobStorage,
        IJobLinkStorage jobLinkStorage,
        IJobResultStorage jobResultStorage,
        ICustomerStorage customerStorage,
        ArmClient armClient,
        IConfiguration config)
    {
        _criteriaStorage = criteriaStorage;
        _criteriaBlobStorage = criteriaBlobStorage;
        _jobLinkStorage = jobLinkStorage;
        _jobResultStorage = jobResultStorage;
        _customerStorage = customerStorage;
        _armClient = armClient;
        _config = config;
    }

    private string? GetCustomerId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [Authorize]
    [HttpGet("get-job")]
    public async Task<IActionResult> GetJob([FromQuery] string blobFileName)
    {
        if (string.IsNullOrWhiteSpace(blobFileName))
            return BadRequest(new { error = "blobFileName is required." });

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var job = await _jobResultStorage.GetJobAsync(customerId, blobFileName);
        if (job == null)
            return NotFound();

        job.BlobFileName = blobFileName;
        return Ok(job);
    }

    [Authorize]
    [HttpGet("slim-criteria")]
    public async Task<IActionResult> GetSlimCriteria()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var criteria = await _criteriaStorage.GetByCustomerIdAsync(customerId);

        return Ok(new CriteriaResponse
        {
            Criteria = criteria
                .Select(c => new CriteriaItem { Id = c.Id, Name = c.Name })
                .ToList(),
        });
    }

    [Authorize]
    [HttpGet("get-jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var links = await _jobLinkStorage.GetRecentByCustomerIdAsync(customerId, MaxJobs);

        // Read each job's blob in parallel, preserving its criteria association.
        var loaded = await Task.WhenAll(links.Select(async link =>
        {
            var job = await _jobResultStorage.GetJobAsync(customerId, link.BlobFileName);
            if (job != null)
                job.BlobFileName = link.BlobFileName;
            return (link.CriteriaId, Job: job);
        }));

        // Resolve criteria id -> display name.
        var criteria = await _criteriaStorage.GetByCustomerIdAsync(customerId);
        var nameById = criteria
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var grouped = loaded
            .Where(x => x.Job != null)
            .GroupBy(x => x.CriteriaId)
            .ToList();

        var groups = await Task.WhenAll(grouped.Select(async g =>
        {
            var (keywords, experienceInYears) = await GetCriteriaDisplayDataAsync(customerId, g.Key);
            return new CriteriaJobsGroup
            {
                CriteriaId = g.Key,
                CriteriaName = nameById.TryGetValue(g.Key, out var name) && !string.IsNullOrEmpty(name)
                    ? name
                    : string.IsNullOrEmpty(g.Key) ? "Uncategorized" : g.Key,
                Keywords = keywords,
                ExperienceInYears = experienceInYears,
                Jobs = g.Select(x => x.Job!)
                    .OrderByDescending(j => ParsePostedDate(j.DatePosted))
                    .ToList(),
            };
        }));

        return Ok(groups);
    }

    [Authorize]
    [HttpPost("scrape")]
    public async Task<IActionResult> Scrape([FromBody] ScrapeRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        if (!request.ScrapeLinkedin && !request.ScrapeIndeed)
            return BadRequest(new { error = "Select at least one job source." });

        if (!await _customerStorage.HasScrapeQuotaRemainingAsync(customerId, MaxDailyScrapes))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"You've reached your daily limit of {MaxDailyScrapes} job scrapes. Please check back tomorrow." });

        var subscriptionId = _config["AZURE_SUBSCRIPTION_ID"];
        var resourceGroup = _config["AZURE_RESOURCE_GROUP"];
        var jobName = _config["CONTAINER_JOB_NAME"];
        if (string.IsNullOrEmpty(subscriptionId) || string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(jobName))
            return Conflict(new { error = "Job trigger is not configured." });

        var jobId = ContainerAppJobResource.CreateResourceIdentifier(subscriptionId, resourceGroup, jobName);
        var job = _armClient.GetContainerAppJobResource(jobId);

        try
        {
            // Load the job to reuse its container image and resource sizing.
            var jobData = (await job.GetAsync()).Value.Data;
            var sourceContainer = jobData.Template.Containers
                .FirstOrDefault(c => c.Name == ScraperContainerName)
                ?? jobData.Template.Containers.FirstOrDefault();

            if (sourceContainer == null)
                return Conflict(new { error = "Container App Job has no container template to run." });

            var execContainer = new JobExecutionContainer
            {
                Name = ScraperContainerName,
                Image = sourceContainer.Image,
                Resources = sourceContainer.Resources,
            };
            // The override replaces the container definition wholesale, so the job's
            // env vars (e.g. the AZURE_STORAGE_CONNECTION_STRING secretRef) must be
            // carried over or the scraper starts with a blank connection string.
            foreach (var env in sourceContainer.Env)
                execContainer.Env.Add(env);
            foreach (var arg in BuildArgs(request, customerId))
                execContainer.Args.Add(arg);

            var template = new ContainerAppJobExecutionTemplate();
            template.Containers.Add(execContainer);

            await job.StartAsync(WaitUntil.Started, template);

            // Count the scrape only once the trigger has actually succeeded — this
            // endpoint is fire-and-forget against an async Container App Job, so a
            // successful trigger (not job completion, which we have no visibility
            // into) is the only success signal available here.
            await _customerStorage.RecordScrapeAsync(customerId);

            return Accepted();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NotFound(new { error = "The job-seeker container job could not be found." });
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (RequestFailedException ex)
        {
            return Conflict(new { error = $"Failed to start job: {ex.Message}" });
        }
    }

    private static IEnumerable<string> BuildArgs(ScrapeRequest request, string customerId) =>
    [
        "--scrape-indeed", Bool(request.ScrapeIndeed),
        "--scrape-linkedin", Bool(request.ScrapeLinkedin),
        "--scrape-ziprecruiter", "false",
        "--customer-id", customerId,
        "--criteria-id", request.CriteriaId,
        "--enrich", "true",
    ];

    private static string Bool(bool value) => value ? "true" : "false";

    // Criteria fields needed to decorate a job group on the My Jobs page: the union of
    // keywords + technology (for tech-skill highlighting) and the criteria's
    // experienceInYears (for the "Match on Years of Experience" filter/display).
    // Fetches the criteria blob once for both, rather than two separate reads.
    private async Task<(List<string> Keywords, int? ExperienceInYears)> GetCriteriaDisplayDataAsync(string customerId, string criteriaId)
    {
        var detail = await _criteriaBlobStorage.GetAsync(customerId, criteriaId);
        if (detail == null)
            return ([], null);

        var keywords = detail.Keywords
            .Concat(detail.Technology)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (keywords, detail.ExperienceInYears);
    }

    // Jobs without a parseable date_posted sort last (oldest).
    private static DateTimeOffset ParsePostedDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
