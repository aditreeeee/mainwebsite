using System.Net;
using System.Net.Mail;
using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly AppDbContext _db;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(AppDbContext db, ILogger<SmtpEmailSender> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        var settings = await _db.SiteSettings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string Get(string key) => settings.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

        var host = Get(SiteSettingKeys.SmtpHost);
        if (string.IsNullOrWhiteSpace(host))
            return new EmailSendResult { Success = false, Error = "SMTP is not configured (no host set in Settings)." };

        if (!int.TryParse(Get(SiteSettingKeys.SmtpPort), out var port) || port <= 0) port = 587;
        var enableSsl = !bool.TryParse(Get(SiteSettingKeys.SmtpEnableSsl), out var ssl) || ssl;
        var username = Get(SiteSettingKeys.SmtpUsername);
        var password = Get(SiteSettingKeys.SmtpPassword);
        var fromEmail = Get(SiteSettingKeys.SmtpFromEmail);
        if (string.IsNullOrWhiteSpace(fromEmail)) fromEmail = username;
        if (string.IsNullOrWhiteSpace(fromEmail))
            return new EmailSendResult { Success = false, Error = "SMTP is not configured (no From address set in Settings)." };
        var fromName = Get(SiteSettingKeys.SmtpFromName);
        if (string.IsNullOrWhiteSpace(fromName)) fromName = Get(SiteSettingKeys.SiteName);

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, string.IsNullOrWhiteSpace(fromName) ? fromEmail : fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message, ct);
            return new EmailSendResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email to {ToEmail} via {Host}:{Port}.", toEmail, host, port);
            return new EmailSendResult { Success = false, Error = ex.Message };
        }
    }
}
