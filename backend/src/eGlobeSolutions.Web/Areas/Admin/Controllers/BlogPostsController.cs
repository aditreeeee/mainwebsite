using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/blog")]
[Authorize(Policy = "AdminOnly")]
public class BlogPostsController : Controller
{
    private readonly AppDbContext _db;
    public BlogPostsController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var posts = await _db.BlogPosts.OrderByDescending(p => p.PublishedAtUtc).ToListAsync(ct);
        return View(posts);
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new BlogPost { PublishedAtUtc = DateTime.UtcNow });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BlogPost model, CancellationToken ct)
    {
        model.Slug = NormalizeSlug(model.Slug);

        if (!string.IsNullOrWhiteSpace(model.Slug) &&
            await _db.BlogPosts.AnyAsync(p => p.Slug == model.Slug, ct))
        {
            ModelState.AddModelError(nameof(model.Slug), "That slug is already used by another post.");
        }

        if (!ModelState.IsValid) return View(model);

        _db.BlogPosts.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Blog post created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();
        return View(post);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BlogPost model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        model.Slug = NormalizeSlug(model.Slug);

        if (!string.IsNullOrWhiteSpace(model.Slug) &&
            await _db.BlogPosts.AnyAsync(p => p.Slug == model.Slug && p.Id != id, ct))
        {
            ModelState.AddModelError(nameof(model.Slug), "That slug is already used by another post.");
        }

        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();

        if (!ModelState.IsValid) return View(model);

        post.Title = model.Title;
        post.Slug = model.Slug;
        post.Category = model.Category;
        post.Excerpt = model.Excerpt;
        post.Body = model.Body;
        post.AuthorName = model.AuthorName;
        post.AuthorRole = model.AuthorRole;
        post.ReadTimeMinutes = model.ReadTimeMinutes;
        post.PublishedAtUtc = model.PublishedAtUtc;
        post.IsFeatured = model.IsFeatured;
        post.IsPublished = model.IsPublished;
        post.SortOrder = model.SortOrder;
        post.CoverImageUrl = model.CoverImageUrl;
        post.MetaTitle = model.MetaTitle;
        post.MetaDescription = model.MetaDescription;
        post.MetaKeywords = model.MetaKeywords;
        post.UpdatedAtUtc = DateTime.UtcNow;
        post.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Blog post updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return NotFound();
        post.IsDeleted = true;
        post.DeletedAtUtc = DateTime.UtcNow;
        post.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Blog post deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static string? NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        return slug.Trim().ToLowerInvariant().Replace(" ", "-");
    }
}
