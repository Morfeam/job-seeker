namespace job_seeker.Server.Models;

public class JobTechnology
{
    public string Name { get; set; } = string.Empty;
    public int? YearsExperience { get; set; }
    public int? PreferredYearsExperience { get; set; }
    public bool IsPreferred { get; set; }
}
