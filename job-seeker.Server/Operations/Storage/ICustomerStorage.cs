using job_seeker.Server.Models;

namespace job_seeker.Server.Operations.Storage;

public interface ICustomerStorage
{
    Task<SecureCustomer?> GetByEmailAsync(string email);
    Task<SecureCustomer?> GetByIdAsync(string id);
    Task<bool> EmailExistsAsync(string email);
    Task CreateAsync(SecureCustomer customer);
    Task UpdatePasswordAsync(string id, string email, string passwordHash, string passwordSalt);
}
