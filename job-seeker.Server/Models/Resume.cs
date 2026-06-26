namespace job_seeker.Server.Models;

// Internal table-row DTO for the Resumes table, mirrors CustomerCriteria.cs.
public class Resume
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? AssociatedCriteriaId { get; set; }
    public DateTimeOffset? UploadDate { get; set; }
}
