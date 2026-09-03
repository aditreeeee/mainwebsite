using Microsoft.Extensions.DependencyInjection;
using eGlobeSolutions.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Exercises the actual [Authorize] gate on the admin area through the real
/// pipeline: cookie auth, role policies (AdminOnly / SuperAdminOnly), and
/// the anonymous-access boundary. These are the edges that matter most for
/// an admin panel, a role bug here means either a locked-out admin or an
/// under-privileged user reaching something they shouldn't.
/// </summary>
[Collection("Integration")]
public class AdminAuthorizationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminAuthorizationTests(CustomWebApplicationFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = true,
    });

    [Fact]
    public async Task Anonymous_request_to_admin_area_is_redirected_to_login_not_served()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false, // inspect the redirect itself, don't follow it
        });

        var response = await client.GetAsync("/admin/blog");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/account/login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Wrong_password_does_not_grant_access_to_the_admin_area()
    {
        // Uses a dedicated throwaway user rather than the shared seeded
        // SuperAdmin: a wrong-password attempt increments Identity's
        // AccessFailedCount (lockout after 5), and other tests in this class
        // depend on the SuperAdmin account staying usable throughout the run.
        const string email = "integration-tests-wrongpw@eglobe-solutions.com";
        const string realPassword = "Test-WrongPw-Pass!2026";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByEmailAsync(email) is null)
            {
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Test Wrong Password User", IsActive = true };
                var createResult = await userManager.CreateAsync(user, realPassword);
                Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(user, ApplicationRole.Names.SalesAgent);
            }
        }

        var client = NewClient();

        var ok = await TestAuthHelper.TryLoginAsync(client, email, "definitely-the-wrong-password");
        Assert.False(ok);

        // No auth cookie should have been issued, admin/blog must still redirect to login.
        var afterFailedLogin = await client.GetAsync("/admin/blog");
        var finalPath = afterFailedLogin.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        Assert.Contains("/account/login", finalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Valid_superadmin_login_can_reach_an_AdminOnly_page()
    {
        var client = NewClient();
        await TestAuthHelper.LoginAsSuperAdminAsync(client);

        var response = await client.GetAsync("/admin/blog");

        response.EnsureSuccessStatusCode();
        var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        Assert.DoesNotContain("/account/login", finalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesAgent_role_is_blocked_from_a_SuperAdminOnly_page()
    {
        // SalesAgent satisfies "AdminOnly" (any of the three roles) but must NOT
        // satisfy "SuperAdminOnly" (UsersController), which is the actual point
        // of having two separate policies rather than one.
        const string email = "integration-tests-salesagent@eglobe-solutions.com";
        const string password = "Test-SalesAgent-Pass!2026";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await userManager.FindByEmailAsync(email) is null)
            {
                var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Test Sales Agent", IsActive = true };
                var createResult = await userManager.CreateAsync(user, password);
                Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(user, ApplicationRole.Names.SalesAgent);
            }
        }

        var client = NewClient();
        var loggedIn = await TestAuthHelper.TryLoginAsync(client, email, password);
        Assert.True(loggedIn);

        // AdminOnly page: SalesAgent is explicitly allowed.
        var blogResponse = await client.GetAsync("/admin/blog");
        blogResponse.EnsureSuccessStatusCode();

        // SuperAdminOnly page: SalesAgent must be denied, not silently let through.
        var usersResponse = await client.GetAsync("/admin/users");
        var finalPath = usersResponse.RequestMessage?.RequestUri?.AbsolutePath ?? "";
        Assert.True(
            usersResponse.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            finalPath.Contains("/account/denied", StringComparison.OrdinalIgnoreCase),
            $"Expected SalesAgent to be denied /admin/users, got {usersResponse.StatusCode} at {finalPath}.");
    }
}
