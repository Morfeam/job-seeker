using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class GenerateCoverLetterRequest
{
    [Required]
    public string ResumeId { get; set; } = string.Empty;

    [Required]
    public string JobBlobFileName { get; set; } = string.Empty;

    [Required]
    public string CvEnrichmentStyle { get; set; } = string.Empty;
}
