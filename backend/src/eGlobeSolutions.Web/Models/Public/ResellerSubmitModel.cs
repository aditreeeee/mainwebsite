using System.ComponentModel.DataAnnotations;

namespace eGlobeSolutions.Web.Models.Public;

/// <summary>
/// Backs the "Talk to Partnerships" flow on reseller.html.
/// </summary>
public class ResellerSubmitModel
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(150)]
    public string? CompanyType { get; set; }

    [StringLength(100)]
    public string? ExpectedPropertyVolume { get; set; }

    [StringLength(2000)]
    public string? Message { get; set; }

    public string? Website { get; set; }
}
