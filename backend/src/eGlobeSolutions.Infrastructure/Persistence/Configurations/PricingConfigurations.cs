using eGlobeSolutions.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace eGlobeSolutions.Infrastructure.Persistence.Configurations;

public class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
{
    public void Configure(EntityTypeBuilder<PricingPlan> builder)
    {
        builder.ToTable("PricingPlans");
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.BadgeText).HasMaxLength(60);
        builder.Property(e => e.UnitDescription).HasMaxLength(300);
        builder.Property(e => e.CtaLabel).HasMaxLength(60);
        builder.Property(e => e.CtaUrl).HasMaxLength(200);
        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasMany(e => e.Features)
            .WithOne(f => f.PricingPlan)
            .HasForeignKey(f => f.PricingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PricingPlanFeatureConfiguration : IEntityTypeConfiguration<PricingPlanFeature>
{
    public void Configure(EntityTypeBuilder<PricingPlanFeature> builder)
    {
        builder.ToTable("PricingPlanFeatures");
        builder.Property(e => e.Text).HasMaxLength(300).IsRequired();
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class PricingComparisonRowConfiguration : IEntityTypeConfiguration<PricingComparisonRow>
{
    public void Configure(EntityTypeBuilder<PricingComparisonRow> builder)
    {
        builder.ToTable("PricingComparisonRows");
        builder.Property(e => e.ModuleName).HasMaxLength(150).IsRequired();
        builder.Property(e => e.PerRoomValue).HasMaxLength(20);
        builder.Property(e => e.PerPropertyValue).HasMaxLength(20);
        builder.Property(e => e.EnterpriseValue).HasMaxLength(20);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}

public class FaqItemConfiguration : IEntityTypeConfiguration<FaqItem>
{
    public void Configure(EntityTypeBuilder<FaqItem> builder)
    {
        builder.ToTable("FaqItems");
        builder.Property(e => e.PageKey).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Question).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Answer).HasMaxLength(2000).IsRequired();
        builder.HasIndex(e => e.PageKey);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
