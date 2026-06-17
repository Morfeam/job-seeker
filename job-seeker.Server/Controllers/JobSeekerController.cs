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

    private readonly ICriteriaStorage _criteriaStorage;
    private readonly ArmClient _armClient;
    private readonly IConfiguration _config;

    public JobSeekerController(
        ICriteriaStorage criteriaStorage,
        ArmClient armClient,
        IConfiguration config)
    {
        _criteriaStorage = criteriaStorage;
        _armClient = armClient;
        _config = config;
    }

    private string? GetCustomerId() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value;

    [Authorize]
    [HttpGet("criteria")]
    public async Task<IActionResult> GetCriteria()
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
            foreach (var arg in BuildArgs(request, customerId))
                execContainer.Args.Add(arg);

            var template = new ContainerAppJobExecutionTemplate();
            template.Containers.Add(execContainer);

            await job.StartAsync(WaitUntil.Started, template);

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
    ];

    private static string Bool(bool value) => value ? "true" : "false";
}
