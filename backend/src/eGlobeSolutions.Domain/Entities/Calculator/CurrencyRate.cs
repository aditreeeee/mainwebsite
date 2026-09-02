using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities.Calculator;

/// <summary>
/// A display currency the calculator can convert its (always INR-computed)
/// totals into. Purely a display conversion, admin-managed, one field per
/// currency: how many units of it equal 1 INR.
/// </summary>
public class CurrencyRate : AuditableEntity
{
    public string Code { get; set; } = string.Empty; // e.g. "INR", "USD"
    public string Symbol { get; set; } = string.Empty; // e.g. "₹", "$"
    public string Name { get; set; } = string.Empty;

    /// <summary>How many units of this currency equal 1 INR (INR's own rate is always 1).</summary>
    public decimal RatePerInr { get; set; } = 1m;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
