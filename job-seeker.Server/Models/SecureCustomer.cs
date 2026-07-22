namespace job_seeker.Server.Models;

public class SecureCustomer
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string CreatedDate { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public int CoverLetterCount { get; set; }
    public int ScrapeCount { get; set; }
    public DateTimeOffset? LastScrapeDate { get; set; }
    public DateTimeOffset? LastCoverLetterDate { get; set; }
}
