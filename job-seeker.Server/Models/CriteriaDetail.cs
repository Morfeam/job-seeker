using System.Text.Json.Serialization;

namespace job_seeker.Server.Models;

// The full criteria object as stored in blob "criteria/{customerId}/{criteriaId}.json"
// and as returned (with Id/Name merged in) by GET /criteria/get-criteria.
// Field names here are load-bearing: the Python scraper (main.py) reads
// "keywords", "location", "distance", "hoursOld", "jobTitleExclusionList",
// "companyNameExclusionList" verbatim.
// "technology", "minSalary", "experienceInYears", "jobType" are webapp-only —
// the scraper does not read them.
public class CriteriaDetail
{
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
}
