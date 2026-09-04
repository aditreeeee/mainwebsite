using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves reseller.html as a database-backed Razor view (hero, partner plan
/// cards, benefits, statement and final CTA are all ContentBlock rows), and
/// backs the "Talk to Partnerships" submission flow.
/// </summary>
[Route("reseller")]
public class ResellerController : Controller
{
    private readonly IEnquiryService _enquiryService;
    private readonly AppDbContext _db;

    public ResellerController(IEnquiryService enquiryService, AppDbContext db)
    {
        _enquiryService = enquiryService;
        _db = db;
    }

    [HttpGet("/reseller.html")]
    [OutputCache(PolicyName = "PublicContent")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var blocks = await _db.ContentBlocks
            .AsNoTracking()
            .Where(b => b.PageKey == "reseller" && b.IsPublished)
            .ToDictionaryAsync(b => b.SectionKey, ct);

        var vm = new ContentPageViewModel
        {
            Blocks = blocks,
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "reseller", ct)
        };
        return View(vm);
    }

    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PublicForms")]
    public async Task<IActionResult> Submit([FromForm] ResellerSubmitModel model, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            return Ok(new { success = true });
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

            return BadRequest(new { success = false, errors });
        }

        var enquiry = new Enquiry
        {
            Type = EnquiryType.ResellerPartnership,
            FullName = model.FullName.Trim(),
            HotelName = model.CompanyName.Trim(),
            Email = model.Email.Trim(),
            Phone = model.Phone.Trim(),
            CompanyType = model.CompanyType,
            ExpectedPropertyVolume = model.ExpectedPropertyVolume,
            Message = model.Message,
            SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            SourceUserAgent = Request.Headers.UserAgent.ToString(),
            SourcePage = Request.Headers.Referer.ToString()
        };

        await _enquiryService.CreateAsync(enquiry, ct);

        return Ok(new
        {
            success = true,
            message = "Thanks for your interest in reselling eGlobe, our partnerships team will be in touch shortly."
        });
    }
}
