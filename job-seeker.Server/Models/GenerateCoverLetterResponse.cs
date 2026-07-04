using System.Text.Json.Serialization;

namespace job_seeker.Server.Models;

public class GenerateCoverLetterResponse
{
    [JsonPropertyName("resumeId")]
    public string ResumeId { get; set; } = string.Empty;

    [JsonPropertyName("jobBlobFileName")]
    public string JobBlobFileName { get; set; } = string.Empty;

    [JsonPropertyName("coverLetterId")]
    public string CoverLetterId { get; set; } = string.Empty;

    [JsonPropertyName("coverLetterName")]
    public string CoverLetterName { get; set; } = string.Empty;
}
