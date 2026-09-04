using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Exercises the SiteSettings business logic through the real admin pipeline:
/// the upsert-by-key save path, and the specific "blank SMTP password field
/// means leave the stored value alone" rule that keeps a routine settings
/// save from silently wiping a working SMTP credential.
/// </summary>
[Collection("Integration")]
public class SettingsManagementTests
{
    private readonly CustomWebApplicationFactory _factory;

    public SettingsManagementTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    private static Dictionary<string, string> BaseForm(string siteName) => new()
    {
        ["SiteName"] = siteName,
        ["Phone"] = "+91-9818880480",
        ["Email"] = "support@eglobe-solutions.com",
        ["WhatsAppNumber"] = "",
        ["CallUsNumbers"] = "",
        ["BusinessHours"] = "",
        ["FacebookUrl"] = "",
        ["YoutubeUrl"] = "",
        ["LinkedInUrl"] = "",
        ["AppStoreUrl"] = "",
        ["GooglePlayUrl"] = "",
        ["FooterCopyright"] = "",
        ["ThemePrimaryColor"] = "#0b2a4a",
        ["ThemeSecondaryColor"] = "#e0a548",
        ["SmtpHost"] = "smtp.example.com",
        ["SmtpPort"] = "587",
        ["SmtpUsername"] = "integration-tests@eglobe-solutions.com",
        ["SmtpEnableSsl"] = "true",
        ["SmtpFromEmail"] = "no-reply@eglobe-solutions.com",
        ["SmtpFromName"] = "eGlobe Solutions",
        ["SmtpNotifyEmail"] = "leads@eglobe-solutions.com",
        ["SmtpNotifyOnEnquiry"] = "true",
    };

    [Fact]
    public async Task Saving_settings_persists_values_as_SiteSetting_rows()
    {
        var client = await NewLoggedInClientAsync();
        var settingsPage = await client.GetAsync("/admin/settings");
        settingsPage.EnsureSuccessStatusCode();
        var token = await TestAuthHelper.ExtractAntiForgeryTokenAsync(settingsPage);

        var uniqueSiteName = $"Integration Test Site {Guid.NewGuid():N}";
        var form = BaseForm(uniqueSiteName);
        form["__RequestVerificationToken"] = token;

        var response = await client.PostAsync("/admin/settings", new FormUrlEncodedContent(form));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.SiteSettings.SingleOrDefaultAsync(s => s.Key == SiteSettingKeys.SiteName);

        Assert.NotNull(saved);
        Assert.Equal(uniqueSiteName, saved!.Value);
    }

    [Fact]
    public async Task Saving_settings_with_a_blank_SMTP_password_field_leaves_the_stored_password_untouched()
    {
        var client = await NewLoggedInClientAsync();

        // First save: sets a real SMTP password.
        var firstPage = await client.GetAsync("/admin/settings");
        var firstToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(firstPage);
        var firstForm = BaseForm($"Integration Test Site {Guid.NewGuid():N}");
        firstForm["__RequestVerificationToken"] = firstToken;
        firstForm["SmtpPassword"] = "Correct-Horse-Battery-Staple-2026";
        var firstResponse = await client.PostAsync("/admin/settings", new FormUrlEncodedContent(firstForm));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, firstResponse.StatusCode);

        // Second save: the SMTP password field is left blank, as the real
        // edit form always renders it (never round-tripping a stored secret).
        var secondPage = await client.GetAsync("/admin/settings");
        var secondToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(secondPage);
        var secondForm = BaseForm($"Integration Test Site {Guid.NewGuid():N}");
        secondForm["__RequestVerificationToken"] = secondToken;
        // SmtpPassword intentionally omitted.
        var secondResponse = await client.PostAsync("/admin/settings", new FormUrlEncodedContent(secondForm));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, secondResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedPassword = await db.SiteSettings.SingleAsync(s => s.Key == SiteSettingKeys.SmtpPassword);

        Assert.Equal("Correct-Horse-Battery-Staple-2026", storedPassword.Value);
    }

    [Fact]
    public async Task Settings_page_never_round_trips_the_stored_SMTP_password_into_the_form()
    {
        var client = await NewLoggedInClientAsync();

        var setPage = await client.GetAsync("/admin/settings");
        var setToken = await TestAuthHelper.ExtractAntiForgeryTokenAsync(setPage);
        var setForm = BaseForm($"Integration Test Site {Guid.NewGuid():N}");
        setForm["__RequestVerificationToken"] = setToken;
        setForm["SmtpPassword"] = "Another-Secret-Value-2026";
        await client.PostAsync("/admin/settings", new FormUrlEncodedContent(setForm));

        var reloadedPage = await client.GetAsync("/admin/settings");
        reloadedPage.EnsureSuccessStatusCode();
        var html = await reloadedPage.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Another-Secret-Value-2026", html);
    }
}
