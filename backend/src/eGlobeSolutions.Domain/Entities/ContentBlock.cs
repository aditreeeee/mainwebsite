using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A generic, ordered content block (heading/subtitle/body/CTA/image) used to drive
/// editable sections of a page, e.g. homepage sections or reseller.html sections.
/// PageKey groups blocks per page ("home", "reseller"); SectionKey identifies the
/// specific section on that page (e.g. "hero", "ecosystem", "why-reseller") so the
/// Razor view can look up the right block without relying on array order.
/// </summary>
public class ContentBlock : AuditableEntity
{
    public string PageKey { get; set; } = string.Empty;
    public string SectionKey { get; set; } = string.Empty;

    public string? Kicker { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Body { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public string? ImageUrl { get; set; }

    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}
