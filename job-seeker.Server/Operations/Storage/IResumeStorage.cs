using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface IResumeStorage
{
    Task<List<Resume>> GetByCustomerIdAsync(string customerId);
    Task<Resume?> GetByIdAsync(string customerId, string resumeId);
    Task CreateAsync(string customerId, string resumeId, string name, string fileName, string? associatedCriteriaId);
    Task DeleteAsync(string customerId, string resumeId);
}
