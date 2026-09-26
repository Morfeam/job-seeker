using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class SignUpRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(29)]
    public string Password { get; set; } = string.Empty;

    // Must be true — the controller rejects sign-ups that haven't accepted the Terms of Service.
    public bool AcceptedTerms { get; set; }

    [Required]
    [MaxLength(20)]
    public string TermsVersion { get; set; } = string.Empty;
}
