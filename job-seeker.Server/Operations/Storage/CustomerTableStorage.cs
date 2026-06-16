using Azure;
using Azure.Data.Tables;
using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public class CustomerTableStorage : ICustomerStorage
{
    private const string TableName = "Customers";
    private readonly TableServiceClient _tableService;

    public CustomerTableStorage(TableServiceClient tableService)
    {
        _tableService = tableService;
    }

    private TableClient GetTable() => _tableService.GetTableClient(TableName);

    public async Task<SecureCustomer?> GetByEmailAsync(string email)
    {
        var pages = GetTable().QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{email.ToLowerInvariant()}'");
        await foreach (var entity in pages)
            return MapToCustomer(entity);
        return null;
    }

    public async Task<SecureCustomer?> GetByIdAsync(string id)
    {
        // Scan is unavoidable here since PartitionKey is email. Acceptable for low-volume auth.
        var pages = GetTable().QueryAsync<TableEntity>(filter: $"RowKey eq '{id}'");
        await foreach (var entity in pages)
            return MapToCustomer(entity);
        return null;
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        var customer = await GetByEmailAsync(email);
        return customer != null;
    }

    public async Task CreateAsync(SecureCustomer customer)
    {
        var entity = new TableEntity(customer.Email.ToLowerInvariant(), customer.Id)
        {
            ["Id"] = customer.Id,
            ["Email"] = customer.Email,
            ["PasswordHash"] = customer.PasswordHash,
            ["PasswordSalt"] = customer.PasswordSalt,
            ["FirstName"] = customer.FirstName,
            ["LastName"] = customer.LastName,
            ["CreatedDate"] = customer.CreatedDate,
        };
        await GetTable().AddEntityAsync(entity);
    }

    public async Task UpdatePasswordAsync(string id, string email, string passwordHash, string passwordSalt)
    {
        var entity = new TableEntity(email.ToLowerInvariant(), id)
        {
            ETag = ETag.All,
            ["PasswordHash"] = passwordHash,
            ["PasswordSalt"] = passwordSalt,
        };
        await GetTable().UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge);
    }

    private static SecureCustomer MapToCustomer(TableEntity entity) => new()
    {
        Id = entity.GetString("Id") ?? string.Empty,
        Email = entity.GetString("Email") ?? string.Empty,
        PasswordHash = entity.GetString("PasswordHash") ?? string.Empty,
        PasswordSalt = entity.GetString("PasswordSalt") ?? string.Empty,
        FirstName = entity.GetString("FirstName") ?? string.Empty,
        LastName = entity.GetString("LastName") ?? string.Empty,
        CreatedDate = entity.GetString("CreatedDate") ?? string.Empty,
    };
}
