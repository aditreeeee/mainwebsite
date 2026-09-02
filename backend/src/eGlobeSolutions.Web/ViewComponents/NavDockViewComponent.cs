using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.ViewComponents;

/// <summary>Renders the mobile nav-dock's link list from MenuItems (Location="nav-dock").</summary>
public class NavDockViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    public NavDockViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var items = await _db.MenuItems
            .AsNoTracking()
            .Where(m => m.Location == "nav-dock" && m.IsPublished)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        if (items.Count == 0)
        {
            items = new List<MenuItem>
            {
                new() { Label = "Home", Url = "index.html", SortOrder = 0 },
                new() { Label = "Pricing", Url = "pricing.html", SortOrder = 1 },
                new() { Label = "Resellers", Url = "reseller.html", SortOrder = 2 },
            };
        }

        return View(items);
    }
}
