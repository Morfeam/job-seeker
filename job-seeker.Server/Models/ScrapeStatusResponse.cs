namespace job_seeker.Server.Models;

public class ScrapeExecutionItem
{
    public string ScrapeId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ScrapeStatusResponse
{
    public List<ScrapeExecutionItem> Executions { get; set; } = [];
    public bool CanScrapeNow { get; set; }
}
