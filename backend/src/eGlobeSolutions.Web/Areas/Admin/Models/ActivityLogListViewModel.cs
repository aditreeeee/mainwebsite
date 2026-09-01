using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

public class ActivityLogListViewModel
{
    public List<ActivityLog> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public string? EntityType { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)Math.Max(PageSize, 1));
}
