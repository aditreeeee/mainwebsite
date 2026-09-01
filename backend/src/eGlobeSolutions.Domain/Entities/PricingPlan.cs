using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>One of the pricing.html "price-teaser-card" columns (Per-Room, Per-Property, Enterprise, etc).</summary>
public class PricingPlan : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string UnitDescription { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public string CtaLabel { get; set; } = "Contact Sales";
    public string CtaUrl { get; set; } = "/contact";
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; } = true;

    public List<PricingPlanFeature> Features { get; set; } = new();
}
