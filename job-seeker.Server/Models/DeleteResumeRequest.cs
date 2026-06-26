using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class DeleteResumeRequest
{
    [Required]
    public string ResumeId { get; set; } = string.Empty;
}
