using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;
}
