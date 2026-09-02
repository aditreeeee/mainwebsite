using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities.Calculator;

/// <summary>
/// A billing interval the calculator can quote against (Monthly, Quarterly,
/// 6 Months, Annual, ...), each with its own admin-editable discount for
/// paying upfront. Purely a multiplier/discount on the recurring total,
/// one-time setup fees are unaffected.
/// </summary>
public class BillingCycle : AuditableEntity
{
    public string Label { get; set; } = string.Empty;

    /// <summary>How many months this cycle covers (1, 3, 6, 12, ...).</summary>
    public int Months { get; set; } = 1;

    /// <summary>Discount off the recurring total for paying this cycle upfront.</summary>
    public decimal DiscountPercent { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
