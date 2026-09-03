using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Web.Models.Public.Calculator;

/// <summary>Read-only catalog snapshot sent to calculator.html on load, so no
/// price is ever hardcoded in the frontend.</summary>
public class CalculatorCatalogDto
{
    public List<PlanBaseDto> Plans { get; set; } = new();
    public List<ModuleDto> Modules { get; set; } = new();
    public List<TaxDto> Taxes { get; set; } = new();
    public List<BillingCycleDto> BillingCycles { get; set; } = new();
}

public class BillingCycleDto
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Months { get; set; }
    public decimal DiscountPercent { get; set; }
    public bool IsDefault { get; set; }
}

public class PlanBaseDto
{
    public string PlanType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UnitDescription { get; set; } = string.Empty;
    public decimal? MonthlyRatePerUnit { get; set; }
    public decimal OneTimeSetupFee { get; set; }
    public bool IsCustomQuote { get; set; }
}

public class ModuleDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ChargeType { get; set; } = string.Empty;
    public string? VolumeInputLabel { get; set; }
    public string? Tooltip { get; set; }
    public decimal MonthlyRate { get; set; }
    public decimal OneTimeSetupFee { get; set; }
    public decimal CommissionPercent { get; set; }
    public Dictionary<string, string> Availability { get; set; } = new();
}

public class TaxDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>One module the caller wants included/priced, with an optional
/// volume figure for commission-based lines.</summary>
public class CalculateModuleSelection
{
    public int ModuleId { get; set; }
    public decimal? VolumeAmount { get; set; }
}

public class CalculateRequest
{
    public CalculatorPlanType PlanType { get; set; }
    public int NumberOfProperties { get; set; } = 1;
    public int TotalRooms { get; set; } = 1;
    public int? TaxId { get; set; }
    public int? BillingCycleId { get; set; }
    public List<CalculateModuleSelection> SelectedModules { get; set; } = new();

    /// <summary>When true, Sales has waived every one-time setup/onboarding fee
    /// (plan + module) for this client, they're still shown on the quote as
    /// waived line items rather than silently disappearing, but excluded from
    /// every total.</summary>
    public bool WaiveOneTimeSetupFees { get; set; }
}

public class QuoteLineDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Included, Base, AddOn, Commission</summary>
    public string LineType { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
    public decimal OneTimeAmount { get; set; }
    public decimal? CommissionPercent { get; set; }
    public decimal? VolumeAmount { get; set; }

    /// <summary>Units billed: room count for PerRoomMonthly, property count for
    /// PerPropertyMonthly, otherwise 1. Printed quotation line-item quantity.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>MonthlyAmount / Quantity, the module's own rate per unit
    /// (or the flat/commission amount when Quantity is 1). Printed quotation
    /// "Price / Unit" column.</summary>
    public decimal UnitPrice { get; set; }
}

public class CalculateResultDto
{
    public bool Success { get; set; } = true;
    public List<string> Errors { get; set; } = new();

    public List<QuoteLineDto> Lines { get; set; } = new();

    public decimal BaseSubscriptionMonthly { get; set; }
    public decimal AddOnMonthlyTotal { get; set; }
    public decimal SubscriptionMonthlySubtotal { get; set; }
    public decimal OneTimeChargesTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal CommissionMonthlyEstimate { get; set; }

    public decimal TotalMonthlyCost { get; set; }
    public decimal TotalAnnualCost { get; set; }

    public decimal? EffectiveCostPerRoom { get; set; }
    public decimal? EffectiveCostPerProperty { get; set; }

    public bool IsCustomQuote { get; set; }

    /// <summary>True when Sales explicitly waived one-time setup fees for this
    /// client. OneTimeChargesTotal is 0 in that case, but individual line items
    /// still carry their normal (waived) OneTimeAmount for display.</summary>
    public bool OneTimeFeesWaived { get; set; }

    /// <summary>All monetary fields above are already converted into this currency.</summary>
    public string CurrencyCode { get; set; } = "INR";
    public string CurrencySymbol { get; set; } = "₹";

    // ---- Billing cycle ----
    public string BillingCycleLabel { get; set; } = "Monthly";
    public int BillingCycleMonths { get; set; } = 1;
    public decimal BillingCycleDiscountPercent { get; set; }
    /// <summary>Recurring charges (subscription + add-ons + tax + commission) due
    /// for the whole cycle, after the cycle's upfront discount. Excludes one-time fees.</summary>
    public decimal BillingCycleRecurringTotal { get; set; }
    /// <summary>BillingCycleRecurringTotal plus the one-time setup charges, due at signup.</summary>
    public decimal BillingCycleTotalDue { get; set; }
}
