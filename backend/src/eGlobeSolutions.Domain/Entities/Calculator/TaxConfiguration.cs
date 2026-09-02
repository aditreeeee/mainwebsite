using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities.Calculator;

/// <summary>An admin-managed tax rate the calculator can apply to the subscription total.</summary>
public class TaxConfiguration : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }

    /// <summary>The tax applied by default when the calculator loads.</summary>
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
}
