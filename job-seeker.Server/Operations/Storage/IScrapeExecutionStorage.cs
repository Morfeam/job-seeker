using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface IScrapeExecutionStorage
{
    // Always inserts a new row — never upserts over an existing one, since a customer
    // (an admin especially) can have more than one execution in flight at once.
    Task CreateAsync(string customerId, string executionName, string criteriaId, DateTimeOffset startedAt);

    // Full partition, filtered to StartedAt >= sinceUtc, newest first.
    Task<List<ScrapeExecution>> GetRecentByCustomerIdAsync(string customerId, DateTimeOffset sinceUtc);

    // Persists a resolved end state (Succeeded/Failed) once a live Azure lookup determines
    // a previously "Running" execution has finished. Never used to write "Running" itself.
    Task UpdateStatusAsync(string customerId, string executionName, string status, DateTimeOffset endedAt);
}
