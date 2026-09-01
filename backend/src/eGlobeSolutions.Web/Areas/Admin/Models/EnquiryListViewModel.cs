using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

public class EnquiryListViewModel
{
    public IReadOnlyList<Enquiry> Items { get; init; } = Array.Empty<Enquiry>();

    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public string? Search { get; init; }
    public EnquiryType? TypeFilter { get; init; }
    public EnquiryStatus? StatusFilter { get; init; }
    public string SortBy { get; init; } = "date_desc";
    public bool ShowTrashed { get; init; }
}
