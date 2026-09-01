using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
using eGlobeSolutions.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/activitylog")]
[Authorize(Policy = "AdminOnly")]
public class ActivityLogController : Controller
{
    private const int PageSize = 30;
    private readonly AppDbContext _db;
    public ActivityLogController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? entityType, int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);

        var query = _db.ActivityLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        query = query.OrderByDescending(a => a.TimestampUtc);

        var totalCount = await query.CountAsync(ct);

        // Same compat-level-100-safe paging as EnquiriesController: order,
        // page the id list in memory, then fetch that page's rows by id.
        var orderedIds = await query.Select(a => a.Id).ToListAsync(ct);
        var pageIds = orderedIds.Skip((page - 1) * PageSize).Take(PageSize).ToList();

        var unordered = await _db.ActivityLogs.WhereIdIn(a => a.Id, pageIds).AsNoTracking().ToListAsync(ct);
        var items = pageIds.Select(id => unordered.First(a => a.Id == id)).ToList();

        return View(new ActivityLogListViewModel
        {
            Items = items,
            Page = page,
            PageSize = PageSize,
            TotalCount = totalCount,
            EntityType = entityType
        });
    }
}
