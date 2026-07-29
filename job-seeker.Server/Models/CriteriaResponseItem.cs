using System.Text.Json.Serialization;

namespace job_seeker.Server.Models;

// Flat response shape for GET /criteria/get-criteria: the table's Id/Name
// merged with the blob's CriteriaDetail fields into one object per criteria.
public class CriteriaResponseItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];

    [JsonPropertyName("jobTitleExclusionList")]
    public List<string> JobTitleExclusionList { get; set; } = [];

    [JsonPropertyName("companyNameExclusionList")]
    public List<string> CompanyNameExclusionList { get; set; } = [];

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("technology")]
    public List<string> Technology { get; set; } = [];

    [JsonPropertyName("distance")]
    public int? Distance { get; set; }

    [JsonPropertyName("minSalary")]
    public decimal? MinSalary { get; set; }

    [JsonPropertyName("experienceInYears")]
    public int? ExperienceInYears { get; set; }

    [JsonPropertyName("jobType")]
    public string? JobType { get; set; }

    [JsonPropertyName("hoursOld")]
    public int? HoursOld { get; set; }

    [JsonPropertyName("timesScraped")]
    public int TimesScraped { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }

    public static CriteriaResponseItem From(string id, string name, int timesScraped, DateTimeOffset? createdDate, CriteriaDetail detail) => new()
    {
        Id = id,
        Name = name,
        Keywords = detail.Keywords,
        JobTitleExclusionList = detail.JobTitleExclusionList,
        CompanyNameExclusionList = detail.CompanyNameExclusionList,
        Location = detail.Location,
        Technology = detail.Technology,
        Distance = detail.Distance,
        MinSalary = detail.MinSalary,
        ExperienceInYears = detail.ExperienceInYears,
        JobType = detail.JobType,
        HoursOld = detail.HoursOld,
        TimesScraped = timesScraped,
        CreatedDate = createdDate,
    };
}
