using Resend;

namespace job_seeker.Server.Operations.Email;

public class SendEmail : ISendEmail
{
    private readonly IResend _resend;
    private readonly IConfiguration _config;
    private readonly string _templatePath;

    public SendEmail(IResend resend, IConfiguration config, IWebHostEnvironment env)
    {
        _resend = resend;
        _config = config;
        _templatePath = Path.Combine(env.ContentRootPath, "Operations", "Email", "PasswordReset.html");
    }

    public async Task SendPasswordResetAsync(string toEmail, string resetLink)
    {
        var html = await File.ReadAllTextAsync(_templatePath);
        html = html.Replace("{{RESET_LINK}}", resetLink);

        var from = _config["resend-from-address"] ?? "noreply@job-seeker.app";

        var message = new EmailMessage
        {
            From = from,
            Subject = "Reset your Matt's Job Seeker password",
            HtmlBody = html,
        };
        message.To.Add(toEmail);

        await _resend.EmailSendAsync(message);
    }
}
