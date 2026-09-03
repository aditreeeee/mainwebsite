using System.Text.RegularExpressions;

namespace eGlobeSolutions.Tests.Integration;

internal static class TestAuthHelper
{
    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"",
        RegexOptions.Compiled);

    public static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(html);
        if (!match.Success)
            throw new InvalidOperationException("Could not find __RequestVerificationToken in response HTML.");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    /// <summary>Logs the given HttpClient in via the real /admin/account/login POST
    /// (cookie auth, antiforgery included), the same path a human admin goes
    /// through in a browser. Client must be created with HandleCookies = true so
    /// the auth cookie persists for later requests. Works whether the client
    /// follows redirects or not: a successful login redirects (302) to
    /// /admin/dashboard (or lands there, if redirects are auto-followed); a
    /// failed login re-renders the login view (200) at the login URL instead of
    /// redirecting anywhere.</summary>
    public static async Task<bool> TryLoginAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/admin/account/login");
        loginPage.EnsureSuccessStatusCode();
        var token = await ExtractAntiForgeryTokenAsync(loginPage);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
        };

        var response = await client.PostAsync("/admin/account/login", new FormUrlEncodedContent(form));

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            // Redirects not auto-followed: success redirects away from login.
            var location = response.Headers.Location?.ToString() ?? "";
            return !location.Contains("/account/login", StringComparison.OrdinalIgnoreCase);
        }

        response.EnsureSuccessStatusCode();
        var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        return !finalPath.Contains("/account/login", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task LoginAsSuperAdminAsync(HttpClient client)
    {
        var ok = await TryLoginAsync(client, CustomWebApplicationFactory.TestSuperAdminEmail, CustomWebApplicationFactory.TestSuperAdminPassword);
        if (!ok) throw new InvalidOperationException("Seeded SuperAdmin login failed unexpectedly.");
    }
}
