namespace job_seeker.Server.Models;

public class CustomerCriteria
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TimesScraped { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
}
