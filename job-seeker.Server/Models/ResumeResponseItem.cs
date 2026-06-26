using System.Text.Json.Serialization;

namespace job_seeker.Server.Models;

// Wire shape for GET /resume/get-resumes: the table row plus a generated SAS download URL.
public class ResumeResponseItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("uploadDate")]
    public DateTimeOffset? UploadDate { get; set; }

    [JsonPropertyName("associatedCriteriaId")]
    public string? AssociatedCriteriaId { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    public static ResumeResponseItem From(Resume row, string downloadUrl) => new()
    {
        Id = row.Id,
        Name = row.Name,
        FileName = row.FileName,
        UploadDate = row.UploadDate,
        AssociatedCriteriaId = row.AssociatedCriteriaId,
        DownloadUrl = downloadUrl,
    };
}
