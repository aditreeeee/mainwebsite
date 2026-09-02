using System.Text;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Generates sitemap.xml from the live database rather than a static file, so
/// every published blog article shows up automatically, no manual sitemap
/// maintenance whenever a post is added in the admin panel.
/// </summary>
public class SitemapController : Controller
{
    private readonly AppDbContext _db;
    public SitemapController(AppDbContext db) => _db = db;

    private const string BaseUrl = "https://www.eglobe-solutions.com";

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        void AddUrl(string path, string changefreq, string priority, DateTime? lastMod = null)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{BaseUrl}{path}</loc>");
            if (lastMod.HasValue) sb.AppendLine($"    <lastmod>{lastMod.Value:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
            sb.AppendLine($"    <priority>{priority}</priority>");
            sb.AppendLine("  </url>");
        }

        AddUrl("/", "weekly", "1.0");
        AddUrl("/about.html", "monthly", "0.6");
        AddUrl("/pricing.html", "monthly", "0.8");
        AddUrl("/calculator.html", "monthly", "0.8");
        AddUrl("/reseller.html", "monthly", "0.6");
        AddUrl("/contact.html", "monthly", "0.7");
        AddUrl("/blog.html", "weekly", "0.7");

        // Static, lightweight product landing pages (see wwwroot/products/).
        string[] productSlugs =
        {
            "pms", "channel-manager", "ai-tools", "finance-revenue", "pos", "housekeeping", "kot",
            "booking-engine", "ota-management", "google-hotel-ads", "meta-search", "b2b-stay",
            "website-builder", "reviews-manager", "payment-gateway", "pms-apis"
        };
        foreach (var slug in productSlugs)
        {
            AddUrl($"/products/{slug}.html", "monthly", "0.7");
        }

        var posts = await _db.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished && p.Slug != null && p.Body != null)
            .Select(p => new { p.Slug, p.PublishedAtUtc, p.UpdatedAtUtc })
            .ToListAsync(ct);

        foreach (var post in posts)
        {
            AddUrl($"/{post.Slug}.html", "monthly", "0.6", post.UpdatedAtUtc ?? post.PublishedAtUtc);
        }

        sb.AppendLine("</urlset>");

        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
