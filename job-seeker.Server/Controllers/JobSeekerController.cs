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

    // Hard ceiling on GET /get-jobs's limit query param, regardless of what's requested.
    private const int MaxJobsLimit = 200;

    // Non-admin customers may trigger this many scrapes per UTC calendar day.
    private const int MaxDailyScrapes = 3;

    // A customer may not start another scrape within this many minutes of their last one,
    // regardless of that scrape's status — a simple time-based cooldown, not a live check
    // against the Container App Job.
    private const int ScrapeCooldownMinutes = 15;

    // Scrape status (and its error subtext) is only surfaced on the dashboard while the
    // triggering scrape started within this many hours; older scrapes show nothing.
    private const int ScrapeDisplayWindowHours = 24;

    private const string DefaultScrapeFailedMessage =
        "Oops! Something went wrong with your previous scrape. Please try again!";

    private readonly ICriteriaStorage _criteriaStorage;
    private readonly ICriteriaBlobStorage _criteriaBlobStorage;
    private readonly IJobLinkStorage _jobLinkStorage;
    private readonly IJobResultStorage _jobResultStorage;
    private readonly ICustomerStorage _customerStorage;
    private readonly IScrapeExecutionStorage _scrapeExecutionStorage;
    private readonly ArmClient _armClient;
    private readonly IConfiguration _config;

    public JobSeekerController(
        ICriteriaStorage criteriaStorage,
        ICriteriaBlobStorage criteriaBlobStorage,
        IJobLinkStorage jobLinkStorage,
        IJobResultStorage jobResultStorage,
        ICustomerStorage customerStorage,
        IScrapeExecutionStorage scrapeExecutionStorage,
        ArmClient armClient,
        IConfiguration config)
    {
        _criteriaStorage = criteriaStorage;
        _criteriaBlobStorage = criteriaBlobStorage;
        _jobLinkStorage = jobLinkStorage;
        _jobResultStorage = jobResultStorage;
        _customerStorage = customerStorage;
        _scrapeExecutionStorage = scrapeExecutionStorage;
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
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? criteriaId,
        [FromQuery] string sort = "recent",
        [FromQuery] bool matchYearsOfExperience = true,
        [FromQuery] int limit = 20)
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        limit = Math.Clamp(limit, 1, MaxJobsLimit);

        // Full per-customer link set, sorted/filtered on its cheap fields (Timestamp,
        // CriteriaMatchScore, YearsExperience, CriteriaId) — no blob reads yet. This is
        // also what makes the criteria filter dropdown always reflect every criteria the
        // customer has jobs for, not just whatever survived an earlier recency cut.
        var allLinks = await _jobLinkStorage.GetAllByCustomerIdAsync(customerId);

        var criteriaList = await _criteriaStorage.GetByCustomerIdAsync(customerId);
        var nameById = criteriaList
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        // One criteria-blob read per distinct criteria present (not per job).
        var distinctCriteriaIds = allLinks.Select(l => l.CriteriaId).Distinct().ToList();
        var displayDataById = new Dictionary<string, (List<string> Keywords, int? ExperienceInYears)>();
        foreach (var id in distinctCriteriaIds)
            displayDataById[id] = await GetCriteriaDisplayDataAsync(customerId, id);

        // Deleted criteria are excluded from the filter dropdown entirely (their jobs still
        // show up under "All criteria", just labeled "Deleted criteria" — see
        // ResolveCriteriaName) since there's nothing meaningful left to filter *by*.
        var availableCriteria = distinctCriteriaIds
            .Where(id => string.IsNullOrEmpty(id) || nameById.ContainsKey(id))
            .Select(id => new CriteriaItem { Id = id, Name = ResolveCriteriaName(id, nameById) })
            .ToList();

        IEnumerable<JobLink> filtered = allLinks;
        if (!string.IsNullOrEmpty(criteriaId) && criteriaId != "all")
            filtered = filtered.Where(l => l.CriteriaId == criteriaId);

        if (matchYearsOfExperience)
        {
            // Fail open: only exclude a link when both values are known and the job
            // requires more years than its own criteria specifies.
            filtered = filtered.Where(l =>
            {
                var experienceInYears = displayDataById.TryGetValue(l.CriteriaId, out var d) ? d.ExperienceInYears : null;
                return l.YearsExperience == null || experienceInYears == null || l.YearsExperience <= experienceInYears;
            });
        }

        filtered = sort == "score"
            ? filtered.OrderByDescending(l => l.CriteriaMatchScore ?? 0)
            : filtered.OrderByDescending(l => l.Timestamp ?? DateTimeOffset.MinValue);

        var filteredList = filtered.ToList();
        var page = filteredList.Take(limit).ToList();

        // Only the page actually being returned pays for a blob read.
        var jobCards = await Task.WhenAll(page.Select(async link =>
        {
            var job = await _jobResultStorage.GetJobAsync(customerId, link.BlobFileName);
            if (job == null)
                return null;
            job.BlobFileName = link.BlobFileName;

            // The blob's own CriteriaMatchScore reflects whichever criteria first
            // discovered this job — once a job matches more than one criteria, each has
            // its own score on the JobLinks row (link.CriteriaMatchScore), which is also
            // what sort=score orders by. Override so the displayed number always matches
            // the criteria this card is actually being shown under, never a stale value
            // from a different criteria's original discovery.
            job.CriteriaMatchScore = link.CriteriaMatchScore;

            var (keywords, experienceInYears) = displayDataById.TryGetValue(link.CriteriaId, out var d)
                ? d
                : ([], null);

            return new JobCardResult
            {
                Job = job,
                CriteriaId = link.CriteriaId,
                CriteriaName = ResolveCriteriaName(link.CriteriaId, nameById),
                Keywords = keywords,
                ExperienceInYears = experienceInYears,
            };
        }));

        return Ok(new GetJobsResponse
        {
            Jobs = jobCards.Where(j => j != null).Select(j => j!).ToList(),
            AvailableCriteria = availableCriteria,
            TotalMatched = filteredList.Count,
        });
    }

    // Empty criteriaId means the job was never attributed to a specific criteria (e.g. a
    // default-criteria CLI scrape) — distinct from a non-empty id that no longer resolves,
    // which means the criteria itself was deleted after the job was linked to it.
    private static string ResolveCriteriaName(string criteriaId, Dictionary<string, string> nameById)
    {
        if (string.IsNullOrEmpty(criteriaId))
            return "Uncategorized";

        return nameById.TryGetValue(criteriaId, out var name) && !string.IsNullOrEmpty(name)
            ? name
            : "Deleted criteria";
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

        var customer = await _customerStorage.GetByIdAsync(customerId);
        if (customer?.IsAdmin != true)
        {
            var recentExecutions = await _scrapeExecutionStorage.GetRecentByCustomerIdAsync(
                customerId, DateTimeOffset.UtcNow.AddMinutes(-ScrapeCooldownMinutes));
            if (recentExecutions.Count > 0)
                return Conflict(new { error = "A job scrape is already in progress. Please wait for it to finish before starting another." });
        }

        if (!await _customerStorage.HasScrapeQuotaRemainingAsync(customerId, MaxDailyScrapes))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"You've reached your daily limit of {MaxDailyScrapes} job scrapes. Please check back tomorrow." });

        var job = GetContainerAppJob();
        if (job == null)
            return Conflict(new { error = "Job trigger is not configured." });

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

            // WaitUntil.Completed here waits only for the execution to be created and
            // assigned a name — NOT for the scrape itself to finish. The LRO's completed
            // value type (ContainerAppJobExecutionBase) carries just Id/Name, unlike
            // ContainerAppJobExecutionData (Status/StartOn/EndOn) used for status polling
            // below, confirming "completed" means "registered", not "job run finished".
            var startResult = await job.StartAsync(WaitUntil.Completed, template);

            // Count the scrape only once the trigger has actually succeeded — this
            // endpoint is fire-and-forget against an async Container App Job, so a
            // successful trigger (not job completion) is the only success signal
            // available here. The execution row lets /scrape-status look up completion
            // later via the Container App Job's own execution history.
            await _customerStorage.RecordScrapeAsync(customerId);
            await _scrapeExecutionStorage.CreateAsync(customerId, startResult.Value.Name, request.CriteriaId, DateTimeOffset.UtcNow);

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

    [Authorize]
    [HttpGet("scrape-status")]
    public async Task<IActionResult> GetScrapeStatus()
    {
        var customerId = GetCustomerId();
        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var customer = await _customerStorage.GetByIdAsync(customerId);
        var recent = await _scrapeExecutionStorage.GetRecentByCustomerIdAsync(
            customerId, DateTimeOffset.UtcNow.AddHours(-ScrapeDisplayWindowHours));

        // Admins are never gated by the cooldown; everyone else is blocked only while a
        // scrape they started is still within the cooldown window, regardless of its status.
        var canScrapeNow = customer?.IsAdmin == true ||
            !recent.Any(e => DateTimeOffset.UtcNow - e.StartedAt < TimeSpan.FromMinutes(ScrapeCooldownMinutes));

        var items = new List<ScrapeExecutionItem>();
        foreach (var execution in recent)
        {
            var status = execution.Status == "Running"
                ? await ResolveExecutionStatusAsync(customerId, execution)
                : execution.Status;

            if (status == null)
                continue; // Stuck past the cooldown window — hidden, row left untouched.

            items.Add(new ScrapeExecutionItem
            {
                ScrapeId = execution.ExecutionName,
                Status = status,
                StartedAt = execution.StartedAt,
                EndedAt = execution.EndedAt,
                ErrorMessage = status == "Failed" ? DefaultScrapeFailedMessage : null,
            });
        }

        return Ok(new ScrapeStatusResponse { Executions = items, CanScrapeNow = canScrapeNow });
    }

    // A stored "Running" execution is only ever refreshed here, never persisted as "Running"
    // itself. Returns null when the run is stuck past the cooldown window — per design, we
    // never rewrite the row in that case, we just stop surfacing it (the table still shows
    // "Running" underneath, but nothing here or on a later call displays it).
    private async Task<string?> ResolveExecutionStatusAsync(string customerId, ScrapeExecution execution)
    {
        if (DateTimeOffset.UtcNow - execution.StartedAt >= TimeSpan.FromMinutes(ScrapeCooldownMinutes))
            return null;

        var job = GetContainerAppJob();
        if (job == null)
            return "Running";

        try
        {
            var result = await job.GetContainerAppJobExecutionAsync(execution.ExecutionName);
            var resolved = MapExecutionState(result.Value.Data.Status);
            if (resolved != "Running")
            {
                var endedAt = DateTimeOffset.UtcNow;
                await _scrapeExecutionStorage.UpdateStatusAsync(customerId, execution.ExecutionName, resolved, endedAt);
                execution.EndedAt = endedAt;

                // Credit the criteria this scrape was actually run for — only on a clean
                // Succeeded resolution, and only once (this branch only runs while the
                // stored status is still "Running", so a later poll of the same execution
                // short-circuits above and can't double-count it).
                if (resolved == "Succeeded" && !string.IsNullOrEmpty(execution.CriteriaId))
                    await _criteriaStorage.IncrementTimesScrapedAsync(customerId, execution.CriteriaId);
            }

            return resolved;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Execution record no longer exists (e.g. purged) — leave status as Running
            // rather than guessing at an outcome; the 15-minute cutoff will hide it soon.
            return "Running";
        }
    }

    // Collapses Azure's native execution states down to the 3 this feature tracks.
    // Processing is still in flight, so it counts as Running; anything that isn't a clean
    // Succeeded (Stopped, Degraded, Unknown, Failed) is surfaced as Failed so the generic
    // subtext points the customer to retry and points devs at the Container App Job logs.
    private static string MapExecutionState(JobExecutionRunningState? state)
    {
        if (state == JobExecutionRunningState.Succeeded)
            return "Succeeded";
        if (state == JobExecutionRunningState.Running || state == JobExecutionRunningState.Processing)
            return "Running";
        return "Failed";
    }

    private ContainerAppJobResource? GetContainerAppJob()
    {
        var subscriptionId = _config["AZURE_SUBSCRIPTION_ID"];
        var resourceGroup = _config["AZURE_RESOURCE_GROUP"];
        var jobName = _config["CONTAINER_JOB_NAME"];
        if (string.IsNullOrEmpty(subscriptionId) || string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(jobName))
            return null;

        var jobId = ContainerAppJobResource.CreateResourceIdentifier(subscriptionId, resourceGroup, jobName);
        return _armClient.GetContainerAppJobResource(jobId);
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
}
