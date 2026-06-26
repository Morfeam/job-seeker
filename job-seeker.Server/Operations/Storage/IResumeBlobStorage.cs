namespace job_seeker.Server.Operations.Storage;

public interface IResumeBlobStorage
{
    Task UploadAsync(string customerId, string fileName, Stream content, string? contentType);
    Task DeleteAsync(string customerId, string fileName);
    Task<Uri?> GenerateDownloadUrlAsync(string customerId, string fileName, TimeSpan validFor);
}
