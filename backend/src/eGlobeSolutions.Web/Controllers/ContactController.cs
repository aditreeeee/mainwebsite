using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves contact.html as a database-backed Razor view (hero, sidebar trust
/// points and SEO tags are ContentBlock/SeoMetadata rows; contact details in
/// the sidebar come from SiteSettings) and backs the "#sales-form" submission.
/// </summary>
[Route("contact")]
public class ContactController : Controller
{
    private readonly IEnquiryService _enquiryService;
    private readonly ILogger<ContactController> _logger;
    private readonly AppDbContext _db;

    public ContactController(IEnquiryService enquiryService, ILogger<ContactController> logger, AppDbContext db)
    {
        _enquiryService = enquiryService;
        _logger = logger;
        _db = db;
    }

    [HttpGet("/contact.html")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var blocks = await _db.ContentBlocks
            .AsNoTracking()
            .Where(b => b.PageKey == "contact" && b.IsPublished)
            .ToDictionaryAsync(b => b.SectionKey, ct);

        var vm = new ContentPageViewModel
        {
            Blocks = blocks,
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "contact", ct),
            Settings = await _db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct)
        };
        return View(vm);
    }

    /// <summary>
    /// JSON endpoint for the "#sales-form" to POST to via fetch(). Returns
    /// { success, message } so main.js can show the same success/error
    /// state it already renders. Now a real Razor view, so the antiforgery
    /// token round-trips normally (see the hidden field in the form + the
    /// header main.js sends alongside it).
    /// </summary>
    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PublicForms")]
    public async Task<IActionResult> Submit([FromForm] ContactSalesSubmitModel model, CancellationToken ct)
    {
        // Honeypot: a filled hidden field means a bot, accept silently and do nothing.
        if (!string.IsNullOrWhiteSpace(model.Website))
        {
            _logger.LogInformation("Contact form honeypot triggered, discarding submission.");
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
            Type = model.FormType == "quick" ? EnquiryType.QuickEnquiry : EnquiryType.ContactSales,
            FullName = model.FullName.Trim(),
            HotelName = model.HotelName?.Trim() ?? string.Empty,
            Email = model.Email?.Trim() ?? string.Empty,
            Phone = model.Phone.Trim(),
            RoomsRange = model.RoomsRange ?? string.Empty,
            InterestedIn = model.InterestedIn,
            OtherInterest = model.OtherInterest,
            Message = model.Message,
            SourceIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            SourceUserAgent = Request.Headers.UserAgent.ToString(),
            SourcePage = Request.Headers.Referer.ToString()
        };

        await _enquiryService.CreateAsync(enquiry, ct);

        return Ok(new
        {
            success = true,
            message = "Thanks, this is a real submission, our team will reach out within one business day."
        });
    }
}
