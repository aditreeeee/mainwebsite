using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>One row of the pricing.html module-comparison table.</summary>
public class PricingComparisonRow : AuditableEntity
{
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>"included", "addon" or "none" for each plan column.</summary>
    public string PerRoomValue { get; set; } = "none";
    public string PerPropertyValue { get; set; } = "none";
    public string EnterpriseValue { get; set; } = "none";

    public int SortOrder { get; set; }
}
