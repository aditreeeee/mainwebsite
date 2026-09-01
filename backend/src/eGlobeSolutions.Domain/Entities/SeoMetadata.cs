using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>Per-page SEO/meta tag content, one row per PageKey (e.g. "home", "pricing").</summary>
public class SeoMetadata : AuditableEntity
{
    public string PageKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? OgImageUrl { get; set; }
}
