using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>
/// WordPress-style "Pages": create/edit/delete a page here, it's live at
/// "/{Slug}.html" immediately, no code change or redeploy. See CmsPage for
/// the full scope note.
/// </summary>
[Area("Admin")]
[Route("admin/pages")]
[Authorize(Policy = "ContentManage")]
public class PagesController : Controller
{
    private readonly AppDbContext _db;
    public PagesController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var pages = await _db.CmsPages.OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToListAsync(ct);
        return View(pages);
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new CmsPage());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CmsPage model, CancellationToken ct)
    {
        model.Slug = NormalizeSlug(model.Slug);
        await ValidateAsync(model, null, ct);

        if (!ModelState.IsValid) return View(model);

        _db.CmsPages.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Page created, live at /{model.Slug}.html.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var page = await _db.CmsPages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return NotFound();
        return View(page);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CmsPage model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        model.Slug = NormalizeSlug(model.Slug);
        await ValidateAsync(model, id, ct);

        var page = await _db.CmsPages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return NotFound();

        if (!ModelState.IsValid) return View(model);

        page.Title = model.Title;
        page.Slug = model.Slug;
        page.Subtitle = model.Subtitle;
        page.Body = model.Body;
        page.UseCustomHero = model.UseCustomHero;
        page.IsPublished = model.IsPublished;
        page.SortOrder = model.SortOrder;
        page.MetaTitle = model.MetaTitle;
        page.MetaDescription = model.MetaDescription;
        page.MetaKeywords = model.MetaKeywords;
        page.UpdatedAtUtc = DateTime.UtcNow;
        page.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Page updated.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var page = await _db.CmsPages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return NotFound();
        page.IsDeleted = true;
        page.DeletedAtUtc = DateTime.UtcNow;
        page.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Page deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(CmsPage model, int? currentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Slug))
        {
            ModelState.AddModelError(nameof(model.Slug), "A URL slug is required.");
            return;
        }

        // Slugs are shared with BlogPosts, both live at "/{slug}.html", so a
        // page can't collide with an existing article's URL either.
        var slugTaken = await _db.CmsPages.AnyAsync(p => p.Slug == model.Slug && p.Id != currentId, ct)
            || await _db.BlogPosts.AnyAsync(p => p.Slug == model.Slug, ct);
        if (slugTaken)
        {
            ModelState.AddModelError(nameof(model.Slug), "That slug is already used by another page or blog post.");
        }
    }

    private static string NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return string.Empty;
        return slug.Trim().ToLowerInvariant().Replace(" ", "-");
    }
}
