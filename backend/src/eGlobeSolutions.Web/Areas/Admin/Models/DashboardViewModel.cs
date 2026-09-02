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

    // ---- System health / quick observability ----
    public int ActiveCalculatorModules { get; init; }
    public bool CalculatorPlansConfigured { get; init; }
    public bool CalculatorTaxConfigured { get; init; }
    public bool CalculatorCurrencyConfigured { get; init; }
    public bool SmtpConfigured { get; init; }
    public bool EnquiryNotificationsEnabled { get; init; }
}
