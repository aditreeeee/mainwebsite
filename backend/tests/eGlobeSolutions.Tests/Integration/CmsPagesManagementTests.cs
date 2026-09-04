using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Exercises the actual CmsPage business logic through the real admin
/// pipeline: create/edit/delete, slug normalization, and the slug-collision
/// rule that keeps a Page from clobbering a BlogPost's URL (both live at
/// "/{slug}.html"). Logged in as SuperAdmin throughout, this class is about
/// the CRUD/validation logic itself, not the ContentManage/EnquiriesManage
/// authorization boundary (see AdminAuthorizationTests for that).
/// </summary>
[Collection("Integration")]
public class CmsPagesManagementTests
{
    private readonly CustomWebApplicationFactory _factory;

    public CmsPagesManagementTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    private async Task<HttpClient> NewLoggedInClientAsync()
    {
        var client = NewClient();
        await TestAuthHelper.LoginAsSuperAdminAsync(client);
        return client;
    }

    private static Dictionary<string, string> CreateForm(string title, string slug, string body = "<p>Body</p>") => new()
    {
        ["Title"] = title,
        ["Slug"] = slug,
        ["Subtitle"] = "",
        ["Body"] = body,
        ["UseCustomHero"] = "false",
        ["IsPublished"] = "true",
        ["SortOrder"] = "0",
        ["MetaTitle"] = "",
        ["MetaDescription"] = "",
        ["MetaKeywords"] = "",
    };

    [Fact]
    public async Task Creating_a_page_persists_it_and_normalizes_the_slug()
    {
        var client = await NewLoggedInClientAsync();
        var createPage = await client.GetAsync("/admin/pages/create");
        createPage.EnsureSuccessStatusCode();
        var token = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage);

        var uniqueTitle = $"Integration Test Page {Guid.NewGuid():N}";
        var rawSlug = $"  Integration Test {Guid.NewGuid():N}  "; // deliberately messy: spaces, mixed case
        var form = CreateForm(uniqueTitle, rawSlug);
        form["__RequestVerificationToken"] = token;

        var response = await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(form));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/pages", response.Headers.Location?.ToString() ?? "");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.CmsPages.SingleOrDefaultAsync(p => p.Title == uniqueTitle);

        Assert.NotNull(saved);
        var expectedSlug = rawSlug.Trim().ToLowerInvariant().Replace(" ", "-");
        Assert.Equal(expectedSlug, saved!.Slug);
    }

    [Fact]
    public async Task Creating_a_page_with_a_slug_already_used_by_another_page_is_rejected()
    {
        var client = await NewLoggedInClientAsync();

        var firstTitle = $"Integration Slug Owner {Guid.NewGuid():N}";
        var sharedSlug = $"integration-shared-slug-{Guid.NewGuid():N}";

        var createPage1 = await client.GetAsync("/admin/pages/create");
        var token1 = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage1);
        var form1 = CreateForm(firstTitle, sharedSlug);
        form1["__RequestVerificationToken"] = token1;
        var response1 = await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(form1));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response1.StatusCode);

        var secondTitle = $"Integration Slug Collider {Guid.NewGuid():N}";
        var createPage2 = await client.GetAsync("/admin/pages/create");
        var token2 = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage2);
        var form2 = CreateForm(secondTitle, sharedSlug); // same slug, different title
        form2["__RequestVerificationToken"] = token2;
        var response2 = await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(form2));

        // Rejected: re-renders the Create view (200), not a redirect to Index.
        Assert.Equal(System.Net.HttpStatusCode.OK, response2.StatusCode);
        var html = await response2.Content.ReadAsStringAsync();
        Assert.Contains("already used", html, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var colliderCount = await db.CmsPages.CountAsync(p => p.Title == secondTitle);
        Assert.Equal(0, colliderCount);
    }

    [Fact]
    public async Task Editing_a_page_updates_its_fields_and_stamps_UpdatedAtUtc()
    {
        var client = await NewLoggedInClientAsync();

        var title = $"Integration Edit Target {Guid.NewGuid():N}";
        var slug = $"integration-edit-target-{Guid.NewGuid():N}";
        var createPage = await client.GetAsync("/admin/pages/create");
        var createToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage);
        var createForm = CreateForm(title, slug);
        createForm["__RequestVerificationToken"] = createToken;
        await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(createForm));

        int id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var created = await db.CmsPages.SingleAsync(p => p.Title == title);
            id = created.Id;
        }

        var editPage = await client.GetAsync($"/admin/pages/{id}/edit");
        editPage.EnsureSuccessStatusCode();
        var editToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(editPage);

        var updatedTitle = $"{title} (updated)";
        var editForm = CreateForm(updatedTitle, slug, "<p>Updated body</p>");
        editForm["Id"] = id.ToString();
        editForm["__RequestVerificationToken"] = editToken;

        var editResponse = await client.PostAsync($"/admin/pages/{id}/edit", new FormUrlEncodedContent(editForm));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, editResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await verifyDb.CmsPages.SingleAsync(p => p.Id == id);

        Assert.Equal(updatedTitle, updated.Title);
        Assert.Equal("<p>Updated body</p>", updated.Body);
        Assert.NotNull(updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Deleting_a_page_soft_deletes_it_rather_than_removing_the_row()
    {
        var client = await NewLoggedInClientAsync();

        var title = $"Integration Delete Target {Guid.NewGuid():N}";
        var slug = $"integration-delete-target-{Guid.NewGuid():N}";
        var createPage = await client.GetAsync("/admin/pages/create");
        var createToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage);
        var createForm = CreateForm(title, slug);
        createForm["__RequestVerificationToken"] = createToken;
        await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(createForm));

        int id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = (await db.CmsPages.SingleAsync(p => p.Title == title)).Id;
        }

        var indexPage = await client.GetAsync("/admin/pages");
        var deleteToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(indexPage);
        var deleteResponse = await client.PostAsync($"/admin/pages/{id}/delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = deleteToken }));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, deleteResponse.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await verifyDb.CmsPages.IgnoreQueryFilters().SingleAsync(p => p.Id == id);

        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAtUtc);
    }

    [Fact]
    public async Task Creating_a_page_without_a_slug_is_rejected_with_a_validation_error()
    {
        var client = await NewLoggedInClientAsync();
        var createPage = await client.GetAsync("/admin/pages/create");
        var token = await TestAuthHelper.ExtractAntiForgeryTokenAsync(createPage);

        var title = $"Integration No Slug {Guid.NewGuid():N}";
        var form = CreateForm(title, ""); // blank slug
        form["__RequestVerificationToken"] = token;

        var response = await client.PostAsync("/admin/pages/create", new FormUrlEncodedContent(form));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // re-renders Create, no redirect
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("required", html, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.CmsPages.CountAsync(p => p.Title == title));
    }
}
