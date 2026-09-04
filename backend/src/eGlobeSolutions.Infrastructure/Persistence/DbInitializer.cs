using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Domain.Entities.Calculator;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eGlobeSolutions.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds the fixed role set plus (in
/// non-production environments, or when explicitly configured) a first
/// SuperAdmin account so the admin panel is reachable on a fresh database.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();

        foreach (var roleName in new[]
                 {
                     ApplicationRole.Names.SuperAdmin,
                     ApplicationRole.Names.ContentEditor,
                     ApplicationRole.Names.SalesAgent
                 })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    Description = roleName switch
                    {
                        ApplicationRole.Names.SuperAdmin => "Full access to all admin modules, users and settings.",
                        ApplicationRole.Names.ContentEditor => "Manages site content and pricing.",
                        ApplicationRole.Names.SalesAgent => "Manages Contact Sales and Reseller enquiries.",
                        _ => null
                    }
                });
            }
        }

        // Seed one SuperAdmin from configuration so the panel is reachable
        // after `dotnet ef database update` on a fresh environment. Values
        // come from appsettings/user-secrets, never hardcoded credentials.
        var seedEmail = config["Seed:SuperAdminEmail"];
        var seedPassword = config["Seed:SuperAdminPassword"];

        if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword)
            && await userManager.FindByEmailAsync(seedEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = seedEmail,
                Email = seedEmail,
                EmailConfirmed = true,
                FullName = "System Administrator",
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, seedPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, ApplicationRole.Names.SuperAdmin);
                logger.LogInformation("Seeded initial SuperAdmin account for {Email}.", seedEmail);
            }
            else
            {
                logger.LogWarning("Failed to seed SuperAdmin account: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        await SeedContentAsync(db);
        await SeedCalculatorAsync(db);
        await SeedProductPagesAsync(db);
        await SeedSolutionsNavAsync(db);
        await SeedSolutionPagesAsync(db);
        await SeedProductAndSolutionFaqsAsync(db);
        await SeedAboutAndLegalPagesAsync(db);
    }

    /// <summary>
    /// Seeds the "Solutions" nav entry (topbar + nav-dock, alongside the
    /// existing "Products"/Platform entry) and the 6 footer-solutions links.
    /// The whole-table MenuItems seed in SeedContentAsync only runs once
    /// (guarded by "if no MenuItems exist at all"), so on an already-seeded
    /// database it would never add these later, hence a separate,
    /// per-item-idempotent seed here instead, same pattern as
    /// SeedProductPagesAsync's Slug.StartsWith("products/") check.
    /// </summary>
    private static async Task SeedSolutionsNavAsync(AppDbContext db)
    {
        foreach (var location in new[] { "topbar", "nav-dock" })
        {
            var exists = await db.MenuItems.AnyAsync(m => m.Location == location && m.Label == "Solutions");
            if (!exists)
            {
                // Insert right after "Home" (sort order 1) so the reading order is
                // Home -> Solutions (who we serve) -> Products/Platform (what we
                // offer) -> Pricing -> Resellers, bumping anything already at 1+
                // up by one to make room. Only runs the one time "Solutions" is
                // being added, so it never re-shifts on later app restarts.
                var toShift = await db.MenuItems.Where(m => m.Location == location && m.SortOrder >= 1).ToListAsync();
                foreach (var item in toShift) item.SortOrder += 1;

                db.MenuItems.Add(new MenuItem
                {
                    Location = location,
                    Label = "Solutions",
                    Url = "solutions/hotels-resorts.html",
                    SortOrder = 1
                });
            }
        }

        // nav-dock (the mobile pill) was never seeded with a "Products" entry
        // at all, unlike topbar, it originally only carried Home/Pricing/
        // Resellers, with the product list reachable only through the desktop
        // mega-menu. That leaves mobile with no way into the Platform/product
        // list. Add it here, positioned after Solutions, pointing at the
        // homepage ecosystem section same as topbar's Products entry, so
        // buildNavDockMega('Products', {renameTo:'Platform', ...}) in main.js
        // has a real link to find and convert (desktop: opens the dropdown,
        // mobile: taps straight through to the ecosystem section).
        if (!await db.MenuItems.AnyAsync(m => m.Location == "nav-dock" && m.Label == "Products"))
        {
            var toShift = await db.MenuItems.Where(m => m.Location == "nav-dock" && m.SortOrder >= 2).ToListAsync();
            foreach (var item in toShift) item.SortOrder += 1;

            db.MenuItems.Add(new MenuItem
            {
                Location = "nav-dock",
                Label = "Products",
                Url = "index.html#ecosystem",
                SortOrder = 2
            });
        }

        if (!await db.MenuItems.AnyAsync(m => m.Location == "footer-solutions"))
        {
            var footerSolutions = new[]
            {
                ("Hotels & Resorts", "/solutions/hotels-resorts.html"),
                ("Boutique Properties", "/solutions/boutique-properties.html"),
                ("Vacation Rentals", "/solutions/vacation-rentals.html"),
                ("Hostels", "/solutions/hostels.html"),
                ("Guest Houses", "/solutions/guest-houses.html"),
                ("Travel Agencies", "/solutions/travel-agencies.html"),
            };
            var i = 0;
            foreach (var (label, url) in footerSolutions)
            {
                db.MenuItems.Add(new MenuItem { Location = "footer-solutions", Label = label, Url = url, SortOrder = i++ });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the price calculator's DB-driven catalog (plan base rates, module
    /// availability/pricing, default tax) on a fresh database. Idempotent: only
    /// runs when the tables are empty, so it never overwrites admin edits.
    /// </summary>
    private static async Task SeedCalculatorAsync(AppDbContext db)
    {
        if (!await db.CalculatorPlanBaseRates.AnyAsync())
        {
            // Flat â‚¹1,200/month base subscription, the same regardless of how
            // many properties or rooms are entered, admin-editable per plan.
            db.CalculatorPlanBaseRates.AddRange(
                new PricingPlanBaseRate
                {
                    PlanType = CalculatorPlanType.PerRoom,
                    DisplayName = "Per Room",
                    UnitDescription = "Flat monthly base fee, the same no matter how many rooms you run. Includes PMS, Channel Manager, Housekeeping and OTA Listing & Management.",
                    MonthlyRatePerUnit = 1200m,
                    OneTimeSetupFee = 7500m
                },
                new PricingPlanBaseRate
                {
                    PlanType = CalculatorPlanType.PerProperty,
                    DisplayName = "Per Property",
                    UnitDescription = "Flat monthly base fee, the same no matter how many properties you run. Includes every core module except B2B Stay.",
                    MonthlyRatePerUnit = 1200m,
                    OneTimeSetupFee = 15000m
                },
                new PricingPlanBaseRate
                {
                    PlanType = CalculatorPlanType.Enterprise,
                    DisplayName = "Enterprise",
                    UnitDescription = "Flat monthly base fee to start; final portfolio pricing is customised. Includes every module plus Portfolio Dashboards and a Dedicated Account Manager.",
                    MonthlyRatePerUnit = 1200m,
                    OneTimeSetupFee = 50000m,
                    IsCustomQuote = true
                });
        }

        if (!await db.CalculatorTaxConfigurations.AnyAsync())
        {
            db.CalculatorTaxConfigurations.AddRange(
                new TaxConfiguration { Name = "GST (18%)", RatePercent = 18m, IsDefault = true, SortOrder = 0 },
                new TaxConfiguration { Name = "IGST (18%)", RatePercent = 18m, SortOrder = 1 },
                new TaxConfiguration { Name = "No Tax", RatePercent = 0m, SortOrder = 2 });
        }

        if (!await db.CalculatorCurrencyRates.AnyAsync())
        {
            db.CalculatorCurrencyRates.AddRange(
                new CurrencyRate { Code = "INR", Symbol = "â‚¹", Name = "Indian Rupee", RatePerInr = 1m, IsDefault = true, SortOrder = 0 },
                new CurrencyRate { Code = "USD", Symbol = "$", Name = "US Dollar", RatePerInr = 0.012m, SortOrder = 1 },
                new CurrencyRate { Code = "EUR", Symbol = "â‚¬", Name = "Euro", RatePerInr = 0.011m, SortOrder = 2 },
                new CurrencyRate { Code = "GBP", Symbol = "Â£", Name = "British Pound", RatePerInr = 0.0095m, SortOrder = 3 },
                new CurrencyRate { Code = "AED", Symbol = "AED", Name = "UAE Dirham", RatePerInr = 0.044m, SortOrder = 4 });
        }

        if (!await db.CalculatorBillingCycles.AnyAsync())
        {
            db.CalculatorBillingCycles.AddRange(
                new BillingCycle { Label = "Monthly", Months = 1, DiscountPercent = 0m, IsDefault = true, SortOrder = 0 },
                new BillingCycle { Label = "3 Months", Months = 3, DiscountPercent = 5m, SortOrder = 1 },
                new BillingCycle { Label = "6 Months", Months = 6, DiscountPercent = 10m, SortOrder = 2 },
                new BillingCycle { Label = "Annual", Months = 12, DiscountPercent = 15m, SortOrder = 3 });
        }

        if (!await db.CalculatorPricingModules.AnyAsync())
        {
            const ModuleAvailability inc = ModuleAvailability.Included;
            const ModuleAvailability add = ModuleAvailability.AddOn;
            const ModuleAvailability na = ModuleAvailability.NotAvailable;

            var modules = new[]
            {
                // ---- Core comparison-table modules ----
                new PricingModule
                {
                    Code = "pms", Name = "PMS", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerRoomMonthly,
                    PerRoomAvailability = inc, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 0
                },
                new PricingModule
                {
                    Code = "channel-manager", Name = "Channel Manager", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerRoomMonthly,
                    PerRoomAvailability = inc, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 1
                },
                new PricingModule
                {
                    Code = "housekeeping", Name = "Housekeeping", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerPropertyMonthly,
                    PerRoomAvailability = inc, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 2
                },
                new PricingModule
                {
                    Code = "pos-kot", Name = "POS & KOT", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerRoomMonthly, MonthlyRate = 19m,
                    PerRoomAvailability = add, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 3
                },
                new PricingModule
                {
                    Code = "finance-revenue", Name = "Finance & Revenue Management", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerRoomMonthly, MonthlyRate = 24m,
                    PerRoomAvailability = add, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 4
                },
                new PricingModule
                {
                    Code = "reviews-manager", Name = "Reviews Manager", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerPropertyMonthly, MonthlyRate = 299m,
                    PerRoomAvailability = add, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 5
                },
                new PricingModule
                {
                    Code = "b2b-stay", Name = "B2B Stay", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerPropertyMonthly, MonthlyRate = 2999m,
                    PerRoomAvailability = na, PerPropertyAvailability = add, EnterpriseAvailability = inc,
                    SortOrder = 6
                },
                new PricingModule
                {
                    Code = "ota-listing", Name = "OTA Listing & Management", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerRoomMonthly,
                    PerRoomAvailability = inc, PerPropertyAvailability = inc, EnterpriseAvailability = inc,
                    SortOrder = 7
                },
                new PricingModule
                {
                    Code = "portfolio-dashboards", Name = "Portfolio Dashboards", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.PerPropertyMonthly,
                    PerRoomAvailability = na, PerPropertyAvailability = na, EnterpriseAvailability = inc,
                    SortOrder = 8
                },
                new PricingModule
                {
                    Code = "dedicated-account-manager", Name = "Dedicated Account Manager", Category = ModuleCategory.CoreModule,
                    ChargeType = ModuleChargeType.FlatMonthly,
                    PerRoomAvailability = na, PerPropertyAvailability = na, EnterpriseAvailability = inc,
                    SortOrder = 9
                },

                // ---- Additional products (available as add-ons across all plans) ----
                new PricingModule
                {
                    Code = "ai-tools", Name = "eGlobe AI Tools", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.PerRoomMonthly, MonthlyRate = 15m, OneTimeSetupFee = 5000m,
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "AI Sales Agent, Smartdesk & Admin Agent, billed per room per month.",
                    SortOrder = 10
                },
                new PricingModule
                {
                    Code = "booking-engine", Name = "Booking Engine", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 5m, OneTimeSetupFee = 2500m,
                    VolumeInputLabel = "Estimated monthly booking value (â‚¹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "5% commission on confirmed direct bookings made through the engine.",
                    SortOrder = 11
                },
                new PricingModule
                {
                    Code = "google-hotel-ads", Name = "Google Hotel Ads", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 12m,
                    VolumeInputLabel = "Estimated monthly Google Hotel Ads booking value (â‚¹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "Management fee/commission on bookings driven through Google Hotel Ads, admin-configurable.",
                    SortOrder = 12
                },
                new PricingModule
                {
                    Code = "meta-search", Name = "Meta Search Engines", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 10m,
                    VolumeInputLabel = "Estimated monthly meta-search booking value (â‚¹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "Commission on bookings driven through Trivago, TripAdvisor and other meta-search channels.",
                    SortOrder = 13
                },
                new PricingModule
                {
                    Code = "website-builder", Name = "Website Builder", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.FlatMonthly, MonthlyRate = 999m, OneTimeSetupFee = 15000m,
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "One-time build fee plus flat monthly hosting & maintenance.",
                    SortOrder = 14
                },
                new PricingModule
                {
                    Code = "payment-gateway", Name = "Payment Gateway", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 2m, OneTimeSetupFee = 1000m,
                    VolumeInputLabel = "Estimated monthly online payment volume (â‚¹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "Transaction fee on payments processed through the gateway.",
                    SortOrder = 15
                },
                new PricingModule
                {
                    Code = "pms-apis", Name = "PMS APIs", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.FlatMonthly, MonthlyRate = 4999m, OneTimeSetupFee = 10000m,
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "Direct API access for custom integrations, flat monthly fee.",
                    SortOrder = 16
                }
            };

            db.CalculatorPricingModules.AddRange(modules);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the CMS content tables with exactly what's currently hardcoded in
    /// pricing.html/site settings, so a fresh database renders the site
    /// identically to the static original on first run. Idempotent: does
    /// nothing if any rows already exist, so it never overwrites admin edits.
    /// </summary>
    private static async Task SeedContentAsync(AppDbContext db)
    {
        if (!await db.SiteSettings.AnyAsync())
        {
            var settings = new Dictionary<string, (string Value, string Group)>
            {
                [SiteSettingKeys.SiteName] = ("eGlobe Solutions", "General"),
                [SiteSettingKeys.Phone] = ("+91 9818880480", "Contact"),
                [SiteSettingKeys.Email] = ("support@eglobe-solutions.com", "Contact"),
                [SiteSettingKeys.WhatsAppNumber] = ("919818880480", "Contact"),
                [SiteSettingKeys.CallUsNumbers] = ("+91 11 41717081/ +91 11 41717082/ +91 11 41717021", "Contact"),
                [SiteSettingKeys.BusinessHours] = ("Monday - Saturday, 9:30 AM - 6:30 PM (IST)", "Contact"),
                [SiteSettingKeys.FacebookUrl] = ("https://www.facebook.com/eglobesolution", "Social"),
                [SiteSettingKeys.YoutubeUrl] = ("https://www.youtube.com/@eglobesolutionspms", "Social"),
                [SiteSettingKeys.LinkedInUrl] = ("https://www.linkedin.com/company/eglobesolutions/", "Social"),
                [SiteSettingKeys.AppStoreUrl] = ("https://apps.apple.com/us/app/eglobe-pms/id1276536495", "Apps"),
                [SiteSettingKeys.GooglePlayUrl] = ("https://play.google.com/store/apps/details?id=in.directhotels.channelmanager&hl=en&pli=1", "Apps"),
                [SiteSettingKeys.FooterCopyright] = ("Â© 2026 eGlobe Solutions. All rights reserved.", "General"),
            };

            foreach (var (key, (value, group)) in settings)
            {
                db.SiteSettings.Add(new SiteSetting { Key = key, Value = value, Group = group });
            }
        }

        if (!await db.MenuItems.AnyAsync())
        {
            // URLs match the actual page routes (index.html / pricing.html /
            // reseller.html), not MVC-style paths, since the rest of the site's
            // markup (brand link, footer links, etc.) links the same way.
            var topNav = new[] { ("Home", "index.html"), ("Products", "index.html#ecosystem"), ("Pricing", "pricing.html"), ("Resellers", "reseller.html") };
            var i = 0;
            foreach (var (label, url) in topNav)
            {
                db.MenuItems.Add(new MenuItem { Location = "topbar", Label = label, Url = url, SortOrder = i++ });
            }

            i = 0;
            foreach (var (label, url) in topNav)
            {
                db.MenuItems.Add(new MenuItem { Location = "nav-dock", Label = label, Url = url, SortOrder = i++ });
            }

            i = 0;
            var footerProduct = new[]
            {
                ("PMS", "/products/pms.html"), ("Channel Manager", "/products/channel-manager.html"),
                ("eGlobe AI Tools", "/products/ai-tools.html"), ("Finance & Revenue", "/products/finance-revenue.html"),
                ("POS", "/products/pos.html"), ("Housekeeping", "/products/housekeeping.html"),
                ("KOT", "/products/kot.html"), ("Booking Engine", "/products/booking-engine.html"),
                ("OTA Listing & Management", "/products/ota-management.html"), ("Google Hotel Ads", "/products/google-hotel-ads.html"),
                ("Meta Search Engines", "/products/meta-search.html"), ("B2B Stay", "/products/b2b-stay.html"),
                ("Website Builder", "/products/website-builder.html"), ("Reviews Manager", "/products/reviews-manager.html"),
                ("Payment Gateway", "/products/payment-gateway.html"), ("PMS APIs", "/products/pms-apis.html"),
            };
            foreach (var (label, url) in footerProduct)
            {
                db.MenuItems.Add(new MenuItem { Location = "footer-product", Label = label, Url = url, SortOrder = i++ });
            }

            i = 0;
            var footerCompany = new[]
            {
                ("Home", "index.html"), ("About Us", "about.html"), ("Pricing", "pricing.html"), ("Resellers", "reseller.html"),
                ("Blog", "blog.html"), ("Contact", "contact.html"),
            };
            foreach (var (label, url) in footerCompany)
            {
                db.MenuItems.Add(new MenuItem { Location = "footer-company", Label = label, Url = url, SortOrder = i++ });
            }
        }

        if (!await db.SeoMetadata.AnyAsync())
        {
            db.SeoMetadata.AddRange(
                new SeoMetadata
                {
                    PageKey = "home",
                    Title = "eGlobe Solutions, Every Hotel System, Sourced by One Partner",
                    Description = "eGlobe Solutions sources and connects the PMS, channel manager, POS, housekeeping and revenue tools your hotel needs, one technology partner, the right system for every job.",
                    Keywords = "hotel management software, hotel PMS, hotel channel manager, hotel revenue management software, hospitality technology, hotel SaaS, hotel management system India",
                    CanonicalUrl = "https://www.eglobe-solutions.com/",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                },
                new SeoMetadata
                {
                    PageKey = "pricing",
                    Title = "Pricing, eGlobe Solutions | Hotel Management Software Pricing",
                    Description = "Simple, transparent pricing for the hotel technology stack eGlobe sources for you. Per-room, per-property and custom enterprise plans covering PMS, channel manager, POS and more.",
                    Keywords = "hotel management software pricing, hotel PMS pricing, hotel software cost, hotel technology pricing",
                    CanonicalUrl = "https://www.eglobe-solutions.com/pricing.html",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                },
                new SeoMetadata
                {
                    PageKey = "reseller",
                    Title = "Resellers, eGlobe Solutions | Hotel Technology Reseller Program",
                    Description = "Become an eGlobe reseller partner. Earn up to 15% recurring commission, white-label our sourced hotel technology stack under your brand, and offer hoteliers a complete hospitality solution.",
                    Keywords = "hotel technology reseller, hotel software partner program, hospitality SaaS partner, hotel PMS reseller, white label hotel software, hotel software commission",
                    CanonicalUrl = "https://www.eglobe-solutions.com/reseller.html",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                },
                new SeoMetadata
                {
                    PageKey = "contact",
                    Title = "Contact Sales, eGlobe Solutions | Hotel Management Software Demo",
                    Description = "Talk to eGlobe's sales team about the hotel technology stack we source and connect for you, PMS, channel manager, POS, housekeeping and revenue management.",
                    Keywords = "hotel management software demo, hotel PMS demo, contact hotel software sales, hospitality SaaS sales",
                    CanonicalUrl = "https://www.eglobe-solutions.com/contact.html",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                },
                new SeoMetadata
                {
                    PageKey = "calculator",
                    Title = "Price Calculator, eGlobe Solutions | Build a Hotel Software Quote",
                    Description = "Build an instant, accurate quotation across eGlobe's Per Room, Per Property and Enterprise pricing models, modules, add-ons, tax and commissions included.",
                    Keywords = "hotel management software price calculator, hotel PMS quote, hotel software pricing estimate",
                    CanonicalUrl = "https://www.eglobe-solutions.com/calculator.html",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                },
                new SeoMetadata
                {
                    PageKey = "blog",
                    Title = "Blog, eGlobe Solutions | Hotel Technology Guides & Updates",
                    Description = "Guides, product updates and industry news on hotel channel managers, PMS, OTA distribution and revenue management from the eGlobe Solutions team.",
                    Keywords = "hotel technology blog, hotel channel manager guide, hotel PMS blog, OTA distribution guide, hospitality technology news",
                    CanonicalUrl = "https://www.eglobe-solutions.com/blog.html",
                    OgImageUrl = "https://www.eglobe-solutions.com/img/eGlobe-Solutions-hotel-management-services.png"
                });
        }

        if (!await db.ContentBlocks.AnyAsync())
        {
            db.ContentBlocks.AddRange(
                // ---- Home ----
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "announcement", SortOrder = 0,
                    Body = "Introducing eGlobe AI Tools: your 24/7 Sales Agent",
                    CtaLabel = "Read more", CtaUrl = "blog.html"
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "hero", SortOrder = 1,
                    Kicker = "Your Hotel Technology Partner",
                    Title = "Every Hotel System.<br>Sourced by <span class=\"accent\">One Partner.</span>",
                    Subtitle = "eGlobe isn't a single piece of software. We're the team that sources, connects and manages your PMS, channel manager, POS, housekeeping and revenue tools, so every department works from the same data without you having to be the integrator.",
                    CtaLabel = "See What We Source", CtaUrl = "#ecosystem"
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "workspaces", SortOrder = 2,
                    Kicker = "One shift, every role",
                    Title = "Built around how your<br>hotel actually runs.",
                    Subtitle = "Every role sees a different screen, pulled from the same data. Pick a department to see what their day looks like on eGlobe."
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "ecosystem-intro", SortOrder = 3,
                    Kicker = "The full technology stack",
                    Title = "Every hotel system,<br>sourced for you."
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "ecosystem-network", SortOrder = 4,
                    Kicker = "Fully connected, not isolated",
                    Title = "Plugs into the tools<br>your hotel already uses.",
                    Subtitle = "None of these modules work in isolation. eGlobe connects to the OTAs, payment systems and websites your property already runs on."
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "mobile-app", SortOrder = 5,
                    Kicker = "Manage from anywhere",
                    Title = "The eGlobe<br>Mobile App.",
                    Subtitle = "Stay in control from anywhere, update inventory, view live bookings, and track performance without being at the front desk."
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "testimonials", SortOrder = 6,
                    Kicker = "Real hotels, real results",
                    Title = "What hoteliers say<br>after switching."
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "pricing-teaser", SortOrder = 7,
                    Kicker = "Pricing",
                    Body = "Priced <span class=\"accent\">per room</span>, <span class=\"accent\">per property</span>, or <span class=\"dim\">custom for larger portfolios</span>, one line item, not a stack of subscriptions.",
                    CtaLabel = "View Pricing", CtaUrl = "pricing.html"
                },
                new ContentBlock
                {
                    PageKey = "home", SectionKey = "final-cta", SortOrder = 8,
                    Title = "See eGlobe sourcing your hotel's stack.",
                    Body = "PMS, channel manager, POS and revenue tools, sourced, connected and set up around your rooms, your rates, your team. See it in a live 20-minute walkthrough.",
                    CtaLabel = "Book a Demo", CtaUrl = "contact.html"
                },

                // ---- Reseller ----
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "hero", SortOrder = 0,
                    Title = "Become an eGlobe <span class=\"accent\">Reseller</span> <span class=\"accent-alt\">Partner.</span>",
                    Subtitle = "Up to 15% recurring commission, white-label options and flexible plans, bring the stack we source to your clients.",
                    CtaLabel = "Talk to Partnerships", CtaUrl = "contact.html"
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "plans-intro", SortOrder = 1,
                    Kicker = "Partner Plans",
                    Title = "Three ways to partner with eGlobe."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "plan-referral", SortOrder = 2,
                    Kicker = "Referral Program", Title = "Refer &amp; Earn",
                    Subtitle = "Refer hotels to us, we handle the rest",
                    Body = "We manage onboarding, demo & training\nWe handle ongoing support\nYou earn on each successful referral",
                    CtaLabel = "Refer a Hotel", CtaUrl = "contact.html"
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "plan-reseller", SortOrder = 3,
                    Kicker = "Most Popular", Title = "Reseller Commission",
                    Subtitle = "Sell directly and earn recurring commission",
                    Body = "Earn up to 15% reseller commission\nConvert once, keep earning monthly\nFull sales & marketing material support",
                    CtaLabel = "Become a Reseller", CtaUrl = "contact.html"
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "plan-whitelabel", SortOrder = 4,
                    Kicker = "White Label", Title = "Your Own Brand",
                    Subtitle = "Offer eGlobe's platform under your identity",
                    Body = "Promote your own brand, not ours\nIncrease client loyalty & retention\nStrengthen long-term client relations",
                    CtaLabel = "Ask About White Label", CtaUrl = "contact.html"
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "statement", SortOrder = 5,
                    Kicker = "No exclusivity requirements",
                    Body = "You don't have to drop your other vendors or lock into one brand. <span class=\"accent\">Sell what makes sense</span> for the hotel in front of you."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "who-should-apply", SortOrder = 6,
                    Kicker = "Who Should Apply",
                    Title = "If you already work with hotels,<br>you already have a head start.",
                    Subtitle = "Revenue management companies, hotel consultants & GMs, IT solutions providers, web designers & marketers, PMS and booking engine providers, reputation management companies, photographers, hardware suppliers, and tour operators.",
                    Body = "Industries served: Hotels & Resorts, Boutique Properties, Vacation Rentals, Hostels, Guest Houses, Travel Agencies."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "benefit-1", SortOrder = 7,
                    Title = "Recurring Revenue &amp; High Commissions",
                    Body = "A generous commission structure that rewards performance, plus special partner pricing."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "benefit-2", SortOrder = 8,
                    Title = "Dedicated Account Manager &amp; Live Support",
                    Body = "A personal success manager plus 24/7 expert support for you and your customers."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "benefit-3", SortOrder = 9,
                    Title = "Sales, Marketing &amp; Onboarding Support",
                    Body = "Ready-to-use marketing materials and a quick, guided onboarding to get you selling fast."
                },
                new ContentBlock
                {
                    PageKey = "reseller", SectionKey = "final-cta", SortOrder = 10,
                    Title = "Ready to Grow Together?",
                    Body = "Talk to our partnerships team about the referral, reseller commission or white label model that fits your business.",
                    CtaLabel = "Talk to Partnerships", CtaUrl = "contact.html"
                },

                // ---- Contact ----
                new ContentBlock
                {
                    PageKey = "contact", SectionKey = "hero", SortOrder = 0,
                    Title = "<span class=\"accent\">Let's Source</span> the <span class=\"accent-alt\">Right Stack</span> for Your Hotel.",
                    Subtitle = "Tell us about your property and we'll walk you through the tools that fit, no pressure, no obligation."
                },
                new ContentBlock
                {
                    PageKey = "contact", SectionKey = "sidebar-benefits", SortOrder = 1,
                    Body = "Reply within 1 business day\nWalkthrough of modules that fit you\nClear pricing, no hidden costs"
                }
            );
        }

        if (!await db.BlogPosts.AnyAsync())
        {
            db.BlogPosts.Add(new BlogPost
            {
                Title = "Introducing eGlobe AI Tools: your Sales Agent, Smartdesk & Admin Agent.",
                Slug = "article-ai-tools",
                Category = "Product",
                Excerpt = "Three AI agents now sit inside every eGlobe property: one that converts guest enquiries into bookings 24/7, one that assists your front desk at check-in, and one that answers business questions like \"what was my occupancy last week?\" instantly.",
                Body = @"<p>Our mission is simple: help hoteliers spend less time switching between systems and more time running a great property. Today, we're launching eGlobe AI Tools, built directly into your existing PMS, Channel Manager and POS screens.</p>
<div class=""article-callout"">
  <p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p>
  <a href=""contact.html"" class=""btn btn-primary btn-sm"">Book a Demo</a>
</div>
<h2>What's inside eGlobe AI Tools</h2>
<p>Three agents, each built for a specific job your team already does every day:</p>
<ol>
  <li><strong>AI Sales Agent</strong>, responds instantly to WhatsApp, website and social enquiries, recommends rooms and pricing, and pushes confirmed bookings straight into your PMS. Works 24/7, without a human in the loop.</li>
  <li><strong>AI Smartdesk</strong>, assists your front desk staff during check-in and check-out, answers common guest questions, and retrieves reservation details from your PMS in seconds.</li>
  <li><strong>AI Admin Agent</strong>, answers business questions in plain language, ""What was my occupancy last week?"", ""How did Booking.com perform this month?"", pulling straight from your live PMS and Channel Manager data.</li>
</ol>
<h2>Built in, not bolted on</h2>
<p>Unlike a standalone chatbot you have to configure and maintain separately, eGlobe AI Tools reads and writes to the same PMS, Channel Manager and POS data your team already works from. There's no second dashboard to check and no data to reconcile.</p>
<h2>Rolling out now</h2>
<p>eGlobe AI Tools is available today across all eGlobe properties. If you're already on eGlobe, reach out to our support team to get it enabled. If you're evaluating eGlobe for the first time, our sales team can show you AI Tools alongside the rest of the platform in a single walkthrough.</p>
<p>Questions about AI Tools? <a href=""contact.html"">Talk to our team</a>, or explore the full <a href=""index.html#ecosystem"">product ecosystem</a>.</p>",
                AuthorName = "eGlobe Team",
                AuthorRole = "Product",
                ReadTimeMinutes = 5,
                PublishedAtUtc = new DateTime(2026, 2, 12, 0, 0, 0, DateTimeKind.Utc),
                IsFeatured = true,
                SortOrder = 0,
                MetaTitle = "Introducing eGlobe AI Tools, eGlobe Solutions Blog",
                MetaDescription = "Three AI agents now sit inside every eGlobe property: a Sales Agent, a Smartdesk and an Admin Agent, working across WhatsApp, front desk and your PMS."
            });

            var teasers = new (string Title, string Slug, string Category, string Excerpt, DateTime Published, int ReadMin, string Body)[]
            {
                ("Best Channel Manager for Small Hotels in India (2026)", "best-channel-manager-small-hotels-india-2026", "Guide",
                    "The best channel manager for a small hotel in India connects directly to MakeMyTrip, Goibibo and Booking.com (not through a third-party bridge), updates rates and availability within seconds of a booking, and is priced so a 10-20 room property isn't subsidising features built for a 200-room chain.",
                    new DateTime(2026, 1, 28, 0, 0, 0, DateTimeKind.Utc), 7,
                    "<p>Most independent hotels in India don't shop for a channel manager, they get pushed into buying one. It's usually after a bad week: a room sold twice on the same night, a rate that didn't update fast enough after a festival weekend, or a manager who spent Sunday morning logging into four different OTA extranets one at a time. By the time that happens, the research gets rushed and the decision gets made on whichever sales call answered the phone first.</p>" +
                    "<p>That's a bad way to pick software you'll depend on every single day. Here's what we've seen actually separate a channel manager that earns its subscription from one that becomes one more login nobody trusts.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>Start with the OTAs you actually depend on</h2>" +
                    "<p>For most Indian independent hotels, three channels do the heavy lifting: MakeMyTrip, Goibibo and Booking.com, usually in that order. A channel manager's real value isn't in the number of OTAs it lists on a marketing page, it's in how solid the connection is to the two or three you actually get bookings from. Ask specifically: is this a certified, direct API connection to MakeMyTrip and Goibibo, or does it route through a generic aggregator layer that adds a lag between what a guest books and what shows up in your inventory? That lag is where double bookings live.</p>" +
                    "<p>A useful test during any demo: ask the salesperson to make a live rate change and show you, in real time, how long it takes to reflect on the actual OTA extranet. If they can't show you that on the spot, assume it's slower than they're claiming.</p>" +
                    "<h2>Inventory sync speed matters more than the feature list</h2>" +
                    "<p>Every channel manager markets a long list of features. The one number that actually protects your revenue is sync latency, how many seconds pass between a guest booking a room on one channel and every other channel showing that room as sold. Slow sync is how a 20-room guesthouse ends up selling the same room twice on a Friday night during wedding season, and then eats the cost of walking one of those guests to a competitor at full retail rate.</p>" +
                    "<p>Two-way, real-time sync should be table stakes in 2026. If a vendor describes their sync as \"periodic\" or \"every 15 minutes,\" that's a polite way of saying you're still exposed to overbookings, just less often than doing it by hand.</p>" +
                    "<h2>Your front desk needs to run it, not just your revenue manager</h2>" +
                    "<p>Small hotels rarely have a dedicated revenue manager. The person closing out a room type on a Tuesday afternoon might be the same person who checked a guest in that morning. A channel manager built for small properties has to be usable by that person, without a training manual. Look for a dashboard where blocking a room or nudging a weekend rate takes two or three clicks, not a settings menu with twelve tabs.</p>" +
                    "<p>During a trial, hand the login to whoever actually runs your front desk day to day, not just to yourself. If they hesitate or need to call you to make a simple change, that's the real usability score, regardless of what the sales deck says.</p>" +
                    "<h2>A standalone tool creates a second source of truth</h2>" +
                    "<p>A channel manager that isn't connected to your PMS is really just a second inventory system you now have to keep honest against your first one. Every booking made on an OTA has to be manually re-entered, or synced through an integration that occasionally breaks and silently falls out of step. For a small team, that reconciliation work is exactly the kind of task that gets skipped when the front desk gets busy, and it's usually skipped right before it matters most.</p>" +
                    "<p>eGlobe's Channel Manager runs on the same platform as the PMS, so a reservation made on MakeMyTrip, Goibibo or Booking.com lands directly in the same front desk screen your staff already uses, no second login, no export/import, no end-of-day reconciliation.</p>" +
                    "<h2>Support that answers in your time zone</h2>" +
                    "<p>The night a rate sync breaks or an OTA connection drops is never a convenient one. For a small property without in-house IT staff, the deciding factor often isn't a feature at all, it's whether a real person answers a WhatsApp message or a phone call at 11pm on a Saturday, or whether you're filing a ticket into a queue and waiting for a response in a different time zone the next business day.</p>" +
                    "<p>Before signing, ask what support actually looks like at 9pm on a weekend. The answer to that question matters more than most of the comparison chart.</p>" +
                    "<h2>What this comes down to</h2>" +
                    "<p>For a small or independent Indian hotel, the right channel manager is the one with a genuinely fast, direct connection to the OTAs that drive your bookings, a dashboard your front desk can run without hand-holding, a PMS connection that removes the manual reconciliation, and support that actually picks up. Everything past that is a nice-to-have.</p>" +
                    "<p>Questions about choosing a channel manager? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/channel-manager.html\">Channel Manager</a> product page.</p>"),
                ("How to Connect MakeMyTrip & Goibibo via Channel Manager", "connect-makemytrip-goibibo-channel-manager", "Guide",
                    "Connecting to MakeMyTrip and Goibibo through a channel manager takes a signed OTA partner agreement, your property's GST and PAN details, and a one-time room mapping between your PMS and the OTA extranet. Done cleanly, most properties are live within 2-3 business days once the paperwork is in.",
                    new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc), 6,
                    "<p>MakeMyTrip and Goibibo sit under the same parent company (MakeMyTrip Group, which also owns a stake in Ibibo Group's Goibibo), but they're managed as two separate extranets with two separate listings. That trips people up: hoteliers assume connecting one automatically connects the other, then wonder a week later why Goibibo still shows a closed calendar. Here's the process we walk properties through, and the mistakes that add the most delay.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>What you need before you start</h2>" +
                    "<p>Both OTAs require a signed partner agreement before any channel manager can push rates or availability, this isn't something the channel manager can bypass, it's an OTA-side compliance requirement. On top of the agreement, keep these ready before you begin:</p>" +
                    "<ul><li>Your property's GST registration and PAN details</li><li>Bank account details for payout settlement</li><li>Photographs of the property already uploaded to the MakeMyTrip and Goibibo extranets (both, separately)</li><li>A finalised list of room types and the rate plans you intend to sell</li></ul>" +
                    "<p>Missing photographs is the single most common reason a listing gets rejected on first submission. If your property was only ever listed on one of the two OTAs before, budget an extra day or two to get the second extranet fully set up before mapping begins.</p>" +
                    "<h2>Connecting the channel</h2>" +
                    "<p>Once your accounts on both extranets are active, the actual technical connection is quick:</p>" +
                    "<ol><li><strong>Add MakeMyTrip and Goibibo as separate channels</strong> in your channel manager dashboard, even though they share a parent company, they need to be configured individually.</li>" +
                    "<li><strong>Map your room types</strong>, each room category in your PMS has to be matched, one to one, against the equivalent listing on each OTA extranet. A \"Deluxe Double\" in your PMS might be listed as \"Deluxe Room\" on one extranet and \"Superior Double\" on the other, get this matching exactly right or rates will silently apply to the wrong room type.</li>" +
                    "<li><strong>Push your rate plans</strong> from the PMS side once, then hand ongoing updates over to the channel manager's automatic two-way sync.</li>" +
                    "<li><strong>Run a test booking</strong> on each channel before opening inventory publicly. Most channel managers, eGlobe's included, support a dummy reservation specifically so you can confirm the full loop, from OTA booking to PMS reservation, before a real guest is involved.</li></ol>" +
                    "<h2>The mapping mistake that causes the most support tickets</h2>" +
                    "<p>By far the most common issue we see isn't a broken connection, it's a mismatched occupancy rule between the PMS and the OTA listing. If your PMS has a room configured for max occupancy of 3 but the OTA listing allows 4, you'll get rate discrepancies and, occasionally, a guest showing up with a booking your PMS thinks shouldn't exist. This is invisible until a guest is standing at your front desk with a confirmation email that doesn't match your system.</p>" +
                    "<p>eGlobe's Channel Manager checks for these mismatches automatically during mapping and flags the conflict before you go live, rather than letting it surface for the first time as a guest complaint.</p>" +
                    "<h2>How long it actually takes</h2>" +
                    "<p>For a property that's already listed and photographed on both extranets, with documents ready, the technical connection and mapping can be completed same-day. Add OTA-side review time (typically 1-2 business days per platform) and most hotels are fully live on both channels within a week of starting. The bottleneck is almost never the channel manager, it's whichever piece of paperwork or photography wasn't ready before mapping began.</p>" +
                    "<p>Questions about choosing a channel manager? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/channel-manager.html\">Channel Manager</a> product page.</p>"),
                ("Channel Manager vs Manual OTA Management: What Hotels Lose", "channel-manager-vs-manual-ota-management", "News",
                    "Hotels managing OTAs manually typically lose 1-2 hours of staff time a day to repetitive rate updates, and the delay between a booking and an inventory close-out is where most overbookings and rate parity penalties actually come from.",
                    new DateTime(2025, 12, 20, 0, 0, 0, DateTimeKind.Utc), 4,
                    "<p>A lot of independent hotels start out managing OTAs manually, and for a while it genuinely works. One property, two channels, a manager who checks the extranets each morning, that's manageable. The problem is that it doesn't fail gradually, it fails suddenly, usually on the exact weekend you can least afford it. Here's what manual OTA management actually costs once volume picks up, broken down by where the money actually leaks.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>The time cost is bigger than it looks</h2>" +
                    "<p>Updating a single rate change across four or five OTA extranets by hand, logging in, finding the right rate plan, applying the change, confirming it saved, takes roughly 20-30 minutes when done carefully. That doesn't sound like much until you count how often it happens: a festival weekend, a sudden dip in demand, a competitor undercutting you, a group booking that needs to close out a block of rooms. For a property adjusting pricing two or three times a week, that's several hours a month that could have gone to guests standing at the front desk instead of screens.</p>" +
                    "<h2>The overbooking is the expensive part</h2>" +
                    "<p>The real cost of manual management isn't the time, it's the gap. Between a guest booking a room on one OTA and someone manually closing that room out everywhere else, there's a window, sometimes minutes, sometimes hours if it happens overnight, where the same room can be sold twice. When that happens, the hotel eats one of three outcomes: a free upgrade to a higher room category, a refund plus an apology, or in the worst case, walking a guest to a competing property at full retail rate and covering the difference. Any one of those costs more than a month of channel manager fees.</p>" +
                    "<h2>Rate parity slips without anyone noticing</h2>" +
                    "<p>OTAs actively monitor listed rates across competing channels, and if your website or one OTA shows a lower rate than another, that's a parity violation. Manually updating five channels makes it easy for one to fall out of sync, a rate gets updated on MakeMyTrip but the change doesn't make it to Booking.com before someone gets pulled away. The penalty for repeated parity violations isn't a warning email, it's reduced visibility in that OTA's search ranking. Bookings quietly drop, and because there's no single obvious event that caused it, most hoteliers never trace it back to the mismatch.</p>" +
                    "<h2>It compounds as you grow</h2>" +
                    "<p>Manual management that's just barely sustainable for one property becomes genuinely unmanageable across two or three. Each additional room, channel or property multiplies the number of manual touchpoints where something can slip, and it's rarely the owner who notices first, it's a guest with a booking confirmation that doesn't match what the front desk sees.</p>" +
                    "<h2>What automation actually changes</h2>" +
                    "<p>A channel manager like eGlobe's closes out inventory the instant a booking is confirmed on any channel, typically within seconds, and pushes a single rate update to every connected OTA at once instead of five separate manual edits. For most properties, the time saved on updates alone covers the subscription cost, and that's before counting the overbookings and parity penalties that never happen in the first place.</p>" +
                    "<p>Questions about choosing a channel manager? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/channel-manager.html\">Channel Manager</a> product page.</p>"),
                ("What's New: Rate Parity Alerts & Mobile Inventory Steppers", "rate-parity-alerts-mobile-inventory-steppers", "Product",
                    "This release adds automatic rate parity alerts that flag mismatched pricing across connected OTAs the moment they happen, and mobile inventory steppers that let front desk staff open, close or adjust room availability from a phone in one tap, without waiting for a desktop.",
                    new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), 4,
                    "<p>Two updates shipped to every eGlobe Channel Manager dashboard this month. Neither is a headline feature, both came directly out of support tickets, and both are aimed at the same underlying problem: the gap between when something changes and when someone actually notices.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>Rate parity alerts</h2>" +
                    "<p>The dashboard now checks your live rates across every connected OTA continuously in the background, rather than as a periodic report you have to remember to pull. The moment a rate on one channel drifts out of sync with another, an alert appears directly on the property dashboard naming the exact channel and rate plan affected, instead of a general parity warning you have to go digging through extranets to trace.</p>" +
                    "<p>Previously, the only way most hoteliers found out about a parity issue was an automated warning email from the OTA itself, days after the mismatch started, and often after it had already dented search ranking. Catching it the moment it happens means it can be fixed before it costs any visibility at all.</p>" +
                    "<h2>Mobile inventory steppers</h2>" +
                    "<p>Front desk staff can now open the eGlobe mobile dashboard and adjust room availability directly with simple +/- steppers on each room type, no drilling into a settings menu, no waiting to get back to a desktop. This came up constantly in support conversations: a manager standing at the front desk during a check-in rush, needing to close out the last room of a category, with the only computer in the building occupied by someone else.</p>" +
                    "<p>The steppers are deliberately minimal, tap to close one unit, tap to reopen it, with the change reflected across every connected OTA within the same sync window as a desktop update. No new screens to learn.</p>" +
                    "<h2>Why these two, and why now</h2>" +
                    "<p>Both features came directly from the same source: the two issues raised most often by hoteliers actively using the platform. Rate mismatches were the single largest category of inbound support tickets over the past quarter, and \"I couldn't update availability without a computer\" was the most common piece of feedback from front desk teams specifically, as opposed to the owners or managers who typically drive purchasing decisions.</p>" +
                    "<p>Neither update required switching plans or paying more, both are live automatically on every existing eGlobe Channel Manager account.</p>" +
                    "<p>Questions about choosing a channel manager? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/channel-manager.html\">Channel Manager</a> product page.</p>"),
                ("5 Signs Your Hotel Has Outgrown Manual OTA Management", "signs-hotel-outgrown-manual-ota-management", "Guide",
                    "A hotel has typically outgrown manual OTA management once it's listed on three or more channels, has had at least one overbooking in the last quarter, or spends more than 30 minutes a day pushing rate changes across extranets by hand. Any one of these on its own is a warning sign; two together mean it's already costing more than a channel manager would.",
                    new DateTime(2025, 11, 18, 0, 0, 0, DateTimeKind.Utc), 5,
                    "<p>Manual OTA management isn't a bad choice on day one. For a single property on one or two channels with steady, predictable demand, logging into an extranet once a day is a perfectly reasonable way to run things. The problem is that nothing tells you when you've crossed the line where that stops being true, it just quietly gets more expensive in ways that don't show up as a single obvious cost. Here are the five signs that reliably show up right before hoteliers make the switch.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>1. You're live on three or more OTAs</h2>" +
                    "<p>Two channels is manageable with discipline. Three creates a different problem: it's not that any single update is hard, it's that every rate change or availability block now has to be repeated three separate times, in three separate places, by whoever happens to be at the desk. Past three channels, the odds of one getting missed during a busy shift rise sharply, and it's rarely the same channel twice, which makes the pattern hard to notice until it's already cost you a booking.</p>" +
                    "<h2>2. You've had an overbooking in the last quarter</h2>" +
                    "<p>A double booking is rarely a one-off bad luck event, it's usually the first visible symptom of a process that's already behind. If you've had to give a guest a free upgrade, a refund, or worse, walk them to another property because a room sold twice, that's not a fluke to shrug off, it's a sign the manual process is no longer keeping pace with how many bookings are actually coming through.</p>" +
                    "<h2>3. Rate changes take more than 30 minutes to push everywhere</h2>" +
                    "<p>Time this honestly, next time you adjust a weekend or festival rate: from the moment you decide on the new price to the moment it's live and confirmed on every OTA you're listed on. If that's regularly pushing past half an hour, that's half an hour your front desk or revenue manager isn't spending on a guest, a strategy call, or literally anything else. It adds up faster than it feels like in the moment.</p>" +
                    "<h2>4. You've been flagged for a rate parity violation</h2>" +
                    "<p>OTAs monitor listed rates across competing channels and penalize properties that show inconsistent pricing, usually by quietly suppressing that listing's ranking rather than sending an obvious warning. If you've ever gotten a parity notice, or noticed bookings on one channel dip for no clear reason, that's a strong signal that manual rate tracking has already fallen behind your actual channel count, whether or not anyone caught it in the moment.</p>" +
                    "<h2>5. You're adding a new property</h2>" +
                    "<p>This is the point almost every hotelier we talk to actually makes the switch. Manual management that's just barely sustainable for one property becomes a genuinely different job across two or three, not because the work doubles, but because the coordination between properties adds a layer that a spreadsheet or a memorized routine can't absorb. If a second property is on the horizon, it's worth setting up a channel manager before it opens, not after the first overbooking there.</p>" +
                    "<p>Questions about choosing a channel manager? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/channel-manager.html\">Channel Manager</a> product page.</p>"),
                ("Why Direct Bookings Are Rising Across Indian Hotels in 2026", "why-direct-bookings-rising-india-2026", "News",
                    "Direct bookings are growing across Indian hotels in 2026 because booking engines are now simple enough for a small property to run without a developer, Google Hotel Ads puts a hotel's own rate next to OTA rates inside Google Search itself, and more guests are actively checking a hotel's own website before booking through an OTA.",
                    new DateTime(2025, 11, 2, 0, 0, 0, DateTimeKind.Utc), 6,
                    "<p>For most of the last decade, OTAs owned both discovery and booking for Indian hotels almost completely. A guest searched an OTA app, compared listings inside that app, and booked inside that app, the hotel's own website barely entered the picture. That's shifting, not because guests suddenly dislike OTAs, but because the tools that make a direct booking easy to find and easy to complete have gotten dramatically better, and more affordable, for exactly the size of property that used to be locked out of them.</p>" +
                    "<div class=\"article-callout\"><p><strong>Want to see it live?</strong> Book a 20-minute walkthrough with our team.</p><a href=\"contact.html\" class=\"btn btn-primary btn-sm\">Book a Demo</a></div>" +
                    "<h2>Booking engines stopped requiring a developer</h2>" +
                    "<p>A few years ago, adding real-time booking to a hotel's website meant either a custom build or a clunky third-party widget that didn't talk to the PMS. Today, a modern booking engine embeds on a hotel's existing website in a day, pulling live rates and availability straight from the same PMS the front desk already runs on. That removes the exact barrier that used to push smaller, independent hotels toward being OTA-only, they simply didn't have the technical resources to run their own booking flow credibly.</p>" +
                    "<h2>Google Hotel Ads put hotel websites next to OTA listings</h2>" +
                    "<p>Google Hotel Ads places a hotel's own website rate directly alongside OTA rates inside Google Search and Google Maps results, in the same rate comparison box a guest is already looking at. That means a guest can see, and book, the hotel's direct rate without ever clicking through to an OTA listing first. For a small hotel, this is a genuinely different kind of visibility than existed five years ago, previously, showing up in that comparison at all required an OTA relationship. Now a hotel's own site competes in the same box.</p>" +
                    "<h2>Guests are actively looking for the direct rate</h2>" +
                    "<p>Guest behaviour has shifted too. More travelers, especially repeat guests and anyone booking a longer stay, now check a hotel's own website for a better rate, a free breakfast, or a late checkout before completing a booking through an OTA. Hotels that make that direct path easy, a fast-loading booking engine, clear rate parity or a small direct-booking perk, are capturing demand that would otherwise go straight to an OTA and its 15-20% commission. Hotels that don't have a usable booking engine are simply losing that guest back to the OTA app, even when the guest actively wanted to book direct.</p>" +
                    "<h2>These channels reinforce each other rather than compete</h2>" +
                    "<p>The properties seeing the strongest direct-booking growth in 2026 aren't abandoning OTAs, they're running a booking engine and Google Hotel Ads alongside their existing OTA channels, and treating them as complementary rather than a trade-off. OTAs still bring new-guest discovery a small hotel couldn't reach on its own; the booking engine and Google Hotel Ads capture the guests who are already looking specifically for that hotel, or who found it once through an OTA and are now ready to book direct on a repeat stay. Direct bookings grow as a share of total revenue without the property giving up OTA visibility at all.</p>" +
                    "<p>Questions about growing direct bookings? <a href=\"contact.html\">Talk to our team</a>, or see the full <a href=\"products/booking-engine.html\">Booking Engine</a> and <a href=\"products/google-hotel-ads.html\">Google Hotel Ads</a> product pages.</p>"),
            };

            var sort = 1;
            foreach (var (title, slug, category, excerpt, published, readMin, body) in teasers)
            {
                db.BlogPosts.Add(new BlogPost
                {
                    Title = title,
                    Slug = slug,
                    Category = category,
                    Excerpt = excerpt,
                    Body = body,
                    PublishedAtUtc = published,
                    ReadTimeMinutes = readMin,
                    SortOrder = sort++,
                    IsPublished = true
                });
            }
        }

        if (!await db.PricingPlans.AnyAsync())
        {
            var perRoom = new PricingPlan
            {
                Name = "Per-Room",
                BadgeText = "Per Room",
                UnitDescription = "Priced against total room count, ideal for independent hotels & small groups",
                IsFeatured = false,
                CtaLabel = "Get a Per-Room Quote",
                CtaUrl = "/contact",
                SortOrder = 0,
                Features = new List<PricingPlanFeature>
                {
                    new() { Text = "Core PMS & Channel Manager", SortOrder = 0 },
                    new() { Text = "Front Desk & Housekeeping", SortOrder = 1 },
                    new() { Text = "Standard email & chat support", SortOrder = 2 },
                    new() { Text = "Add modules as you grow", SortOrder = 3 },
                }
            };
            var perProperty = new PricingPlan
            {
                Name = "Per-Property",
                BadgeText = "Per Property",
                UnitDescription = "One flat rate covering the full platform for a single property",
                IsFeatured = true,
                CtaLabel = "Talk to Sales",
                CtaUrl = "/contact",
                SortOrder = 1,
                Features = new List<PricingPlanFeature>
                {
                    new() { Text = "All modules included", SortOrder = 0 },
                    new() { Text = "Revenue management & forecasting", SortOrder = 1 },
                    new() { Text = "Priority support & onboarding", SortOrder = 2 },
                    new() { Text = "Unlimited staff logins", SortOrder = 3 },
                }
            };
            var enterprise = new PricingPlan
            {
                Name = "Custom / Enterprise",
                BadgeText = "Enterprise",
                UnitDescription = "For multi-property groups, chains & management companies",
                IsFeatured = false,
                CtaLabel = "Request Enterprise Pricing",
                CtaUrl = "/contact",
                SortOrder = 2,
                Features = new List<PricingPlanFeature>
                {
                    new() { Text = "Portfolio-wide dashboards", SortOrder = 0 },
                    new() { Text = "Dedicated account manager", SortOrder = 1 },
                    new() { Text = "Custom integrations & API access", SortOrder = 2 },
                    new() { Text = "SLA-backed support", SortOrder = 3 },
                }
            };
            db.PricingPlans.AddRange(perRoom, perProperty, enterprise);
        }

        if (!await db.PricingComparisonRows.AnyAsync())
        {
            var rows = new (string Module, string Room, string Property, string Ent)[]
            {
                ("Property Management System (PMS)", "included", "included", "included"),
                ("Channel Manager", "included", "included", "included"),
                ("Housekeeping", "included", "included", "included"),
                ("POS & KOT", "addon", "included", "included"),
                ("Finance & Revenue Management", "addon", "included", "included"),
                ("Reviews Manager", "addon", "included", "included"),
                ("B2B Stay", "none", "addon", "included"),
                ("OTA Listing & Management", "included", "included", "included"),
                ("Portfolio Dashboards", "none", "none", "included"),
                ("Dedicated Account Manager", "none", "none", "included"),
            };
            var i = 0;
            foreach (var (module, room, property, ent) in rows)
            {
                db.PricingComparisonRows.Add(new PricingComparisonRow
                {
                    ModuleName = module,
                    PerRoomValue = room,
                    PerPropertyValue = property,
                    EnterpriseValue = ent,
                    SortOrder = i++
                });
            }
        }

        if (!await db.FaqItems.AnyAsync())
        {
            var faqs = new (string Q, string A)[]
            {
                ("How is eGlobe priced?", "eGlobe offers per-room, per-property and custom enterprise pricing models. Final pricing depends on the modules selected and property size, contact sales for a quote."),
                ("Can I choose individual modules instead of the full platform?", "Yes. Modules such as PMS, Channel Manager, POS and Housekeeping can be licensed individually or as a bundled platform."),
                ("Is there a setup or onboarding fee?", "Onboarding and setup terms vary by plan and property size. Our sales team will walk through this during your consultation."),
                ("Can I switch plans or scale up as my property grows?", "Yes. You can move from per-room to per-property or enterprise pricing at any time, including custom terms for multi-property groups and chains, talk to sales to discuss the transition."),
            };
            var i = 0;
            foreach (var (q, a) in faqs)
            {
                db.FaqItems.Add(new FaqItem { PageKey = "pricing", Question = q, Answer = a, SortOrder = i++ });
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// <summary>
    /// <summary>
    /// Seeds the 16 product CmsPages (Slug = "products/{name}") from the
    /// original static wwwroot/products/*.html markup, so BlogController.ProductPage
    /// has rows to serve once the static files are removed. Idempotent: does
    /// nothing if any "products/*" CmsPages already exist.
    /// </summary>
    private static async Task SeedProductPagesAsync(AppDbContext db)
    {
        if (await db.CmsPages.AnyAsync(p => p.Slug.StartsWith("products/"))) return;

        db.CmsPages.AddRange(
                new CmsPage
                {
                    Title = @"Property Management System",
                    Slug = "products/pms",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 0,
                    MetaTitle = @"Property Management System, eGlobe Solutions | Hotel Cloud PMS",
                    MetaDescription = @"A smart, secure and scalable cloud PMS, fully integrated with Channel Manager for real-time OTA sync. Trusted by 7,000+ properties worldwide, it...",
                    MetaKeywords = @"property management system, hotel cloud pms, eGlobe cloud pms, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Property Management System</span>
    </div>
    <span class=""pp-hero__badge"">Cloud PMS</span>
    <h1 data-reveal>Property Management System</h1>
    <p class=""lead"" data-reveal>A smart, secure and scalable cloud PMS, fully integrated with Channel Manager for real-time OTA sync.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Included in every eGlobe plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Cloud PMS is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Cloud PMS is the operational core of the hotel, reservations, guest profiles, folios and live room status, hosted online instead of on a server in your property, and connected out of the box to Channel Manager, Booking Engine, POS and payments.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Book or check a guest in</h3><p>Reservations from any channel land in one calendar, and check-in takes seconds.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Charges post to the folio</h3><p>Room, POS and service charges accumulate automatically against the guest's stay.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Room status updates live</h3><p>Housekeeping and front desk always see the same, current room status.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Reports, anytime, anywhere</h3><p>Pull occupancy, revenue and audit reports from any device, no server or IT visit needed.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Property Management System.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Auto room allotment on every booking</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Access anytime, from any device</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Multi-currency &amp; multi-language support</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">No servers or IT maintenance, hosted and backed up on the cloud</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Built-in Channel Manager, Booking Engine, POS and payments</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Independent hotels</h3></div>
        <div class=""pp-usecase""><h3>Multi-property groups</h3></div>
        <div class=""pp-usecase""><h3>New openings</h3></div>
        <div class=""pp-usecase""><h3>Boutique properties &amp; vacation rentals</h3></div>
        <div class=""pp-usecase""><h3>Hotels operating across cities or countries</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Property Management System on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Channel Manager",
                    Slug = "products/channel-manager",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 1,
                    MetaTitle = @"Channel Manager, eGlobe Solutions | Hotel Channel Manager",
                    MetaDescription = @"Keeps rates, availability and inventory in sync across 100+ Indian and global OTAs (Booking.com, Expedia, MakeMyTrip, Goibibo and more) from one...",
                    MetaKeywords = @"channel manager, hotel channel manager, eGlobe channel manager, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Channel Manager</span>
    </div>
    <span class=""pp-hero__badge"">Channel Manager</span>
    <h1 data-reveal>Channel Manager</h1>
    <p class=""lead"" data-reveal>Keeps rates, availability and inventory in sync across 100+ Indian and global OTAs (Booking.com, Expedia, MakeMyTrip, Goibibo and more) from one dashboard, in real time.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Included in every eGlobe plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Channel Manager is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Channel Manager is a cloud dashboard that connects your property to 100+ Indian and global OTAs, including Booking.com, Expedia, MakeMyTrip and Goibibo, so rates, availability and inventory stay identical everywhere without manual updates on each site.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Connect your OTA accounts</h3><p>We link your existing Booking.com, Expedia and other OTA logins to the dashboard.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Set rates &amp; inventory once</h3><p>Update pricing, room counts or close-outs a single time, from desktop or the mobile app.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Two-way sync pushes it live</h3><p>Every connected OTA reflects the change within seconds, and a new booking on any channel blocks that room everywhere else.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Track it all from one screen</h3><p>See bookings, occupancy and channel performance across every OTA without logging into each extranet.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Channel Manager.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Two-way real-time sync across 100+ OTAs</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Dynamic pricing based on demand</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Automatic rate-parity alerts</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Cuts daily OTA updates from 2 hours to under 20 minutes</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Update rates and inventory from the mobile app, anywhere</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>OTA-heavy properties</h3></div>
        <div class=""pp-usecase""><h3>Revenue managers</h3></div>
        <div class=""pp-usecase""><h3>Growing hotels</h3></div>
        <div class=""pp-usecase""><h3>Independent hotels &amp; guesthouses</h3></div>
        <div class=""pp-usecase""><h3>Multi-property hotel groups</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Channel Manager on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Housekeeping",
                    Slug = "products/housekeeping",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 2,
                    MetaTitle = @"Housekeeping, eGlobe Solutions | Hotel Housekeeping",
                    MetaDescription = @"Room status updates flow instantly between front desk and housekeeping staff on mobile, replacing radios and phone calls with a live board everyone can...",
                    MetaKeywords = @"housekeeping, hotel housekeeping, eGlobe housekeeping, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Housekeeping</span>
    </div>
    <span class=""pp-hero__badge"">Housekeeping</span>
    <h1 data-reveal>Housekeeping</h1>
    <p class=""lead"" data-reveal>Room status updates flow instantly between front desk and housekeeping staff on mobile, replacing radios and phone calls with a live board everyone can see.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Included in every eGlobe plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Housekeeping is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Housekeeping keeps room status, clean, dirty, inspected or out-of-order, updated live between front desk and housekeeping staff, so a room is bookable the moment it's actually ready instead of after a phone call or radio check.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Front desk assigns rooms</h3><p>Checkouts and stayovers appear on the housekeeping task list automatically.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Staff update status from the room</h3><p>Marked clean, dirty or needs-maintenance from a phone or tablet.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Front desk sees it instantly</h3><p>PMS room status updates in real time, no walking over or calling to check.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Rooms sell the moment they're ready</h3><p>A cleaned, inspected room becomes bookable immediately.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Housekeeping.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Live room-status board</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Task assignment on mobile</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Faster room turnover</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Housekeeping teams</h3></div>
        <div class=""pp-usecase""><h3>Front desk staff</h3></div>
        <div class=""pp-usecase""><h3>Multi-property groups</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Housekeeping on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Point of Sale (POS)",
                    Slug = "products/pos",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 3,
                    MetaTitle = @"Point of Sale (POS), eGlobe Solutions | Hotel POS",
                    MetaDescription = @"Manage restaurant orders, table assignments and billing from any device, fully integrated with your hotel PMS. Post charges from the restaurant, bar,...",
                    MetaKeywords = @"point of sale (pos), hotel pos, eGlobe pos, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-hero__grid"">
      <div class=""pp-hero__content"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Point of Sale (POS)</span>
    </div>
    <span class=""pp-hero__badge"">POS</span>
    <h1 data-reveal>Point of Sale (POS)</h1>
    <p class=""lead"" data-reveal>Manage restaurant orders, table assignments and billing from any device, fully integrated with your hotel PMS.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on Per Room Â· Included on Per Property &amp; Enterprise</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
      </div>
      <div class=""pp-hero__visual"">
<div class=""demo-widget"">
      <p class=""demo-hint"" style=""padding-left:0;"">Try it, tap items to build an order.</p>
      <div class=""demo-box demo-pos"">
        <div class=""demo-pos__menu"">
          <button class=""demo-pos__chip"" type=""button"" data-name=""Vegetable Cutlet"" data-price=""150"">Vegetable Cutlet Â· â‚¹150</button>
          <button class=""demo-pos__chip"" type=""button"" data-name=""French Fries"" data-price=""80"">French Fries Â· â‚¹80</button>
          <button class=""demo-pos__chip"" type=""button"" data-name=""Paneer Pakora"" data-price=""200"">Paneer Pakora Â· â‚¹200</button>
          <button class=""demo-pos__chip"" type=""button"" data-name=""Peanut Masala"" data-price=""90"">Peanut Masala Â· â‚¹90</button>
        </div>
        <div class=""demo-pos__order"" id=""pp-pos-order""><span class=""demo-pos__empty"">No items yet, tap a dish above.</span></div>
        <div class=""demo-pos__total""><span>Total Payable</span><b id=""pp-pos-total"">â‚¹0.00</b></div>
      </div>
    </div>
      </div>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Cloud POS is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Cloud POS is billing software for your restaurant, cafe or any outlet, built to post charges straight to the guest's room folio instead of needing a separate cash settlement at the table.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Take the order</h3><p>Staff enter the order at the table, counter or via takeaway/delivery screen.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Bill at the outlet</h3><p>An itemised, GST-compliant bill is generated instantly.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Charge to room, or settle direct</h3><p>In-house guests can post the bill straight to their folio; walk-ins pay on the spot.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Reconciled automatically</h3><p>Every charge shows up in PMS reporting, no manual entry or end-of-day reconciliation.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Point of Sale (POS).</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Bill to room, posts directly to guest folio</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Table management with real-time overview</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Multi-outlet support: restaurant, bar, spa, room service</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">GST-compliant billing with itemised, automated invoices</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Manage takeaway and delivery orders from one screen</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Hotels with F&amp;B outlets</h3></div>
        <div class=""pp-usecase""><h3>Resorts with multiple outlets</h3></div>
        <div class=""pp-usecase""><h3>Front-office &amp; F&amp;B teams</h3></div>
        <div class=""pp-usecase""><h3>Boutique properties needing simple, fast billing</h3></div>
        <div class=""pp-usecase""><h3>Hostels &amp; guest houses running a cafe or common area</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Point of Sale (POS) on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>
<script src=""../js/main.min.js""></script>

<script>
(function () {
  ""use strict"";
  var orderEl = document.getElementById(""pp-pos-order"");
  var totalEl = document.getElementById(""pp-pos-total"");
  if (!orderEl) return;
  var order = [];
  function render() {
    if (!order.length) {
      orderEl.innerHTML = '<span class=""demo-pos__empty"">No items yet, tap a dish above.</span>';
      totalEl.textContent = ""â‚¹0.00"";
      return;
    }
    var total = 0;
    orderEl.innerHTML = order.map(function (item, i) {
      total += item.price * item.qty;
      return '<div class=""demo-pos__row""><span>' + item.name + ' Ã— ' + item.qty + '</span><span>â‚¹' + (item.price * item.qty).toFixed(2) + '<span class=""rm"" data-i=""' + i + '"">âœ•</span></span></div>';
    }).join("""");
    totalEl.textContent = ""â‚¹"" + total.toFixed(2);
    orderEl.querySelectorAll("".rm"").forEach(function (rm) {
      rm.addEventListener(""click"", function () {
        order.splice(parseInt(rm.getAttribute(""data-i""), 10), 1);
        render();
      });
    });
  }
  document.querySelectorAll("".demo-pos__chip"").forEach(function (chip) {
    chip.addEventListener(""click"", function () {
      var name = chip.getAttribute(""data-name"");
      var price = parseFloat(chip.getAttribute(""data-price""));
      var existing = order.find(function (o) { return o.name === name; });
      if (existing) existing.qty += 1;
      else order.push({ name: name, price: price, qty: 1 });
      render();
    });
  });
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Kitchen Order Ticket (KOT)",
                    Slug = "products/kot",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 4,
                    MetaTitle = @"Kitchen Order Ticket (KOT), eGlobe Solutions | Hotel KOT",
                    MetaDescription = @"Orders taken at the POS route instantly to a live kitchen display, so the kitchen starts cooking the moment a guest orders instead of waiting on a...",
                    MetaKeywords = @"kitchen order ticket (kot), hotel kot, eGlobe kot, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Kitchen Order Ticket (KOT)</span>
    </div>
    <span class=""pp-hero__badge"">KOT</span>
    <h1 data-reveal>Kitchen Order Ticket (KOT)</h1>
    <p class=""lead"" data-reveal>Orders taken at the POS route instantly to a live kitchen display, so the kitchen starts cooking the moment a guest orders instead of waiting on a paper ticket to be walked over.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Bundled with POS Â· Add-on on Per Room, included on Per Property &amp; Enterprise</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Kitchen Order Ticket (KOT) is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>KOT routes orders from POS straight to a kitchen display or printer the instant they're placed, so the kitchen gets accurate tickets in real time instead of relying on handwritten or shouted orders.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Order is entered in POS</h3><p>At the table, counter, or for takeaway/delivery.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>It routes to the kitchen instantly</h3><p>The ticket appears on a kitchen display or prints automatically.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Kitchen marks progress</h3><p>Items are tracked as preparing or ready, visible back at the outlet.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Nothing gets lost in translation</h3><p>No re-entry, no misheard orders, everything traces back to the original ticket.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Kitchen Order Ticket (KOT).</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Instant order routing from POS</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Live kitchen display screen</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Fewer order mix-ups</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Hotel kitchens</h3></div>
        <div class=""pp-usecase""><h3>Busy F&amp;B service</h3></div>
        <div class=""pp-usecase""><h3>Room service teams</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Kitchen Order Ticket (KOT) on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Booking Engine",
                    Slug = "products/booking-engine",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 5,
                    MetaTitle = @"Booking Engine, eGlobe Solutions | Hotel Booking Engine",
                    MetaDescription = @"A single-page, 4-step direct booking flow fully integrated with your PMS, Channel Manager and Payment Gateway, so guests book straight from your...",
                    MetaKeywords = @"booking engine, hotel booking engine, eGlobe booking engine, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-hero__grid"">
      <div class=""pp-hero__content"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Booking Engine</span>
    </div>
    <span class=""pp-hero__badge"">Booking Engine</span>
    <h1 data-reveal>Booking Engine</h1>
    <p class=""lead"" data-reveal>A single-page, 4-step direct booking flow fully integrated with your PMS, Channel Manager and Payment Gateway, so guests book straight from your website instead of an OTA.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan Â· 5% commission on confirmed bookings</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
      </div>
      <div class=""pp-hero__visual"">
<div class=""demo-widget"">
      <p class=""demo-hint"" style=""padding-left:0;"">Try it, pick room quantities.</p>
      <div class=""demo-box demo-book"">
        <div class=""demo-book__room"" data-rate=""9503"" data-qty=""0"">
          <div><span class=""demo-book__room-name"">Standard Double or Twin Room</span><span class=""demo-book__room-rate"">â‚¹9,503 / night</span></div>
          <span class=""demo-inv__stepper""><button type=""button"" data-d=""-1"">âˆ’</button><span class=""demo-inv__qty-val"">0</span><button type=""button"" data-d=""1"">+</button></span>
        </div>
        <div class=""demo-book__room"" data-rate=""10309"" data-qty=""0"">
          <div><span class=""demo-book__room-name"">Deluxe Double Pool View</span><span class=""demo-book__room-rate"">â‚¹10,309 / night</span></div>
          <span class=""demo-inv__stepper""><button type=""button"" data-d=""-1"">âˆ’</button><span class=""demo-inv__qty-val"">0</span><button type=""button"" data-d=""1"">+</button></span>
        </div>
        <div class=""demo-book__total""><span>Grand Total</span><b id=""pp-book-total"">â‚¹0</b></div>
      </div>
    </div>
      </div>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Booking Engine is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Booking Engine is a reservation widget that lives on your own website, letting guests book directly with you, at your rate, with no OTA commission, instead of routing through a third-party channel.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Guest picks dates on your site</h3><p>A single-page, mobile-optimised widget shows live rates and a real-time price breakup.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Discounts &amp; packages apply automatically</h3><p>Early-bird, seasonal, coupon or bundled-package pricing is applied at checkout.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Guest pays securely</h3><p>Payment completes through your connected gateway, no separate reconciliation step.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Booking lands in your PMS</h3><p>The reservation, guest details and payment flow straight into PMS and Channel Manager.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Booking Engine.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Instant confirmation, no OTA commission</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Auto-optimised for mobile devices</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Multiple payment gateways supported</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Sell packages, add-ons and multi-night discounts</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Early bird, last-minute and coupon-based discount rules</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Properties growing direct bookings</h3></div>
        <div class=""pp-usecase""><h3>Marketing-led hotels</h3></div>
        <div class=""pp-usecase""><h3>Mobile-first guests</h3></div>
        <div class=""pp-usecase""><h3>Hotels wanting to reduce OTA dependency</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Booking Engine on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>
<script src=""../js/main.min.js""></script>

<script>
(function () {
  ""use strict"";
  var totalEl = document.getElementById(""pp-book-total"");
  if (!totalEl) return;
  var rooms = document.querySelectorAll("".demo-book__room"");
  function updateTotal() {
    var total = 0;
    rooms.forEach(function (room) {
      total += parseInt(room.getAttribute(""data-rate""), 10) * parseInt(room.getAttribute(""data-qty""), 10);
    });
    totalEl.textContent = ""â‚¹"" + total.toLocaleString(""en-IN"");
  }
  rooms.forEach(function (room) {
    var valEl = room.querySelector("".demo-inv__qty-val"");
    room.querySelectorAll("".demo-inv__stepper button"").forEach(function (btn) {
      btn.addEventListener(""click"", function () {
        var qty = parseInt(room.getAttribute(""data-qty""), 10) + parseInt(btn.getAttribute(""data-d""), 10);
        if (qty < 0) qty = 0;
        room.setAttribute(""data-qty"", qty);
        valEl.textContent = qty;
        updateTotal();
      });
    });
  });
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Finance & Revenue Management",
                    Slug = "products/finance-revenue",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 6,
                    MetaTitle = @"Finance & Revenue Management, eGlobe Solutions | Hotel Finance & Revenue",
                    MetaDescription = @"Dynamic pricing and demand forecasting that adjusts your rates automatically as occupancy and demand shift, backed by financial reporting that stays...",
                    MetaKeywords = @"finance & revenue management, hotel finance & revenue, eGlobe finance & revenue, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Finance &amp; Revenue Management</span>
    </div>
    <span class=""pp-hero__badge"">Finance &amp; Revenue</span>
    <h1 data-reveal>Finance &amp; Revenue Management</h1>
    <p class=""lead"" data-reveal>Dynamic pricing and demand forecasting that adjusts your rates automatically as occupancy and demand shift, backed by financial reporting that stays accurate without manual spreadsheet work.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on Per Room Â· Included on Per Property &amp; Enterprise</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Finance &amp; Revenue Management is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Finance &amp; Revenue Management turns your PMS data into pricing decisions and financial reports, dynamic rates, demand forecasts and audits, instead of leaving revenue calls to guesswork or a separate spreadsheet.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Data flows in from PMS</h3><p>Occupancy, bookings and rates are already live, no manual export.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Demand is forecast</h3><p>Pricing recommendations reflect upcoming demand, not just history.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Rates adjust dynamically</h3><p>Pushed out through Channel Manager once you approve or automate them.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Reports are ready when you need them</h3><p>Revenue, audit and financial reports generate without manual reconciliation.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Finance &amp; Revenue Management.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Demand-based pricing suggestions</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Live RevPAR &amp; ADR tracking</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Automated financial reports</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Revenue managers</h3></div>
        <div class=""pp-usecase""><h3>Owners tracking margins</h3></div>
        <div class=""pp-usecase""><h3>Seasonal properties</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Finance &amp; Revenue Management on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Reviews Manager",
                    Slug = "products/reviews-manager",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 7,
                    MetaTitle = @"Reviews Manager, eGlobe Solutions | Hotel Reviews Manager",
                    MetaDescription = @"Brings guest reviews from every platform (Google, Booking.com, TripAdvisor and more) into a single inbox, so nothing gets missed and nothing waits days...",
                    MetaKeywords = @"reviews manager, hotel reviews manager, eGlobe reviews manager, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Reviews Manager</span>
    </div>
    <span class=""pp-hero__badge"">Reviews Manager</span>
    <h1 data-reveal>Reviews Manager</h1>
    <p class=""lead"" data-reveal>Brings guest reviews from every platform (Google, Booking.com, TripAdvisor and more) into a single inbox, so nothing gets missed and nothing waits days for a reply.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on Per Room Â· Included on Per Property &amp; Enterprise</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Reviews Manager is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Reviews Manager pulls guest reviews from every OTA and review platform into a single inbox, so you can read, respond and track sentiment in one place instead of logging into each site separately.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Reviews are pulled in automatically</h3><p>New reviews from Google, Booking.com, TripAdvisor and more land in one inbox.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>You get notified</h3><p>Especially for low ratings, so nothing sits unanswered.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Reply from one place</h3><p>Respond directly without switching between platform logins.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Track trends over time</h3><p>See sentiment and recurring feedback across properties and periods.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Reviews Manager.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">All platforms, one inbox</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Faster response times</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Track rating trends over time</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Reputation-focused owners</h3></div>
        <div class=""pp-usecase""><h3>Multi-property groups</h3></div>
        <div class=""pp-usecase""><h3>Guest experience teams</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Reviews Manager on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"B2B Stay",
                    Slug = "products/b2b-stay",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 8,
                    MetaTitle = @"B2B Stay, eGlobe Solutions | Hotel B2B Stay",
                    MetaDescription = @"A dedicated network for your corporate clients and travel agents to book your inventory at special rates, through their own branded mobile app or...",
                    MetaKeywords = @"b2b stay, hotel b2b stay, eGlobe b2b stay, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">B2B Stay</span>
    </div>
    <span class=""pp-hero__badge"">B2B Stay</span>
    <h1 data-reveal>B2B Stay</h1>
    <p class=""lead"" data-reveal>A dedicated network for your corporate clients and travel agents to book your inventory at special rates, through their own branded mobile app or secure, role-based corporate logins, either online or via city ledger.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Not available on Per Room Â· Add-on on Per Property Â· Included on Enterprise</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What B2B Stay is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>B2B Stay is a private booking portal for your corporate clients and travel agent partners, giving each one a dedicated login and negotiated rate instead of routing repeat business through public OTA listings.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>You set custom rates per partner</h3><p>Negotiated pricing and commission are configured for each corporate account or agent.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Partners log into their own portal</h3><p>A secure, role-based login, or the dedicated branded mobile app.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>They book at their rate</h3><p>Availability and pricing reflect the terms set for that specific partner.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>It flows into your PMS</h3><p>Bookings and consolidated invoicing land directly in your existing systems.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use B2B Stay.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Dedicated branded mobile app for partners</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Secure, role-based corporate logins</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Custom rates &amp; commission per partner</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">A private booking network, not exposed on public OTAs</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Consolidated invoicing for repeat corporate stays</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Hotels with corporate accounts</h3></div>
        <div class=""pp-usecase""><h3>Travel agent partners</h3></div>
        <div class=""pp-usecase""><h3>Multi-property groups</h3></div>
        <div class=""pp-usecase""><h3>Hotels building repeat B2B relationships</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See B2B Stay on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"OTA Listing & Management",
                    Slug = "products/ota-management",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 9,
                    MetaTitle = @"OTA Listing & Management, eGlobe Solutions | Hotel OTA Listing",
                    MetaDescription = @"A fully managed OTA listing service. We set up and optimise your profiles on Booking.com, Expedia, MakeMyTrip and 100+ more, then connect them to your...",
                    MetaKeywords = @"ota listing & management, hotel ota listing, eGlobe ota listing, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">OTA Listing &amp; Management</span>
    </div>
    <span class=""pp-hero__badge"">OTA Listing</span>
    <h1 data-reveal>OTA Listing &amp; Management</h1>
    <p class=""lead"" data-reveal>A fully managed OTA listing service. We set up and optimise your profiles on Booking.com, Expedia, MakeMyTrip and 100+ more, then connect them to your Channel Manager for real-time inventory and rate sync.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Included in every eGlobe plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What OTA Listing &amp; Management is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>OTA Listing &amp; Management is a done-for-you service that sets up, lists and keeps your property consistent across 100+ OTAs, so every listing shows the same accurate content instead of you managing each extranet separately.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>We set up your accounts</h3><p>New OTA accounts are created and existing ones are audited and cleaned up.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>We optimise your listings</h3><p>Photos, descriptions and amenities are professionally written and standardised.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Rates &amp; inventory sync automatically</h3><p>Through Channel Manager, every OTA reflects the same live availability.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>We monitor performance</h3><p>Ongoing checks catch outdated content or listing issues before they cost you bookings.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use OTA Listing &amp; Management.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">End-to-end account setup on 100+ OTAs</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Real-time inventory &amp; rate sync</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Professional listing &amp; photo optimisation</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">One dashboard to manage rates across every OTA</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Avoids overbookings from unsynced availability</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>New properties</h3></div>
        <div class=""pp-usecase""><h3>Properties with outdated listings</h3></div>
        <div class=""pp-usecase""><h3>Revenue teams</h3></div>
        <div class=""pp-usecase""><h3>Hotels listed on multiple OTAs</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See OTA Listing &amp; Management on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Google Hotel Ads",
                    Slug = "products/google-hotel-ads",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 10,
                    MetaTitle = @"Google Hotel Ads, eGlobe Solutions | Hotel Google Hotel Ads",
                    MetaDescription = @"Display your live rates and availability on Google Search, Google Maps and your Google Business listing, and pay only for confirmed bookings. No setup...",
                    MetaKeywords = @"google hotel ads, hotel google hotel ads, eGlobe google hotel ads, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Google Hotel Ads</span>
    </div>
    <span class=""pp-hero__badge"">Google Hotel Ads</span>
    <h1 data-reveal>Google Hotel Ads</h1>
    <p class=""lead"" data-reveal>Display your live rates and availability on Google Search, Google Maps and your Google Business listing, and pay only for confirmed bookings.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan Â· commission-based, rate confirmed by sales</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Google Hotel Ads is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Google Hotel Ads shows your live rates on Google Search and Maps right alongside OTA pricing, so travellers researching your property can book direct at the exact moment they're deciding, and you pay only for confirmed bookings.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>We connect your rates &amp; availability</h3><p>Fed live from your Booking Engine, no separate data entry.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Your property appears on Search &amp; Maps</h3><p>Right where travellers already look when researching a stay.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Your direct rate sits next to OTA pricing</h3><p>Giving guests a reason to book with you instead of a third party.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>You pay only for confirmed bookings</h3><p>No charge for impressions, clicks that don't convert cost nothing.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Google Hotel Ads.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Real-time rate updates on Search &amp; Maps</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Pay Per Conversion, no cost for clicks alone</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">No setup fees or rental costs</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Direct-booking focused hotels</h3></div>
        <div class=""pp-usecase""><h3>Budget-conscious owners</h3></div>
        <div class=""pp-usecase""><h3>Data-driven revenue teams</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Google Hotel Ads on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Meta Search Engines",
                    Slug = "products/meta-search",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 11,
                    MetaTitle = @"Meta Search Engines, eGlobe Solutions | Hotel Meta Search",
                    MetaDescription = @"eGlobe is an official Google Hotel Ads partner in India, connecting your booking engine to the worldâ€™s leading hotel meta search platforms, including...",
                    MetaKeywords = @"meta search engines, hotel meta search, eGlobe meta search, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Meta Search Engines</span>
    </div>
    <span class=""pp-hero__badge"">Meta Search</span>
    <h1 data-reveal>Meta Search Engines</h1>
    <p class=""lead"" data-reveal>eGlobe is an official Google Hotel Ads partner in India, connecting your booking engine to the worldâ€™s leading hotel meta search platforms, including direct Google Maps integration, so your live rates surface right where travellers are searching, alongside OTA rates, at the moment of decision.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Meta Search Engines are, and how they work.</h2>
    <p class=""pp-what-is"" data-reveal>Meta Search puts your live rates on Google Hotel Ads, Google Maps and other meta search engines, alongside OTA pricing, so travellers can find and book direct with you, and you pay only for confirmed bookings, not clicks.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>We connect your live rates</h3><p>Your Booking Engine's real-time pricing and availability feed into Google and meta search partners.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Rates appear on Search &amp; Maps</h3><p>Your property shows up right where travellers are already searching.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Guests compare and click through</h3><p>Your direct rate sits next to OTA pricing, visible at the moment of decision.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>You pay only for confirmed bookings</h3><p>No cost for impressions or browsing, only completed direct reservations.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Meta Search Engines.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Official Google Hotel Ads partner in India</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Google Maps integration for real-time visibility</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Rates shown alongside OTA pricing</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Drives direct bookings straight to your engine</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">No per-booking commission, unlike OTAs</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Properties competing with OTA rates</h3></div>
        <div class=""pp-usecase""><h3>Mobile-heavy markets</h3></div>
        <div class=""pp-usecase""><h3>Independent hotels</h3></div>
        <div class=""pp-usecase""><h3>Hotels wanting commission-free bookings</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Meta Search Engines on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Website Builder",
                    Slug = "products/website-builder",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 12,
                    MetaTitle = @"Website Builder, eGlobe Solutions | Hotel Website Builder",
                    MetaDescription = @"We design and build your hotel website for you, one-page or multi-page, mobile-ready and SEO-optimised. Use your own domain or have us register one...",
                    MetaKeywords = @"website builder, hotel website builder, eGlobe website builder, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Website Builder</span>
    </div>
    <span class=""pp-hero__badge"">Website Builder</span>
    <h1 data-reveal>Website Builder</h1>
    <p class=""lead"" data-reveal>We design and build your hotel's website for you, as a single page or a full multi-page site, whichever fits your property. Use a domain you already own, or have us register one for you for an additional charge.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Website Builder is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Website Builder is a hotel website designed, built and hosted for you, already wired to your Booking Engine and live rates, instead of a template you're left to build and connect yourself.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Tell us about your property</h3><p>Share your brand, photos and pages you need, one-page or multi-page.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>We design &amp; build it</h3><p>An SEO-optimised, mobile-friendly site goes live on your domain, or ours.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Booking Engine is built in</h3><p>The site connects directly to live rates and availability, no plugin required.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>We handle hosting &amp; upkeep</h3><p>Uptime, updates and hosting are managed for you going forward.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Website Builder.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Built for you, one-page or multi-page</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">SEO-optimised, mobile-friendly design</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Your domain, or ours for an added charge</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Booking Engine built directly into your site, no plugins</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">We handle hosting, updates and uptime for you</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Hotels without a website</h3></div>
        <div class=""pp-usecase""><h3>Properties with an outdated site</h3></div>
        <div class=""pp-usecase""><h3>Owners who'd rather not build it themselves</h3></div>
        <div class=""pp-usecase""><h3>Hotels wanting direct bookings from their own site</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Website Builder on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"Payment Gateway",
                    Slug = "products/payment-gateway",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 13,
                    MetaTitle = @"Payment Gateway, eGlobe Solutions | Hotel Payment Gateway",
                    MetaDescription = @"Secure card and digital payment processing that posts straight to the guest folio the moment a transaction clears, with no manual entry required by...",
                    MetaKeywords = @"payment gateway, hotel payment gateway, eGlobe payment gateway, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">Payment Gateway</span>
    </div>
    <span class=""pp-hero__badge"">Payment Gateway</span>
    <h1 data-reveal>Payment Gateway</h1>
    <p class=""lead"" data-reveal>Secure card and digital payment processing that posts straight to the guest folio the moment a transaction clears, with no manual entry required by front desk.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan Â· transaction fee applies</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What Payment Gateway is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>Payment Gateway is secure payment processing built for hotels, accepting cards, UPI, wallets and netbanking and posting the amount straight to the guest folio, instead of leaving front desk or finance to reconcile it manually.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Guest pays</h3><p>At Booking Engine checkout, front desk, or on a payment link, using their preferred method.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Transaction is processed securely</h3><p>Card, UPI, wallet and netbanking payments are all handled through one integration.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>It posts to the folio</h3><p>The amount lands directly against the guest's stay, no manual entry.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>Reconciliation happens automatically</h3><p>Finance teams see matched transactions without chasing down receipts.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use Payment Gateway.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Secure card &amp; digital processing</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Direct-to-folio posting</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Multiple payment methods supported</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">UPI, wallets, netbanking and cards, all in one link</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Automatic reconciliation, no manual matching</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Front desk teams</h3></div>
        <div class=""pp-usecase""><h3>Booking Engine users</h3></div>
        <div class=""pp-usecase""><h3>Finance teams</h3></div>
        <div class=""pp-usecase""><h3>Hotels wanting fewer failed transactions</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See Payment Gateway on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"PMS APIs",
                    Slug = "products/pms-apis",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 14,
                    MetaTitle = @"PMS APIs, eGlobe Solutions | Hotel PMS APIs",
                    MetaDescription = @"Bi-directional, OAuth 2.0-secured endpoints for revenue-management tools, analytics platforms and PMS providers who need direct programmatic access....",
                    MetaKeywords = @"pms apis, hotel pms apis, eGlobe pms apis, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">PMS APIs</span>
    </div>
    <span class=""pp-hero__badge"">PMS APIs</span>
    <h1 data-reveal>PMS APIs</h1>
    <p class=""lead"" data-reveal>Bi-directional, OAuth 2.0-secured endpoints for revenue-management tools, analytics platforms and PMS providers who need direct programmatic access.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What PMS APIs are, and how they work.</h2>
    <p class=""pp-what-is"" data-reveal>PMS APIs are documented, secure REST endpoints that let revenue management tools, analytics platforms and technology partners pull rates and inventory or push bookings directly into eGlobe PMS, instead of relying on manual exports.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>Get API credentials</h3><p>We issue OAuth 2.0 secured access for your integration.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>Build against the docs</h3><p>Well-documented endpoints and a sandbox make integration straightforward.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Pull or push data in real time</h3><p>Rates, inventory and bookings sync bi-directionally as they change.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>We support the integration</h3><p>A dedicated team is available while you build and after you go live.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use PMS APIs.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Real-time, bi-directional data sync</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">OAuth 2.0 secured endpoints</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">99.9% uptime guarantee</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Well-documented REST APIs, fast to integrate</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Dedicated integration support team</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Revenue management tools</h3></div>
        <div class=""pp-usecase""><h3>Analytics &amp; BI platforms</h3></div>
        <div class=""pp-usecase""><h3>Technology partners</h3></div>
        <div class=""pp-usecase""><h3>Hotel groups building custom tools</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See PMS APIs on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = @"eGlobe AI Tools",
                    Slug = "products/ai-tools",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 15,
                    MetaTitle = @"eGlobe AI Tools, eGlobe Solutions | Hotel AI Tools",
                    MetaDescription = @"Three AI agents built into your daily workflow: a Sales Agent that answers WhatsApp and website enquiries and pushes bookings to your PMS 24/7, a...",
                    MetaKeywords = @"eglobe ai tools, hotel ai tools, eGlobe ai tools, hotel management software",
                    Body = @"<!-- ===================== HERO / INTRO ===================== -->
<header class=""pp-hero panel-white"">
  <div class=""container"">
    <div class=""pp-hero__grid"">
      <div class=""pp-hero__content"">
    <div class=""pp-breadcrumb"">
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../index.html#ecosystem"">Products</a><span>/</span>
      <span class=""current"">eGlobe AI Tools</span>
    </div>
    <span class=""pp-hero__badge"">AI Tools</span>
    <h1 data-reveal>eGlobe AI Tools</h1>
    <p class=""lead"" data-reveal>Three AI agents built into your daily workflow: a Sales Agent, a Smartdesk and an Admin Agent, working across WhatsApp, front desk and your PMS.</p>
    <div class=""pp-availability""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><polyline points=""16 8 10 16 8 12""/></svg>Add-on on every plan</div>
    <div class=""pp-hero__actions"">
      <a href=""../contact.html"" class=""btn btn-primary"">Request a Demo</a>
      <a href=""../contact.html"" class=""btn btn-ghost"">Talk to Sales</a>
    </div>
      </div>
      <div class=""pp-hero__visual"">
        <img class=""pp-hero__mascot-img"" src=""../assets/img/ai-img1.png"" alt=""eGlobe AI mascot"">
      </div>
    </div>
  </div>
</header>

<!-- ===================== WHAT IS / HOW IT WORKS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">How It Works</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">What eGlobe AI Tools is, and how it works.</h2>
    <p class=""pp-what-is"" data-reveal>eGlobe AI Tools is a set of AI agents, a Sales Agent, a Smartdesk and an Admin Agent, that respond to guests and surface business insights automatically, instead of every enquiry waiting on a staff member to be free.</p>
    <div class=""pp-steps"" data-reveal-group>
      <div class=""pp-step""><span class=""pp-step__num"">1</span><div><h3>A guest reaches out</h3><p>Via WhatsApp, website chat or a call, at any hour.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">2</span><div><h3>The AI Sales Agent responds</h3><p>It answers, quotes rates and can convert the enquiry into a booking on the spot.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">3</span><div><h3>Smartdesk assists your team</h3><p>Complex or in-stay requests are routed to front desk with the context already gathered.</p></div></div>
      <div class=""pp-step""><span class=""pp-step__num"">4</span><div><h3>The Admin Agent reports back</h3><p>Owners and GMs get instant, plain-language insights on performance and trends.</p></div></div>
    </div>
  </div>
</section>

<!-- ===================== BENEFITS ===================== -->
<section class=""pp-section section-border-top panel-white"">
  <div class=""container"">
    <span class=""section-kicker"">Key Benefits</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Why hotels use eGlobe AI Tools.</h2>
    <div class=""pp-benefits"" style=""margin-top:24px;"" data-reveal-group>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">AI Sales Agent, converts enquiries into bookings</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">AI Smartdesk, assists front desk &amp; guest queries</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">AI Admin Agent, instant business insights</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Responds to guest enquiries 24/7, no missed leads</p></div>
        </div>
        <div class=""why-item"">
          <div class=""why-item__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.4""><polyline points=""20 6 9 17 4 12""/></svg></div>
          <div><p style=""font-size:14.5px;font-weight:600;color:var(--ink);"">Works across WhatsApp, website chat and calls</p></div>
        </div>
    </div>
  </div>
</section>

<!-- ===================== USE CASES ===================== -->
<section class=""pp-section section-border-top panel-white pp-pixelgrid"">
  <div class=""container"">
    <span class=""section-kicker"">Who It's For</span>
    <h2 data-reveal style=""margin-top:8px;font-size:24px;"">Built for these teams.</h2>
    <div class=""pp-usecases"" data-reveal-group>
        <div class=""pp-usecase""><h3>Lean front-desk teams</h3></div>
        <div class=""pp-usecase""><h3>Owners &amp; GMs</h3></div>
        <div class=""pp-usecase""><h3>Properties chasing direct bookings</h3></div>
        <div class=""pp-usecase""><h3>Hotels short-staffed after hours</h3></div>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See eGlobe AI Tools on Your Property.</h2>
        <p>Talk to our team for a live walkthrough tailored to your rooms, modules and portfolio size.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                }
        );

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// <summary>
    /// Seeds the 6 Solutions CmsPages (Slug = "solutions/{name}"), property-type
    /// audience segments distinct from the 16 Platform/product pages. Idempotent:
    /// does nothing if any "solutions/*" CmsPages already exist.
    /// </summary>
    private static async Task SeedSolutionPagesAsync(AppDbContext db)
    {
        if (await db.CmsPages.AnyAsync(p => p.Slug.StartsWith("solutions/"))) return;

        db.CmsPages.AddRange(
                new CmsPage
                {
                    Title = @"Hotels & Resorts",
                    Slug = "solutions/hotels-resorts",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 0,
                    MetaTitle = @"Hotel & Resort Management Software, eGlobe Solutions | Full Technology Suite",
                    MetaDescription = @"A complete technology suite for hotels and resorts: PMS, Channel Manager and Booking Engine built to grow direct bookings, sync 250+ OTAs in real time, and lift RevPAR.",
                    MetaKeywords = @"hotel management software, resort management software, hotel PMS, hotel channel manager, hotel revenue management",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Hotels & Resorts</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">01</span>
          <span class=""sol-hero__index-label"">For Hotels &amp; Resorts</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>Empowering Hotels &amp; Resorts</h1>
        <p class=""lead"" data-reveal>A comprehensive technology suite designed to increase direct bookings, streamline operations, and maximise global distribution across 250+ OTAs.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Channel Sync</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-orbit"" id=""orbit-widget"">
              <div class=""sol-orbit__ring sol-orbit__ring--1""></div>
              <div class=""sol-orbit__ring sol-orbit__ring--2""></div>
              <div class=""sol-orbit__hub"" id=""orbit-hub"" role=""button"" tabindex=""0"" aria-label=""Sync all channels"">eGlobe<br>Hub</div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Click the hub to sync every connected channel.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""7000"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Hotels Worldwide</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""250"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Integrations</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""30"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">Avg. Increase in Revenue</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""24"" data-suffix=""/7"">0</span></div>
        <div class=""lbl"">Expert Support</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Why Hotels Trust eGlobe</span>
      <h2 data-reveal>Tailored technology for every property size.</h2>
      <p class=""lead"" style=""margin-top:12px;"">Whether you run a boutique resort or a multi-property chain, eGlobe's modular platform scales with your business.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 3v18h18M7 15l4-4 3 3 5-6""/></svg></div>
        <h3>Revenue Growth</h3>
        <p>Increase RevPAR by optimising your pricing strategy and expanding reach across 250+ OTAs.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""18"" rx=""2""/><line x1=""16"" y1=""2"" x2=""16"" y2=""6""/><line x1=""8"" y1=""2"" x2=""8"" y2=""6""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Direct Bookings</h3>
        <p>Reduce dependency on OTAs with a conversion-optimised booking engine and loyalty features.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>Real-Time Sync</h3>
        <p>Say goodbye to overbookings with a lightning-fast Channel Manager and two-way XML connectivity.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""16"" rx=""2""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Central Reservations</h3>
        <p>Manage multiple properties from a single dashboard with advanced reporting and analytics.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 6l9-4 9 4M4 10h16v10H4z""/></svg></div>
        <h3>Full Operations Suite</h3>
        <p>Housekeeping, POS and finance modules connect to the same PMS data, no separate logins.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M12 2a4 4 0 014 4c0 1.5-.8 2.5-1.5 3.3-.7.8-1.3 1.4-1.3 2.7v1h-2.4v-1c0-1.3-.6-1.9-1.3-2.7C8.8 8.5 8 7.5 8 6a4 4 0 014-4zM9 18h6M10 21h4""/></svg></div>
        <h3>AI Revenue Tools</h3>
        <p>eGlobe AI Tools auto-adjust rates, forecast demand and track competitor pricing for you.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Built For</span>
      <h2 data-reveal>Every kind of hotel and resort.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">Independent Hotels</span>
      <span class=""sol-chip"">Resort Chains</span>
      <span class=""sol-chip"">Multi-Property Groups</span>
      <span class=""sol-chip"">Heritage Properties</span>
      <span class=""sol-chip"">City Business Hotels</span>
      <span class=""sol-chip"">Airport &amp; Transit Hotels</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See eGlobe running your hotel or resort.</h2>
        <p>PMS, channel manager, booking engine and revenue tools, sourced, connected and set up around your rooms and your rate strategy.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var widget = document.getElementById(""orbit-widget"");
  var hub = document.getElementById(""orbit-hub"");
  if(!widget || !hub) return;
  var channels = [""Booking.com"",""Expedia"",""Agoda"",""MakeMyTrip"",""Goibibo""];
  var radius = widget.clientWidth / 2 - 34;
  channels.forEach(function(name, i){
    var angle = (i / channels.length) * Math.PI * 2 - Math.PI / 2;
    var pill = document.createElement(""div"");
    pill.className = ""sol-orbit__pill"";
    pill.textContent = name;
    pill.style.transform = ""translate("" + (radius * Math.cos(angle)) + ""px, "" + (radius * Math.sin(angle)) + ""px) translate(-50%,-50%)"";
    widget.insertBefore(pill, hub);
  });
  var reduceMotion = window.matchMedia && window.matchMedia(""(prefers-reduced-motion: reduce)"").matches;
  if(!reduceMotion){
    widget.addEventListener(""mousemove"", function(e){
      var r = widget.getBoundingClientRect();
      var x = (e.clientX - r.left) / r.width - 0.5;
      var y = (e.clientY - r.top) / r.height - 0.5;
      widget.style.transform = ""perspective(600px) rotateY("" + (x * 10) + ""deg) rotateX("" + (y * -10) + ""deg)"";
    });
    widget.addEventListener(""mouseleave"", function(){ widget.style.transform = """"; });
  }
  function pulse(){
    hub.classList.remove(""pulse"");
    void hub.offsetWidth;
    hub.classList.add(""pulse"");
  }
  hub.addEventListener(""click"", pulse);
  hub.addEventListener(""keydown"", function(e){ if(e.key === ""Enter"" || e.key === "" ""){ e.preventDefault(); pulse(); } });
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Boutique Properties",
                    Slug = "solutions/boutique-properties",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 1,
                    MetaTitle = @"Boutique Hotel Software, eGlobe Solutions | PMS + Channel Manager + Booking Engine",
                    MetaDescription = @"Boutique hotel software built for 10-50 room properties: Channel Manager, Cloud PMS and Booking Engine in one system, without enterprise complexity or enterprise pricing.",
                    MetaKeywords = @"boutique hotel software, small hotel PMS, boutique hotel channel manager, boutique property management system",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Boutique Properties</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">02</span>
          <span class=""sol-hero__index-label"">For Boutique Properties</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>Boutique Hotel Software, Without the Enterprise Bloat</h1>
        <p class=""lead"" data-reveal>Channel Manager, Cloud PMS and Booking Engine built for boutique hotels, 10-50 rooms, one considered system instead of a stack you have to stitch together.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Stack Comparison</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-compare"" id=""compare-widget"">
              <div class=""sol-compare__switch"">
                <span id=""compare-label-off"" class=""active"">Generic Enterprise Stack</span>
                <div class=""sol-compare__track"" id=""compare-track"" role=""switch"" aria-checked=""false"" tabindex=""0"" aria-label=""Compare generic software with eGlobe Boutique""><div class=""sol-compare__thumb""></div></div>
                <span id=""compare-label-on"">eGlobe Boutique</span>
              </div>
              <div class=""sol-compare__stats"">
                <div class=""sol-compare__stat""><div class=""num"" id=""compare-stat-1"">0%</div><div class=""lbl"">Bookings Growth</div></div>
                <div class=""sol-compare__stat""><div class=""num"" id=""compare-stat-2"">1</div><div class=""lbl"">OTA Channels</div></div>
                <div class=""sol-compare__stat""><div class=""num"" id=""compare-stat-3"">8%</div><div class=""lbl"">Overbooking Rate</div></div>
              </div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Flip the switch to compare a generic stack with eGlobe Boutique.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""40"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">Bookings Growth</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Channels</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""0"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">Overbooking</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""24"" data-suffix=""/7"">0</span></div>
        <div class=""lbl"">Support</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Everything You Need</span>
      <h2 data-reveal>A right-sized stack for a boutique property.</h2>
      <p class=""lead"" style=""margin-top:12px;"">No modules you'll never touch, no dashboards built for a 500-room chain.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>Channel Manager</h3>
        <p>Sync rates and inventory across 100+ OTAs in real time, no manual double-entry.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""18"" rx=""2""/><line x1=""16"" y1=""2"" x2=""16"" y2=""6""/><line x1=""8"" y1=""2"" x2=""8"" y2=""6""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Booking Engine</h3>
        <p>Get direct bookings from your own website with zero commission on every reservation.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""16"" rx=""2""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Cloud PMS</h3>
        <p>Manage rooms, guests, billing and reports from any device, no on-site server to maintain.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M12 2a4 4 0 014 4c0 1.5-.8 2.5-1.5 3.3-.7.8-1.3 1.4-1.3 2.7v1h-2.4v-1c0-1.3-.6-1.9-1.3-2.7C8.8 8.5 8 7.5 8 6a4 4 0 014-4zM9 18h6M10 21h4""/></svg></div>
        <h3>AI Front Desk</h3>
        <p>An AI Sales Agent answers enquiries around the clock so no lead goes cold overnight.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M1 10h22M5 15h1M10 15h1""/><rect x=""1"" y=""4"" width=""22"" height=""16"" rx=""2""/></svg></div>
        <h3>Payment Gateway</h3>
        <p>Accept cards, UPI and international payments with automatic reconciliation built in.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M12 17.75l-6.16 3.24 1.18-6.88L2 9.24l6.92-1.01L12 2l3.08 6.23L22 9.24l-5.02 4.87 1.18 6.88z""/></svg></div>
        <h3>Reviews Manager</h3>
        <p>Automated post-stay review requests keep your rating, and your ranking, moving up.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Perfect For</span>
      <h2 data-reveal>Small, considered properties.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">Hotels (10&ndash;50 rooms)</span>
      <span class=""sol-chip"">Boutique Properties</span>
      <span class=""sol-chip"">Vacation Rentals</span>
      <span class=""sol-chip"">Heritage Homestays</span>
      <span class=""sol-chip"">Design Hotels</span>
      <span class=""sol-chip"">Independent City Stays</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See eGlobe running a property like yours.</h2>
        <p>A right-sized PMS, channel manager and booking engine, sourced and set up around a boutique operation, not a chain.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var track = document.getElementById(""compare-track"");
  var labelOff = document.getElementById(""compare-label-off"");
  var labelOn = document.getElementById(""compare-label-on"");
  var s1 = document.getElementById(""compare-stat-1"");
  var s2 = document.getElementById(""compare-stat-2"");
  var s3 = document.getElementById(""compare-stat-3"");
  if(!track) return;
  var states = {
    off: { growth: 0, channels: 1, overbook: 8 },
    on: { growth: 40, channels: 100, overbook: 0 }
  };
  function animateNum(el, from, to, suffix){
    var start = null, dur = 700;
    function step(ts){
      if(!start) start = ts;
      var p = Math.min((ts - start) / dur, 1);
      var eased = 1 - Math.pow(1 - p, 3);
      var val = Math.round(from + (to - from) * eased);
      el.textContent = val + suffix;
      if(p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }
  var on = false;
  function apply(){
    var from = on ? states.off : states.on;
    var to = on ? states.on : states.off;
    track.classList.toggle(""on"", on);
    track.setAttribute(""aria-checked"", on ? ""true"" : ""false"");
    labelOff.classList.toggle(""active"", !on);
    labelOn.classList.toggle(""active"", on);
    animateNum(s1, from.growth, to.growth, ""%"");
    animateNum(s2, from.channels, to.channels, on ? ""+"" : """");
    animateNum(s3, from.overbook, to.overbook, ""%"");
  }
  track.addEventListener(""click"", function(){ on = !on; apply(); });
  track.addEventListener(""keydown"", function(e){ if(e.key === ""Enter"" || e.key === "" ""){ e.preventDefault(); on = !on; apply(); } });
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Vacation Rentals",
                    Slug = "solutions/vacation-rentals",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 2,
                    MetaTitle = @"Vacation Rental Software, eGlobe Solutions | OTA Sync, PMS & Booking Engine",
                    MetaDescription = @"An all-in-one platform for vacation rental managers: sync 100+ OTAs, manage bookings, automate guest communication and grow direct reservations from one dashboard.",
                    MetaKeywords = @"vacation rental software, short term rental management, vacation rental channel manager, vacation rental PMS",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Vacation Rentals</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">03</span>
          <span class=""sol-hero__index-label"">For Vacation Rentals</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>One Dashboard for Every Villa, Apartment and Holiday Home</h1>
        <p class=""lead"" data-reveal>Sync with 100+ OTAs, manage bookings, automate guest communication, and grow direct reservations, all from one smart dashboard built for vacation rental managers.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Request a Free Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Booking Calendar</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-cal"" id=""cal-widget"">
              <div class=""sol-cal__grid"" id=""cal-grid""></div>
              <div class=""sol-cal__meta"" style=""margin-top:14px;""><span>Click a day to toggle</span><span><span class=""sol-cal__counter"" id=""cal-counter"">0</span> nights booked</span></div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Click a day to mark it booked and watch the count update.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid"" style=""grid-template-columns:repeat(3,1fr);"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Channels</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""6"" data-suffix="""">0</span></div>
        <div class=""lbl"">Core Modules</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""24"" data-suffix=""/7"">0</span></div>
        <div class=""lbl"">Real-Time Alerts</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Our Products</span>
      <h2 data-reveal>Everything to run a successful vacation rental business.</h2>
      <p class=""lead"" style=""margin-top:12px;"">Each module solves a specific challenge, from OTA sync to guest communication to upsells.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>OTA Sync</h3>
        <p>Sync rates, availability and calendars across Airbnb, Booking.com, Vrbo and 100+ OTAs in real time, no double bookings.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""16"" rx=""2""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Cloud PMS</h3>
        <p>Manage every reservation, check-in and housekeeping task from one live dashboard, on any device.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""18"" rx=""2""/><line x1=""16"" y1=""2"" x2=""16"" y2=""6""/><line x1=""8"" y1=""2"" x2=""8"" y2=""6""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Booking Engine</h3>
        <p>A commission-free direct booking widget for your own site, instant confirmation included.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M1 10h22M5 15h1M10 15h1""/><rect x=""1"" y=""4"" width=""22"" height=""16"" rx=""2""/></svg></div>
        <h3>Payment Gateway</h3>
        <p>Collect payments, manage deposits and automate invoicing without extra tools.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 3v18h18M7 15l4-4 3 3 5-6""/></svg></div>
        <h3>Dynamic Pricing</h3>
        <p>Smart rules adjust rates automatically by demand, season and lead time.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M9 11l3 3L22 4M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11""/></svg></div>
        <h3>Reports &amp; Exports</h3>
        <p>Revenue, occupancy and channel performance, exportable in one click.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Built For</span>
      <h2 data-reveal>Every kind of short-term rental.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">Villas</span>
      <span class=""sol-chip"">Apartments</span>
      <span class=""sol-chip"">Holiday Homes</span>
      <span class=""sol-chip"">Multi-Unit Portfolios</span>
      <span class=""sol-chip"">Serviced Apartments</span>
      <span class=""sol-chip"">Farm Stays</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>Ready to transform your vacation rental business?</h2>
        <p>Join property managers who trust eGlobe to run smarter, grow faster and delight every guest.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Free Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var grid = document.getElementById(""cal-grid"");
  var counter = document.getElementById(""cal-counter"");
  if(!grid) return;
  var preset = [3,4,10,11,12,17,18,24];
  var total = 21;
  function updateCounter(){
    counter.textContent = grid.querySelectorAll("".booked"").length;
  }
  for(var i = 0; i < total; i++){
    var cell = document.createElement(""div"");
    cell.className = ""sol-cal__day"" + (preset.indexOf(i) > -1 ? "" booked"" : """");
    cell.setAttribute(""role"", ""button"");
    cell.setAttribute(""tabindex"", ""0"");
    cell.textContent = i + 1;
    cell.addEventListener(""click"", function(){ this.classList.toggle(""booked""); updateCounter(); });
    cell.addEventListener(""keydown"", function(e){ if(e.key === ""Enter"" || e.key === "" ""){ e.preventDefault(); this.classList.toggle(""booked""); updateCounter(); } });
    grid.appendChild(cell);
  }
  updateCounter();
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Hostels",
                    Slug = "solutions/hostels",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 3,
                    MetaTitle = @"Hostel Management Software, eGlobe Solutions | Beds, Dorms & OTA Sync",
                    MetaDescription = @"Hostel management software that handles beds, dorms and bookings across OTAs with ease, built for backpacker hostels, dormitory stays and budget properties.",
                    MetaKeywords = @"hostel management software, dorm bed management, hostel PMS, hostel channel manager",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Hostels</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">04</span>
          <span class=""sol-hero__index-label"">For Hostels</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>Manage Beds, Dorms and Bookings Across OTAs With Ease</h1>
        <p class=""lead"" data-reveal>Sell individual beds or full rooms, sync with Hostelworld and 100+ other OTAs, and automate operations to lift occupancy, built for backpacker and dormitory stays.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Bed Map</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-beds"" id=""beds-widget"">
              <div class=""sol-beds__grid"" id=""beds-grid""></div>
              <div class=""sol-beds__meta""><span>Click a bed to toggle</span><span><span class=""sol-beds__counter"" id=""beds-occupancy"">0%</span> occupied</span></div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Click a bed to mark it sold and watch occupancy update.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""50"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">More Bookings</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Channels</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""0"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">Double Booking</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""24"" data-suffix=""/7"">0</span></div>
        <div class=""lbl"">Support</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Core Features</span>
      <h2 data-reveal>Purpose-built for bed-level inventory.</h2>
      <p class=""lead"" style=""margin-top:12px;"">Most hotel software sells rooms. eGlobe sells beds, dorms and rooms, side by side, from one inventory.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 6l9-4 9 4M4 10h16v10H4z""/></svg></div>
        <h3>Bed &amp; Dorm Management</h3>
        <p>Sell individual beds or full rooms with flexible, bed-level inventory control.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>Channel Manager</h3>
        <p>Sync with Hostelworld, Booking.com and other OTAs in real time.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""18"" rx=""2""/><line x1=""16"" y1=""2"" x2=""16"" y2=""6""/><line x1=""8"" y1=""2"" x2=""8"" y2=""6""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Booking Engine</h3>
        <p>Get direct bookings from your own website with zero commission.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""16"" rx=""2""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Cloud PMS</h3>
        <p>Manage check-ins, billing, housekeeping and reports from one screen.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M12 2a4 4 0 014 4c0 1.5-.8 2.5-1.5 3.3-.7.8-1.3 1.4-1.3 2.7v1h-2.4v-1c0-1.3-.6-1.9-1.3-2.7C8.8 8.5 8 7.5 8 6a4 4 0 014-4zM9 18h6M10 21h4""/></svg></div>
        <h3>AI Automation</h3>
        <p>Auto-reply to guest queries and increase booking conversions around the clock.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 3v18h18M7 15l4-4 3 3 5-6""/></svg></div>
        <h3>Dynamic Pricing</h3>
        <p>Adjust bed and dorm prices automatically based on demand, weekends and occupancy.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Perfect For</span>
      <h2 data-reveal>Every kind of budget stay.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">Backpacker Hostels</span>
      <span class=""sol-chip"">Dormitory-Based Stays</span>
      <span class=""sol-chip"">Budget Hotels &amp; PGs</span>
      <span class=""sol-chip"">Co-Living Spaces</span>
      <span class=""sol-chip"">Youth Hostels</span>
      <span class=""sol-chip"">Capsule Hotels</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>See eGlobe running beds, dorms and rooms together.</h2>
        <p>One inventory, every bed type, synced across every OTA your hostel lists on.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Book a Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var grid = document.getElementById(""beds-grid"");
  var occ = document.getElementById(""beds-occupancy"");
  if(!grid) return;
  var total = 24;
  var preset = [0,1,2,6,7,8,9,12,13,18,19,20];
  function update(){
    var sold = grid.querySelectorAll("".sold"").length;
    occ.textContent = Math.round((sold / total) * 100) + ""%"";
  }
  for(var i = 0; i < total; i++){
    var bed = document.createElement(""div"");
    bed.className = ""sol-beds__bed"" + (preset.indexOf(i) > -1 ? "" sold"" : """");
    bed.setAttribute(""role"", ""button"");
    bed.setAttribute(""tabindex"", ""0"");
    bed.setAttribute(""aria-label"", ""Bed "" + (i + 1));
    bed.addEventListener(""click"", function(){ this.classList.toggle(""sold""); update(); });
    bed.addEventListener(""keydown"", function(e){ if(e.key === ""Enter"" || e.key === "" ""){ e.preventDefault(); this.classList.toggle(""sold""); update(); } });
    grid.appendChild(bed);
  }
  update();
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Guest Houses",
                    Slug = "solutions/guest-houses",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 4,
                    MetaTitle = @"Guest House & B&B Software, eGlobe Solutions | Live in Under 30 Minutes",
                    MetaDescription = @"All-in-one cloud software for guest houses and B&Bs: booking calendar, channel manager, front desk PMS and payments, live in under 30 minutes.",
                    MetaKeywords = @"guest house software, B&B management software, guest house PMS, guest house booking system",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Guest Houses</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">05</span>
          <span class=""sol-hero__index-label"">For Guest Houses</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>Smart Software for Guest Houses &amp; B&amp;Bs</h1>
        <p class=""lead"" data-reveal>Automate bookings, manage rooms, and boost revenue with all-in-one cloud software built for modern guest house owners, live in under 30 minutes.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Get Started Free</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Onboarding Path</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-timeline"" id=""timeline-widget"">
              <div class=""sol-timeline__track""><div class=""sol-timeline__fill"" id=""timeline-fill""></div></div>
              <div class=""sol-timeline__steps"" id=""timeline-steps"">
                <div class=""sol-timeline__step active"" data-step=""0"">1</div>
                <div class=""sol-timeline__step"" data-step=""1"">2</div>
                <div class=""sol-timeline__step"" data-step=""2"">3</div>
                <div class=""sol-timeline__step"" data-step=""3"">4</div>
              </div>
              <div class=""sol-timeline__label"" id=""timeline-label"">Register Your Property</div>
              <div class=""sol-timeline__desc"" id=""timeline-desc"">Sign up and add your room types, photos and pricing.</div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Auto-advances every few seconds, or click a step to jump.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""7"" data-suffix=""K+"">0</span></div>
        <div class=""lbl"">Properties Managed</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Channels</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""40"" data-suffix=""%"">0</span></div>
        <div class=""lbl"">Avg. Revenue Boost</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""30"" data-suffix="" min"">0</span></div>
        <div class=""lbl"">Onboarding Time</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Everything You Need</span>
      <h2 data-reveal>Every tool a guest house owner needs, in one dashboard.</h2>
      <p class=""lead"" style=""margin-top:12px;"">From online reservations to housekeeping, not a bloated enterprise system, a purpose-built fit for small and mid-size properties.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""18"" rx=""2""/><line x1=""16"" y1=""2"" x2=""16"" y2=""6""/><line x1=""8"" y1=""2"" x2=""8"" y2=""6""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Booking Calendar</h3>
        <p>A drag-and-drop room calendar with colour-coded availability and instant confirmations.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>Channel Manager</h3>
        <p>Sync rates and inventory across Booking.com, Airbnb, Agoda and 100+ OTAs in real time.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M9 11l3 3L22 4M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11""/></svg></div>
        <h3>Online Booking Engine</h3>
        <p>Commission-free direct bookings from your own website with secure payments.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><rect x=""3"" y=""4"" width=""18"" height=""16"" rx=""2""/><line x1=""3"" y1=""10"" x2=""21"" y2=""10""/></svg></div>
        <h3>Front Desk PMS</h3>
        <p>Check-in, check-out, guest profiles, invoicing and housekeeping in one screen.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M1 10h22M5 15h1M10 15h1""/><rect x=""1"" y=""4"" width=""22"" height=""16"" rx=""2""/></svg></div>
        <h3>Payment Gateway</h3>
        <p>Accept cards, UPI, net banking and international payments with auto reconciliation.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 3v18h18M7 15l4-4 3 3 5-6""/></svg></div>
        <h3>Revenue Analytics</h3>
        <p>Live occupancy, RevPAR and ADR dashboards, export-ready for accounting.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Built For</span>
      <h2 data-reveal>Small, family-run stays.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">B&amp;Bs</span>
      <span class=""sol-chip"">Homestays</span>
      <span class=""sol-chip"">Independent Guest Houses</span>
      <span class=""sol-chip"">Farm Stays</span>
      <span class=""sol-chip"">Heritage Homes</span>
      <span class=""sol-chip"">Family-Run Properties</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>Get your guest house live on eGlobe.</h2>
        <p>Booking calendar, channel manager and payments, set up in under 30 minutes, no technical skills required.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Get Started Free</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var steps = document.querySelectorAll("".sol-timeline__step"");
  var fill = document.getElementById(""timeline-fill"");
  var label = document.getElementById(""timeline-label"");
  var desc = document.getElementById(""timeline-desc"");
  if(!steps.length) return;
  var data = [
    { label: ""Register Your Property"", desc: ""Sign up and add your room types, photos and pricing."" },
    { label: ""Connect Your Channels"", desc: ""Link Booking.com, Airbnb and other OTAs in one click."" },
    { label: ""Set Your Rates"", desc: ""Use dynamic pricing rules and seasonal templates automatically."" },
    { label: ""Grow &amp; Analyse"", desc: ""Track bookings and revenue as your guest house grows."" }
  ];
  var current = 0;
  function render(i){
    current = i;
    steps.forEach(function(s, idx){ s.classList.toggle(""active"", idx <= i); });
    fill.style.width = (((i + 1) / steps.length) * 100) + ""%"";
    label.innerHTML = data[i].label;
    desc.innerHTML = data[i].desc;
  }
  steps.forEach(function(s){
    s.addEventListener(""click"", function(){ render(parseInt(s.getAttribute(""data-step""), 10)); });
  });
  var reduceMotion = window.matchMedia && window.matchMedia(""(prefers-reduced-motion: reduce)"").matches;
  if(!reduceMotion){
    setInterval(function(){ render((current + 1) % steps.length); }, 3200);
  }
})();
</script>"
                },
                new CmsPage
                {
                    Title = @"Travel Agencies",
                    Slug = "solutions/travel-agencies",
                    UseCustomHero = true,
                    IsPublished = true,
                    SortOrder = 5,
                    MetaTitle = @"Travel Agency Management Software, eGlobe Solutions | Itineraries, B2B & OTA Sync",
                    MetaDescription = @"A complete platform for travel agencies: itinerary builder, B2B agent portal, booking engine and real-time OTA sync, all from one dashboard.",
                    MetaKeywords = @"travel agency software, travel agency management system, B2B travel portal, tour operator software",
                    Body = @"<!-- ===================== HERO ===================== -->
<header class=""sol-hero panel-white"">
  <div class=""container"">
    <div class=""pp-breadcrumb"" data-reveal>
      <a href=""../index.html"">Home</a><span>/</span>
      <a href=""../solutions/hotels-resorts.html"">Solutions</a><span>/</span>
      <span class=""current"">Travel Agencies</span>
    </div>
    <div class=""sol-hero__grid"" style=""margin-top:24px;"">
      <div>
        <div class=""sol-hero__index"" data-reveal>
          <span class=""sol-hero__index-num"">06</span>
          <span class=""sol-hero__index-label"">For Travel Agencies</span>
          <span class=""sol-hero__index-total"">/ 06</span>
        </div>
        <h1 data-reveal>Grow Your Travel Agency With Smart Tech</h1>
        <p class=""lead"" data-reveal>Manage bookings, tour packages, B2B partners and OTA connections, all from one powerful dashboard built for travel agencies.</p>
        <div class=""sol-hero__actions"" data-reveal>
          <a href=""../contact.html"" class=""btn btn-primary"">Request a Free Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
      <div class=""sol-hero__visual"" data-reveal=""scale"">
        <div class=""sol-panel"">
          <div class=""sol-panel__readout""><span>Live Instrument &mdash; Agency Dashboard</span><span class=""sol-panel__readout-dot""></span></div>
          <div class=""sol-panel__body"">
            <div class=""sol-dash"" id=""dash-widget"">
              <div class=""sol-dash__head"">
                <span class=""sol-dash__title"">Agency Dashboard</span>
                <span class=""sol-dash__live""><span class=""sol-dash__live-dot""></span>Live</span>
              </div>
              <div class=""sol-dash__grid"">
                <div class=""sol-dash__cell""><div class=""num"" id=""dash-bookings"">18</div><div class=""lbl"">Today's Bookings</div></div>
                <div class=""sol-dash__cell""><div class=""num"" id=""dash-travellers"">142</div><div class=""lbl"">Active Travellers</div></div>
                <div class=""sol-dash__cell""><div class=""num"" id=""dash-revenue"">&#8377;4.8L</div><div class=""lbl"">Monthly Revenue</div></div>
                <div class=""sol-dash__cell""><div class=""num"">4.8 / 5</div><div class=""lbl"">Avg. Rating</div></div>
              </div>
            </div>
          </div>
          <div class=""sol-panel__caption"">Figures tick up automatically to simulate a live agency feed.</div>
        </div>
      </div>
    </div>
  </div>
</header>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""7000"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Properties on Platform</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">OTA Integrations</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""2"" data-suffix=""M+"">0</span></div>
        <div class=""lbl"">Bookings Processed</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""40"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Countries Served</div>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section"">
  <div class=""container"">
    <div class=""sol-section-head"">
      <span class=""section-kicker"">Platform Features</span>
      <h2 data-reveal>Everything your agency needs to thrive.</h2>
      <p class=""lead"" style=""margin-top:12px;"">From itinerary builders and B2B portals to real-time OTA sync, one cohesive system.</p>
    </div>
    <div class=""sol-feature-grid"" data-reveal-group>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""10""/><path d=""M2 12h20M12 2a15 15 0 014 10 15 15 0 01-4 10 15 15 0 01-4-10 15 15 0 014-10z""/></svg></div>
        <h3>Itinerary Builder</h3>
        <p>Create day-by-day travel itineraries with drag-and-drop, auto hotel mapping and PDF export.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><circle cx=""12"" cy=""12"" r=""9""/><line x1=""3"" y1=""12"" x2=""21"" y2=""12""/><path d=""M12 3a15 15 0 010 18a15 15 0 010-18""/></svg></div>
        <h3>Channel Manager</h3>
        <p>Sync rates and availability across Booking.com, Airbnb, Expedia, MakeMyTrip and 100+ OTAs.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2M9 11a4 4 0 100-8 4 4 0 000 8zM23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75""/></svg></div>
        <h3>B2B Agent Portal</h3>
        <p>A branded portal for corporate clients and sub-agents to search, book and manage at negotiated rates.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M9 11l3 3L22 4M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11""/></svg></div>
        <h3>Booking Engine</h3>
        <p>Commission-free direct bookings via your own website with secure payments and auto confirmation.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M3 3v18h18M7 15l4-4 3 3 5-6""/></svg></div>
        <h3>Revenue Analytics</h3>
        <p>Live dashboards tracking occupancy, RevPAR and agent-wise commission performance.</p>
      </div>
      <div class=""sol-feature"" data-reveal>
        <div class=""sol-feature__icon""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M1 10h22M5 15h1M10 15h1""/><rect x=""1"" y=""4"" width=""22"" height=""16"" rx=""2""/></svg></div>
        <h3>Payment Gateway</h3>
        <p>Accept UPI, cards and international payments with automatic GST invoicing and reconciliation.</p>
      </div>
    </div>
  </div>
</section>

<section class=""sol-section panel-white"">
  <div class=""container"">
    <div class=""sol-section-head"" style=""margin-bottom:20px;"">
      <span class=""section-kicker"">Built For</span>
      <h2 data-reveal>Every kind of travel business.</h2>
    </div>
    <div class=""sol-chips"" data-reveal-group>
      <span class=""sol-chip"">Retail Travel Agents</span>
      <span class=""sol-chip"">Tour Operators</span>
      <span class=""sol-chip"">DMCs</span>
      <span class=""sol-chip"">Corporate Travel Desks</span>
      <span class=""sol-chip"">Destination Weddings Planners</span>
      <span class=""sol-chip"">Group Travel Organisers</span>
    </div>
  </div>
</section>

<!--FAQ_PLACEHOLDER-->

<section class=""sol-section"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>Set up your agency on eGlobe.</h2>
        <p>Itineraries, B2B portals, OTA sync and payments, live in under 30 minutes, no technical expertise needed.</p>
        <div class=""final-cta__row"">
          <a href=""../contact.html"" class=""btn btn-dark"">Request a Free Demo</a>
          <a href=""../pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>

<script>
(function(){
  ""use strict"";
  var reduceMotion = window.matchMedia && window.matchMedia(""(prefers-reduced-motion: reduce)"").matches;
  if(reduceMotion) return;
  var bookingsEl = document.getElementById(""dash-bookings"");
  var travellersEl = document.getElementById(""dash-travellers"");
  var revenueEl = document.getElementById(""dash-revenue"");
  if(!bookingsEl) return;
  var bookings = 18, travellers = 142, revenue = 4.8;
  setInterval(function(){
    bookings += 1;
    travellers += Math.floor(Math.random() * 3) + 1;
    revenue = Math.round((revenue + 0.1) * 10) / 10;
    bookingsEl.textContent = bookings;
    travellersEl.textContent = travellers;
    revenueEl.innerHTML = ""&#8377;"" + revenue + ""L"";
  }, 4000);
})();
</script>"
                }
        );

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds FaqItems for every Product and Solution page (PageKey == "products/{slug}"
    /// or "solutions/{slug}"), the FAQ content that used to be hardcoded HTML inside
    /// each CmsPage.Body (see PageFaqsViewComponent / the "<!--FAQ_PLACEHOLDER-->" marker
    /// in Page.cshtml), moved here so it is admin-editable at /admin/faqs?page=<slug>
    /// instead of requiring a code change to update. Idempotent: does nothing if any
    /// "products/*" or "solutions/*" FaqItems already exist.
    /// </summary>
    private static async Task SeedProductAndSolutionFaqsAsync(AppDbContext db)
    {
        if (await db.FaqItems.AnyAsync(f => f.PageKey.StartsWith("products/") || f.PageKey.StartsWith("solutions/"))) return;

        db.FaqItems.AddRange(
            new FaqItem { PageKey = "products/pms", Question = @"What is a Property Management System (PMS)?", Answer = @"Hotel management software hosted on the cloud that lets you manage bookings, check-ins, housekeeping and billing from any device with an internet connection.", SortOrder = 0 },
            new FaqItem { PageKey = "products/pms", Question = @"Is PMS included in every eGlobe plan?", Answer = @"Yes, PMS is included in Per Room, Per Property and Enterprise plans, itâ€™s the foundation every other module connects to.", SortOrder = 1 },
            new FaqItem { PageKey = "products/channel-manager", Question = @"Will it prevent overbookings?", Answer = @"Yes, real-time two-way sync updates every connected OTA within seconds of a booking, virtually eliminating double bookings.", SortOrder = 0 },
            new FaqItem { PageKey = "products/channel-manager", Question = @"Which OTAs are supported?", Answer = @"Booking.com, Expedia, MakeMyTrip, Goibibo, Agoda and 100+ more Indian and global channels.", SortOrder = 1 },
            new FaqItem { PageKey = "products/housekeeping", Question = @"Do staff need a special device?", Answer = @"No, it works from any smartphone or tablet housekeeping already carries.", SortOrder = 0 },
            new FaqItem { PageKey = "products/housekeeping", Question = @"Is Housekeeping included in every plan?", Answer = @"Yes, itâ€™s included in Per Room, Per Property and Enterprise plans at no extra cost.", SortOrder = 1 },
            new FaqItem { PageKey = "products/pos", Question = @"Does it connect to my hotel PMS?", Answer = @"Yes, bills post directly to the guest folio and sync in real time with eGlobe Cloud PMS.", SortOrder = 0 },
            new FaqItem { PageKey = "products/pos", Question = @"Is POS included in my plan?", Answer = @"Included on Per Property and Enterprise; available as an add-on on Per Room.", SortOrder = 1 },
            new FaqItem { PageKey = "products/kot", Question = @"Does it need a separate kitchen device?", Answer = @"Just a screen or printer at the kitchen pass, no special hardware or software installation required.", SortOrder = 0 },
            new FaqItem { PageKey = "products/kot", Question = @"Is KOT part of the POS module?", Answer = @"Yes, KOT works together with POS, orders taken at the POS route straight to the kitchen display.", SortOrder = 1 },
            new FaqItem { PageKey = "products/booking-engine", Question = @"Is it fully integrated with my other systems?", Answer = @"Yes, the Booking Engine is fully integrated with your PMS, Channel Manager, and Payment Gateway.", SortOrder = 0 },
            new FaqItem { PageKey = "products/booking-engine", Question = @"How is the Booking Engine billed?", Answer = @"A 5% commission applies to confirmed bookings made through the engine, no OTA fees on top.", SortOrder = 1 },
            new FaqItem { PageKey = "products/finance-revenue", Question = @"Does it set rates automatically?", Answer = @"It suggests rate changes based on demand, your revenue team approves before anything goes live.", SortOrder = 0 },
            new FaqItem { PageKey = "products/finance-revenue", Question = @"Is Finance & Revenue Management included in my plan?", Answer = @"Included on Per Property and Enterprise; available as an add-on on Per Room.", SortOrder = 1 },
            new FaqItem { PageKey = "products/reviews-manager", Question = @"Which review platforms are covered?", Answer = @"Google, Booking.com, TripAdvisor and the other major platforms your guests already review you on.", SortOrder = 0 },
            new FaqItem { PageKey = "products/reviews-manager", Question = @"Is Reviews Manager included in my plan?", Answer = @"Included on Per Property and Enterprise; available as an add-on on Per Room.", SortOrder = 1 },
            new FaqItem { PageKey = "products/b2b-stay", Question = @"Can partners see my public rates too?", Answer = @"No, role-based access means each partner only sees the rates and inventory you assign to them.", SortOrder = 0 },
            new FaqItem { PageKey = "products/b2b-stay", Question = @"Is B2B Stay included in my plan?", Answer = @"Not available on Per Room; available as an add-on on Per Property, and included on Enterprise.", SortOrder = 1 },
            new FaqItem { PageKey = "products/ota-management", Question = @"How long does OTA listing take?", Answer = @"Usually 2 to 5 working days, depending on each platformâ€™s approval process and how complete your property details are.", SortOrder = 0 },
            new FaqItem { PageKey = "products/ota-management", Question = @"Is OTA Listing & Management included in my plan?", Answer = @"Yes, itâ€™s included in Per Room, Per Property and Enterprise plans.", SortOrder = 1 },
            new FaqItem { PageKey = "products/google-hotel-ads", Question = @"Is there a cost to participate?", Answer = @"No setup fees or rental costs, you pay a low commission only on confirmed bookings, under the Pay Per Conversion model.", SortOrder = 0 },
            new FaqItem { PageKey = "products/google-hotel-ads", Question = @"Is the commission rate fixed?", Answer = @"The commission is admin-configurable per your agreement, ask your eGlobe account manager for your current rate.", SortOrder = 1 },
            new FaqItem { PageKey = "products/meta-search", Question = @"Which meta search platforms are supported?", Answer = @"Google Hotel Ads and Google Maps Integration, with your rates shown alongside major OTAs at the point of search.", SortOrder = 0 },
            new FaqItem { PageKey = "products/meta-search", Question = @"Do I need a Booking Engine to use this?", Answer = @"Yes, meta search sends travellers to book directly through your eGlobe Booking Engine.", SortOrder = 1 },
            new FaqItem { PageKey = "products/website-builder", Question = @"Do you build the website, or do we?", Answer = @"We build it for you, as a single page or a full multi-page site depending on what your property needs.", SortOrder = 0 },
            new FaqItem { PageKey = "products/website-builder", Question = @"Who buys the domain?", Answer = @"Either works, use a domain you already own, or we can register one for you for an additional charge.", SortOrder = 1 },
            new FaqItem { PageKey = "products/payment-gateway", Question = @"Is it PCI compliant?", Answer = @"Yes, all card processing runs through PCI-certified payment partners.", SortOrder = 0 },
            new FaqItem { PageKey = "products/payment-gateway", Question = @"Is Payment Gateway included in my plan?", Answer = @"Itâ€™s an add-on available across Per Room, Per Property and Enterprise plans, billed on a small transaction fee.", SortOrder = 1 },
            new FaqItem { PageKey = "products/pms-apis", Question = @"How secure is the API?", Answer = @"All endpoints are protected with OAuth 2.0 authentication, so your data transfers are always secure.", SortOrder = 0 },
            new FaqItem { PageKey = "products/pms-apis", Question = @"Is there an uptime guarantee?", Answer = @"Yes, a 99.9% uptime guarantee backs the API alongside full documentation.", SortOrder = 1 },
            new FaqItem { PageKey = "products/ai-tools", Question = @"Does this replace my front-desk staff?", Answer = @"No, it handles routine enquiries and check-in support so your team can focus on guests, reducing workload rather than replacing staff.", SortOrder = 0 },
            new FaqItem { PageKey = "products/ai-tools", Question = @"Which channels does the AI Sales Agent cover?", Answer = @"WhatsApp, your website and social enquiries, with confirmed bookings pushed straight into your PMS.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/hotels-resorts", Question = @"What makes eGlobe different for hotels and resorts?", Answer = @"eGlobe sources and connects your entire technology stack, PMS, channel manager, booking engine and more, as one integrated system instead of a pile of disconnected tools you have to reconcile yourself.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/hotels-resorts", Question = @"Can a multi-property chain manage everything from one dashboard?", Answer = @"Yes, the Central Reservation System gives portfolio-level visibility across every property, with per-property drill-down for rates, inventory and reporting.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/hotels-resorts", Question = @"How many OTAs can I connect to?", Answer = @"250+, including Booking.com, Expedia, Agoda, MakeMyTrip and Goibibo, all synced two-way in real time through the Channel Manager.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/hotels-resorts", Question = @"Is there a setup fee?", Answer = @"Onboarding and setup terms vary by plan and property size, our sales team will walk through exact pricing during your consultation.", SortOrder = 3 },
            new FaqItem { PageKey = "solutions/boutique-properties", Question = @"What is boutique hotel software?", Answer = @"Software sized for small, independent properties, it helps you manage bookings, pricing and OTA distribution without the setup overhead of enterprise hotel systems.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/boutique-properties", Question = @"Is it good for very small hotels?", Answer = @"Yes, eGlobe's Per Room plan is built specifically for properties in the 10-50 room range, with a flat monthly fee regardless of how many modules you turn on.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/boutique-properties", Question = @"Do I need separate software just for OTAs?", Answer = @"No, the Channel Manager is included and syncs rates and availability across 100+ OTAs from the same dashboard as your PMS.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/boutique-properties", Question = @"Can I upgrade as my property grows?", Answer = @"Yes, you can move from Per Room to Per Property or Enterprise pricing at any time as you add rooms or properties.", SortOrder = 3 },
            new FaqItem { PageKey = "solutions/vacation-rentals", Question = @"How does OTA sync work for vacation rentals?", Answer = @"One update to your rates or availability pushes instantly across Airbnb, Booking.com, Vrbo, Expedia and 100+ other connected OTAs, no manual re-entry.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/vacation-rentals", Question = @"Can I manage multiple properties from one account?", Answer = @"Yes, the Multi-Property module lets you switch between villas, apartments and holiday homes, or run bulk actions across all of them at once.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/vacation-rentals", Question = @"Is there a mobile app?", Answer = @"Yes, both iOS and Android apps let you approve bookings, message guests and track revenue from anywhere.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/vacation-rentals", Question = @"Do guests pay directly through my website?", Answer = @"Yes, the commission-free Booking Engine accepts payment at the time of booking, with the funds settling straight into your connected payment gateway.", SortOrder = 3 },
            new FaqItem { PageKey = "solutions/hostels", Question = @"What is hostel management software?", Answer = @"It helps you manage beds, dorms, bookings and guest operations efficiently, at the bed level rather than only the room level.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/hostels", Question = @"Can I manage beds individually?", Answer = @"Yes, each bed in a dorm has its own inventory record, so you can sell single beds while still selling private rooms from the same dashboard.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/hostels", Question = @"Does it sync with Hostelworld?", Answer = @"Yes, alongside 100+ other OTAs, all synced in real time through the Channel Manager.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/hostels", Question = @"Is group booking supported?", Answer = @"Yes, you can block multiple beds or an entire dorm for group bookings directly from the PMS.", SortOrder = 3 },
            new FaqItem { PageKey = "solutions/guest-houses", Question = @"Do I need technical knowledge to set up eGlobe?", Answer = @"No, a guided setup wizard gets your property live and accepting bookings within about 30 minutes, no developer required.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/guest-houses", Question = @"Which OTAs does eGlobe connect to?", Answer = @"Booking.com, Airbnb, Agoda, MakeMyTrip and 100+ others, all synced through the same Channel Manager.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/guest-houses", Question = @"Is there a free trial available?", Answer = @"Talk to our sales team about current trial terms, they vary by plan and are confirmed during your consultation.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/guest-houses", Question = @"Does eGlobe handle GST invoicing for Indian properties?", Answer = @"Yes, GST-compliant invoices are generated automatically as part of the Payment Gateway module.", SortOrder = 3 },
            new FaqItem { PageKey = "solutions/travel-agencies", Question = @"Can sub-agents get their own portal?", Answer = @"Yes, the B2B Agent Portal gives corporate clients and sub-agents a branded space to search, book and manage travel at your negotiated rates.", SortOrder = 0 },
            new FaqItem { PageKey = "solutions/travel-agencies", Question = @"Does it handle GST invoicing automatically?", Answer = @"Yes, GST-compliant invoices generate automatically through the integrated Payment Gateway.", SortOrder = 1 },
            new FaqItem { PageKey = "solutions/travel-agencies", Question = @"Can I build custom itineraries?", Answer = @"Yes, the Itinerary Builder supports drag-and-drop day-by-day planning with automatic hotel mapping and PDF export for clients.", SortOrder = 2 },
            new FaqItem { PageKey = "solutions/travel-agencies", Question = @"Is there a mobile app for agents?", Answer = @"Yes, iOS and Android apps let your team manage bookings and track agency performance on the go.", SortOrder = 3 }
        );

        await db.SaveChangesAsync();
    }


    /// <summary>Seeds About Us + the 3 legal pages as CmsPages (UseCustomHero=true,
    /// Body is the page's full self-contained markup) so they render through the
    /// same live TopNav/SiteFooter/NavDock components every other page uses,
    /// instead of static wwwroot/*.html files that silently drift out of sync
    /// with the shared nav whenever it changes (which is exactly what happened
    /// before this: the static files still had the pre-Solutions-menu topbar and
    /// the pre-redesign footer).</summary>
    private static async Task SeedAboutAndLegalPagesAsync(AppDbContext db)
    {
        if (await db.CmsPages.AnyAsync(p => p.Slug == "about" || p.Slug == "privacy-policy" || p.Slug == "terms-of-use" || p.Slug == "refund-and-cancellation")) return;

        db.CmsPages.AddRange(
            new CmsPage[]
            {
                new CmsPage
                {
                    Title = "About Us",
                    Slug = "about",
                    UseCustomHero = true,
                    IsPublished = true,
                    MetaTitle = "About Us, eGlobe Solutions | Hotel Technology Since 2007",
                    MetaDescription = "eGlobe Solutions, the brand name of Direct Hotels Ltd., has delivered hotel technology since 2007. 100+ channel partners, 7,000+ properties worldwide.",
                    MetaKeywords = "about eglobe solutions, direct hotels private limited, hotel technology company, hotel software company India",
                    Body = @"<header class=""page-hero page-hero--compact"">
  <div class=""container page-hero__split"">
    <h1 data-reveal><span class=""accent"">About</span> eGlobe Solutions</h1>
    <p class=""lead"" data-reveal>eGlobe Solutions, the brand name of Direct Hotels Ltd., was established in 2007 as a bootstrap company, with the goal of building a successful, scalable business in travel technology.</p>
  </div>
</header>

<!-- ===================== STATS ===================== -->
<section class=""section-tight panel-white"">
  <div class=""container"">
    <div class=""stat-grid stat-grid--4"" data-reveal-group>
      <div class=""stat-card"">
        <div class=""num"">2007</div>
        <div class=""lbl"">Founded</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""18"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Years in Hospitality Tech</div>
      </div>
      <div class=""stat-card"">
        <div class=""num""><span data-counter=""100"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Global Channel Partners</div>
      </div>
      <div class=""stat-card accent"">
        <div class=""num""><span data-counter=""7000"" data-suffix=""+"">0</span></div>
        <div class=""lbl"">Properties Worldwide</div>
      </div>
    </div>
  </div>
</section>

<!-- ===================== STORY ===================== -->
<section class=""section-tight section-border-top panel-white"">
  <div class=""container"" style=""max-width:1080px;"">
    <div class=""article-layout"">
      <div class=""article-content"">
        <h2>How We <span class=""accent"">Started</span></h2>
        <p>eGlobe Solutions is the brand name of Direct Hotels Ltd. We were set up in 2007 as a bootstrap company, meaning we grew using our own resources and revenue, not outside investors. That mattered from day one: it meant every decision was made with one goal in mind, building something hotels would actually find useful, rather than something built to impress a boardroom.</p>
        <p>From the beginning, we saw where travel technology was headed. Hotels needed software that worked the way they worked, not the other way around. That belief is still the reason eGlobe exists today, almost two decades later.</p>

        <h2>What We <span class=""accent-alt"">Started With</span></h2>
        <p>Our first product was a Hotel Booking Engine, simply put, the tool that lets a guest book a room directly on a hotel's own website instead of through a third-party site. It sounds simple, but it solves a real problem: every booking made this way saves the hotel a commission it would otherwise pay to an OTA (an online travel agency, like Booking.com or Expedia).</p>

        <h2>What We've <span class=""accent"">Built</span> Since</h2>
        <p>A hotel runs on more than just bookings, so over the years we kept building around that first product until it became a full system:</p>
        <ul>
          <li><strong>Channel Manager</strong>, keeps room rates and availability updated everywhere at once, so a room sold on one site is instantly blocked on every other site. No double bookings.</li>
          <li><strong>Mobile Applications</strong>, puts hotel management in the owner's or manager's pocket, so decisions don't have to wait until someone is back at a desk.</li>
          <li><strong>Cloud-based Property Management System (PMS)</strong>, the operational core of a hotel: reservations, guest details, room status and billing, all in one place, accessible from anywhere.</li>
          <li><strong>Point of Sale (POS)</strong>, runs billing for a hotel's restaurant or other outlets, and connects that spending straight back to the guest's room bill.</li>
        </ul>
        <p>Each of these was built to work together, not as separate add-ons. That's still how we build today: one connected system, instead of a hotel having to stitch together five different vendors.</p>

        <h2>A <span class=""accent-alt"">Connected</span>, Global Network</h2>
        <p>A hotel's rooms need to be visible wherever travellers are actually looking for them. Today, eGlobe connects hotels to more than 100 global channel partners, the booking platforms and travel sites where guests search and book, including major names like Airbnb, HostelWorld, Ctrip, Booking.com, Expedia and Agoda. Update a rate once inside eGlobe, and it reflects everywhere, automatically.</p>

        <h2>What Our <span class=""accent"">Google Hotel Ads</span> Leadership Means</h2>
        <p>We've also become one of the leading providers of Google Hotel Ads, the rate listings that show up when someone searches for a hotel directly on Google Search or Google Maps. We currently support this for more than 7,000 properties worldwide, helping hotels get found by guests at the exact moment they're searching, without paying a commission on every booking the way OTA listings require.</p>

        <h2><span class=""accent-alt"">18 Years</span>, and Counting</h2>
        <p>We've now been building hospitality technology for 18 years. That track record isn't just a number on a page: it means the systems we've built have been tested through real hotel operations, real seasons, real busy nights, and real problems, and refined because of them. It's the difference between software built in theory and software shaped by actually running alongside hotels for almost two decades.</p>

        <h2>Where We're <span class=""accent"">Headed</span></h2>
        <p>We're not done. Our goal is to keep building on that 18-year foundation and become a genuinely global leader in travel technology, not by chasing every new feature, but by staying focused on the same question we started with: what actually makes a hotel easier to run, one system at a time.</p>
      </div>

      <aside class=""article-sidebar"">
        <div class=""article-toc"">
          <div class=""article-toc__label"">Quick Facts</div>
          <a href=""#"" onclick=""return false;"">Brand of Direct Hotels Private Limited</a>
          <a href=""#"" onclick=""return false;"">Founded 2007</a>
          <a href=""#"" onclick=""return false;"">100+ global channel partners</a>
          <a href=""#"" onclick=""return false;"">7,000+ properties worldwide</a>
          <a href=""#"" onclick=""return false;"">Google Hotel Ads partner</a>
        </div>
        <div class=""article-toc"" style=""margin-top:24px;"">
          <div class=""article-toc__label"">Talk to Us</div>
          <a href=""contact.html"">Contact Our Team</a>
          <a href=""mailto:support@eglobe-solutions.com"" class=""footer__strip-priority"">Email Support</a>
          <a href=""tel:+919818880480"" class=""footer__strip-priority"">Call +91 9818880480</a>
        </div>
      </aside>
    </div>
  </div>
</section>

<!-- ===================== FINAL CTA ===================== -->
<section class=""section-tight"">
  <div class=""container"">
    <div class=""final-cta"" data-reveal=""scale"">
      <div class=""grid-bg"" style=""opacity:.15""></div>
      <div style=""position:relative;z-index:1;"">
        <h2>Ready to See eGlobe in <span class=""accent-alt"">Action</span>?</h2>
        <p>Talk to our team about the PMS, channel manager and everything else eGlobe sources for your hotel.</p>
        <div class=""final-cta__row"">
          <a href=""contact.html"" class=""btn btn-dark"">Talk to Sales</a>
          <a href=""pricing.html"" class=""btn btn-ghost"">View Pricing</a>
        </div>
      </div>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = "Privacy Policy",
                    Slug = "privacy-policy",
                    UseCustomHero = true,
                    IsPublished = true,
                    MetaTitle = "Privacy Policy, eGlobe Solutions",
                    MetaDescription = "eGlobe Solutions' Privacy Policy, how we collect, use and protect the data of hoteliers, guests and website visitors.",
                    MetaKeywords = "",
                    Body = @"<header class=""page-hero page-hero--compact"">
  <div class=""container page-hero__split"">
    <h1 data-reveal>Privacy Policy</h1>
    <p class=""lead"" data-reveal>This Privacy Policy explains what data we collect through our website and software, how it's used, and the choices you have.</p>
  </div>
</header>

<section class=""section-tight panel-white"">
  <div class=""container"" style=""max-width:1080px;"">
    <div class=""article-layout"">
    <div class=""article-content"">
      <h2 id=""data-protection"">Data Protection</h2>
      <p>We respect your privacy at eGlobe Solutions. Your personal information is securely handled and never shared with third parties without your consent.</p>

      <h2 id=""personal-information"">Personal Information</h2>
      <p>This includes your email address and any personal details provided while using our platform. We ensure strict confidentiality of all user data.</p>

      <h2 id=""policy-updates"">Policy Updates</h2>
      <p>We may update this privacy policy from time to time. Any changes will be communicated, giving users the option to review and opt-out if required.</p>

      <h2 id=""no-tracking"">No Individual Tracking</h2>
      <p>We do not track or monitor individual user activity on our website, ensuring a safe and private browsing experience.</p>

      <div class=""article-callout"" id=""contact-us"">
        <p><strong>Have a question about your data or this policy?</strong> Our team is glad to help.</p>
        <a href=""contact.html"" class=""btn btn-primary btn-sm"">Contact Us</a>
      </div>
    </div>

    <aside class=""article-sidebar"">
      <div class=""article-toc"">
        <div class=""article-toc__label"">On this page</div>
        <a href=""#data-protection"">Data Protection</a>
        <a href=""#personal-information"">Personal Information</a>
        <a href=""#policy-updates"">Policy Updates</a>
        <a href=""#no-tracking"">No Individual Tracking</a>
      </div>
      <div class=""article-toc"" style=""margin-top:24px;"">
        <div class=""article-toc__label"">Need Help?</div>
        <a href=""contact.html"">Contact Our Team</a>
        <a href=""mailto:support@eglobe-solutions.com"" class=""footer__strip-priority"">Email Support</a>
      </div>
    </aside>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = "Terms of Use",
                    Slug = "terms-of-use",
                    UseCustomHero = true,
                    IsPublished = true,
                    MetaTitle = "Terms of Use, eGlobe Solutions",
                    MetaDescription = "eGlobe Solutions' Terms of Use, the terms and conditions governing your use of our website and hotel management software.",
                    MetaKeywords = "",
                    Body = @"<header class=""page-hero page-hero--compact"">
  <div class=""container page-hero__split"">
    <h1 data-reveal>Terms of Use</h1>
    <p class=""lead"" data-reveal>These Terms of Use govern your access to and use of the eGlobe Solutions website, product calculator and the hotel management software and services we provide.</p>
  </div>
</header>

<section class=""section-tight panel-white"">
  <div class=""container"" style=""max-width:1080px;"">
    <div class=""article-layout"">
    <div class=""article-content"">
      <p>In terms of Information Technology Act, 2000, this document is an electronic record. Being generated by a computer system it does not require any physical or digital signatures. This document is published in accordance with the provisions of Rule 3 (1) of the Information Technology (Intermediaries guidelines) Rules, 2011 that require publishing the rules and regulations, privacy policy and Terms of Use for access or usage of www.eglobe-solutions.com</p>

      <h2 id=""ownership"">The domain name www.eglobe-solutions.com (hereinafter referred to as &ldquo;Website&rdquo;) is owned by</h2>
      <p>Direct Hotels Private Limited, a Company registered under the Companies Act, 1956 and having its Registered Office at 301, Ansal Classique Tower, Rajouri Garden, New Delhi 110027 (hereinafter referred to as &lsquo;the company&rsquo;). The use of this website by You is solely governed by this policy and any policy so mentioned by terms of reference. Moving past home page, or using any of the services shall be taken to mean that You have read and agreed to all of the policies so binding in You and that You are contracting with the Company and have undertaken binding obligations with the Company. For the purpose of these Terms of Use, wherever the context so requires &ldquo;You&rdquo; or &ldquo;User&rdquo; shall mean any natural or legal person who has agreed to become a member on the Website by providing Registration Data while registering on the Website. The site also providing it&rsquo;s services without registration does not absolve You of this contractual relationship. The term &ldquo;We&rdquo;, &ldquo;Us&rdquo;, &ldquo;Our&rdquo; shall mean www.eglobe-solutions.com.</p>
      <p>You will be subject to the rules, guidelines, policies, terms, and conditions applicable to any service that is provided by this site, and they shall be deemed to be incorporated into this Terms of Use and shall be considered as part and parcel of this Terms of Use.</p>
      <p>We hold the sole right to modify the Terms of Service without prior permission from You or informing You. The relationship creates on You a duty to periodically check the terms and stay updated on its requirements. If You continue to use the website following such a change, this is deemed as consent by You to the so amended policies. As long as You comply with these Terms of Use, We grant You a personal, non-exclusive, non-transferable, limited privilege to enter and use the Website.</p>
      <p>By impliedly or expressly accepting these Terms of Service, You also accept and agree to be bound by other Company Policies, inter alia Privacy Policy, which would be amended from time to time.</p>
      <p>These Terms of Service are to be read in concurrence with any other agreement or contract that the user has with Direct Hotels Private Limited.</p>

      <div class=""section-head"" style=""margin:40px 0 20px;"">
        <h2 data-reveal>Frequently Asked Questions</h2>
      </div>
      <div class=""faq-list"" data-reveal-group>

      <div class=""faq-item"" id=""general"">
        <div class=""faq-q""><span>01. General</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a""><p>www.eglobe-solutions.com is a website that provides online hotel distribution and reservation systems, and hosts a software / application (hereinafter &ldquo;Software&rdquo;) that enables hotels to update information on online platforms, and make room reservations through distribution to &lsquo;online&rsquo; and &lsquo;offline&rsquo; travel companies and travel portals.</p></div>
      </div>

      <div class=""faq-item"" id=""intermediary"">
        <div class=""faq-q""><span>02. The Website as an Intermediary Platform</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>The Website is a platform that Users utilize to meet and interact with one another for their transactions. We are not a party to such interaction and take no liability that arises from any such communication.</p>
          <p>2.1. All communication which inter alia include the contract, its terms, your obligations, the hotel&rsquo;s obligations, prices, etc are outcomes of the communication between the hotel and You. This includes, without any limitation, the prices, rent, payment details, date, period of stay and warranties related to services and products and after booking/reservation services related to services and products. We do not have any control over such information and play no determinative role in the finalization of the same and hence do not stand liable for the outcomes of such communication.</p>
          <p>2.2. We do not endorse any of the Hotels or the services offered on the website nor place any guarantee as to its nature, rent, quality, etc.</p>
          <p>2.3. Subject to the above sub-clauses, a contract exists between the Hotel and the User and as such any breach of contract and thus, any claim arising from such breach is the subject matter of the Hotel and the User alone and we are in no way a party to such breach or involved in any suit arising from the same breach. The contact/communication arising from such breach may entail between the Hotel and the User directly without Us being involved.</p>
          <p>2.4. As we hold no possession, nor title of the Hotel rooms or directly provide any service apart from reservation services at any time, or enter/determine the communication between the User and the Hotel or determine its outcome, the contract is purely a bipartite contract between the User and the Hotel and We are not responsible for claims arising from such a contract.</p>
          <p><strong>Disclaimer:</strong> Due to some technical issue, typographical error or information related to services published, Pricing on any product(s) or services as is reflected on the Website by Hotel may be incorrectly reflected and in such an event Hotel may cancel such reservations made by you.</p>
          <p>2.5. At no point of time between communication and delivery of services between the user making the reservation and the hotel do we come into possession of the goods or its title.</p>
          <p>2.6. As the contract is limited to the User and the Hotel and not Us, we are in no way liable for any deficiency of service that may arise which includes and is not limited to cancellation of the reservation due to non-availability of rooms, services not meeting expectations of the User, and poor quality of rooms.</p>
          <p>2.7. While making a reservation or booking on the website, you are expected to check the creditworthiness of the Hotel and the genuineness of the service offered by them. We are not liable for the same.</p>
          <p>2.8. You release and indemnify Us and/or any of its officers and representatives from any cost, damage, liability or other consequence of any of the actions of the Users of the Website and specifically waive any claims that you may have in this behalf under any applicable law. Please note that there could be risks in dealing with underage persons or people acting under false pretense.</p>
        </div>
      </div>

      <div class=""faq-item"" id=""membership"">
        <div class=""faq-q""><span>03. Membership</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a""><p>The membership of this website is available only to those above the age of 18 barring those &lsquo;Incompetent to Contract&rsquo; which inter alia include insolvents and the same is not allowed to minors as described by the Indian Contract Act, 1872. If You are a minor and wish to use the website, you may do so through your legal guardian and www.eglobe-solutions.com reserves the right to terminate your account on knowledge of You being a minor and using the membership of the site. Further, You are solely responsible for protecting the confidentiality of your username and password and any activity under the account will be deemed to have been done by you. In the case that you provide us with false and inaccurate details or the company has reasonable reasons to believe you have done so, We hold the rights to permanently suspend your account.</p></div>
      </div>

      <div class=""faq-item"" id=""communication"">
        <div class=""faq-q""><span>04. Communication</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>By using this website, it is deemed that you have consented to receiving calls, autodialed and/or pre-recorded message calls, from Us at any time with the use of the telephone that has been provided by you for the use of this website which are subject to the Privacy Policy. This includes contacting you through information received through other parties. The use of this website is also your consent to receive SMSs from us at any time we deem fit. This consent to be contacted is for purposes that include and are not limited to clarification calls and marketing and promotional calls. In case you wish to stop contact from Us for the same, you may send us a mail to the effect.</p>
          <p>The sharing of the information provided by you will be governed by the Privacy Policy and we will not give out such contact information of yours to third parties not connected with the Website.</p>
          <p>You may also be contacted by Service Providers with whom we have entered into a contract in furtherance of our rights, duties and obligations under this documents and all other policies followed by Us. Such contact will be made only in pursuance of such objectives, and no other calls will be made.</p>
        </div>
      </div>

      <div class=""faq-item"" id=""charges"">
        <div class=""faq-q""><span>05. Charges</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a""><p>The membership of this website is free of cost and this includes the browsing of the site and the use of the services. However, we reserve the right to amend this no-fee policy and charge for the services rendered. In a case that such happens, Users will be intimated of the same, and it will be up to you to decide whether or not you will continue with services offered by us. Such changes are effective as soon as they are posted on the website.</p></div>
      </div>

      <div class=""faq-item"" id=""third-party"">
        <div class=""faq-q""><span>06. Third Party Information</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>All text, graphics, user interfaces, visual interfaces, photographs, trademarks, logos, sounds, music and artwork (collectively, &ldquo;Content&rdquo;), is a third party user generated content and We have no control over such third party user generated content as We are merely an intermediary for the purposes of this Terms of Use. Other than when provided for, the use of such content and it being reproduced, republished, uploaded, posted, publicly displayed, encoded, translated, transmitted or distributed in any way (including &ldquo;mirroring&rdquo;) to any other computer, server, Website or other medium for publication or distribution or for any commercial enterprise, without Our express prior written consent is not allowed.</p>
          <p>The content that you post will become Our property and You grant Us the worldwide, perpetual and transferable rights in such Content. We shall be entitled to, consistent with Our Privacy Policy as adopted in accordance with applicable law, use the Content or any of its elements for any type of use forever, including but not limited to promotional and advertising purposes and in any media whether now known or hereafter devised, including the creation of derivative works that may include the Content You provide and are not entitled to any payment or other compensation for such use.</p>
        </div>
      </div>

      <div class=""faq-item"" id=""obligations"">
        <div class=""faq-q""><span>07. User Obligations</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>You are a restricted user of this website.</p>
          <p>7.1 You agree not to access (or attempt to access) the Website and/or the materials or Services by any means other than through the interface that is provided by the website. The use of deep-link, robot, spider or other automatic device, program, algorithm or methodology, or any similar or equivalent manual process, to access, acquire, copy or monitor any portion of the Website or Content, or in any way reproduce or circumvent the navigational structure or presentation of the Website, materials or any Content, to obtain or attempt to obtain any materials, documents or information through any means not specifically made available through the Website. You acknowledge and agree that by accessing or using the Website or Services, You may be exposed to content from other users that You may consider offensive, indecent or otherwise objectionable. We disclaims all liabilities arising in relation to such offensive content on the Website. Further, You may report such offensive content.</p>
          <p>7.2 In places where this website allows you to post or upload data/information, You undertake to ensure that such material is not offensive and in accordance with applicable laws. Further, You undertake not to:<br>
          a) Abuse, harass, threaten, defame, disillusion, erode, abrogate, demean or otherwise violate the legal rights of others;<br>
          b) Engage in any activity that interferes with or disrupts access to the Website or the Services (or the servers and networks which are connected to the Website)<br>
          c) Impersonate any person or entity, or falsely state or otherwise misrepresent Your affiliation with a person or entity;<br>
          d) Publish, post, disseminate, any information which is grossly harmful, harassing, blasphemous, defamatory, obscene, pornographic, paedophilic, libellous, invasive of another&rsquo;s privacy, hateful, or racially, ethnically objectionable, disparaging, relating or encouraging money laundering or gambling, or otherwise unlawful in any manner whatever; or unlawfully threatening or unlawfully harassing including but not limited to &ldquo;indecent representation of women&rdquo; within the meaning of the Indecent Representation of Women (Prohibition) Act, 1986;<br>
          e) Post any file that infringes the copyright, patent or trademark of other legal entities.<br>
          f) Upload or distribute files that contain viruses, corrupted files, or any other similar software or programs that may damage the operation of the Website or another&rsquo;s computer.<br>
          g) Download any file posted by another user of a Service that you know, or reasonably should know, cannot be legally distributed in such manner;<br>
          h) Probe, scan or test the vulnerability of the Website or any network connected to the Website, nor breach the security or authentication measures on the Website or any network connected to the Website. You may not reverse look-up, trace or seek to trace any information on any other user, of or visitor to, the Website, or any other customer of the website, including any website Account not owned by You, to its source, or exploit the Website or Service or information made available or offered by or through the Website, in any way whether or not the purpose is to reveal any information, including but not limited to personal identification information, other than Your own information, as provided for by the Website;<br>
          i) Disrupt or interfere with the security of, or otherwise cause harm to, the Website, systems resources, accounts, passwords, servers or networks connected to or accessible through the Websites or any affiliated or linked sites;<br>
          j) Collect or store data about other users in connection with the prohibited conduct and activities set forth in this Section.<br>
          k) Use the Website or any material or Content for any purpose that is unlawful or prohibited by these Terms of Use, or to solicit the performance of any illegal activity or other activity which infringes the rights of this website or other third parties;<br>
          l) Violate any code of conduct or other guidelines, which may be applicable for or to any particular Service.<br>
          m) Violate any applicable laws or regulations for the time being in force within or outside India;<br>
          n) Violate the Terms of Use including but not limited to any applicable Additional Terms of the Website contained herein or elsewhere;<br>
          o) Violate any code of conduct or other guidelines, which may be applicable for or to any particular Service;<br>
          p) Threatens the unity, integrity, defence, security or sovereignty of India, friendly relations with foreign states, or public order or causes incitement to the commission of any cognizable offence or prevents investigation of any offence or is insulting any other nation.<br>
          q) Publish, post, disseminate information that is false, inaccurate or misleading; violate any applicable laws or regulations for the time being in force in or outside India.<br>
          r) Directly or indirectly, offer, attempt to offer, trade or attempt to trade in any item, the dealing of which is prohibited or restricted in any manner under the provisions of any applicable law, rule, regulation or guideline for the time being in force.<br>
          s) Create liability for Us or cause Us to lose (in whole or in part) the services of Our internet service provider (&ldquo;ISPs&rdquo;) or other suppliers.</p>
        </div>
      </div>

      <div class=""faq-item"" id=""disclaimer"">
        <div class=""faq-q""><span>08. Disclaimer of Warranties and Liabilities</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>You expressly understand and agree that, to the maximum extent permitted by applicable law: the website, services and other materials are provided by this website is on an &ldquo;as is&rdquo; basis without warranty of any kind, express, implied, statutory or otherwise, including the implied warranties of title, non-infringement, merchantability or fitness for a particular purpose. Without limiting the foregoing, the website makes no warranty that:</p>
          <p>(i) your requirements will be met or that services provided will be uninterrupted, timely, secure or error-free;<br>
          (ii) materials, information obtained and results will be effective, accurate or reliable;<br>
          (iii) any errors or defects in the website, services or other materials will be corrected.</p>
          <p>To the maximum extent permitted by applicable law, we will have no liability related to user content arising under intellectual property rights, libel, privacy, publicity, obscenity or other laws. The website also disclaims all liability with respect to the misuse, loss, modification or unavailability of any user content. The user understands and agrees that any material or data downloaded or otherwise obtained through the website is done entirely at their own discretion and risk and they will be solely responsible for any damage to their computer systems or loss of data that results from the download of such material or data. We are not responsible for any typographical error leading to an invalid coupon. The website accepts no liability for any errors of this nature.</p>
          <p><strong>8.1 Indemnification and Limitation of Liability.</strong> You agree to indemnify, defend and hold harmless this website including but not limited to its affiliate vendors, agents and employees from and against any and all losses, liabilities, claims, damages, demands, costs and expenses (including legal fees and disbursements in connection therewith and interest chargeable thereon) asserted against or incurred by us that arise out of, result from, or may be payable by virtue of, any breach or non-performance of any representation, warranty, covenant or agreement made or obligation to be performed by you pursuant to these terms of use. Further, you agree to hold us harmless against any claims made by any third party due to, or arising out of, or in connection with, your use of the website, any claim that your material caused damage to a third party, your violation of the terms of use, or your violation of any rights of another, including any intellectual property rights. In no event shall we, its officers, directors, employees, partners or suppliers be liable to you, the vendor or any third party for any special, incidental, indirect, consequential or punitive damages whatsoever, including those resulting from loss of use, data or profits, whether or not foreseeable or whether or not we have been advised of the possibility of such damages, or based on any theory of liability, including breach of contract or warranty, negligence or other tortious action, or any other claim arising out of or in connection with your use of or access to the website, services or materials. The limitations and exclusions in this section apply to the maximum extent permitted by applicable law.</p>
        </div>
      </div>

      <div class=""faq-item"" id=""hosting"">
        <div class=""faq-q""><span>09. Hosting of Third Party Information</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a""><p>The website hosts information provided by third party. We are in no way responsible to you for the accuracy, legitimacy and trueness of the information so hosted. We take reasonable care to ensure such accuracy but, You agree to not hold us liable for the falsification of any such provided information.</p></div>
      </div>

      <div class=""faq-item"" id=""compliance"">
        <div class=""faq-q""><span>10. Compliance with Laws</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a""><p>Users and Hotels shall comply with all the applicable laws (including without limitation Foreign Exchange Management Act, 1999 and the rules made and notifications issued there under and the Exchange Control Manual as may be issued by Reserve Bank of India from time to time, Customs Act, Information and Technology Act, 2000 as amended by the Information Technology (Amendment) Act 2008, Prevention of Money Laundering Act, 2002 and the rules made there under, Foreign Contribution Regulation Act, 1976 and the rules made there under, Income Tax Act, 1961 and the rules made there under, Export Import Policy of government of India) applicable to them respectively for using Payment Facility and the website.</p></div>
      </div>

      <div class=""faq-item"" id=""disputes"">
        <div class=""faq-q""><span>11. Disputes and Jurisdiction</span><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><polyline points=""6 9 12 15 18 9""/></svg></div>
        <div class=""faq-a"">
          <p>All disputes involving but not limited to rights conferred, compensation, refunds, and other claims will be resolved through a two-step Alternate Dispute Resolution mechanism.</p>
          <p>11.1. Stage 1: Mediation. In case of a dispute, the matter will first be attempted to be resolved by a sole mediator who is a neutral third party and will be selected at the mutual acceptance of a proposed mediator by both parties. Both parties may raise a name for sole arbitrator and in the case both parties accept the proposed name, the said person shall be appointed sole mediator. In the case the parties are not able to reach a consensus within two proposed mediators, the Company reserves the right to decide who the final mediator is. The decision of the mediator is not binding on both parties however, the parties in good faith will attempt to bind by the decision.</p>
          <p>11.2. Stage 2: Arbitration. In the case that mediation does not yield a result suitable or preferred by any one of the parties, arbitration may follow, the award of which is binding on both parties. The Arbitration Board is to comprise three members. One is to be appointed by each party and the third member is to be nominated by the two appointed members by mutual consent between them. The award as the outcome of the arbitration is final and binding on both parties and there shall be no further remedy available to both parties. The arbitration proceedings will take place in the English Language and will be situated in New Delhi, Delhi. The mode of appointment of the arbitrators is as provided above.</p>
        </div>
      </div>

      </div>
    </div>

    <aside class=""article-sidebar"">
      <div class=""article-toc"">
        <div class=""article-toc__label"">On this page</div>
        <a href=""#ownership"">Ownership</a>
        <a href=""#general"">01. General</a>
        <a href=""#intermediary"">02. Intermediary Platform</a>
        <a href=""#membership"">03. Membership</a>
        <a href=""#communication"">04. Communication</a>
        <a href=""#charges"">05. Charges</a>
        <a href=""#third-party"">06. Third Party Info</a>
        <a href=""#obligations"">07. User Obligations</a>
        <a href=""#disclaimer"">08. Disclaimer &amp; Liability</a>
        <a href=""#hosting"">09. Hosting of Info</a>
        <a href=""#compliance"">10. Compliance</a>
        <a href=""#disputes"">11. Disputes</a>
      </div>
      <div class=""article-toc"" style=""margin-top:24px;"">
        <div class=""article-toc__label"">Need Help?</div>
        <a href=""contact.html"">Contact Our Team</a>
        <a href=""mailto:support@eglobe-solutions.com"" class=""footer__strip-priority"">Email Support</a>
      </div>
    </aside>
    </div>
  </div>
</section>"
                },
                new CmsPage
                {
                    Title = "Refund & Cancellation Policy",
                    Slug = "refund-and-cancellation",
                    UseCustomHero = true,
                    IsPublished = true,
                    MetaTitle = "Refund & Cancellation Policy, eGlobe Solutions",
                    MetaDescription = "eGlobe Solutions' Refund & Cancellation Policy, covering subscription cancellations, refund eligibility and how to request one.",
                    MetaKeywords = "",
                    Body = @"<header class=""page-hero page-hero--compact"">
  <div class=""container page-hero__split"">
    <h1 data-reveal>Refund & Cancellation Policy</h1>
    <p class=""lead"" data-reveal>This policy explains how subscription cancellations and refunds work across eGlobe's Per Room, Per Property and Enterprise plans.</p>
  </div>
</header>

<section class=""section-tight panel-white"">
  <div class=""container"" style=""max-width:1080px;"">
    <div class=""article-layout"">
    <div class=""article-content"">
      <h2 id=""cancellation-policy"">Cancellation Policy</h2>
      <p>Cancellation policies may vary depending on the hotel or booking type. Charges will be applied as per the terms mentioned in your booking confirmation.</p>

      <h2 id=""refund-process"">Refund Process</h2>
      <p>Refunds are processed to the original payment method and may take 5&ndash;7 working days depending on your bank or provider.</p>

      <h2 id=""cancellation-charges"">Cancellation Charges</h2>
      <p>Cancellation or no-show charges may apply based on hotel policies and booking conditions.</p>

      <h2 id=""non-refundable"">Non-Refundable Bookings</h2>
      <p>Certain promotional offers or discounted rates may not be eligible for cancellation or refund. Please review booking details carefully.</p>

      <div class=""article-callout"" id=""contact-us"">
        <p><strong>Have a question about a refund or cancellation?</strong> Our team can look into your booking directly.</p>
        <a href=""contact.html"" class=""btn btn-primary btn-sm"">Contact Us</a>
      </div>
    </div>

    <aside class=""article-sidebar"">
      <div class=""article-toc"">
        <div class=""article-toc__label"">On this page</div>
        <a href=""#cancellation-policy"">Cancellation Policy</a>
        <a href=""#refund-process"">Refund Process</a>
        <a href=""#cancellation-charges"">Cancellation Charges</a>
        <a href=""#non-refundable"">Non-Refundable Bookings</a>
      </div>
      <div class=""article-toc"" style=""margin-top:24px;"">
        <div class=""article-toc__label"">Need Help?</div>
        <a href=""contact.html"">Contact Our Team</a>
        <a href=""mailto:support@eglobe-solutions.com"" class=""footer__strip-priority"">Email Support</a>
        <a href=""tel:+919818880480"" class=""footer__strip-priority"">Call +91 9818880480</a>
      </div>
    </aside>
    </div>
  </div>
</section>"
                },
            }
        );
        await db.SaveChangesAsync();
    }

}
