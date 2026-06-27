using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace job_seeker.Server.Operations.Storage;

// Reads/writes resume blobs (container "resumes", blob "{customerId}/{fileName}")
// and generates time-limited read-only download links.
public class ResumeBlobStorage : IResumeBlobStorage
{
    private const string ContainerName = "resumes";

    private readonly BlobServiceClient _blobService;

    public ResumeBlobStorage(BlobServiceClient blobService)
    {
        _blobService = blobService;
    }

    public async Task UploadAsync(string customerId, string fileName, Stream content, string? contentType)
    {
        var containerClient = _blobService.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient($"{customerId}/{fileName}");
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            // Equivalent to overwrite: false — fail with a 409 if the blob already
            // exists, as defense-in-depth against the table-uniqueness check missing
            // a half-written state.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        await blobClient.UploadAsync(content, options);
    }

    public async Task DeleteAsync(string customerId, string fileName)
    {
        await GetBlobClient(customerId, fileName).DeleteIfExistsAsync();
    }

    public async Task<Uri?> GenerateDownloadUrlAsync(string customerId, string fileName, TimeSpan validFor)
    {
        var blobClient = GetBlobClient(customerId, fileName);
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

    public async Task<(byte[] Content, string? ContentType)?> DownloadAsync(string customerId, string fileName)
    {
        var blobClient = GetBlobClient(customerId, fileName);
        try
        {
            var response = await blobClient.DownloadContentAsync();
            return (response.Value.Content.ToArray(), response.Value.Details.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private BlobClient GetBlobClient(string customerId, string fileName) =>
        _blobService.GetBlobContainerClient(ContainerName).GetBlobClient($"{customerId}/{fileName}");
}
