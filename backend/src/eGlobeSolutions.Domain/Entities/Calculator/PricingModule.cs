using eGlobeSolutions.Domain.Common;
using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Domain.Entities.Calculator;

/// <summary>
/// A module or standalone product the price calculator can quote (PMS,
/// Channel Manager, Booking Engine, Google Hotel Ads, ...). All pricing is
/// admin-editable here, the calculator never hardcodes a rate.
/// </summary>
public class PricingModule : AuditableEntity
{
    /// <summary>Stable machine key (e.g. "pms", "booking-engine"). Never shown to users.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ModuleCategory Category { get; set; } = ModuleCategory.CoreModule;

    /// <summary>Whether this line is charged as a % commission on a volume the
    /// user supplies at calculation time (Booking Engine, Google Hotel Ads,
    /// Payment Gateway, ...), rather than a flat/per-unit subscription rate.</summary>
    public ModuleChargeType ChargeType { get; set; } = ModuleChargeType.PerRoomMonthly;

    /// <summary>Label shown next to the volume input for commission-based
    /// modules, e.g. "Estimated monthly booking value (₹)".</summary>
    public string? VolumeInputLabel { get; set; }

    // ---- Availability per plan ----
    public ModuleAvailability PerRoomAvailability { get; set; } = ModuleAvailability.NotAvailable;
    public ModuleAvailability PerPropertyAvailability { get; set; } = ModuleAvailability.NotAvailable;
    public ModuleAvailability EnterpriseAvailability { get; set; } = ModuleAvailability.NotAvailable;

    // ---- Rates (interpretation depends on ChargeType) ----
    /// <summary>Recurring monthly rate: per room, per property or flat, per ChargeType.</summary>
    public decimal MonthlyRate { get; set; }

    /// <summary>One-time setup/onboarding charge, independent of ChargeType.</summary>
    public decimal OneTimeSetupFee { get; set; }

    /// <summary>Commission percentage (0-100) used when ChargeType is Commission.</summary>
    public decimal CommissionPercent { get; set; }

    /// <summary>Tooltip copy shown to Sales explaining how this line is billed.</summary>
    public string? Tooltip { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>When this rate was last changed by an admin, for the "effective
    /// date" shown in the pricing manager.</summary>
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
}
