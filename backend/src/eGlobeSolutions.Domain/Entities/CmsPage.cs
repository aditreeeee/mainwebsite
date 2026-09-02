using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A generic, admin-created page, the WordPress "Pages" equivalent: create
/// one from /admin/pages, give it a title, a URL slug and an HTML body, and
/// it's immediately live at "/{Slug}.html" through PageController, no code
/// change or redeploy needed. Meant for pages that don't already have a
/// dedicated Razor view (product pages, landing pages, one-off campaign
/// pages, ...), not a replacement for Home/Pricing/Contact/Blog/Calculator/
/// Reseller, which stay their own purpose-built views. A new page isn't
/// automatically linked from anywhere, same as WordPress, add it to a menu
/// via /admin/menus (footer/topbar/nav-dock) if it should be reachable from
/// navigation, or just share the URL directly.
/// </summary>
public class CmsPage : AuditableEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>URL segment before ".html". Must be unique across both CmsPages and BlogPosts,
    /// they share the same "/{slug}.html" route (PageController checks BlogPosts first).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Full page HTML body, rendered after the hero.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Short line shown under the title in the page's hero.</summary>
    public string? Subtitle { get; set; }

    /// <summary>When true, the template's generic Title/Subtitle hero is skipped
    /// entirely, Body is expected to start with its own full hero section
    /// (e.g. migrated product pages, which need a badge/breadcrumb/CTA hero
    /// richer than the generic one).</summary>
    public bool UseCustomHero { get; set; }

    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
}
