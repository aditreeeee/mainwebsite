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
            // Flat ₹1,200/month base subscription, the same regardless of how
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
                new CurrencyRate { Code = "INR", Symbol = "₹", Name = "Indian Rupee", RatePerInr = 1m, IsDefault = true, SortOrder = 0 },
                new CurrencyRate { Code = "USD", Symbol = "$", Name = "US Dollar", RatePerInr = 0.012m, SortOrder = 1 },
                new CurrencyRate { Code = "EUR", Symbol = "€", Name = "Euro", RatePerInr = 0.011m, SortOrder = 2 },
                new CurrencyRate { Code = "GBP", Symbol = "£", Name = "British Pound", RatePerInr = 0.0095m, SortOrder = 3 },
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
                    VolumeInputLabel = "Estimated monthly booking value (₹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "5% commission on confirmed direct bookings made through the engine.",
                    SortOrder = 11
                },
                new PricingModule
                {
                    Code = "google-hotel-ads", Name = "Google Hotel Ads", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 12m,
                    VolumeInputLabel = "Estimated monthly Google Hotel Ads booking value (₹)",
                    PerRoomAvailability = add, PerPropertyAvailability = add, EnterpriseAvailability = add,
                    Tooltip = "Management fee/commission on bookings driven through Google Hotel Ads, admin-configurable.",
                    SortOrder = 12
                },
                new PricingModule
                {
                    Code = "meta-search", Name = "Meta Search Engines", Category = ModuleCategory.AdditionalProduct,
                    ChargeType = ModuleChargeType.Commission, CommissionPercent = 10m,
                    VolumeInputLabel = "Estimated monthly meta-search booking value (₹)",
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
                    VolumeInputLabel = "Estimated monthly online payment volume (₹)",
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
                [SiteSettingKeys.FooterCopyright] = ("© 2026 eGlobe Solutions. All rights reserved.", "General"),
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
}
