using System.ComponentModel.DataAnnotations;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

/// <summary>Typed form over the fixed SiteSettingKeys set, so Settings feels like a real
/// WordPress-style settings page instead of a raw key/value editor.</summary>
public class SiteSettingsFormModel
{
    [Required, StringLength(150)]
    public string SiteName { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = string.Empty;

    [StringLength(20)]
    public string? WhatsAppNumber { get; set; }

    [StringLength(200)]
    public string? CallUsNumbers { get; set; }

    [StringLength(200)]
    public string? BusinessHours { get; set; }

    [Url, StringLength(300)]
    public string? FacebookUrl { get; set; }

    [Url, StringLength(300)]
    public string? YoutubeUrl { get; set; }

    [Url, StringLength(300)]
    public string? LinkedInUrl { get; set; }

    [Url, StringLength(300)]
    public string? AppStoreUrl { get; set; }

    [Url, StringLength(300)]
    public string? GooglePlayUrl { get; set; }

    [StringLength(200)]
    public string? FooterCopyright { get; set; }

    // ---- SMTP ----
    [StringLength(200)]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int? SmtpPort { get; set; } = 587;

    [StringLength(200)]
    public string? SmtpUsername { get; set; }

    /// <summary>Left blank on save means "keep the existing stored password".</summary>
    [StringLength(300)]
    public string? SmtpPassword { get; set; }

    public bool SmtpEnableSsl { get; set; } = true;

    [EmailAddress, StringLength(200)]
    public string? SmtpFromEmail { get; set; }

    [StringLength(150)]
    public string? SmtpFromName { get; set; }

    [EmailAddress, StringLength(200)]
    public string? SmtpNotifyEmail { get; set; }

    public bool SmtpNotifyOnEnquiry { get; set; } = true;
}

public class SmtpTestModel
{
    [Required, EmailAddress]
    public string ToEmail { get; set; } = string.Empty;
}
