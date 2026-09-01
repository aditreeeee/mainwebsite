using eGlobeSolutions.Domain.Entities;

namespace eGlobeSolutions.Web.Models.Public;

public class BlogIndexViewModel
{
    public BlogPost? Featured { get; set; }
    public List<BlogPost> Posts { get; set; } = new();
    public SeoMetadata? Seo { get; set; }
}

public class BlogArticleViewModel
{
    public BlogPost Post { get; set; } = null!;
}
