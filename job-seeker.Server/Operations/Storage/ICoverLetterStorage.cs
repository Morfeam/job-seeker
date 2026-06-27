using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICoverLetterStorage
{
    Task<List<CoverLetter>> GetByCustomerIdAsync(string customerId);
    Task<CoverLetter?> GetByIdAsync(string customerId, string coverLetterId);
    Task<List<CoverLetter>> GetRecentByCustomerIdAsync(string customerId, int max);
    Task CreateAsync(string customerId, string coverLetterId, string name, string resumeId, string jobId);
    Task SetPrimeAsync(string customerId, string coverLetterId);
    Task DeleteAsync(string customerId, string coverLetterId);
}
