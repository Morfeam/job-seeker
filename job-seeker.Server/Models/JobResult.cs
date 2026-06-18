namespace job_seeker.Server.Models;

// Mirrors the NormalizedJob blob written by the Python scraper to the
// job-seeker-results container. Deserialized with snake_case naming policy.
public class JobResult
{
    public string? Id { get; set; }
    public string? Source { get; set; }
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public bool? IsRemote { get; set; }
    public string? Salary { get; set; }
    public string? JobType { get; set; }
    public string? DatePosted { get; set; }
    public string? Description { get; set; }
    public string? JobUrl { get; set; }
    public string? CompanyUrl { get; set; }
    public string? CompanyIndustry { get; set; }
    public int? YearsExperience { get; set; }
    public List<JobTechnology>? Technologies { get; set; }
    public List<string>? HardRequirements { get; set; }
    public List<string>? PreferredRequirements { get; set; }
    public List<string>? SoftRequirements { get; set; }
    public string? AiSummary { get; set; }
    public string? CreatedDt { get; set; }
    public string? LastUpdatedDt { get; set; }
    public string? FormattedUpdatedDt { get; set; }
}
