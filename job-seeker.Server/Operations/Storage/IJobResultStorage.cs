using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface IJobResultStorage
{
    Task<JobResult?> GetJobAsync(string customerId, string blobFileName);
}
