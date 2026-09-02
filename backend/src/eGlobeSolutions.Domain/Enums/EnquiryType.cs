namespace eGlobeSolutions.Domain.Enums;

/// <summary>
/// Which public form an enquiry originated from. Both Contact Sales and the
/// Reseller "Talk to Partnerships" form write to the same enquiry table,
/// distinguished by this type so the admin panel can filter/report on each.
/// </summary>
public enum EnquiryType
{
    ContactSales = 0,
    ResellerPartnership = 1,

    /// <summary>Submitted from the small "Quick Enquiry" popup shown on the
    /// homepage, a lighter-weight version of the full Contact Sales form.</summary>
    QuickEnquiry = 2
}
