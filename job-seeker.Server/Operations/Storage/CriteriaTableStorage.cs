using Azure;
using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class CriteriaTableStorage : ICriteriaStorage
{
    private const string TableName = "CustomerCriteria";
    private readonly TableServiceClient _tableService;

    public CriteriaTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task<List<CustomerCriteria>> GetByCustomerIdAsync(string customerId)
    {
        var results = new List<CustomerCriteria>();
        var pages = GetTable().QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{customerId}'");
        await foreach (var entity in pages)
            results.Add(MapToCriteria(entity));
        return results;
    }

    private static CustomerCriteria MapToCriteria(TableEntity entity) => new()
    {
        Id = entity.RowKey,
        Name = entity.GetString("CriteriaName") ?? string.Empty,
        TimesScraped = entity.GetInt32("timesScraped") ?? 0,
        CreatedDate = entity.GetDateTimeOffset("CreatedDate"),
    };

    public async Task CreateAsync(string customerId, string criteriaId, string name)
    {
        // Stamped explicitly (rather than relying on Table Storage's built-in Timestamp)
        // since that column reflects last-modified, not created — IncrementTimesScrapedAsync
        // updates this same row on every successful scrape, which would otherwise make the
        // "created on" date silently drift forward.
        var entity = new TableEntity(customerId, criteriaId)
        {
            ["CriteriaName"] = name,
            ["CreatedDate"] = DateTimeOffset.UtcNow,
        };
        await GetTable().AddEntityAsync(entity);
    }

    public async Task DeleteAsync(string customerId, string criteriaId)
    {
        await GetTable().DeleteEntityAsync(customerId, criteriaId);
    }

    public async Task IncrementTimesScrapedAsync(string customerId, string criteriaId)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            TableEntity entity;
            try
            {
                entity = await GetTable().GetEntityAsync<TableEntity>(customerId, criteriaId);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return; // Criteria was deleted since the scrape started — nothing to increment.
            }

            entity["timesScraped"] = (entity.GetInt32("timesScraped") ?? 0) + 1;

            try
            {
                await GetTable().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Concurrent modification — retry with a fresh read.
            }
        }
        // Exhausted retries under contention — non-critical stat, don't fail the caller over it.
    }
}
