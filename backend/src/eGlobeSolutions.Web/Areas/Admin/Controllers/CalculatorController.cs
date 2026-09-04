using eGlobeSolutions.Domain.Entities.Calculator;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>
/// Single-page pricing manager for the price calculator (calculator.html).
/// Every row saves inline via AJAX, no separate create/edit pages, so Sales
/// pricing changes take one click and are live immediately. This is the only
/// place calculator pricing can be changed.
/// </summary>
[Area("Admin")]
[Route("admin/calculator")]
[Authorize(Policy = "ContentManage")]
public class CalculatorController : Controller
{
    private readonly AppDbContext _db;
    public CalculatorController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Modules = await _db.CalculatorPricingModules
            .OrderBy(m => m.Category).ThenBy(m => m.SortOrder).ToListAsync(ct);
        ViewBag.Plans = await _db.CalculatorPlanBaseRates
            .OrderBy(p => p.PlanType).ToListAsync(ct);
        ViewBag.Taxes = await _db.CalculatorTaxConfigurations
            .OrderBy(t => t.SortOrder).ToListAsync(ct);
        ViewBag.Currencies = await _db.CalculatorCurrencyRates
            .OrderBy(c => c.SortOrder).ToListAsync(ct);
        ViewBag.BillingCycles = await _db.CalculatorBillingCycles
            .OrderBy(b => b.SortOrder).ToListAsync(ct);
        return View();
    }

    [HttpPost("module/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveModule([FromForm] ModuleSaveModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name))
            return BadRequest(new { success = false, message = "Code and Name are required." });

        PricingModule module;
        if (model.Id > 0)
        {
            var existing = await _db.CalculatorPricingModules.FirstOrDefaultAsync(m => m.Id == model.Id, ct);
            if (existing is null) return NotFound(new { success = false, message = "Module not found." });
            module = existing;
            module.UpdatedAtUtc = DateTime.UtcNow;
            module.UpdatedBy = User.Identity?.Name;
        }
        else
        {
            module = new PricingModule();
            _db.CalculatorPricingModules.Add(module);
        }

        module.Code = model.Code.Trim();
        module.Name = model.Name.Trim();
        module.Description = model.Description;
        module.Category = model.Category;
        module.ChargeType = model.ChargeType;
        module.VolumeInputLabel = model.VolumeInputLabel;
        module.Tooltip = model.Tooltip;
        module.PerRoomAvailability = model.PerRoomAvailability;
        module.PerPropertyAvailability = model.PerPropertyAvailability;
        module.EnterpriseAvailability = model.EnterpriseAvailability;
        module.MonthlyRate = model.MonthlyRate;
        module.OneTimeSetupFee = model.OneTimeSetupFee;
        module.CommissionPercent = model.CommissionPercent;
        module.SortOrder = model.SortOrder;
        module.IsActive = model.IsActive;
        module.EffectiveFromUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, id = module.Id, message = "Module saved." });
    }

    [HttpPost("module/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteModule(int id, CancellationToken ct)
    {
        var module = await _db.CalculatorPricingModules.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module is null) return NotFound(new { success = false, message = "Module not found." });
        module.IsDeleted = true;
        module.DeletedAtUtc = DateTime.UtcNow;
        module.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, message = "Module removed." });
    }

    [HttpPost("planbase/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlanBase([FromForm] PlanBaseSaveModel model, CancellationToken ct)
    {
        var plan = await _db.CalculatorPlanBaseRates.FirstOrDefaultAsync(p => p.PlanType == model.PlanType, ct);
        if (plan is null)
        {
            plan = new PricingPlanBaseRate { PlanType = model.PlanType };
            _db.CalculatorPlanBaseRates.Add(plan);
        }
        else
        {
            plan.UpdatedAtUtc = DateTime.UtcNow;
            plan.UpdatedBy = User.Identity?.Name;
        }

        plan.DisplayName = model.DisplayName.Trim();
        plan.UnitDescription = model.UnitDescription;
        plan.MonthlyRatePerUnit = model.MonthlyRatePerUnit;
        plan.OneTimeSetupFee = model.OneTimeSetupFee;
        plan.IsCustomQuote = model.IsCustomQuote;
        plan.EffectiveFromUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, message = "Plan pricing saved." });
    }

    [HttpPost("tax/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTax([FromForm] TaxSaveModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return BadRequest(new { success = false, message = "Name is required." });

        TaxConfiguration tax;
        if (model.Id > 0)
        {
            var existing = await _db.CalculatorTaxConfigurations.FirstOrDefaultAsync(t => t.Id == model.Id, ct);
            if (existing is null) return NotFound(new { success = false, message = "Tax not found." });
            tax = existing;
            tax.UpdatedAtUtc = DateTime.UtcNow;
            tax.UpdatedBy = User.Identity?.Name;
        }
        else
        {
            tax = new TaxConfiguration();
            _db.CalculatorTaxConfigurations.Add(tax);
        }

        tax.Name = model.Name.Trim();
        tax.RatePercent = model.RatePercent;
        tax.IsActive = model.IsActive;
        tax.SortOrder = model.SortOrder;
        tax.EffectiveFromUtc = DateTime.UtcNow;

        if (model.IsDefault)
        {
            var others = await _db.CalculatorTaxConfigurations.Where(t => t.Id != tax.Id).ToListAsync(ct);
            foreach (var o in others) o.IsDefault = false;
        }
        tax.IsDefault = model.IsDefault;

        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, id = tax.Id, message = "Tax saved." });
    }

    [HttpPost("tax/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTax(int id, CancellationToken ct)
    {
        var tax = await _db.CalculatorTaxConfigurations.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tax is null) return NotFound(new { success = false, message = "Tax not found." });
        tax.IsDeleted = true;
        tax.DeletedAtUtc = DateTime.UtcNow;
        tax.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, message = "Tax removed." });
    }

    // ---- Currency converter ----

    [HttpPost("currency/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCurrency([FromForm] CurrencySaveModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Symbol))
            return BadRequest(new { success = false, message = "Code and Symbol are required." });

        CurrencyRate currency;
        if (model.Id > 0)
        {
            var existing = await _db.CalculatorCurrencyRates.FirstOrDefaultAsync(c => c.Id == model.Id, ct);
            if (existing is null) return NotFound(new { success = false, message = "Currency not found." });
            currency = existing;
            currency.UpdatedAtUtc = DateTime.UtcNow;
            currency.UpdatedBy = User.Identity?.Name;
        }
        else
        {
            currency = new CurrencyRate();
            _db.CalculatorCurrencyRates.Add(currency);
        }

        currency.Code = model.Code.Trim().ToUpperInvariant();
        currency.Symbol = model.Symbol.Trim();
        currency.Name = model.Name.Trim();
        currency.RatePerInr = model.RatePerInr;
        currency.IsActive = model.IsActive;
        currency.SortOrder = model.SortOrder;

        if (model.IsDefault)
        {
            var others = await _db.CalculatorCurrencyRates.Where(c => c.Id != currency.Id).ToListAsync(ct);
            foreach (var o in others) o.IsDefault = false;
        }
        currency.IsDefault = model.IsDefault;

        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, id = currency.Id, message = "Currency saved." });
    }

    [HttpPost("currency/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCurrency(int id, CancellationToken ct)
    {
        var currency = await _db.CalculatorCurrencyRates.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (currency is null) return NotFound(new { success = false, message = "Currency not found." });
        currency.IsDeleted = true;
        currency.DeletedAtUtc = DateTime.UtcNow;
        currency.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, message = "Currency removed." });
    }

    // ---- Billing cycles (Monthly / 3 Months / 6 Months / Annual) ----

    [HttpPost("billingcycle/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBillingCycle([FromForm] BillingCycleSaveModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Label) || model.Months <= 0)
            return BadRequest(new { success = false, message = "Label and a positive number of months are required." });

        BillingCycle cycle;
        if (model.Id > 0)
        {
            var existing = await _db.CalculatorBillingCycles.FirstOrDefaultAsync(b => b.Id == model.Id, ct);
            if (existing is null) return NotFound(new { success = false, message = "Billing cycle not found." });
            cycle = existing;
            cycle.UpdatedAtUtc = DateTime.UtcNow;
            cycle.UpdatedBy = User.Identity?.Name;
        }
        else
        {
            cycle = new BillingCycle();
            _db.CalculatorBillingCycles.Add(cycle);
        }

        cycle.Label = model.Label.Trim();
        cycle.Months = model.Months;
        cycle.DiscountPercent = model.DiscountPercent;
        cycle.IsActive = model.IsActive;
        cycle.SortOrder = model.SortOrder;

        if (model.IsDefault)
        {
            var others = await _db.CalculatorBillingCycles.Where(b => b.Id != cycle.Id).ToListAsync(ct);
            foreach (var o in others) o.IsDefault = false;
        }
        cycle.IsDefault = model.IsDefault;

        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, id = cycle.Id, message = "Billing cycle saved." });
    }

    [HttpPost("billingcycle/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBillingCycle(int id, CancellationToken ct)
    {
        var cycle = await _db.CalculatorBillingCycles.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (cycle is null) return NotFound(new { success = false, message = "Billing cycle not found." });
        cycle.IsDeleted = true;
        cycle.DeletedAtUtc = DateTime.UtcNow;
        cycle.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);
        return Json(new { success = true, message = "Billing cycle removed." });
    }
}
