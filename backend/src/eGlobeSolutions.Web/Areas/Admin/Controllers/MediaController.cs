using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eGlobeSolutions.Web.Areas.Admin.Controllers;

/// <summary>Simple media library: uploads land in wwwroot/uploads and are tracked in MediaAssets.</summary>
[Area("Admin")]
[Route("admin/media")]
[Authorize(Policy = "AdminOnly")]
public class MediaController : Controller
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg", ".pdf"
    };
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MediaController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var assets = await _db.MediaAssets.OrderByDescending(a => a.CreatedAtUtc).ToListAsync(ct);
        return View(assets);
    }

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file, string? altText, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
        {
            TempData["Error"] = "File type not allowed. Use jpg, png, webp, gif, svg or pdf.";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > MaxSizeBytes)
        {
            TempData["Error"] = "File is larger than the 10 MB limit.";
            return RedirectToAction(nameof(Index));
        }

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var storedName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsDir, storedName);
        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var asset = new MediaAsset
        {
            FileName = storedName,
            OriginalFileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Url = $"/uploads/{storedName}",
            AltText = altText,
            CreatedBy = User.Identity?.Name
        };
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "File uploaded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var asset = await _db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound();

        asset.IsDeleted = true;
        asset.DeletedAtUtc = DateTime.UtcNow;
        asset.DeletedBy = User.Identity?.Name;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "File removed from the library.";
        return RedirectToAction(nameof(Index));
    }
}
