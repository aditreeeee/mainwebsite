using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

public class EnquiryStatusUpdateModel
{
    public int Id { get; set; }
    public EnquiryStatus Status { get; set; }
    public string? InternalNotes { get; set; }
    public DateTime? FollowUpAtUtc { get; set; }
}
