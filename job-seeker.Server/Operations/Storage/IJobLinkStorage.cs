using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface IJobLinkStorage
{
    Task<List<JobLink>> GetAllByCustomerIdAsync(string customerId);
}
