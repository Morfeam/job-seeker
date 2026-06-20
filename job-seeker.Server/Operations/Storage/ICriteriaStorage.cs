using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICriteriaStorage
{
    Task<List<CustomerCriteria>> GetByCustomerIdAsync(string customerId);
    Task CreateAsync(string customerId, string criteriaId, string name);
    Task DeleteAsync(string customerId, string criteriaId);
}
