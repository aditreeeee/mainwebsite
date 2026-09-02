using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Models.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Controllers;

/// <summary>
/// Serves index.html as a database-backed Razor view. The interactive
/// dashboard demo, department tabs, product grid/modal system and logo
/// strips stay as static markup (they're UI/interaction structure, not
/// editorial content); the headline copy, section intros and CTAs for each
/// section come from ContentBlock rows so an editor can change them without
/// a redeploy. See README for the full scope note.
/// </summary>
public class HomeController : Controller
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    // Adding any attribute route below opts this action out of the default
    // conventional "/" route entirely, so "/" has to be listed explicitly
    // alongside "index.html", the literal link every page's brand/Home nav uses.
    [HttpGet("")]
    [HttpGet("index.html")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var blocks = await _db.ContentBlocks
            .Where(b => b.PageKey == "home" && b.IsPublished)
            .ToDictionaryAsync(b => b.SectionKey, ct);

        var vm = new ContentPageViewModel
        {
            Blocks = blocks,
            Seo = await _db.SeoMetadata.FirstOrDefaultAsync(s => s.PageKey == "home", ct)
        };
        return View(vm);
    }

    [Route("error")]
    public IActionResult Error() => View("Error");
}
