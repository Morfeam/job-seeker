using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class SignInRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
