using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>A single FAQ entry, grouped by page (pricing.html FAQ, etc).</summary>
public class FaqItem : AuditableEntity
{
    public string PageKey { get; set; } = "pricing";
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;
}
