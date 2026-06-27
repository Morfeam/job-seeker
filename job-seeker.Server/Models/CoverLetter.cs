namespace job_seeker.Server.Models;

// Internal table-row DTO for the CoverLetters table, mirrors Resume.cs.
public class CoverLetter
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResumeId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public bool IsPrime { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
}
