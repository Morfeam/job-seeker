namespace job_seeker.Server.Models;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime SessionStartDate { get; set; }
}
