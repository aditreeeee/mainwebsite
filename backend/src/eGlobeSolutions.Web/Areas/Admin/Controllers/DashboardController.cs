using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin")]
[Authorize(Policy = "AdminOnly")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var last7 = now.AddDays(-7);
        var last30 = now.AddDays(-30);

        var vm = new DashboardViewModel
        {
            NewEnquiriesLast7Days = await _db.Enquiries.CountAsync(e => e.CreatedAtUtc >= last7, ct),
            NewEnquiriesLast30Days = await _db.Enquiries.CountAsync(e => e.CreatedAtUtc >= last30, ct),
            OpenEnquiries = await _db.Enquiries.CountAsync(
                e => e.Status != EnquiryStatus.Won && e.Status != EnquiryStatus.Lost && e.Status != EnquiryStatus.Spam, ct),
            TotalContactSalesEnquiries = await _db.Enquiries.CountAsync(e => e.Type == EnquiryType.ContactSales, ct),
            TotalResellerEnquiries = await _db.Enquiries.CountAsync(e => e.Type == EnquiryType.ResellerPartnership, ct),
            RecentEnquiries = await _db.Enquiries
                .OrderByDescending(e => e.CreatedAtUtc)
                .Take(8)
                .AsNoTracking()
                .ToListAsync(ct),
            RecentActivity = await _db.ActivityLogs
                .OrderByDescending(a => a.TimestampUtc)
                .Take(10)
                .AsNoTracking()
                .ToListAsync(ct)
        };

        return View(vm);
    }
}
