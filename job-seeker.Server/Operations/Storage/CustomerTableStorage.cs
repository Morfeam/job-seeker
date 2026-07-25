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
        var entity = await GetEntityByIdAsync(id);
        return entity == null ? null : MapToCustomer(entity);
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
        IsAdmin = entity.GetBoolean("IsAdmin") ?? false,
        CoverLetterCount = entity.GetInt32("CoverLetterCount") ?? 0,
        ScrapeCount = entity.GetInt32("ScrapeCount") ?? 0,
        LastScrapeDate = entity.GetDateTimeOffset("LastScrapeDate"),
        LastCoverLetterDate = entity.GetDateTimeOffset("LastCoverLetterDate"),
        LastScrapeExecutionName = entity.GetString("LastScrapeExecutionName"),
        LastScrapeStatus = entity.GetString("LastScrapeStatus"),
    };

    public async Task<bool> HasScrapeQuotaRemainingAsync(string customerId, int dailyLimit) =>
        await HasQuotaRemainingAsync(customerId, "ScrapeCount", "LastScrapeDate", dailyLimit);

    public async Task<bool> HasCoverLetterQuotaRemainingAsync(string customerId, int dailyLimit) =>
        await HasQuotaRemainingAsync(customerId, "CoverLetterCount", "LastCoverLetterDate", dailyLimit);

    public async Task RecordScrapeStartedAsync(string customerId, string executionName)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var entity = await GetEntityByIdAsync(customerId);
            if (entity == null)
                return;

            entity["ScrapeCount"] = TodayUsageCount(entity, "ScrapeCount", "LastScrapeDate") + 1;
            entity["LastScrapeDate"] = DateTimeOffset.UtcNow;
            entity["LastScrapeExecutionName"] = executionName;
            entity["LastScrapeStatus"] = "Running";

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
        // Exhausted retries under contention — same tradeoff as RecordUsageAsync: the trigger
        // itself already succeeded, so we don't fail the request over a stats-tracking race.
    }

    // Refreshes just the status column once a live Azure lookup resolves a "Running" scrape
    // to an end state. No ETag retry: this is an idempotent refresh of Azure's own current
    // truth, so a lost race under concurrent dashboard loads just means the next poll corrects it.
    public async Task UpdateScrapeStatusAsync(string customerId, string status)
    {
        var entity = await GetEntityByIdAsync(customerId);
        if (entity == null)
            return;

        entity["LastScrapeStatus"] = status;
        await GetTable().UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge);
    }

    public async Task RecordCoverLetterAsync(string customerId) =>
        await RecordUsageAsync(customerId, "CoverLetterCount", "LastCoverLetterDate");

    // Read-only: does this customer currently have quota remaining for the given
    // daily-capped action? Admins always have unlimited quota. The count column only
    // reflects usage "so far today" — if the date column isn't today (UTC), the
    // customer is treated as having used none of today's quota yet, regardless of
    // whatever count value happens to be stored from a previous day.
    private async Task<bool> HasQuotaRemainingAsync(string customerId, string countColumn, string dateColumn, int dailyLimit)
    {
        var entity = await GetEntityByIdAsync(customerId);
        if (entity == null)
            return false;

        if (entity.GetBoolean("IsAdmin") ?? false)
            return true;

        return TodayUsageCount(entity, countColumn, dateColumn) < dailyLimit;
    }

    // Records one successful use of a daily-capped action — increments the count
    // (resetting it first if the last use wasn't today, UTC) and stamps the date
    // column with UtcNow. Called only after the action itself has actually succeeded,
    // and applies to admins too since these columns double as usage stats for every
    // customer regardless of whether the cap is enforced for them.
    private async Task RecordUsageAsync(string customerId, string countColumn, string dateColumn)
    {
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var entity = await GetEntityByIdAsync(customerId);
            if (entity == null)
                return;

            entity[countColumn] = TodayUsageCount(entity, countColumn, dateColumn) + 1;
            entity[dateColumn] = DateTimeOffset.UtcNow;

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
        // Exhausted retries under contention — the action itself already succeeded, so
        // we don't fail the request over a stats-tracking race; the count is just
        // under-reported this one time.
    }

    private static int TodayUsageCount(TableEntity entity, string countColumn, string dateColumn)
    {
        var lastDate = entity.GetDateTimeOffset(dateColumn);
        var isSameUtcDay = lastDate.HasValue && lastDate.Value.UtcDateTime.Date == DateTimeOffset.UtcNow.UtcDateTime.Date;
        return isSameUtcDay ? (entity.GetInt32(countColumn) ?? 0) : 0;
    }

    private async Task<TableEntity?> GetEntityByIdAsync(string id)
    {
        // Scan is unavoidable here since PartitionKey is email. Acceptable for low-volume auth.
        var pages = GetTable().QueryAsync<TableEntity>(filter: $"RowKey eq '{id}'");
        await foreach (var entity in pages)
            return entity;
        return null;
    }
}
