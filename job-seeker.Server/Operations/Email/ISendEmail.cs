namespace job_seeker.Server.Operations.Email;

public interface ISendEmail
{
    Task SendPasswordResetAsync(string toEmail, string resetLink);
}
