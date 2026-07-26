namespace job_seeker.Server.Models;

public class GetJobsResponse
{
    public List<JobCardResult> Jobs { get; set; } = [];
    public List<CriteriaItem> AvailableCriteria { get; set; } = [];

    // Count after filtering (criteria + years-of-experience) but before the limit cut —
    // lets the frontend note when a search was capped short of the full matching set.
    public int TotalMatched { get; set; }
}
