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
    };
}
