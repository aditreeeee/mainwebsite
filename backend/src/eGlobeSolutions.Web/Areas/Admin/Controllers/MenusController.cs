using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/menus")]
[Authorize(Policy = "AdminOnly")]
public class MenusController : Controller
{
    private readonly AppDbContext _db;
    public MenusController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string location = "topbar", CancellationToken ct = default)
    {
        var items = await _db.MenuItems.Where(m => m.Location == location).OrderBy(m => m.SortOrder).ToListAsync(ct);
        ViewBag.Location = location;
        return View(items);
    }

    [HttpGet("create")]
    public IActionResult Create(string location = "topbar") => View(new MenuItem { Location = location });

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MenuItem model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.MenuItems.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Menu item created.";
        return RedirectToAction(nameof(Index), new { location = model.Location });
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return NotFound();
        return View(item);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, MenuItem model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var item = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return NotFound();

        item.Location = model.Location;
        item.Label = model.Label;
        item.Url = model.Url;
        item.OpenInNewTab = model.OpenInNewTab;
        item.SortOrder = model.SortOrder;
        item.IsPublished = model.IsPublished;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Menu item updated.";
        return RedirectToAction(nameof(Index), new { location = item.Location });
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var item = await _db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return NotFound();
        item.IsDeleted = true;
        item.DeletedAtUtc = DateTime.UtcNow;
        item.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Menu item deleted.";
        return RedirectToAction(nameof(Index), new { location = item.Location });
    }
}
