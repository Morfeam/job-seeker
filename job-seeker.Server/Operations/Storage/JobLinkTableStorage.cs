using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class JobLinkTableStorage : IJobLinkStorage
{
    private const string TableName = "JobLinks";
    private readonly TableServiceClient _tableService;

    public JobLinkTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task<List<JobLink>> GetRecentByCustomerIdAsync(string customerId, int max)
    {
        // Table Storage can't order server-side, so pull the partition and sort by Timestamp.
        var links = new List<JobLink>();
        var pages = GetTable().QueryAsync<TableEntity>(filter: $"PartitionKey eq '{customerId}'");
        await foreach (var entity in pages)
        {
            links.Add(new JobLink
            {
                JobId = entity.RowKey,
                BlobFileName = entity.GetString("BlobFileName") ?? string.Empty,
                CriteriaId = entity.GetString("criteriaId") ?? string.Empty,
                Timestamp = entity.Timestamp,
            });
        }

        return links
            .OrderByDescending(l => l.Timestamp ?? DateTimeOffset.MinValue)
            .Take(max)
            .ToList();
    }
}
