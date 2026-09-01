using eGlobeSolutions.Domain.Common;
using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A submission from either the Contact Sales form (contact.html) or the
/// Reseller "Talk to Partnerships" form (reseller.html). Both forms post to
/// the same backend module so the admin team works one enquiry queue.
/// </summary>
public class Enquiry : AuditableEntity
{
    public EnquiryType Type { get; set; }
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;

    // Contact Sales fields (mirrors the existing #sales-form on contact.html)
    public string FullName { get; set; } = string.Empty;
    public string HotelName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>Room-count bucket selected in the UI, e.g. "1-10", "11-50", "51-150", "150+".</summary>
    public string? RoomsRange { get; set; }

    /// <summary>Comma-separated product chip selections (Demo Call, Free Trial, Cloud PMS, ...).</summary>
    public string? InterestedIn { get; set; }

    /// <summary>Free-text value from the "Others" chip's follow-up input, if used.</summary>
    public string? OtherInterest { get; set; }

    public string? Message { get; set; }

    // Reseller-specific optional fields
    public string? CompanyType { get; set; }
    public string? ExpectedPropertyVolume { get; set; }

    // Admin workflow
    public string? AssignedToUserId { get; set; }
    public string? InternalNotes { get; set; }
    public DateTime? FollowUpAtUtc { get; set; }

    // Anti-abuse / provenance
    public string? SourceIpAddress { get; set; }
    public string? SourceUserAgent { get; set; }
    public string? SourcePage { get; set; }
}
