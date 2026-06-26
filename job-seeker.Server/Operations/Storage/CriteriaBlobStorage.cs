using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

// Reads/writes the criteria.json blob the Python scraper also reads
// (container "criteria", blob "{customerId}/{criteriaId}.json").
public class CriteriaBlobStorage : ICriteriaBlobStorage
{
    private const string ContainerName = "criteria";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly BlobServiceClient _blobService;

    public CriteriaBlobStorage(BlobServiceClient blobService)
    {
        _blobService = blobService;
    }

    public async Task<CriteriaDetail?> GetAsync(string customerId, string criteriaId)
    {
        if (string.IsNullOrEmpty(criteriaId))
            return null;

        var blobClient = GetBlobClient(customerId, criteriaId);
        try
        {
            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToObjectFromJson<CriteriaDetail>(JsonOptions);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertAsync(string customerId, string criteriaId, CriteriaDetail detail)
    {
        var containerClient = _blobService.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, detail, JsonOptions);
        stream.Position = 0;
        await containerClient.GetBlobClient($"{customerId}/{criteriaId}.json").UploadAsync(stream, overwrite: true);
    }

    public async Task DeleteAsync(string customerId, string criteriaId)
    {
        await GetBlobClient(customerId, criteriaId).DeleteIfExistsAsync();
    }

    private BlobClient GetBlobClient(string customerId, string criteriaId) =>
        _blobService.GetBlobContainerClient(ContainerName).GetBlobClient($"{customerId}/{criteriaId}.json");
}
