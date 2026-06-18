using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class JobResultBlobStorage : IJobResultStorage
{
    private const string ContainerName = "job-seeker-results";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly BlobServiceClient _blobService;

    public JobResultBlobStorage(BlobServiceClient blobService)
    {
        _blobService = blobService;
    }

    public async Task<JobResult?> GetJobAsync(string customerId, string blobFileName)
    {
        // Blobs are stored under "{customerId}/{blobFileName}" in the results container.
        var blobName = $"{customerId}/{blobFileName}";
        var blobClient = _blobService.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToObjectFromJson<JobResult>(JsonOptions);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Link row exists but the blob is gone — skip it.
            return null;
        }
    }
}
