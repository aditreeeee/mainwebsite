using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

public class DashboardViewModel
{
    public int NewEnquiriesLast7Days { get; init; }
    public int NewEnquiriesLast30Days { get; init; }
    public int OpenEnquiries { get; init; }
    public int TotalContactSalesEnquiries { get; init; }
    public int TotalResellerEnquiries { get; init; }

    public IReadOnlyList<Enquiry> RecentEnquiries { get; init; } = Array.Empty<Enquiry>();
    public IReadOnlyList<ActivityLog> RecentActivity { get; init; } = Array.Empty<ActivityLog>();
}
