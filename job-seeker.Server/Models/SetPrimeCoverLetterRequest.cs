using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class SetPrimeCoverLetterRequest
{
    [Required]
    public string CoverLetterId { get; set; } = string.Empty;
}
