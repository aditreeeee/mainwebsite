using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/seo")]
[Authorize(Policy = "AdminOnly")]
public class SeoController : Controller
{
    private readonly AppDbContext _db;
    public SeoController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await _db.SeoMetadata.OrderBy(s => s.PageKey).ToListAsync(ct);
        return View(items);
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new SeoMetadata());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SeoMetadata model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        if (await _db.SeoMetadata.AnyAsync(s => s.PageKey == model.PageKey, ct))
        {
            ModelState.AddModelError(nameof(model.PageKey), "SEO metadata already exists for this page key.");
            return View(model);
        }
        _db.SeoMetadata.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "SEO metadata created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var item = await _db.SeoMetadata.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (item is null) return NotFound();
        return View(item);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SeoMetadata model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var item = await _db.SeoMetadata.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (item is null) return NotFound();

        item.PageKey = model.PageKey;
        item.Title = model.Title;
        item.Description = model.Description;
        item.Keywords = model.Keywords;
        item.CanonicalUrl = model.CanonicalUrl;
        item.OgImageUrl = model.OgImageUrl;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "SEO metadata updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var item = await _db.SeoMetadata.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (item is null) return NotFound();
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        item.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "SEO metadata deleted.";
        return RedirectToAction(nameof(Index));
    }
}
