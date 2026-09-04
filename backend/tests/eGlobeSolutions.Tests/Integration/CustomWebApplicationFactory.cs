using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// Boots the real ASP.NET Core app (Program.cs) against a dedicated
/// SQL Server database (eGlobeSolutionsCms_IntegrationTests, distinct from
/// the dev DB), running the app's actual startup path, DbInitializer
/// migrate + seed included. This is a genuine end-to-end pipeline, not a
/// mocked host: real Identity, real cookie auth, real antiforgery, real
/// rate limiting middleware, real EF Core against real SQL Server.
///
/// Requires a reachable SQL Server instance at "localhost" with Windows
/// auth (matches the app's own Development connection string). Skips
/// nothing silently, if SQL Server isn't reachable the tests fail loudly
/// rather than pretending to pass against a mock.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestSuperAdminEmail = "integration-tests-admin@eglobe-solutions.com";
    public const string TestSuperAdminPassword = "Test-Admin-Pass!2026";

    public CustomWebApplicationFactory()
    {
        // IMPORTANT: Program.cs reads builder.Configuration.GetConnectionString("Default")
        // eagerly, synchronously, at the top of its top-level statements (AddInfrastructure
        // call), before WebApplicationFactory's ConfigureWebHost/ConfigureAppConfiguration
        // hooks ever get a chance to run. An override added there arrives too late and is
        // silently ignored, the app ends up using the real appsettings.Development.json
        // connection string, i.e. the actual dev database, not an isolated test one.
        // Process environment variables are read by WebApplication.CreateBuilder itself at
        // construction time (AddEnvironmentVariables, double-underscore = nested key), so
        // setting them here, in the constructor, before the host is ever built, is what
        // actually takes effect. This must run before any CreateClient()/Server access.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default",
            "Server=localhost;Database=eGlobeSolutionsCms_IntegrationTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("Seed__SuperAdminEmail", TestSuperAdminEmail);
        Environment.SetEnvironmentVariable("Seed__SuperAdminPassword", TestSuperAdminPassword);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    // The admin auth cookie is Secure-only (CookieSecurePolicy.Always, see
    // ServiceCollectionExtensions.ConfigureApplicationCookie), correctly so,
    // the app is HTTPS-only everywhere real traffic reaches it. TestServer's
    // default client base address is http://localhost though, under which a
    // Secure cookie set by login is silently dropped by HttpClient before
    // the next request, every "should still be logged in" assertion sees a
    // 302-to-login instead. Pointing the default client at https://localhost
    // makes TestServer mark the request pipeline HTTPS end to end, so the
    // cookie round-trips exactly like it does in production, this is a test
    // fixture fix, not a relaxation of the real cookie policy.
    protected override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://localhost");
    }
}
