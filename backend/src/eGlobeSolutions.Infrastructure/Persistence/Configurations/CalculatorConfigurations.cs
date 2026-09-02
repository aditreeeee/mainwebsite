using eGlobeSolutions.Domain.Entities.Calculator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class PricingModuleConfiguration : IEntityTypeConfiguration<PricingModule>
{
    public void Configure(EntityTypeBuilder<PricingModule> builder)
    {
        builder.ToTable("CalculatorPricingModules");
        builder.Property(e => e.Code).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(400);
        builder.Property(e => e.VolumeInputLabel).HasMaxLength(150);
        builder.Property(e => e.Tooltip).HasMaxLength(400);
        builder.Property(e => e.MonthlyRate).HasColumnType("decimal(12,2)");
        builder.Property(e => e.OneTimeSetupFee).HasColumnType("decimal(12,2)");
        builder.Property(e => e.CommissionPercent).HasColumnType("decimal(5,2)");
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class PricingPlanBaseRateConfiguration : IEntityTypeConfiguration<PricingPlanBaseRate>
{
    public void Configure(EntityTypeBuilder<PricingPlanBaseRate> builder)
    {
        builder.ToTable("CalculatorPlanBaseRates");
        builder.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.UnitDescription).HasMaxLength(300);
        builder.Property(e => e.MonthlyRatePerUnit).HasColumnType("decimal(12,2)");
        builder.Property(e => e.OneTimeSetupFee).HasColumnType("decimal(12,2)");
        builder.HasIndex(e => e.PlanType).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class TaxConfigurationConfiguration : IEntityTypeConfiguration<TaxConfiguration>
{
    public void Configure(EntityTypeBuilder<TaxConfiguration> builder)
    {
        builder.ToTable("CalculatorTaxConfigurations");
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.Property(e => e.RatePercent).HasColumnType("decimal(5,2)");
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class CurrencyRateConfiguration : IEntityTypeConfiguration<CurrencyRate>
{
    public void Configure(EntityTypeBuilder<CurrencyRate> builder)
    {
        builder.ToTable("CalculatorCurrencyRates");
        builder.Property(e => e.Code).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Symbol).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(60).IsRequired();
        builder.Property(e => e.RatePerInr).HasColumnType("decimal(12,6)");
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class BillingCycleConfiguration : IEntityTypeConfiguration<BillingCycle>
{
    public void Configure(EntityTypeBuilder<BillingCycle> builder)
    {
        builder.ToTable("CalculatorBillingCycles");
        builder.Property(e => e.Label).HasMaxLength(40).IsRequired();
        builder.Property(e => e.DiscountPercent).HasColumnType("decimal(5,2)");
        builder.HasIndex(e => e.Months).IsUnique();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
