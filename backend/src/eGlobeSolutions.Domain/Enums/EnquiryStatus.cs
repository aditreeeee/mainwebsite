namespace eGlobeSolutions.Domain.Enums;

/// <summary>
/// Workflow status for a sales/reseller enquiry, driven from the admin panel.
/// </summary>
public enum EnquiryStatus
{
    New = 0,
    Contacted = 10,
    Qualified = 20,
    ProposalSent = 30,
    Won = 40,
    Lost = 50,
    Spam = 60
}
