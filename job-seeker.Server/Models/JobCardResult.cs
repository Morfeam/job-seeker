namespace job_seeker.Server.Models;

// A single job entry returned by GET /job-seeker/get-jobs, decorated with the
// criteria context needed to render it (since results are no longer grouped).
public class JobCardResult
{
    public JobResult Job { get; set; } = new();
    public string CriteriaId { get; set; } = string.Empty;
    public string CriteriaName { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];
    public int? ExperienceInYears { get; set; }
}
