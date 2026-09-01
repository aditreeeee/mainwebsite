using eGlobeSolutions.Domain.Entities;
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
            var topNav = new[] { ("Home", "index.html"), ("Pricing", "pricing.html"), ("Resellers", "reseller.html") };
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
                ("Cloud PMS", "index.html#ecosystem"), ("Channel Manager", "index.html#ecosystem"),
                ("Cloud POS", "index.html#ecosystem"), ("Booking Engine", "index.html#ecosystem"),
                ("Website Builder", "index.html#ecosystem"), ("eGlobe AI Tools", "index.html#ecosystem"),
            };
            foreach (var (label, url) in footerProduct)
            {
                db.MenuItems.Add(new MenuItem { Location = "footer-product", Label = label, Url = url, SortOrder = i++ });
            }

            i = 0;
            var footerCompany = new[]
            {
                ("Home", "index.html"), ("Pricing", "pricing.html"), ("Resellers", "reseller.html"),
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

            var teasers = new (string Title, string Category, string Excerpt, DateTime Published, int ReadMin)[]
            {
                ("Best Channel Manager for Small Hotels in India (2026)", "Guide",
                    "Everything a guesthouse or independent property needs to know before choosing a channel manager.",
                    new DateTime(2026, 1, 28, 0, 0, 0, DateTimeKind.Utc), 7),
                ("How to Connect MakeMyTrip & Goibibo via Channel Manager", "Guide",
                    "Step-by-step walkthrough for Indian hoteliers to get live on India's top OTAs fast.",
                    new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc), 6),
                ("Channel Manager vs Manual OTA Management: What Hotels Lose", "News",
                    "The real cost of managing OTAs manually, in time, money and missed bookings.",
                    new DateTime(2025, 12, 20, 0, 0, 0, DateTimeKind.Utc), 4),
                ("What's New: Rate Parity Alerts & Mobile Inventory Steppers", "Product",
                    "Two small dashboard updates that save your revenue team a daily headache.",
                    new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), 3),
                ("5 Signs Your Hotel Has Outgrown Manual OTA Management", "Guide",
                    "If any of these sound familiar, a channel manager will pay for itself within a month.",
                    new DateTime(2025, 11, 18, 0, 0, 0, DateTimeKind.Utc), 5),
                ("Why Direct Bookings Are Rising Across Indian Hotels in 2026", "News",
                    "Booking engines, Google Hotel Ads and guest trust are reshaping the OTA-versus-direct balance.",
                    new DateTime(2025, 11, 2, 0, 0, 0, DateTimeKind.Utc), 6),
            };

            var sort = 1;
            foreach (var (title, category, excerpt, published, readMin) in teasers)
            {
                db.BlogPosts.Add(new BlogPost
                {
                    Title = title,
                    Category = category,
                    Excerpt = excerpt,
                    PublishedAtUtc = published,
                    ReadTimeMinutes = readMin,
                    SortOrder = sort++
                    // No Slug/Body: these were "#" teaser cards on the original static
                    // page too, not real linked articles. Give one a Body via the
                    // admin Blog Posts screen to turn it into a real article page.
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
                CtaLabel = "Contact Sales",
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
                CtaLabel = "Contact Sales",
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
                CtaLabel = "Contact Sales",
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
