using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICustomerStorage
{
    Task<SecureCustomer?> GetByEmailAsync(string email);
    Task<SecureCustomer?> GetByIdAsync(string id);
    Task<bool> EmailExistsAsync(string email);
    Task CreateAsync(SecureCustomer customer);
    Task UpdatePasswordAsync(string id, string email, string passwordHash, string passwordSalt);

    // Daily-capped usage tracking for costly (Anthropic-backed) actions. Admins
    // (IsAdmin=true) always have quota remaining; the count/date pair is still
    // recorded for them since it doubles as usage stats for every customer.
    Task<bool> HasScrapeQuotaRemainingAsync(string customerId, int dailyLimit);
    Task<bool> HasCoverLetterQuotaRemainingAsync(string customerId, int dailyLimit);

    Task RecordScrapeAsync(string customerId);
    Task RecordCoverLetterAsync(string customerId);
}
