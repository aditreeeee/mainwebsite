using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Automated check for the audit gap "no verification that a genuinely
/// fresh database seeds correctly end-to-end": rather than a one-off manual
/// script, this asserts, against the real DbInitializer.InitializeAsync
/// output (CustomWebApplicationFactory runs it on every test host startup,
/// same code path production uses), that every piece of seeded content this
/// session added actually exists. A broken/incomplete seed, a slug typo, a
/// forgotten SeedXAsync call, a migration that silently drops rows, fails
/// here on every test run instead of only being noticed by a visitor
/// hitting a 404 or an empty nav menu.
/// </summary>
[Collection("Integration")]
public class SeedCompletenessTests
{
    private readonly AppDbContext _db;

    public SeedCompletenessTests(CustomWebApplicationFactory factory)
    {
        _db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
    }

    [Fact]
    public async Task All_16_product_pages_are_seeded()
    {
        var count = await _db.CmsPages.CountAsync(p => p.Slug.StartsWith("products/") && p.IsPublished);
        Assert.Equal(16, count);
    }

    [Fact]
    public async Task All_6_solution_pages_are_seeded()
    {
        var count = await _db.CmsPages.CountAsync(p => p.Slug.StartsWith("solutions/") && p.IsPublished);
        Assert.Equal(6, count);
    }

    [Fact]
    public async Task Topbar_and_nav_dock_both_carry_a_Solutions_and_a_Products_entry()
    {
        // Regression guard for the exact bug fixed earlier this session:
        // nav-dock (mobile pill) originally had no "Products" entry at all,
        // only topbar did, silently making Platform unreachable on mobile.
        foreach (var location in new[] { "topbar", "nav-dock" })
        {
            Assert.True(await _db.MenuItems.AnyAsync(m => m.Location == location && m.Label == "Solutions"),
                $"{location} is missing a Solutions entry.");
            Assert.True(await _db.MenuItems.AnyAsync(m => m.Location == location && m.Label == "Products"),
                $"{location} is missing a Products entry.");
        }
    }

    [Fact]
    public async Task Footer_solutions_and_product_link_columns_are_both_seeded()
    {
        Assert.Equal(6, await _db.MenuItems.CountAsync(m => m.Location == "footer-solutions" && m.IsPublished));
        Assert.Equal(16, await _db.MenuItems.CountAsync(m => m.Location == "footer-product" && m.IsPublished));
    }

    [Fact]
    public async Task Calculator_catalog_tables_are_all_non_empty()
    {
        // Every table CalculatorPricingService reads from; an empty one
        // would make the price calculator quietly return zero for everything
        // instead of a real quote.
        Assert.True(await _db.CalculatorPlanBaseRates.AnyAsync());
        Assert.True(await _db.CalculatorTaxConfigurations.AnyAsync());
        Assert.True(await _db.CalculatorCurrencyRates.AnyAsync());
        Assert.True(await _db.CalculatorBillingCycles.AnyAsync());
        Assert.True(await _db.CalculatorPricingModules.AnyAsync());
    }

    [Fact]
    public async Task All_three_admin_roles_exist()
    {
        var roleNames = await _db.Roles.Select(r => r.Name).ToListAsync();
        Assert.Contains("SuperAdmin", roleNames);
        Assert.Contains("ContentEditor", roleNames);
        Assert.Contains("SalesAgent", roleNames);
    }

    [Fact]
    public async Task Seeded_SuperAdmin_account_from_config_exists_and_is_in_role()
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == CustomWebApplicationFactory.TestSuperAdminEmail);
        Assert.NotNull(user);

        var isSuperAdmin = await (from ur in _db.UserRoles
                                   join r in _db.Roles on ur.RoleId equals r.Id
                                   where ur.UserId == user!.Id && r.Name == "SuperAdmin"
                                   select ur).AnyAsync();
        Assert.True(isSuperAdmin, "Seeded SuperAdmin user exists but isn't actually in the SuperAdmin role.");
    }
}
