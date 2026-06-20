using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICriteriaBlobStorage
{
    Task<CriteriaDetail?> GetAsync(string customerId, string criteriaId);
    Task UpsertAsync(string customerId, string criteriaId, CriteriaDetail detail);
    Task DeleteAsync(string customerId, string criteriaId);
}
