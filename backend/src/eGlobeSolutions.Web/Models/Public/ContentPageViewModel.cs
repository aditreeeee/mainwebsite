using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Models.Public;

/// <summary>Shared shape for pages driven by ContentBlock rows (Home, Reseller).</summary>
public class ContentPageViewModel
{
    public Dictionary<string, ContentBlock> Blocks { get; set; } = new();
    public SeoMetadata? Seo { get; set; }

    /// <summary>Populated only by pages that also need SiteSettings outside the shared footer (e.g. Contact's "Get In Touch" sidebar).</summary>
    public Dictionary<string, string?> Settings { get; set; } = new();

    /// <summary>Looks up a block by section key; returns null if the admin hasn't created it (or unpublished it) yet, so the view can fall back to static copy.</summary>
    public ContentBlock? Block(string sectionKey) => Blocks.GetValueOrDefault(sectionKey);

    public string Setting(string key, string fallback) =>
        Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : fallback;
}
