using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/settings")]
[Authorize(Policy = "AdminOnly")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    public SettingsController(AppDbContext db, IEmailSender emailSender)
    {
        _db = db;
        _emailSender = emailSender;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await _db.SiteSettings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string Get(string key) => settings.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

        var model = new SiteSettingsFormModel
        {
            SiteName = Get(SiteSettingKeys.SiteName),
            Phone = Get(SiteSettingKeys.Phone),
            Email = Get(SiteSettingKeys.Email),
            WhatsAppNumber = Get(SiteSettingKeys.WhatsAppNumber),
            CallUsNumbers = Get(SiteSettingKeys.CallUsNumbers),
            BusinessHours = Get(SiteSettingKeys.BusinessHours),
            FacebookUrl = Get(SiteSettingKeys.FacebookUrl),
            YoutubeUrl = Get(SiteSettingKeys.YoutubeUrl),
            LinkedInUrl = Get(SiteSettingKeys.LinkedInUrl),
            AppStoreUrl = Get(SiteSettingKeys.AppStoreUrl),
            GooglePlayUrl = Get(SiteSettingKeys.GooglePlayUrl),
            FooterCopyright = Get(SiteSettingKeys.FooterCopyright),

            SmtpHost = Get(SiteSettingKeys.SmtpHost),
            SmtpPort = int.TryParse(Get(SiteSettingKeys.SmtpPort), out var port) ? port : 587,
            SmtpUsername = Get(SiteSettingKeys.SmtpUsername),
            SmtpEnableSsl = !bool.TryParse(Get(SiteSettingKeys.SmtpEnableSsl), out var ssl) || ssl,
            SmtpFromEmail = Get(SiteSettingKeys.SmtpFromEmail),
            SmtpFromName = Get(SiteSettingKeys.SmtpFromName),
            SmtpNotifyEmail = Get(SiteSettingKeys.SmtpNotifyEmail),
            SmtpNotifyOnEnquiry = !bool.TryParse(Get(SiteSettingKeys.SmtpNotifyOnEnquiry), out var notify) || notify
            // SmtpPassword intentionally left blank: never round-tripped to the form.
        };
        ViewBag.SmtpPasswordIsSet = !string.IsNullOrWhiteSpace(Get(SiteSettingKeys.SmtpPassword));
        return View(model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SiteSettingsFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.SmtpPasswordIsSet = await _db.SiteSettings.AnyAsync(s => s.Key == SiteSettingKeys.SmtpPassword && s.Value != null && s.Value != "", ct);
            return View(model);
        }

        var values = new Dictionary<string, string?>
        {
            [SiteSettingKeys.SiteName] = model.SiteName,
            [SiteSettingKeys.Phone] = model.Phone,
            [SiteSettingKeys.Email] = model.Email,
            [SiteSettingKeys.WhatsAppNumber] = model.WhatsAppNumber,
            [SiteSettingKeys.CallUsNumbers] = model.CallUsNumbers,
            [SiteSettingKeys.BusinessHours] = model.BusinessHours,
            [SiteSettingKeys.FacebookUrl] = model.FacebookUrl,
            [SiteSettingKeys.YoutubeUrl] = model.YoutubeUrl,
            [SiteSettingKeys.LinkedInUrl] = model.LinkedInUrl,
            [SiteSettingKeys.AppStoreUrl] = model.AppStoreUrl,
            [SiteSettingKeys.GooglePlayUrl] = model.GooglePlayUrl,
            [SiteSettingKeys.FooterCopyright] = model.FooterCopyright,

            [SiteSettingKeys.SmtpHost] = model.SmtpHost,
            [SiteSettingKeys.SmtpPort] = model.SmtpPort?.ToString() ?? "587",
            [SiteSettingKeys.SmtpUsername] = model.SmtpUsername,
            [SiteSettingKeys.SmtpEnableSsl] = model.SmtpEnableSsl.ToString(),
            [SiteSettingKeys.SmtpFromEmail] = model.SmtpFromEmail,
            [SiteSettingKeys.SmtpFromName] = model.SmtpFromName,
            [SiteSettingKeys.SmtpNotifyEmail] = model.SmtpNotifyEmail,
            [SiteSettingKeys.SmtpNotifyOnEnquiry] = model.SmtpNotifyOnEnquiry.ToString()
        };

        // A blank password field means "leave the stored password alone", so a
        // re-save of the form never accidentally wipes a working credential.
        if (!string.IsNullOrEmpty(model.SmtpPassword))
        {
            values[SiteSettingKeys.SmtpPassword] = model.SmtpPassword;
        }

        var existing = await _db.SiteSettings.ToDictionaryAsync(s => s.Key, s => s, ct);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.Value = value;
                row.UpdatedAtUtc = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;
            }
            else
            {
                _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value, Group = key.StartsWith("Smtp.") ? "Smtp" : "General", UpdatedBy = User.Identity?.Name });
            }
        }

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Site settings saved.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Sends a real test email using the currently *saved* SMTP settings, so
    /// admins can confirm the configuration works without leaving the page.</summary>
    [HttpPost("smtp/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmtp([FromForm] SmtpTestModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.ToEmail))
            return BadRequest(new { success = false, message = "Enter a valid email address to send the test to." });

        var result = await _emailSender.SendAsync(
            model.ToEmail,
            "eGlobe Admin, SMTP test email",
            $"This is a test email from the eGlobe Solutions admin panel, sent at {DateTime.UtcNow:u} UTC. If you received this, your SMTP settings are working.",
            ct);

        return Json(new { success = result.Success, message = result.Success ? "Test email sent." : result.Error });
    }
}