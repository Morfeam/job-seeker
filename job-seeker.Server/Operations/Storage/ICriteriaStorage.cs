using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICriteriaStorage
{
    Task<List<CustomerCriteria>> GetByCustomerIdAsync(string customerId);
    Task CreateAsync(string customerId, string criteriaId, string name);
    Task DeleteAsync(string customerId, string criteriaId);

    // Called once a triggered scrape resolves to "Succeeded" (see JobSeekerController's
    // scrape-status resolution) — increments the criteria's running usage counter.
    Task IncrementTimesScrapedAsync(string customerId, string criteriaId);
}
