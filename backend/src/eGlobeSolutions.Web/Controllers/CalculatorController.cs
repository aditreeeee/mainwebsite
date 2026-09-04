using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using eGlobeSolutions.Web.Models.Public.Calculator;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves calculator.html and the JSON endpoints it calls for the live
/// quotation summary. All pricing is read from the DB-driven catalog via
/// ICalculatorPricingService, the page never hardcodes a rate.
/// </summary>
public class CalculatorController : Controller
{
    private readonly AppDbContext _db;
    private readonly ICalculatorPricingService _pricing;

    public CalculatorController(AppDbContext db, ICalculatorPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    [HttpGet("/calculator.html")]
    [HttpGet("/calculator")]
    [OutputCache(PolicyName = "PublicContent")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new ContentPageViewModel
        {
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "calculator", ct)
        };
        return View(vm);
    }

    /// <summary>Full pricing catalog as JSON, used to render the module picker client-side.</summary>
    [HttpGet("/calculator/catalog")]
    [OutputCache(PolicyName = "PublicContent")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        return Json(await _pricing.GetCatalogAsync(ct));
    }

    /// <summary>
    /// Authoritative live calculation, formulas + rates all applied server-side.
    /// No [ValidateAntiForgeryToken]: this is a stateless read-only computation (no DB
    /// writes, no session/auth effects), so a forged cross-site POST here has nothing to
    /// exploit. Rate-limited instead to bound abuse.
    /// </summary>
    [HttpPost("/calculator/calculate")]
    [EnableRateLimiting("CalculatorCalculate")]
    public async Task<IActionResult> Calculate([FromBody] CalculateRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { success = false, errors = new[] { "Invalid request." } });
        return Json(await _pricing.CalculateAsync(request, ct));
    }
}
