namespace job_seeker.Server.Models;

// A single Container App Job execution triggered for a customer's scrape. Unlike the
// old single-row-per-customer tracking, a customer can have several of these at once
// (e.g. an admin, who isn't subject to the one-at-a-time cooldown).
public class ScrapeExecution
{
    public string ExecutionName { get; set; } = string.Empty;
    public string CriteriaId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
