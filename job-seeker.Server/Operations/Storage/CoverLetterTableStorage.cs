using Azure;
using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class CoverLetterTableStorage : ICoverLetterStorage
{
    private const string TableName = "CoverLetters";
    private readonly TableServiceClient _tableService;

    public CoverLetterTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task<List<CoverLetter>> GetByCustomerIdAsync(string customerId)
    {
        var results = new List<CoverLetter>();
        var pages = GetTable().QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{customerId}'");
        await foreach (var entity in pages)
            results.Add(MapToCoverLetter(entity));
        return results;
    }

    public async Task<CoverLetter?> GetByIdAsync(string customerId, string coverLetterId)
    {
        try
        {
            var response = await GetTable().GetEntityAsync<TableEntity>(customerId, coverLetterId);
            return MapToCoverLetter(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<CoverLetter?> GetLatestForJobAsync(string customerId, string jobId)
    {
        var all = await GetByCustomerIdAsync(customerId);
        return all
            .Where(c => string.Equals(c.JobId, jobId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.CreatedDate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public async Task<List<CoverLetter>> GetRecentByCustomerIdAsync(string customerId, int max)
    {
        // Table Storage can't order server-side, so pull the partition and sort by Timestamp.
        var all = await GetByCustomerIdAsync(customerId);
        return all
            .OrderByDescending(c => c.CreatedDate ?? DateTimeOffset.MinValue)
            .Take(max)
            .ToList();
    }

    public async Task CreateAsync(string customerId, string coverLetterId, string name, string resumeId, string jobId)
    {
        var entity = new TableEntity(customerId, coverLetterId)
        {
            ["CoverLetterName"] = name,
            ["ResumeId"] = resumeId,
            ["JobId"] = jobId,
            ["IsPrime"] = false,
        };
        await GetTable().AddEntityAsync(entity);
    }

    public async Task SetPrimeAsync(string customerId, string coverLetterId)
    {
        var existingPrime = (await GetByCustomerIdAsync(customerId)).FirstOrDefault(c => c.IsPrime && c.Id != coverLetterId);
        if (existingPrime != null)
        {
            var clearEntity = new TableEntity(customerId, existingPrime.Id)
            {
                ETag = ETag.All,
                ["IsPrime"] = false,
            };
            await GetTable().UpdateEntityAsync(clearEntity, ETag.All, TableUpdateMode.Merge);
        }

        var setEntity = new TableEntity(customerId, coverLetterId)
        {
            ETag = ETag.All,
            ["IsPrime"] = true,
        };
        await GetTable().UpdateEntityAsync(setEntity, ETag.All, TableUpdateMode.Merge);
    }

    public async Task DeleteAsync(string customerId, string coverLetterId)
    {
        await GetTable().DeleteEntityAsync(customerId, coverLetterId);
    }

    private static CoverLetter MapToCoverLetter(TableEntity entity) => new()
    {
        Id = entity.RowKey,
        Name = entity.GetString("CoverLetterName") ?? string.Empty,
        ResumeId = entity.GetString("ResumeId") ?? string.Empty,
        JobId = entity.GetString("JobId") ?? string.Empty,
        IsPrime = entity.GetBoolean("IsPrime") ?? false,
        CreatedDate = entity.Timestamp,
    };
}
