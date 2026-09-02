using eGlobeSolutions.Domain.Common;
using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Domain.Entities.Calculator;

/// <summary>
/// The base subscription rate for one of the three pricing models. Covers
/// whatever modules are marked "Included" for that plan in <see cref="PricingModule"/>.
/// </summary>
public class PricingPlanBaseRate : AuditableEntity
{
    public CalculatorPlanType PlanType { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string UnitDescription { get; set; } = string.Empty;

    /// <summary>
    /// Monthly rate per room (PerRoom plan) or per property (PerProperty /
    /// Enterprise plans). Null on Enterprise means "custom, contact sales" and
    /// the calculator shows an estimate note instead of a hard number.
    /// </summary>
    public decimal? MonthlyRatePerUnit { get; set; }

    public decimal OneTimeSetupFee { get; set; }

    /// <summary>True when this plan is priced entirely on request (Enterprise);
    /// the calculator still computes an estimate from MonthlyRatePerUnit if set,
    /// but labels the total as an estimate.</summary>
    public bool IsCustomQuote { get; set; }

    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
}
