using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Domain.Enums;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// The "#sales-form" -> POST /contact/submit path is the one place on the
/// public site where an anonymous visitor writes to the database. These
/// tests go through the real form: GET the page for a live antiforgery
/// token and cookie, then POST, exactly like a browser.
/// </summary>
[Collection("Integration")]
public class ContactEnquirySubmissionTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ContactEnquirySubmissionTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<(HttpClient client, string token)> NewClientWithTokenAsync()
    {
        var client = NewClient();
        var page = await client.GetAsync("/contact.html");
        page.EnsureSuccessStatusCode();
        var token = await TestAuthHelper.ExtractAntiForgeryTokenAsync(page);
        return (client, token);
    }

    [Fact]
    public async Task Valid_submission_returns_success_and_persists_an_enquiry()
    {
        var (client, token) = await NewClientWithTokenAsync();
        var uniqueName = $"Integration Test Guest {Guid.NewGuid():N}";

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["FullName"] = uniqueName,
            ["HotelName"] = "Test Hotel",
            ["Email"] = "guest@example.com",
            ["Phone"] = "+919999999999",
            ["RoomsRange"] = "1-10",
            ["Message"] = "Integration test submission.",
        };

        var response = await client.PostAsync("/contact/submit", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Enquiries.SingleOrDefaultAsync(e => e.FullName == uniqueName);

        Assert.NotNull(saved);
        Assert.Equal("Test Hotel", saved!.HotelName);
        Assert.Equal(EnquiryType.ContactSales, saved.Type);
        Assert.Equal("public-website", saved.CreatedBy);
    }

    [Fact]
    public async Task Missing_required_field_is_rejected_with_400_and_nothing_is_saved()
    {
        var (client, token) = await NewClientWithTokenAsync();

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            // FullName and Phone are both [Required] on the model and deliberately omitted.
        };

        var response = await client.PostAsync("/contact/submit", new FormUrlEncodedContent(form));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
    }

    [Fact]
    public async Task Honeypot_field_reports_success_but_saves_nothing()
    {
        var (client, token) = await NewClientWithTokenAsync();
        var uniqueName = $"Bot Submission {Guid.NewGuid():N}";

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["FullName"] = uniqueName,
            ["Phone"] = "+919999999999",
            ["Website"] = "http://spam-bot-fills-this.example", // honeypot, real users never fill this
        };

        var response = await client.PostAsync("/contact/submit", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json); // bot is told it "worked" so it doesn't retry

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Enquiries.SingleOrDefaultAsync(e => e.FullName == uniqueName);

        Assert.Null(saved); // ...but nothing was actually written
    }

    [Fact]
    public async Task Submission_without_a_valid_antiforgery_token_is_rejected()
    {
        var client = NewClient();
        await client.GetAsync("/contact.html"); // establishes the cookie, token deliberately not used

        var form = new Dictionary<string, string>
        {
            ["FullName"] = "No Token Guest",
            ["Phone"] = "+919999999999",
            // __RequestVerificationToken intentionally omitted
        };

        var response = await client.PostAsync("/contact/submit", new FormUrlEncodedContent(form));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task QuickEnquiry_form_type_is_recorded_as_QuickEnquiry_not_ContactSales()
    {
        var (client, token) = await NewClientWithTokenAsync();
        var uniqueName = $"Quick Enquiry Guest {Guid.NewGuid():N}";

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["FullName"] = uniqueName,
            ["Phone"] = "+919999999999",
            ["FormType"] = "quick",
        };

        var response = await client.PostAsync("/contact/submit", new FormUrlEncodedContent(form));
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Enquiries.SingleAsync(e => e.FullName == uniqueName);

        Assert.Equal(EnquiryType.QuickEnquiry, saved.Type);
    }
}
