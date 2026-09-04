using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>
/// Manages editable homepage and reseller-page content blocks (heading,
/// subtitle, body, CTA, image per section). Grouped by PageKey so it covers
/// both "homepage section management" and "reseller content" in one screen.
/// </summary>
[Area("Admin")]
[Route("admin/content")]
[Authorize(Policy = "ContentManage")]
public class ContentController : Controller
{
    private readonly AppDbContext _db;
    public ContentController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string page = "home", CancellationToken ct = default)
    {
        var blocks = await _db.ContentBlocks
            .Where(b => b.PageKey == page)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);
        ViewBag.Page = page;
        return View(blocks);
    }

    [HttpGet("create")]
    public IActionResult Create(string page = "home") => View(new ContentBlock { PageKey = page });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContentBlock model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.ContentBlocks.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Content block created.";
        return RedirectToAction(nameof(Index), new { page = model.PageKey });
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var block = await _db.ContentBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        return View(block);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ContentBlock model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var block = await _db.ContentBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();

        block.PageKey = model.PageKey;
        block.SectionKey = model.SectionKey;
        block.Kicker = model.Kicker;
        block.Title = model.Title;
        block.Subtitle = model.Subtitle;
        block.Body = model.Body;
        block.CtaLabel = model.CtaLabel;
        block.CtaUrl = model.CtaUrl;
        block.ImageUrl = model.ImageUrl;
        block.SortOrder = model.SortOrder;
        block.IsPublished = model.IsPublished;
        block.UpdatedAtUtc = DateTime.UtcNow;
        block.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Content block updated.";
        return RedirectToAction(nameof(Index), new { page = block.PageKey });
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var block = await _db.ContentBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (block is null) return NotFound();
        block.IsDeleted = true;
        block.DeletedAtUtc = DateTime.UtcNow;
        block.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Content block deleted.";
        return RedirectToAction(nameof(Index), new { page = block.PageKey });
    }
}
