using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
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
    public SettingsController(AppDbContext db) => _db = db;

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
            FooterCopyright = Get(SiteSettingKeys.FooterCopyright)
        };
        return View(model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SiteSettingsFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

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
            [SiteSettingKeys.FooterCopyright] = model.FooterCopyright
        };

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
                _db.SiteSettings.Add(new SiteSetting { Key = key, Value = value, UpdatedBy = User.Identity?.Name });
            }
        }

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Site settings saved.";
        return RedirectToAction(nameof(Index));
    }
}
