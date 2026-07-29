using Azure;
using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class ScrapeExecutionTableStorage : IScrapeExecutionStorage
{
    private const string TableName = "ScrapeExecutions";
    private readonly TableServiceClient _tableService;

    public ScrapeExecutionTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task CreateAsync(string customerId, string executionName, string criteriaId, DateTimeOffset startedAt)
    {
        var entity = new TableEntity(customerId, executionName)
        {
            ["criteriaId"] = criteriaId,
            ["status"] = "Running",
            ["startedAt"] = startedAt,
        };
        await GetTable().AddEntityAsync(entity);
    }

    public async Task<List<ScrapeExecution>> GetRecentByCustomerIdAsync(string customerId, DateTimeOffset sinceUtc)
    {
        // Table Storage can't filter server-side on startedAt, so pull the whole
        // partition and filter/sort in memory.
        var executions = new List<ScrapeExecution>();
        var pages = GetTable().QueryAsync<TableEntity>(filter: $"PartitionKey eq '{customerId}'");
        await foreach (var entity in pages)
        {
            var startedAt = entity.GetDateTimeOffset("startedAt") ?? DateTimeOffset.MinValue;
            if (startedAt < sinceUtc)
                continue;

            executions.Add(new ScrapeExecution
            {
                ExecutionName = entity.RowKey,
                CriteriaId = entity.GetString("criteriaId") ?? string.Empty,
                Status = entity.GetString("status") ?? "Running",
                StartedAt = startedAt,
                EndedAt = entity.GetDateTimeOffset("endedAt"),
            });
        }

        return executions.OrderByDescending(e => e.StartedAt).ToList();
    }

    public async Task UpdateStatusAsync(string customerId, string executionName, string status, DateTimeOffset endedAt)
    {
        var entity = new TableEntity(customerId, executionName)
        {
            ["status"] = status,
            ["endedAt"] = endedAt,
        };
        await GetTable().UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge);
    }
}
