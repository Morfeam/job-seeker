using System.Text.Json.Serialization;

namespace job_seeker.Server.Models;

// Wire shape for GET /cv/cover-letters: the table row plus a resolved resume name
// and a generated SAS download URL.
public class CoverLetterResponseItem
{
    [JsonPropertyName("coverLetterId")]
    public string CoverLetterId { get; set; } = string.Empty;

    [JsonPropertyName("coverLetterName")]
    public string CoverLetterName { get; set; } = string.Empty;

    [JsonPropertyName("coverLetterFileName")]
    public string CoverLetterFileName { get; set; } = string.Empty;

    [JsonPropertyName("relatedResumeId")]
    public string RelatedResumeId { get; set; } = string.Empty;

    [JsonPropertyName("relatedResumeName")]
    public string RelatedResumeName { get; set; } = string.Empty;

    [JsonPropertyName("isPrime")]
    public bool IsPrime { get; set; }

    [JsonPropertyName("blobDownloadURL")]
    public string BlobDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }

    public static CoverLetterResponseItem From(CoverLetter row, string relatedResumeName, string downloadUrl) => new()
    {
        CoverLetterId = row.Id,
        CoverLetterName = row.Name,
        CoverLetterFileName = $"{row.Id}.txt",
        RelatedResumeId = row.ResumeId,
        RelatedResumeName = relatedResumeName,
        IsPrime = row.IsPrime,
        BlobDownloadUrl = downloadUrl,
        CreatedDate = row.CreatedDate,
    };
}
