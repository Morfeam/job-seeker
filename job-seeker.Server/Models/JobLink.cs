namespace job_seeker.Server.Models;

// A row from the JobLinks table mapping a scraped job to its blob and criteria.
public class JobLink
{
    public string JobId { get; set; } = string.Empty;
    public string BlobFileName { get; set; } = string.Empty;
    public string CriteriaId { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public int? CriteriaMatchScore { get; set; }
    public int? YearsExperience { get; set; }
}
