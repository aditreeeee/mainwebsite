using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves blog.html (list) and each post's own "/{slug}.html" page (e.g.
/// article-ai-tools.html) from the BlogPosts table. The slug route is a
/// catch-all pattern, but ASP.NET Core's routing gives literal-segment
/// routes (pricing.html, contact.html, etc.) higher precedence than a
/// parameterized one, so it only ever matches genuine post slugs.
///
/// Article also owns admin-created CmsPages (the WordPress "Pages"
/// equivalent, /admin/pages): both share the same "/{slug}.html" URL
/// shape, and only one controller can register that route pattern, so
/// this action checks BlogPosts first, then CmsPages, before 404ing.
/// </summary>
public class BlogController : Controller
{
    private readonly AppDbContext _db;
    public BlogController(AppDbContext db) => _db = db;

    [HttpGet("/blog.html")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var featured = await _db.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished && p.IsFeatured)
            .OrderByDescending(p => p.PublishedAtUtc)
            .FirstOrDefaultAsync(ct);

        var posts = await _db.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished && !p.IsFeatured)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

        return View(new BlogIndexViewModel
        {
            Featured = featured,
            Posts = posts,
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "blog", ct)
        });
    }

    [HttpGet("/{slug}.html")]
    public async Task<IActionResult> Article(string slug, CancellationToken ct)
    {
        var post = await _db.BlogPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished && p.Body != null, ct);

        if (post is not null) return View("Article", new BlogArticleViewModel { Post = post });

        var page = await _db.CmsPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, ct);

        if (page is not null) return View("Page", new CmsPageViewModel { Page = page });

        return NotFound();
    }

    /// <summary>The 16 product pages (originally static files under wwwroot/products/)
    /// are CmsPages whose Slug is stored with the "products/" prefix baked in (e.g.
    /// "products/pms"), so this route and Article's never collide, they're different
    /// path shapes entirely.</summary>
    [HttpGet("/products/{slug}.html")]
    public async Task<IActionResult> ProductPage(string slug, CancellationToken ct)
    {
        var page = await _db.CmsPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == "products/" + slug && p.IsPublished, ct);

        if (page is null) return NotFound();

        return View("Page", new CmsPageViewModel { Page = page });
    }
}
