using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace eGlobeSolutions.Web.ViewComponents;

/// <summary>Renders a small inline &lt;style&gt; block overriding the site's --blue
/// (primary accent) and --navy (secondary) CSS custom properties from
/// SiteSettings (see Areas/Admin/Views/Settings/Index.cshtml, "Display Colors"),
/// so an admin can reskin the two most visible brand colors without touching
/// code. Renders nothing if neither is set, the built-in css/style.css
/// defaults apply as-is.</summary>
public class ThemeColorsViewComponent : ViewComponent
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    public ThemeColorsViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var primary = await _db.SiteSettings.AsNoTracking()
            .Where(s => s.Key == SiteSettingKeys.ThemePrimaryColor)
            .Select(s => s.Value).FirstOrDefaultAsync();
        var secondary = await _db.SiteSettings.AsNoTracking()
            .Where(s => s.Key == SiteSettingKeys.ThemeSecondaryColor)
            .Select(s => s.Value).FirstOrDefaultAsync();

        // Re-validate here even though the admin form already does: this value
        // is about to be written into a <style> block, never trust stored data
        // to still be well-formed by the time it's rendered.
        string? primaryValid = !string.IsNullOrWhiteSpace(primary) && HexColor.IsMatch(primary) ? primary : null;
        string? secondaryValid = !string.IsNullOrWhiteSpace(secondary) && HexColor.IsMatch(secondary) ? secondary : null;

        return View((primaryValid, secondaryValid));
    }
}
