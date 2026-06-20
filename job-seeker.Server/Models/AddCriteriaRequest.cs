using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

public class AddCriteriaRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    [MaxCount(5)]
    public List<string> Keywords { get; set; } = [];

    [MaxCount(10)]
    public List<string> JobTitleExclusionList { get; set; } = [];

    [Required]
    public string Location { get; set; } = string.Empty;

    public List<string> Technology { get; set; } = [];

    public int? Distance { get; set; }

    public decimal? MinSalary { get; set; }

    public int? ExperienceInYears { get; set; }

    public string? JobType { get; set; }

    public int? HoursOld { get; set; }

    public CriteriaDetail ToDetail() => new()
    {
        Keywords = Keywords ?? [],
        JobTitleExclusionList = JobTitleExclusionList ?? [],
        Location = Location,
        Technology = Technology ?? [],
        Distance = Distance,
        MinSalary = MinSalary,
        ExperienceInYears = ExperienceInYears,
        JobType = JobType,
        HoursOld = HoursOld,
    };
}
