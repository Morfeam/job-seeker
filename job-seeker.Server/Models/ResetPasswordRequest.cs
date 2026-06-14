using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(29)]
    public string Password { get; set; } = string.Empty;
}
