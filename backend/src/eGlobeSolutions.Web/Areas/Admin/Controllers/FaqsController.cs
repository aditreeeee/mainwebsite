using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/faqs")]
[Authorize(Policy = "AdminOnly")]
public class FaqsController : Controller
{
    private readonly AppDbContext _db;
    public FaqsController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string page = "pricing", CancellationToken ct = default)
    {
        var faqs = await _db.FaqItems.Where(f => f.PageKey == page).OrderBy(f => f.SortOrder).ToListAsync(ct);
        ViewBag.Page = page;
        return View(faqs);
    }

    [HttpGet("create")]
    public IActionResult Create(string page = "pricing") => View(new FaqItem { PageKey = page });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FaqItem model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.FaqItems.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "FAQ added.";
        return RedirectToAction(nameof(Index), new { page = model.PageKey });
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var faq = await _db.FaqItems.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (faq is null) return NotFound();
        return View(faq);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, FaqItem model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var faq = await _db.FaqItems.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (faq is null) return NotFound();

        faq.PageKey = model.PageKey;
        faq.Question = model.Question;
        faq.Answer = model.Answer;
        faq.SortOrder = model.SortOrder;
        faq.IsPublished = model.IsPublished;
        faq.UpdatedAtUtc = DateTime.UtcNow;
        faq.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "FAQ updated.";
        return RedirectToAction(nameof(Index), new { page = faq.PageKey });
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var faq = await _db.FaqItems.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (faq is null) return NotFound();
        faq.IsDeleted = true;
        faq.DeletedAtUtc = DateTime.UtcNow;
        faq.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "FAQ deleted.";
        return RedirectToAction(nameof(Index), new { page = faq.PageKey });
    }
}
