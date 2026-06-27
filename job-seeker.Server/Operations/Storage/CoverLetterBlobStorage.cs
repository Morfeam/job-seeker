using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace job_seeker.Server.Operations.Storage;

// Reads/writes cover-letter blobs (container "cover-letters", blob "{customerId}/{coverLetterId}.txt")
// and generates time-limited read-only download links.
public class CoverLetterBlobStorage : ICoverLetterBlobStorage
{
    private const string ContainerName = "cover-letters";

    private readonly BlobServiceClient _blobService;

    public CoverLetterBlobStorage(BlobServiceClient blobService)
    {
        _blobService = blobService;
    }

    public async Task UploadAsync(string customerId, string coverLetterId, string content)
    {
        var containerClient = _blobService.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient(BlobName(customerId, coverLetterId));
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/plain; charset=utf-8" },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        await blobClient.UploadAsync(BinaryData.FromString(content), options);
    }

    public async Task DeleteAsync(string customerId, string coverLetterId)
    {
        await GetBlobClient(customerId, coverLetterId).DeleteIfExistsAsync();
    }

    public async Task<string?> DownloadTextAsync(string customerId, string coverLetterId)
    {
        var blobClient = GetBlobClient(customerId, coverLetterId);
        try
        {
            var response = await blobClient.DownloadContentAsync();
            return response.Value.Content.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Uri?> GenerateDownloadUrlAsync(string customerId, string coverLetterId, TimeSpan validFor)
    {
        var blobClient = GetBlobClient(customerId, coverLetterId);
        if (!await blobClient.ExistsAsync())
            return null;

        var expiresOn = DateTimeOffset.UtcNow.Add(validFor);

        if (blobClient.CanGenerateSasUri)
        {
            // Local/connection-string path: the client holds the account's shared key,
            // so a self-signed SAS can be generated directly.
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobClient.BlobContainerName,
                BlobName = blobClient.Name,
                Resource = "b",
                ExpiresOn = expiresOn,
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(sasBuilder);
        }

        // Azure/managed-identity path: no account key, so a user-delegation SAS
        // is required instead (requires the "Storage Blob Delegator" role).
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delegationKey = await _blobService.GetUserDelegationKeyAsync(startsOn, expiresOn);

        var delegationSasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
        };
        delegationSasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasQueryParameters = delegationSasBuilder.ToSasQueryParameters(delegationKey.Value, _blobService.AccountName);

        var uriBuilder = new BlobUriBuilder(blobClient.Uri) { Sas = sasQueryParameters };
        return uriBuilder.ToUri();
    }

    private static string BlobName(string customerId, string coverLetterId) => $"{customerId}/{coverLetterId}.txt";

    private BlobClient GetBlobClient(string customerId, string coverLetterId) =>
        _blobService.GetBlobContainerClient(ContainerName).GetBlobClient(BlobName(customerId, coverLetterId));
}
