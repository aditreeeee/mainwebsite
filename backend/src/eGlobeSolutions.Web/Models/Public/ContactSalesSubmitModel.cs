using System.ComponentModel.DataAnnotations;

namespace eGlobeSolutions.Web.Models.Public;

/// <summary>
/// Matches the fields on the existing #sales-form in contact.html
/// (Full Name, Hotel Name, Work Email, Phone, room-range chips, product
/// chips, optional message). The frontend's own client-side validation
/// stays as-is; this is the server-side authority.
/// </summary>
public class ContactSalesSubmitModel
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Optional on the homepage's mini "Quick Enquiry" popup, which
    /// asks for less up front than the full Contact Sales form.</summary>
    [StringLength(150)]
    public string? HotelName { get; set; }

    /// <summary>Required on the full Contact Sales form; optional on the
    /// homepage's mini popup, which only insists on a phone number to call back.</summary>
    [EmailAddress, StringLength(200)]
    public string? Email { get; set; }

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Optional on the mini popup, which skips the room-range chips.</summary>
    [StringLength(20)]
    public string? RoomsRange { get; set; }

    /// <summary>Comma-separated chip values, e.g. "Demo Call,Cloud PMS".</summary>
    [StringLength(500)]
    public string? InterestedIn { get; set; }

    [StringLength(300)]
    public string? OtherInterest { get; set; }

    [StringLength(2000)]
    public string? Message { get; set; }

    /// <summary>Honeypot field: real users never fill this in. Bots that
    /// auto-fill every input will, and the submission is silently dropped.</summary>
    public string? Website { get; set; }

    /// <summary>Set to "quick" by the homepage's mini popup form so the
    /// controller records it as EnquiryType.QuickEnquiry instead of the
    /// default full Contact Sales submission.</summary>
    [StringLength(20)]
    public string? FormType { get; set; }
}
