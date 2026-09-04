using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.ViewComponents;

/// <summary>Renders the FAQ section for a CmsPage (a product or solution page,
/// keyed by pageKey == CmsPage.Slug, e.g. "products/pms" or
/// "solutions/hostels-resorts") from the same FaqItems table /admin/faqs
/// already manages for Pricing. Renders nothing if the page has no FAQs
/// yet, rather than an empty "Frequently Asked Questions" heading.</summary>
public class PageFaqsViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    public PageFaqsViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync(string pageKey)
    {
        var faqs = await _db.FaqItems
            .AsNoTracking()
            .Where(f => f.PageKey == pageKey && f.IsPublished)
            .OrderBy(f => f.SortOrder)
            .ToListAsync();

        return View(faqs);
    }
}
