using Azure;
using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class ResumeTableStorage : IResumeStorage
{
    private const string TableName = "Resumes";
    private readonly TableServiceClient _tableService;

    public ResumeTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task<List<Resume>> GetByCustomerIdAsync(string customerId)
    {
        var results = new List<Resume>();
        var pages = GetTable().QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{customerId}'");
        await foreach (var entity in pages)
            results.Add(MapToResume(entity));
        return results;
    }

    public async Task<Resume?> GetByIdAsync(string customerId, string resumeId)
    {
        try
        {
            var response = await GetTable().GetEntityAsync<TableEntity>(customerId, resumeId);
            return MapToResume(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateAsync(string customerId, string resumeId, string name, string fileName, string? associatedCriteriaId)
    {
        var entity = new TableEntity(customerId, resumeId)
        {
            ["ResumeName"] = name,
            ["resumeFileName"] = fileName,
        };
        if (!string.IsNullOrEmpty(associatedCriteriaId))
            entity["associatedCriteriaId"] = associatedCriteriaId;

        await GetTable().AddEntityAsync(entity);
    }

    public async Task DeleteAsync(string customerId, string resumeId)
    {
        await GetTable().DeleteEntityAsync(customerId, resumeId);
    }

    private static Resume MapToResume(TableEntity entity) => new()
    {
        Id = entity.RowKey,
        Name = entity.GetString("ResumeName") ?? string.Empty,
        FileName = entity.GetString("resumeFileName") ?? string.Empty,
        AssociatedCriteriaId = entity.GetString("associatedCriteriaId"),
        UploadDate = entity.Timestamp,
    };
}
