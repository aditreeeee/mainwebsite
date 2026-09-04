using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.ViewComponents;

/// <summary>Renders the shared site footer from SiteSettings (contact info, socials, apps, copyright) and MenuItems (Product/Company columns).</summary>
public class SiteFooterViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    public SiteFooterViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var settings = await _db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);

        var productLinks = await _db.MenuItems
            .AsNoTracking()
            .Where(m => m.Location == "footer-product" && m.IsPublished)
            .OrderBy(m => m.SortOrder).ToListAsync();
        if (productLinks.Count == 0)
        {
            productLinks = new List<MenuItem>
            {
                new() { Label = "Cloud PMS", Url = "index.html#ecosystem" },
                new() { Label = "Channel Manager", Url = "index.html#ecosystem" },
                new() { Label = "Cloud POS", Url = "index.html#ecosystem" },
                new() { Label = "Booking Engine", Url = "index.html#ecosystem" },
                new() { Label = "Website Builder", Url = "index.html#ecosystem" },
                new() { Label = "eGlobe AI Tools", Url = "index.html#ecosystem" },
            };
        }

        var solutionLinks = await _db.MenuItems
            .AsNoTracking()
            .Where(m => m.Location == "footer-solutions" && m.IsPublished)
            .OrderBy(m => m.SortOrder).ToListAsync();
        if (solutionLinks.Count == 0)
        {
            solutionLinks = new List<MenuItem>
            {
                new() { Label = "Hotels & Resorts", Url = "solutions/hotels-resorts.html" },
                new() { Label = "Boutique Properties", Url = "solutions/boutique-properties.html" },
                new() { Label = "Vacation Rentals", Url = "solutions/vacation-rentals.html" },
                new() { Label = "Hostels", Url = "solutions/hostels.html" },
                new() { Label = "Guest Houses", Url = "solutions/guest-houses.html" },
                new() { Label = "Travel Agencies", Url = "solutions/travel-agencies.html" },
            };
        }

        var companyLinks = await _db.MenuItems
            .AsNoTracking()
            .Where(m => m.Location == "footer-company" && m.IsPublished)
            .OrderBy(m => m.SortOrder).ToListAsync();
        if (companyLinks.Count == 0)
        {
            companyLinks = new List<MenuItem>
            {
                new() { Label = "Home", Url = "index.html" },
                new() { Label = "Pricing", Url = "pricing.html" },
                new() { Label = "Resellers", Url = "reseller.html" },
                new() { Label = "Blog", Url = "blog.html" },
                new() { Label = "Contact", Url = "contact.html" },
            };
        }

        return View(new SiteFooterViewModel
        {
            Settings = settings,
            ProductLinks = productLinks,
            SolutionLinks = solutionLinks,
            CompanyLinks = companyLinks
        });
    }
}
