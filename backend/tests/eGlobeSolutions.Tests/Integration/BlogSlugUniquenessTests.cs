using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// The blog has two layers of slug-uniqueness protection: the admin
/// controller checks before insert (BlogPostsController.Create/Edit) and,
/// as defense in depth, a real unique filtered index in SQL Server
/// (BlogPostConfiguration). These tests go through the actual admin HTTP
/// form flow (not calling the DbContext directly) so both layers are
/// exercised the way a real editor hitting the admin UI would.
/// </summary>
[Collection("Integration")]
public class BlogSlugUniquenessTests
{
    private readonly CustomWebApplicationFactory _factory;

    public BlogSlugUniquenessTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> NewLoggedInAdminClientAsync()
    {
        // AllowAutoRedirect is off so the Create/Edit POST assertions below can
        // see the raw 302 (success) vs 200 (validation error, form re-rendered)
        // response instead of a followed redirect masking that distinction.
        // TestAuthHelper's login works correctly either way.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        await TestAuthHelper.LoginAsSuperAdminAsync(client);
        return client;
    }

    private static Dictionary<string, string> MinimalPostForm(string title, string slug, string token) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Title"] = title,
        ["Slug"] = slug,
        ["Category"] = "Guide",
        ["Excerpt"] = "Test excerpt for integration test.",
        ["Body"] = "<p>Test body.</p>",
        ["AuthorName"] = "Test Author",
        ["AuthorRole"] = "Product",
        ["ReadTimeMinutes"] = "5",
        ["PublishedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["SortOrder"] = "0",
        ["IsPublished"] = "true",
    };

    private async Task<HttpResponseMessage> CreatePostAsync(HttpClient client, string title, string slug)
    {
        var createPage = await client.GetAsync("/admin/blog/create");
        createPage.EnsureSuccessStatusCode();
        var token = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage);

        return await client.PostAsync("/admin/blog/create",
            new FormUrlEncodedContent(MinimalPostForm(title, slug, token)));
    }

    [Fact]
    public async Task Creating_a_post_with_a_brand_new_slug_succeeds()
    {
        var client = await NewLoggedInAdminClientAsync();
        var slug = $"integration-test-unique-{Guid.NewGuid():N}";

        var response = await CreatePostAsync(client, "Unique Slug Test", slug);

        // Successful create redirects to Index; a validation failure re-renders Create (200).
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.BlogPosts.CountAsync(p => p.Slug == slug));
    }

    [Fact]
    public async Task Creating_a_second_post_with_a_duplicate_slug_is_rejected_not_500()
    {
        var client = await NewLoggedInAdminClientAsync();
        var slug = $"integration-test-duplicate-{Guid.NewGuid():N}";

        var first = await CreatePostAsync(client, "First Post", slug);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, first.StatusCode);

        var second = await CreatePostAsync(client, "Second Post, Same Slug", slug);

        // Must be handled gracefully (200, re-rendered form with an error),
        // never a 500 from an unhandled unique-index violation.
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("already used", body, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.BlogPosts.CountAsync(p => p.Slug == slug)); // still exactly one row
    }

    [Fact]
    public async Task Slugs_are_normalized_so_casing_and_spacing_still_collide()
    {
        var client = await NewLoggedInAdminClientAsync();
        var baseSlug = $"integration-test-normalize-{Guid.NewGuid():N}";

        var first = await CreatePostAsync(client, "Normalize Base", baseSlug.Replace('-', ' '));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, first.StatusCode);

        // Same slug, different case and spacing, should normalize to the same value and collide.
        var second = await CreatePostAsync(client, "Normalize Collide", baseSlug.Replace('-', ' ').ToUpperInvariant());

        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("already used", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_posts_with_no_slug_at_all_are_both_allowed_teaser_only()
    {
        // BlogPost.Slug is nullable, the unique index has a "WHERE Slug IS NOT NULL"
        // filter specifically so multiple teaser-only posts (no dedicated page) don't
        // collide with each other on NULL. Confirms that filter actually works.
        var client = await NewLoggedInAdminClientAsync();

        var first = await CreatePostAsync(client, $"No Slug A {Guid.NewGuid():N}", "");
        var second = await CreatePostAsync(client, $"No Slug B {Guid.NewGuid():N}", "");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, second.StatusCode);
    }
}
