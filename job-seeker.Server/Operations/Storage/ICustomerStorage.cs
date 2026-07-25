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

    // Records a newly-triggered scrape (usage count/date, same as RecordScrapeAsync did) and
    // stamps the Container App Job execution name so its status can be looked up later.
    Task RecordScrapeStartedAsync(string customerId, string executionName);

    // Persists the resolved end state (Succeeded/Failed) once a live Azure lookup determines
    // a previously "Running" scrape has finished. Never used to write "Running" itself.
    Task UpdateScrapeStatusAsync(string customerId, string status);
    Task RecordCoverLetterAsync(string customerId);
}
