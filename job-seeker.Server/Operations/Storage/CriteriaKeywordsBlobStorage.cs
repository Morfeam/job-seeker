using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;

namespace job_seeker.Server.Operations.Storage;

// Reads the "keywords" and "technology" arrays out of the same criteria.json blob
// the Python scraper uses (container "criteria", blob "{customerId}/{criteriaId}.json").
// "technology" is an addendum list used only for tech-skill highlighting on job cards —
// the scraper itself only ever reads "keywords".
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

            return ReadStringArray(doc.RootElement, "keywords")
                .Concat(ReadStringArray(doc.RootElement, "technology"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            return [];

        return array.EnumerateArray()
            .Select(v => v.GetString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!);
    }
}
