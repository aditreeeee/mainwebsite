using eGlobeSolutions.Domain.Entities.Calculator;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public.Calculator;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Services;

/// <summary>
/// Single source of truth for calculator pricing math. Every number the
/// calculator ever shows comes from the DB-driven catalog (CalculatorPricingModules
/// / CalculatorPlanBaseRates / CalculatorTaxConfigurations) via this service, never
/// hardcoded on the client.
/// </summary>
public class CalculatorPricingService : ICalculatorPricingService
{
    private readonly AppDbContext _db;
    public CalculatorPricingService(AppDbContext db) => _db = db;

    public async Task<CalculatorCatalogDto> GetCatalogAsync(CancellationToken ct = default)
    {
        var plans = await _db.CalculatorPlanBaseRates.OrderBy(p => p.PlanType).ToListAsync(ct);
        var modules = await _db.CalculatorPricingModules
            .Where(m => m.IsActive)
            .OrderBy(m => m.Category).ThenBy(m => m.SortOrder)
            .ToListAsync(ct);
        var taxes = await _db.CalculatorTaxConfigurations
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);
        var currencies = await _db.CalculatorCurrencyRates
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        var billingCycles = await _db.CalculatorBillingCycles
            .Where(b => b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        return new CalculatorCatalogDto
        {
            Plans = plans.Select(p => new PlanBaseDto
            {
                PlanType = p.PlanType.ToString(),
                DisplayName = p.DisplayName,
                UnitDescription = p.UnitDescription,
                MonthlyRatePerUnit = p.MonthlyRatePerUnit,
                OneTimeSetupFee = p.OneTimeSetupFee,
                IsCustomQuote = p.IsCustomQuote
            }).ToList(),
            Modules = modules.Select(ToDto).ToList(),
            Taxes = taxes.Select(t => new TaxDto
            {
                Id = t.Id,
                Name = t.Name,
                RatePercent = t.RatePercent,
                IsDefault = t.IsDefault
            }).ToList(),
            Currencies = currencies.Select(c => new CurrencyDto
            {
                Id = c.Id,
                Code = c.Code,
                Symbol = c.Symbol,
                Name = c.Name,
                RatePerInr = c.RatePerInr,
                IsDefault = c.IsDefault
            }).ToList(),
            BillingCycles = billingCycles.Select(b => new BillingCycleDto
            {
                Id = b.Id,
                Label = b.Label,
                Months = b.Months,
                DiscountPercent = b.DiscountPercent,
                IsDefault = b.IsDefault
            }).ToList()
        };
    }

    private static ModuleDto ToDto(PricingModule m) => new()
    {
        Id = m.Id,
        Code = m.Code,
        Name = m.Name,
        Description = m.Description,
        Category = m.Category.ToString(),
        ChargeType = m.ChargeType.ToString(),
        VolumeInputLabel = m.VolumeInputLabel,
        Tooltip = m.Tooltip,
        MonthlyRate = m.MonthlyRate,
        OneTimeSetupFee = m.OneTimeSetupFee,
        CommissionPercent = m.CommissionPercent,
        Availability = new Dictionary<string, string>
        {
            [CalculatorPlanType.PerRoom.ToString()] = m.PerRoomAvailability.ToString(),
            [CalculatorPlanType.PerProperty.ToString()] = m.PerPropertyAvailability.ToString(),
            [CalculatorPlanType.Enterprise.ToString()] = m.EnterpriseAvailability.ToString()
        }
    };

    public async Task<CalculateResultDto> CalculateAsync(CalculateRequest request, CancellationToken ct = default)
    {
        var result = new CalculateResultDto();
        var errors = new List<string>();

        var rooms = Math.Max(request.TotalRooms, 0);
        var properties = Math.Max(request.NumberOfProperties, 0);
        if (rooms <= 0) errors.Add("Total rooms must be at least 1.");
        if (properties <= 0) errors.Add("Number of properties must be at least 1.");

        var plan = await _db.CalculatorPlanBaseRates
            .FirstOrDefaultAsync(p => p.PlanType == request.PlanType, ct);
        if (plan is null)
        {
            errors.Add("Selected pricing plan is not configured.");
            return new CalculateResultDto { Success = false, Errors = errors };
        }

        if (errors.Count > 0) return new CalculateResultDto { Success = false, Errors = errors };

        result.IsCustomQuote = plan.IsCustomQuote;

        // ---- Base subscription ----
        // Flat monthly base fee regardless of property/room count, admin-set per plan.
        var baseMonthly = plan.MonthlyRatePerUnit ?? 0m;
        result.BaseSubscriptionMonthly = baseMonthly;
        result.OneTimeChargesTotal += plan.OneTimeSetupFee;

        result.Lines.Add(new QuoteLineDto
        {
            Name = $"{plan.DisplayName} base subscription",
            LineType = "Base",
            MonthlyAmount = baseMonthly,
            OneTimeAmount = plan.OneTimeSetupFee
        });

        // ---- Modules ----
        // Catalog is small (a few dozen rows at most), so pull the active set
        // and filter in memory rather than translating a Contains(list) to SQL,
        // which needs OPENJSON support some SQL Server compatibility levels lack.
        var moduleIds = request.SelectedModules.Select(s => s.ModuleId).Distinct().ToHashSet();
        var allActiveModules = await _db.CalculatorPricingModules.Where(m => m.IsActive).ToListAsync(ct);
        var modules = allActiveModules.Where(m => moduleIds.Contains(m.Id)).ToList();

        foreach (var sel in request.SelectedModules)
        {
            var module = modules.FirstOrDefault(m => m.Id == sel.ModuleId);
            if (module is null) continue;

            var availability = GetAvailability(module, request.PlanType);
            if (availability == ModuleAvailability.NotAvailable)
            {
                result.Lines.Add(new QuoteLineDto
                {
                    Name = module.Name,
                    LineType = "Ineligible",
                    MonthlyAmount = 0,
                    OneTimeAmount = 0
                });
                continue;
            }

            if (availability == ModuleAvailability.Included)
            {
                result.Lines.Add(new QuoteLineDto
                {
                    Name = module.Name,
                    LineType = "Included",
                    MonthlyAmount = 0,
                    OneTimeAmount = 0
                });
                continue;
            }

            // AddOn
            switch (module.ChargeType)
            {
                case ModuleChargeType.Commission:
                    var volume = Math.Max(sel.VolumeAmount ?? 0m, 0m);
                    var commissionAmount = volume * (module.CommissionPercent / 100m);
                    result.CommissionMonthlyEstimate += commissionAmount;
                    result.OneTimeChargesTotal += module.OneTimeSetupFee;
                    result.Lines.Add(new QuoteLineDto
                    {
                        Name = module.Name,
                        LineType = "Commission",
                        MonthlyAmount = commissionAmount,
                        OneTimeAmount = module.OneTimeSetupFee,
                        CommissionPercent = module.CommissionPercent,
                        VolumeAmount = volume
                    });
                    break;

                default:
                    var monthly = module.ChargeType switch
                    {
                        ModuleChargeType.PerRoomMonthly => module.MonthlyRate * rooms,
                        ModuleChargeType.PerPropertyMonthly => module.MonthlyRate * properties,
                        ModuleChargeType.FlatMonthly => module.MonthlyRate,
                        ModuleChargeType.OneTimeOnly => 0m,
                        _ => 0m
                    };
                    result.AddOnMonthlyTotal += monthly;
                    result.OneTimeChargesTotal += module.OneTimeSetupFee;
                    result.Lines.Add(new QuoteLineDto
                    {
                        Name = module.Name,
                        LineType = "AddOn",
                        MonthlyAmount = monthly,
                        OneTimeAmount = module.OneTimeSetupFee
                    });
                    break;
            }
        }

        // ---- Tax ----
        var tax = request.TaxId.HasValue
            ? await _db.CalculatorTaxConfigurations.FirstOrDefaultAsync(t => t.Id == request.TaxId && t.IsActive, ct)
            : await _db.CalculatorTaxConfigurations.FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, ct);

        result.SubscriptionMonthlySubtotal = result.BaseSubscriptionMonthly + result.AddOnMonthlyTotal;
        result.TaxRatePercent = tax?.RatePercent ?? 0m;
        result.TaxAmount = result.SubscriptionMonthlySubtotal * (result.TaxRatePercent / 100m);

        result.TotalMonthlyCost = result.SubscriptionMonthlySubtotal + result.TaxAmount + result.CommissionMonthlyEstimate;
        result.TotalAnnualCost = result.TotalMonthlyCost * 12m;

        if (rooms > 0) result.EffectiveCostPerRoom = Math.Round(result.TotalMonthlyCost / rooms, 2);
        if (properties > 0) result.EffectiveCostPerProperty = Math.Round(result.TotalMonthlyCost / properties, 2);

        // ---- Billing cycle (3/6/12 months, ...) ----
        var cycle = request.BillingCycleId.HasValue
            ? await _db.CalculatorBillingCycles.FirstOrDefaultAsync(b => b.Id == request.BillingCycleId && b.IsActive, ct)
            : await _db.CalculatorBillingCycles.FirstOrDefaultAsync(b => b.IsDefault && b.IsActive, ct);

        var cycleMonths = cycle?.Months ?? 1;
        var cycleDiscount = cycle?.DiscountPercent ?? 0m;
        var recurringForCycle = result.TotalMonthlyCost * cycleMonths * (1 - cycleDiscount / 100m);

        result.BillingCycleLabel = cycle?.Label ?? "Monthly";
        result.BillingCycleMonths = cycleMonths;
        result.BillingCycleDiscountPercent = cycleDiscount;
        result.BillingCycleRecurringTotal = Round(recurringForCycle);
        result.BillingCycleTotalDue = Round(recurringForCycle + result.OneTimeChargesTotal);

        // ---- Currency conversion (display only, math above is always in INR) ----
        var currency = request.CurrencyId.HasValue
            ? await _db.CalculatorCurrencyRates.FirstOrDefaultAsync(c => c.Id == request.CurrencyId && c.IsActive, ct)
            : await _db.CalculatorCurrencyRates.FirstOrDefaultAsync(c => c.IsDefault && c.IsActive, ct);

        if (currency is not null && currency.RatePerInr != 1m)
        {
            var rate = currency.RatePerInr;
            result.BaseSubscriptionMonthly = Round(result.BaseSubscriptionMonthly * rate);
            result.AddOnMonthlyTotal = Round(result.AddOnMonthlyTotal * rate);
            result.SubscriptionMonthlySubtotal = Round(result.SubscriptionMonthlySubtotal * rate);
            result.OneTimeChargesTotal = Round(result.OneTimeChargesTotal * rate);
            result.TaxAmount = Round(result.TaxAmount * rate);
            result.CommissionMonthlyEstimate = Round(result.CommissionMonthlyEstimate * rate);
            result.TotalMonthlyCost = Round(result.TotalMonthlyCost * rate);
            result.TotalAnnualCost = Round(result.TotalAnnualCost * rate);
            if (result.EffectiveCostPerRoom.HasValue) result.EffectiveCostPerRoom = Round(result.EffectiveCostPerRoom.Value * rate);
            if (result.EffectiveCostPerProperty.HasValue) result.EffectiveCostPerProperty = Round(result.EffectiveCostPerProperty.Value * rate);
            result.BillingCycleRecurringTotal = Round(result.BillingCycleRecurringTotal * rate);
            result.BillingCycleTotalDue = Round(result.BillingCycleTotalDue * rate);
            foreach (var line in result.Lines)
            {
                line.MonthlyAmount = Round(line.MonthlyAmount * rate);
                line.OneTimeAmount = Round(line.OneTimeAmount * rate);
                if (line.VolumeAmount.HasValue) line.VolumeAmount = Round(line.VolumeAmount.Value * rate);
            }
        }

        result.CurrencyCode = currency?.Code ?? "INR";
        result.CurrencySymbol = currency?.Symbol ?? "₹";

        return result;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2);

    private static ModuleAvailability GetAvailability(PricingModule m, CalculatorPlanType plan) => plan switch
    {
        CalculatorPlanType.PerRoom => m.PerRoomAvailability,
        CalculatorPlanType.PerProperty => m.PerPropertyAvailability,
        CalculatorPlanType.Enterprise => m.EnterpriseAvailability,
        _ => ModuleAvailability.NotAvailable
    };
}
