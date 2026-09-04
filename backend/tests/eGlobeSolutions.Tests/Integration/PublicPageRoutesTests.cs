using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Smoke-tests every publicly reachable route driven by the CMS/seeded
/// content: the 16 Platform product pages and 6 Solutions pages (both
/// served through BlogController's shared "/products/{slug}.html" and
/// "/solutions/{slug}.html" routes, backed by CmsPages seeded in
/// DbInitializer), the blog list/article pages, and the static legal pages
/// (Terms/Privacy/Refund, plain files under wwwroot, no controller).
/// Doesn't assert on visual content, only that each real route actually
/// resolves to a live page instead of 404ing, exactly the class of bug this
/// session's product-page shadowing fix and Solutions rollout both hit
/// before being caught. A route silently regressing to 404 (a typo'd slug,
/// a shadowed static file, a missing CmsPage seed) fails loudly here
/// instead of only being noticed by a visitor.
/// </summary>
[Collection("Integration")]
public class PublicPageRoutesTests
{
    private readonly CustomWebApplicationFactory _factory;
    public PublicPageRoutesTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
    });

    public static IEnumerable<object[]> ProductSlugs()
    {
        string[] slugs =
        {
            "pms", "channel-manager", "housekeeping", "pos", "kot", "booking-engine",
            "finance-revenue", "reviews-manager", "b2b-stay", "ota-management",
            "google-hotel-ads", "meta-search", "website-builder", "payment-gateway",
            "pms-apis", "ai-tools"
        };
        foreach (var slug in slugs) yield return new object[] { slug };
    }

    public static IEnumerable<object[]> SolutionSlugs()
    {
        string[] slugs =
        {
            "hotels-resorts", "boutique-properties", "vacation-rentals",
            "hostels", "guest-houses", "travel-agencies"
        };
        foreach (var slug in slugs) yield return new object[] { slug };
    }

    public static IEnumerable<object[]> LegalPages()
    {
        string[] pages = { "terms-of-use.html", "privacy-policy.html", "refund-and-cancellation.html" };
        foreach (var page in pages) yield return new object[] { page };
    }

    [Theory]
    [MemberData(nameof(ProductSlugs))]
    public async Task Product_page_resolves_not_404(string slug)
    {
        var response = await NewClient().GetAsync($"/products/{slug}.html");
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [MemberData(nameof(SolutionSlugs))]
    public async Task Solution_page_resolves_not_404(string slug)
    {
        var response = await NewClient().GetAsync($"/solutions/{slug}.html");
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [MemberData(nameof(LegalPages))]
    public async Task Legal_page_resolves_not_404(string page)
    {
        var response = await NewClient().GetAsync($"/{page}");
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing.html")]
    [InlineData("/reseller.html")]
    [InlineData("/contact.html")]
    [InlineData("/blog.html")]
    [InlineData("/about.html")]
    [InlineData("/calculator.html")]
    [InlineData("/article-ai-tools.html")] // seeded blog post, exercises BlogController.Article
    public async Task Core_page_resolves_not_404(string path)
    {
        var response = await NewClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unknown_slug_still_returns_404_not_a_silent_200()
    {
        // Guards the other direction: BlogController.Article/ProductPage/
        // SolutionPage must genuinely 404 on a slug that isn't seeded, not
        // fall through to some default view.
        var client = NewClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/products/does-not-exist.html")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/solutions/does-not-exist.html")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/this-slug-was-never-seeded.html")).StatusCode);
    }

    [Fact]
    public async Task Product_page_is_served_by_the_CMS_not_a_shadowing_static_file()
    {
        // Regression guard for the exact bug fixed earlier this session:
        // a static file left in wwwroot/products/ would be served instead
        // of the CmsPage, silently disconnecting the admin Pages editor
        // from the page. The CMS-rendered Page.cshtml always includes the
        // shared TopNav/SiteFooter markup; a shadowing static file (a
        // frozen copy from before the CMS migration) would not.
        var html = await NewClient().GetStringAsync("/products/pms.html");
        Assert.Contains("footer__nav-cols", html);
    }
}
