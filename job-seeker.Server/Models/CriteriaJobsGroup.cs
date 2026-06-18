namespace job_seeker.Server.Models;

// A group of scraped jobs that share the same criteria, returned by GET /job-seeker/get-jobs.
public class CriteriaJobsGroup
{
    public string CriteriaId { get; set; } = string.Empty;
    public string CriteriaName { get; set; } = string.Empty;
    public List<JobResult> Jobs { get; set; } = [];
}
