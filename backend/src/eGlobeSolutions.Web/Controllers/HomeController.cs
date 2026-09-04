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
            .AsNoTracking()
            .Where(b => b.PageKey == "home" && b.IsPublished)
            .ToDictionaryAsync(b => b.SectionKey, ct);

        var vm = new ContentPageViewModel
        {
            Blocks = blocks,
            Seo = await _db.SeoMetadata.AsNoTracking().FirstOrDefaultAsync(s => s.PageKey == "home", ct)
        };
        return View(vm);
    }

    // Handles both UseExceptionHandler("/error") (an unhandled 500, code is
    // null) and UseStatusCodePagesWithReExecute("/error/{0}") (a routing/auth
    // status like 404 or 403, code is set) so each gets its own branded page
    // instead of a blank status-only response.
    [Route("error/{code:int?}")]
    public IActionResult Error(int? code) => View(code switch
    {
        404 => "NotFound",
        403 => "Forbidden",
        _ => "Error"
    });
}
