using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace job_seeker.Server.Models;

public class AddResumeRequest
{
    [StringLength(100)]
    public string? Name { get; set; }

    [Required]
    public IFormFile File { get; set; } = null!;

    public string? AssociatedCriteriaId { get; set; }
}
