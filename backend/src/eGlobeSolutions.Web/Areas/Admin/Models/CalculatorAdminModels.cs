using eGlobeSolutions.Domain.Enums;

namespace eGlobeSolutions.Web.Areas.Admin.Models;

/// <summary>Inline-edit payload for one calculator module row (upsert).</summary>
public class ModuleSaveModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ModuleCategory Category { get; set; }
    public ModuleChargeType ChargeType { get; set; }
    public string? VolumeInputLabel { get; set; }
    public string? Tooltip { get; set; }

    public ModuleAvailability PerRoomAvailability { get; set; }
    public ModuleAvailability PerPropertyAvailability { get; set; }
    public ModuleAvailability EnterpriseAvailability { get; set; }

    public decimal MonthlyRate { get; set; }
    public decimal OneTimeSetupFee { get; set; }
    public decimal CommissionPercent { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PlanBaseSaveModel
{
    public CalculatorPlanType PlanType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UnitDescription { get; set; } = string.Empty;
    public decimal? MonthlyRatePerUnit { get; set; }
    public decimal OneTimeSetupFee { get; set; }
    public bool IsCustomQuote { get; set; }
}

public class TaxSaveModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal RatePercent { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class CurrencySaveModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal RatePerInr { get; set; } = 1m;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class BillingCycleSaveModel
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Months { get; set; } = 1;
    public decimal DiscountPercent { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
