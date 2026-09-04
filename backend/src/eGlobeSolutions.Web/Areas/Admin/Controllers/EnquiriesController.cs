using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Areas.Admin.Models;
using eGlobeSolutions.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/enquiries")]
[Authorize(Policy = "EnquiriesManage")]
public class EnquiriesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnquiriesController> _logger;
    private const int DefaultPageSize = 20;

    public EnquiriesController(AppDbContext db, ILogger<EnquiriesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        EnquiryType? type,
        EnquiryStatus? status,
        string sort = "date_desc",
        bool trashed = false,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize is > 0 and <= 100 ? pageSize : DefaultPageSize;

        IQueryable<Enquiry> query = trashed
            ? _db.Enquiries.IgnoreQueryFilters().Where(e => e.IsDeleted)
            : _db.Enquiries;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e =>
                e.FullName.Contains(term) ||
                e.HotelName.Contains(term) ||
                e.Email.Contains(term) ||
                e.Phone.Contains(term));
        }

        if (type.HasValue) query = query.Where(e => e.Type == type.Value);
        if (status.HasValue) query = query.Where(e => e.Status == status.Value);

        query = sort switch
        {
            "date_asc" => query.OrderBy(e => e.CreatedAtUtc),
            "name_asc" => query.OrderBy(e => e.FullName),
            "name_desc" => query.OrderByDescending(e => e.FullName),
            "status_asc" => query.OrderBy(e => e.Status),
            _ => query.OrderByDescending(e => e.CreatedAtUtc) // date_desc, default
        };

        var totalCount = await query.CountAsync(ct);

        // NOTE: deliberately not .Skip().Take() on the entity query. That
        // compiles to SQL Server's OFFSET...FETCH NEXT, which requires
        // database compatibility level >= 110. This database runs at
        // compatibility level 100 (SQL Server 2008) per the project's
        // requirements, and level 100 has no server-side paging syntax EF
        // Core can target. Instead: pull the ordered id list (a single
        // narrow SELECT), page it in memory, then fetch just that page's
        // rows by id. Acceptable at this table's scale; if the enquiries
        // table grows very large, raise compatibility level instead (see
        // README) so OFFSET/FETCH becomes available again.
        var orderedIds = await query.Select(e => e.Id).ToListAsync(ct);
        var pageIds = orderedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var unordered = await (trashed
                ? _db.Enquiries.IgnoreQueryFilters()
                : _db.Enquiries)
            .WhereIdIn(e => e.Id, pageIds)
            .AsNoTracking()
            .ToListAsync(ct);

        var items = pageIds
            .Select(id => unordered.First(e => e.Id == id))
            .ToList();

        var vm = new EnquiryListViewModel
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Search = search,
            TypeFilter = type,
            StatusFilter = status,
            SortBy = sort,
            ShowTrashed = trashed
        };

        return View(vm);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var enquiry = await _db.Enquiries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (enquiry is null) return NotFound();

        await LogActivityAsync("Enquiry.Viewed", enquiry.Id.ToString(), $"Viewed enquiry from {enquiry.FullName}.");

        return View(enquiry);
    }

    [HttpPost("{id:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(EnquiryStatusUpdateModel model, CancellationToken ct)
    {
        var enquiry = await _db.Enquiries.FirstOrDefaultAsync(e => e.Id == model.Id, ct);
        if (enquiry is null) return NotFound();

        var previousStatus = enquiry.Status;
        enquiry.Status = model.Status;
        enquiry.InternalNotes = model.InternalNotes;
        enquiry.FollowUpAtUtc = model.FollowUpAtUtc;
        enquiry.UpdatedAtUtc = DateTime.UtcNow;
        enquiry.UpdatedBy = User.Identity?.Name;

        await _db.SaveChangesAsync(ct);

        await LogActivityAsync(
            "Enquiry.StatusChanged",
            enquiry.Id.ToString(),
            $"Status changed from {previousStatus} to {enquiry.Status} for {enquiry.FullName}.");

        TempData["Success"] = "Enquiry updated.";
        return RedirectToAction(nameof(Details), new { id = enquiry.Id });
    }

    /// <summary>Soft delete: the enquiry moves to Trash, never hard-deleted here.</summary>
    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var enquiry = await _db.Enquiries.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquiry is null) return NotFound();

        enquiry.IsDeleted = true;
        enquiry.DeletedAtUtc = DateTime.UtcNow;
        enquiry.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);

        await LogActivityAsync("Enquiry.Deleted", enquiry.Id.ToString(), $"Moved enquiry from {enquiry.FullName} to trash.");

        TempData["Success"] = "Enquiry moved to trash.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        var enquiry = await _db.Enquiries.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquiry is null) return NotFound();

        enquiry.IsDeleted = false;
        enquiry.DeletedAtUtc = null;
        enquiry.DeletedBy = null;
        await _db.SaveChangesAsync(ct);

        await LogActivityAsync("Enquiry.Restored", enquiry.Id.ToString(), $"Restored enquiry from {enquiry.FullName}.");

        TempData["Success"] = "Enquiry restored.";
        return RedirectToAction(nameof(Index), new { trashed = true });
    }

    private async Task LogActivityAsync(string action, string entityId, string summary)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = action,
            EntityType = nameof(Enquiry),
            EntityId = entityId,
            Summary = summary,
            UserId = User.Identity?.Name,
            UserDisplayName = User.Identity?.Name,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }
}
