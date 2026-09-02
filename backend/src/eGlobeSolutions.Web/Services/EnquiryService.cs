using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Services;

public class EnquiryService : IEnquiryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnquiryService> _logger;
    private readonly IEmailSender _emailSender;

    public EnquiryService(AppDbContext db, ILogger<EnquiryService> logger, IEmailSender emailSender)
    {
        _db = db;
        _logger = logger;
        _emailSender = emailSender;
    }

    public async Task<Enquiry> CreateAsync(Enquiry enquiry, CancellationToken ct = default)
    {
        enquiry.CreatedAtUtc = DateTime.UtcNow;
        enquiry.CreatedBy = "public-website";

        _db.Enquiries.Add(enquiry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "New {Type} enquiry #{Id} received from {Email}.",
            enquiry.Type, enquiry.Id, enquiry.Email);

        // Best-effort admin notification email. Never lets a mail failure
        // (or missing SMTP config) fail the visitor's form submission.
        await NotifyAdminAsync(enquiry, ct);

        return enquiry;
    }

    private async Task NotifyAdminAsync(Enquiry enquiry, CancellationToken ct)
    {
        try
        {
            var settings = await _db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
            string Get(string key) => settings.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

            var notifyEnabled = !bool.TryParse(Get(SiteSettingKeys.SmtpNotifyOnEnquiry), out var enabled) || enabled;
            if (!notifyEnabled) return;

            var notifyEmail = Get(SiteSettingKeys.SmtpNotifyEmail);
            if (string.IsNullOrWhiteSpace(notifyEmail)) notifyEmail = Get(SiteSettingKeys.Email);
            if (string.IsNullOrWhiteSpace(notifyEmail)) return;

            var subject = $"New {enquiry.Type} enquiry: {enquiry.FullName}";
            var body = $"""
                A new enquiry was submitted on the website.

                Type: {enquiry.Type}
                Name: {enquiry.FullName}
                Hotel / Company: {enquiry.HotelName}
                Email: {enquiry.Email}
                Phone: {enquiry.Phone}
                Rooms range: {enquiry.RoomsRange}
                Interested in: {enquiry.InterestedIn}
                Message: {enquiry.Message}
                Source page: {enquiry.SourcePage}

                View in admin: /admin/enquiries/{enquiry.Id}
                """;

            var result = await _emailSender.SendAsync(notifyEmail, subject, body, ct);
            if (!result.Success)
            {
                _logger.LogWarning("Enquiry #{Id} notification email not sent: {Error}", enquiry.Id, result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error sending enquiry #{Id} notification email.", enquiry.Id);
        }
    }
}
