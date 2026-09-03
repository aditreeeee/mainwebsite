using eGlobeSolutions.Domain.Entities.Calculator;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public.Calculator;
using eGlobeSolutions.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eGlobeSolutions.Tests;

/// <summary>
/// The calculator is used live by the sales team while on a call with a
/// prospect, so a wrong number here isn't a cosmetic bug, it's a wrong quote
/// read out to a customer. These tests pin down the pricing math in
/// CalculatorPricingService against a small in-memory catalog.
/// </summary>
public class CalculatorPricingServiceTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = NewDb();

        db.CalculatorPlanBaseRates.AddRange(
            new PricingPlanBaseRate
            {
                PlanType = CalculatorPlanType.PerRoom,
                DisplayName = "Per Room",
                UnitDescription = "unit",
                MonthlyRatePerUnit = 1200m,
                OneTimeSetupFee = 7500m
            },
            new PricingPlanBaseRate
            {
                PlanType = CalculatorPlanType.Enterprise,
                DisplayName = "Enterprise",
                UnitDescription = "unit",
                MonthlyRatePerUnit = 1200m,
                OneTimeSetupFee = 50000m,
                IsCustomQuote = true
            });

        db.CalculatorPricingModules.AddRange(
            new PricingModule
            {
                Code = "pms", Name = "PMS", ChargeType = ModuleChargeType.PerRoomMonthly,
                PerRoomAvailability = ModuleAvailability.Included, EnterpriseAvailability = ModuleAvailability.Included,
                IsActive = true
            },
            new PricingModule
            {
                Code = "pos-kot", Name = "POS & KOT", ChargeType = ModuleChargeType.PerRoomMonthly, MonthlyRate = 19m,
                PerRoomAvailability = ModuleAvailability.AddOn, EnterpriseAvailability = ModuleAvailability.Included,
                IsActive = true
            },
            new PricingModule
            {
                Code = "b2b-stay", Name = "B2B Stay", ChargeType = ModuleChargeType.PerPropertyMonthly, MonthlyRate = 2999m,
                PerRoomAvailability = ModuleAvailability.NotAvailable, EnterpriseAvailability = ModuleAvailability.Included,
                IsActive = true
            },
            new PricingModule
            {
                Code = "booking-engine", Name = "Booking Engine", ChargeType = ModuleChargeType.Commission, CommissionPercent = 5m,
                OneTimeSetupFee = 2500m,
                PerRoomAvailability = ModuleAvailability.AddOn, EnterpriseAvailability = ModuleAvailability.AddOn,
                IsActive = true
            },
            new PricingModule
            {
                // Inactive modules must never be selectable/priced, even if a stale id is posted.
                Code = "retired-module", Name = "Retired Module", ChargeType = ModuleChargeType.FlatMonthly, MonthlyRate = 999m,
                PerRoomAvailability = ModuleAvailability.AddOn, EnterpriseAvailability = ModuleAvailability.AddOn,
                IsActive = false
            });

        db.CalculatorTaxConfigurations.AddRange(
            new TaxConfiguration { Name = "GST (18%)", RatePercent = 18m, IsDefault = true, IsActive = true, SortOrder = 0 },
            new TaxConfiguration { Name = "No Tax", RatePercent = 0m, IsActive = true, SortOrder = 1 });

        db.CalculatorBillingCycles.AddRange(
            new BillingCycle { Label = "Monthly", Months = 1, DiscountPercent = 0m, IsDefault = true, IsActive = true, SortOrder = 0 },
            new BillingCycle { Label = "Annual", Months = 12, DiscountPercent = 15m, IsActive = true, SortOrder = 1 });

        await db.SaveChangesAsync();
        return db;
    }

    private static int ModuleId(AppDbContext db, string code) =>
        db.CalculatorPricingModules.Single(m => m.Code == code).Id;

    [Fact]
    public async Task PerRoom_plan_charges_base_fee_plus_addon_per_room()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 20,
            NumberOfProperties = 1,
            SelectedModules =
            {
                new CalculateModuleSelection { ModuleId = ModuleId(db, "pms") },       // Included on PerRoom -> free
                new CalculateModuleSelection { ModuleId = ModuleId(db, "pos-kot") },    // AddOn on PerRoom -> 19 * 20 rooms
            }
        });

        Assert.True(result.Success);
        Assert.Equal(1200m, result.BaseSubscriptionMonthly);
        Assert.Equal(380m, result.AddOnMonthlyTotal); // 19 * 20
        Assert.Equal(1580m, result.SubscriptionMonthlySubtotal); // base + addon
        Assert.Equal(7500m, result.OneTimeChargesTotal); // plan setup fee only, PMS/POS have none here

        var posLine = Assert.Single(result.Lines, l => l.Name == "POS & KOT");
        Assert.Equal(20, posLine.Quantity); // billed per room
        Assert.Equal(19m, posLine.UnitPrice); // the module's own rate, quotation "Price / Unit"
    }

    [Fact]
    public async Task Module_not_typical_for_plan_can_still_be_manually_selected_and_is_charged_normally()
    {
        // Plan availability (Included/AddOn/NotAvailable) is a reference badge only:
        // Sales can select any module for any plan for a specific client, and it's
        // priced at the module's own normal rate, same as a standard add-on would be.
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 2,
            SelectedModules = { new CalculateModuleSelection { ModuleId = ModuleId(db, "b2b-stay") } } // NotAvailable on PerRoom, selected anyway
        });

        Assert.True(result.Success);
        Assert.Equal(2999m * 2, result.AddOnMonthlyTotal); // PerPropertyMonthly rate * 2 properties
        var line = Assert.Single(result.Lines, l => l.Name == "B2B Stay");
        Assert.Equal("AddOn", line.LineType);
        Assert.Equal(2999m * 2, line.MonthlyAmount);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(2999m, line.UnitPrice);
    }

    [Fact]
    public async Task Commission_module_charges_percent_of_supplied_volume_not_a_flat_rate()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            SelectedModules =
            {
                new CalculateModuleSelection { ModuleId = ModuleId(db, "booking-engine"), VolumeAmount = 200000m }
            }
        });

        Assert.True(result.Success);
        Assert.Equal(10000m, result.CommissionMonthlyEstimate); // 5% of 200,000
        Assert.Equal(2500m, result.OneTimeChargesTotal - 7500m); // booking engine setup fee on top of the plan's
        var line = Assert.Single(result.Lines, l => l.Name == "Booking Engine");
        Assert.Equal("Commission", line.LineType);
        Assert.Equal(5m, line.CommissionPercent);
    }

    [Fact]
    public async Task Negative_or_missing_commission_volume_never_produces_negative_revenue()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            SelectedModules =
            {
                new CalculateModuleSelection { ModuleId = ModuleId(db, "booking-engine"), VolumeAmount = -500m }
            }
        });

        Assert.True(result.Success);
        Assert.Equal(0m, result.CommissionMonthlyEstimate);
    }

    [Fact]
    public async Task Tax_is_applied_to_subscription_subtotal_only_not_to_commission()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            SelectedModules =
            {
                new CalculateModuleSelection { ModuleId = ModuleId(db, "booking-engine"), VolumeAmount = 100000m }
            }
        });

        // Base 1200, no per-room addon selected -> subtotal 1200, tax = 18% of 1200 = 216
        Assert.Equal(1200m, result.SubscriptionMonthlySubtotal);
        Assert.Equal(216m, result.TaxAmount);
        Assert.Equal(5000m, result.CommissionMonthlyEstimate); // 5% of 100,000, untaxed
        Assert.Equal(1200m + 216m + 5000m, result.TotalMonthlyCost);
    }

    [Fact]
    public async Task Explicit_tax_id_overrides_the_default_tax()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);
        var noTaxId = db.CalculatorTaxConfigurations.Single(t => t.Name == "No Tax").Id;

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            TaxId = noTaxId
        });

        Assert.Equal(0m, result.TaxRatePercent);
        Assert.Equal(0m, result.TaxAmount);
    }

    [Fact]
    public async Task Annual_billing_cycle_applies_discount_to_recurring_total_but_not_to_onetime_fees()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);
        var annualId = db.CalculatorBillingCycles.Single(b => b.Label == "Annual").Id;

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            BillingCycleId = annualId,
            TaxId = db.CalculatorTaxConfigurations.Single(t => t.Name == "No Tax").Id
        });

        // Monthly cost = base 1200 only (no addons/tax). 12 months * 15% off = 1200*12*0.85 = 12240
        Assert.Equal(12240m, result.BillingCycleRecurringTotal);
        // Plus the plan's one-time setup fee (7500), not discounted.
        Assert.Equal(12240m + 7500m, result.BillingCycleTotalDue);
    }

    [Fact]
    public async Task Inactive_module_id_is_silently_ignored_even_if_posted()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);
        var retiredId = db.CalculatorPricingModules.Single(m => m.Code == "retired-module").Id;

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            SelectedModules = { new CalculateModuleSelection { ModuleId = retiredId } }
        });

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Lines, l => l.Name == "Retired Module");
        Assert.Equal(0m, result.AddOnMonthlyTotal);
    }

    [Fact]
    public async Task Zero_or_negative_rooms_or_properties_fails_with_a_validation_error()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 0,
            NumberOfProperties = 0
        });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Enterprise_plan_is_flagged_as_a_custom_quote()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.Enterprise,
            TotalRooms = 50,
            NumberOfProperties = 3
        });

        Assert.True(result.Success);
        Assert.True(result.IsCustomQuote);
    }

    [Fact]
    public async Task Effective_cost_per_room_and_per_property_are_computed_from_the_total()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 2,
            TaxId = db.CalculatorTaxConfigurations.Single(t => t.Name == "No Tax").Id
        });

        // TotalMonthlyCost = 1200 (base only, no addons, no tax)
        Assert.Equal(120m, result.EffectiveCostPerRoom);   // 1200 / 10
        Assert.Equal(600m, result.EffectiveCostPerProperty); // 1200 / 2
    }

    [Fact]
    public async Task Waiving_setup_fees_zeroes_the_total_but_line_items_still_show_the_normal_fee()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            SelectedModules = { new CalculateModuleSelection { ModuleId = ModuleId(db, "booking-engine"), VolumeAmount = 50000m } },
            WaiveOneTimeSetupFees = true
        });

        Assert.True(result.Success);
        Assert.True(result.OneTimeFeesWaived);
        Assert.Equal(0m, result.OneTimeChargesTotal); // plan setup fee (7500) + booking engine setup fee (2500), waived
        Assert.Equal(result.BillingCycleRecurringTotal, result.BillingCycleTotalDue); // no setup fee added on top

        // Individual lines still carry their normal (waived) fee, for the printed quote to show what was waived.
        var baseLine = Assert.Single(result.Lines, l => l.LineType == "Base");
        Assert.Equal(7500m, baseLine.OneTimeAmount);
        var bookingLine = Assert.Single(result.Lines, l => l.Name == "Booking Engine");
        Assert.Equal(2500m, bookingLine.OneTimeAmount);
    }

    [Fact]
    public async Task Not_waiving_setup_fees_charges_them_normally()
    {
        var db = await SeedAsync();
        var service = new CalculatorPricingService(db);

        var result = await service.CalculateAsync(new CalculateRequest
        {
            PlanType = CalculatorPlanType.PerRoom,
            TotalRooms = 10,
            NumberOfProperties = 1,
            WaiveOneTimeSetupFees = false
        });

        Assert.False(result.OneTimeFeesWaived);
        Assert.Equal(7500m, result.OneTimeChargesTotal);
    }
}
