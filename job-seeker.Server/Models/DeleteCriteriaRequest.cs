using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class DeleteCriteriaRequest
{
    [Required]
    public string CriteriaId { get; set; } = string.Empty;
}
