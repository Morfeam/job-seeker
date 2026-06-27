namespace job_seeker.Server.Operations.Storage;

public interface ICoverLetterBlobStorage
{
    Task UploadAsync(string customerId, string coverLetterId, string content);
    Task DeleteAsync(string customerId, string coverLetterId);
    Task<string?> DownloadTextAsync(string customerId, string coverLetterId);
    Task<Uri?> GenerateDownloadUrlAsync(string customerId, string coverLetterId, TimeSpan validFor);
}
