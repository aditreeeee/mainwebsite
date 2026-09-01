using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A navigation link. Location groups items by where they render
/// (e.g. "topbar", "nav-dock", "footer-product", "footer-company"),
/// matching the distinct nav lists already in the frontend markup.
/// </summary>
public class MenuItem : AuditableEntity
{
    public string Location { get; set; } = "topbar";
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool OpenInNewTab { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}
