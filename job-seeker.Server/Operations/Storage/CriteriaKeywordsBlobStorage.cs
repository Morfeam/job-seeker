using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;

namespace job_seeker.Server.Operations.Storage;

// Reads the "keywords" array out of the same criteria.json blob the Python
// scraper uses (container "criteria", blob "{customerId}/{criteriaId}.json").
public class CriteriaKeywordsBlobStorage : ICriteriaKeywordsStorage
{
    private const string ContainerName = "criteria";

    private readonly BlobServiceClient _blobService;

    public CriteriaKeywordsBlobStorage(BlobServiceClient blobService)
    {
        _blobService = blobService;
    }

    public async Task<List<string>> GetKeywordsAsync(string customerId, string criteriaId)
    {
        if (string.IsNullOrEmpty(criteriaId))
            return [];

        var blobName = $"{customerId}/{criteriaId}.json";
        var blobClient = _blobService.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);

        try
        {
            var response = await blobClient.DownloadContentAsync();
            using var doc = JsonDocument.Parse(response.Value.Content.ToStream());
            if (!doc.RootElement.TryGetProperty("keywords", out var keywords) || keywords.ValueKind != JsonValueKind.Array)
                return [];

            return keywords.EnumerateArray()
                .Select(k => k.GetString())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k!)
                .ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }
}
