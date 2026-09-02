namespace eGlobeSolutions.Web.Services;

public class EmailSendResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Sends outbound mail using the SMTP settings configured in the admin
/// Settings screen (SiteSettings, Smtp.* keys), read fresh from the DB on
/// every call so a settings change takes effect without a restart.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}
