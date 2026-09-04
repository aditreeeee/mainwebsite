using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>Manages the pricing.html plans, their feature bullets, the comparison table and CTA.</summary>
[Area("Admin")]
[Route("admin/pricing")]
[Authorize(Policy = "ContentManage")]
public class PricingController : Controller
{
    private readonly AppDbContext _db;

    public PricingController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var plans = await _db.PricingPlans
            .Include(p => p.Features.OrderBy(f => f.SortOrder))
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);
        var rows = await _db.PricingComparisonRows.OrderBy(r => r.SortOrder).ToListAsync(ct);
        ViewBag.ComparisonRows = rows;
        return View(plans);
    }

    [HttpGet("plan/create")]
    public IActionResult CreatePlan() => View(new PricingPlan());

    [HttpPost("plan/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlan(PricingPlan model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.PricingPlans.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Pricing plan created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("plan/{id:int}/edit")]
    public async Task<IActionResult> EditPlan(int id, CancellationToken ct)
    {
        var plan = await _db.PricingPlans.Include(p => p.Features.OrderBy(f => f.SortOrder))
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        return View(plan);
    }

    [HttpPost("plan/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlan(int id, PricingPlan model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var plan = await _db.PricingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();

        plan.Name = model.Name;
        plan.BadgeText = model.BadgeText;
        plan.UnitDescription = model.UnitDescription;
        plan.IsFeatured = model.IsFeatured;
        plan.CtaLabel = model.CtaLabel;
        plan.CtaUrl = model.CtaUrl;
        plan.SortOrder = model.SortOrder;
        plan.IsPublished = model.IsPublished;
        plan.UpdatedAtUtc = DateTime.UtcNow;
        plan.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Pricing plan updated.";
        return RedirectToAction(nameof(EditPlan), new { id });
    }

    [HttpPost("plan/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlan(int id, CancellationToken ct)
    {
        var plan = await _db.PricingPlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        plan.IsDeleted = true;
        plan.DeletedAtUtc = DateTime.UtcNow;
        plan.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Pricing plan deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("plan/{planId:int}/feature/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFeature(int planId, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return RedirectToAction(nameof(EditPlan), new { id = planId });
        var maxSort = await _db.PricingPlanFeatures.Where(f => f.PricingPlanId == planId)
            .Select(f => (int?)f.SortOrder).MaxAsync(ct) ?? -1;
        _db.PricingPlanFeatures.Add(new PricingPlanFeature { PricingPlanId = planId, Text = text.Trim(), SortOrder = maxSort + 1 });
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(EditPlan), new { id = planId });
    }

    [HttpPost("feature/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFeature(int id, int planId, CancellationToken ct)
    {
        var feature = await _db.PricingPlanFeatures.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is not null)
        {
            feature.IsDeleted = true;
            feature.DeletedAtUtc = DateTime.UtcNow;
            feature.DeletedBy = User.Identity?.Name;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(EditPlan), new { id = planId });
    }

    [HttpGet("comparison/create")]
    public IActionResult CreateComparisonRow() => View(new PricingComparisonRow());

    [HttpPost("comparison/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateComparisonRow(PricingComparisonRow model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);
        _db.PricingComparisonRows.Add(model);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Comparison row added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("comparison/{id:int}/edit")]
    public async Task<IActionResult> EditComparisonRow(int id, CancellationToken ct)
    {
        var row = await _db.PricingComparisonRows.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();
        return View(row);
    }

    [HttpPost("comparison/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditComparisonRow(int id, PricingComparisonRow model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        var row = await _db.PricingComparisonRows.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();

        row.ModuleName = model.ModuleName;
        row.PerRoomValue = model.PerRoomValue;
        row.PerPropertyValue = model.PerPropertyValue;
        row.EnterpriseValue = model.EnterpriseValue;
        row.SortOrder = model.SortOrder;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Comparison row updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("comparison/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComparisonRow(int id, CancellationToken ct)
    {
        var row = await _db.PricingComparisonRows.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();
        row.IsDeleted = true;
        row.DeletedAtUtc = DateTime.UtcNow;
        row.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Comparison row deleted.";
        return RedirectToAction(nameof(Index));
    }
}
