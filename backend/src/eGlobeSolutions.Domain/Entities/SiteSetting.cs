using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A single global site setting (contact info, social links, footer text, etc),
/// stored as a key/value pair so new settings can be added without a migration.
/// The admin Settings screen presents a fixed, typed form over a known key set
/// (see SiteSettingKeys) rather than a raw key/value editor, for a WordPress-style
/// "Settings" page feel.
/// </summary>
public class SiteSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Group { get; set; } = "General";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

public static class SiteSettingKeys
{
    public const string SiteName = "SiteName";
    public const string Phone = "Phone";
    public const string Email = "Email";
    public const string WhatsAppNumber = "WhatsAppNumber";
    public const string BusinessHours = "BusinessHours";
    public const string FacebookUrl = "FacebookUrl";
    public const string YoutubeUrl = "YoutubeUrl";
    public const string LinkedInUrl = "LinkedInUrl";
    public const string AppStoreUrl = "AppStoreUrl";
    public const string GooglePlayUrl = "GooglePlayUrl";
    public const string FooterCopyright = "FooterCopyright";

    /// <summary>Landline "Call Us" numbers, slash-separated (e.g. "+91 11 41717081/ +91 11 41717082/ +91 11 41717021"), shown as a separate line from the main mobile Phone number.</summary>
    public const string CallUsNumbers = "CallUsNumbers";

    public static readonly string[] All =
    {
        SiteName, Phone, Email, WhatsAppNumber, CallUsNumbers, BusinessHours,
        FacebookUrl, YoutubeUrl, LinkedInUrl, AppStoreUrl, GooglePlayUrl, FooterCopyright
    };
}
