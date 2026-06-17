using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class ScrapeRequest
{
    [Required]
    public bool ScrapeLinkedin { get; set; }

    [Required]
    public bool ScrapeIndeed { get; set; }

    public bool ScrapeZiprecruiter { get; set; } = false;

    [Required]
    public string CriteriaId { get; set; } = string.Empty;
}
