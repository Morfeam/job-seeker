namespace job_seeker.Server.Models;

public class ScrapeStatusResponse
{
    public bool HasRecentScrape { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanScrapeNow { get; set; }
}
