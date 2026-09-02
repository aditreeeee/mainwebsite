using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves pricing.html as a database-backed Razor view instead of a static
/// file, so admin edits to plans/comparison rows/FAQs/SEO show up live. The
/// URL and markup stay identical to the original static page.
/// </summary>
public class PricingController : Controller
{
    private readonly AppDbContext _db;
    public PricingController(AppDbContext db) => _db = db;

    [HttpGet("pricing.html")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new PricingPageViewModel
        {
            Plans = await _db.PricingPlans
                .AsNoTracking()
                .Where(p => p.IsPublished)
                .Include(p => p.Features.OrderBy(f => f.SortOrder))
                .OrderBy(p => p.SortOrder)
                .ToListAsync(ct),
            ComparisonRows = await _db.PricingComparisonRows.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct),
            Faqs = await _db.FaqItems.AsNoTracking().Where(f => f.PageKey == "pricing" && f.IsPublished).OrderBy(f => f.SortOrder).ToListAsync(ct),
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "pricing", ct)
        };
        return View(vm);
    }
}
