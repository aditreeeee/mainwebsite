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

    [Required, StringLength(150, MinimumLength = 2)]
    public string HotelName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string RoomsRange { get; set; } = string.Empty;

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
}
