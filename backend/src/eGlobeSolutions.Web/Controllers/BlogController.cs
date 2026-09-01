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
/// </summary>
public class BlogController : Controller
{
    private readonly AppDbContext _db;
    public BlogController(AppDbContext db) => _db = db;

    [HttpGet("/blog.html")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var featured = await _db.BlogPosts
            .Where(p => p.IsPublished && p.IsFeatured)
            .OrderByDescending(p => p.PublishedAtUtc)
            .FirstOrDefaultAsync(ct);

        var posts = await _db.BlogPosts
            .Where(p => p.IsPublished && !p.IsFeatured)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

        return View(new BlogIndexViewModel
        {
            Featured = featured,
            Posts = posts,
            Seo = await _db.SeoMetadata.FirstOrDefaultAsync(s => s.PageKey == "blog", ct)
        });
    }

    [HttpGet("/{slug}.html")]
    public async Task<IActionResult> Article(string slug, CancellationToken ct)
    {
        var post = await _db.BlogPosts
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished && p.Body != null, ct);

        if (post is null) return NotFound();

        return View("Article", new BlogArticleViewModel { Post = post });
    }
}
