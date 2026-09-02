using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Entities.Calculator;
using eGlobeSolutions.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Infrastructure.Persistence;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();
    public DbSet<PricingPlanFeature> PricingPlanFeatures => Set<PricingPlanFeature>();
    public DbSet<PricingComparisonRow> PricingComparisonRows => Set<PricingComparisonRow>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<SeoMetadata> SeoMetadata => Set<SeoMetadata>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<CmsPage> CmsPages => Set<CmsPage>();
    public DbSet<PricingModule> CalculatorPricingModules => Set<PricingModule>();
    public DbSet<PricingPlanBaseRate> CalculatorPlanBaseRates => Set<PricingPlanBaseRate>();
    public DbSet<TaxConfiguration> CalculatorTaxConfigurations => Set<TaxConfiguration>();
    public DbSet<CurrencyRate> CalculatorCurrencyRates => Set<CurrencyRate>();
    public DbSet<BillingCycle> CalculatorBillingCycles => Set<BillingCycle>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Identity tables use the framework's default (long) names; rename to
        // a consistent "Admin*" prefix so they read clearly in SSMS alongside
        // the CMS content tables.
        builder.Entity<ApplicationUser>().ToTable("AdminUsers");
        builder.Entity<ApplicationRole>().ToTable("AdminRoles");
        builder.Entity<IdentityUserRole<string>>().ToTable("AdminUserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("AdminUserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("AdminUserLogins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("AdminRoleClaims");
        builder.Entity<IdentityUserToken<string>>().ToTable("AdminUserTokens");
    }
}
