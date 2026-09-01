using eGlobeSolutions.Domain.Common;

namespace eGlobeSolutions.Domain.Entities;

/// <summary>
/// A blog post/article. The listing page (blog.html) shows the featured
/// post plus a filterable grid; posts with a non-empty Slug and Body get
/// their own page at "/{Slug}.html" (matches the original static URL
/// scheme, e.g. article-ai-tools.html), posts without a Body are teaser
/// cards only, same as the original static placeholders that linked to "#".
/// </summary>
public class BlogPost : AuditableEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>URL segment before ".html". Must be unique. Empty/null means no dedicated article page yet.</summary>
    public string? Slug { get; set; }

    public string Category { get; set; } = "Guide"; // Guide | Product | News, drives the blog-filter buttons
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>Full article HTML body. Null/empty means this post has no dedicated page (teaser card only).</summary>
    public string? Body { get; set; }

    public string AuthorName { get; set; } = "eGlobe Team";
    public string AuthorRole { get; set; } = "Product";
    public int ReadTimeMinutes { get; set; } = 5;
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsFeatured { get; set; }
    public bool IsPublished { get; set; } = true;
    public int SortOrder { get; set; }

    public string? CoverImageUrl { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
}
