using eGlobeSolutions.Domain.Entities;
using eGlobeSolutions.Infrastructure.Identity;
using eGlobeSolutions.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eGlobeSolutions.Web;

/// <summary>
/// Checked once at startup, after DbInitializer has migrated/seeded: logs a
/// clear, structured warning for each piece of required-in-production
/// configuration that's missing, instead of the previous silent failure
/// mode (DbInitializer just skips seeding a SuperAdmin if the config is
/// blank, and SmtpEmailSender just fails enquiry notifications one at a
/// time with no single place that says why). This never throws, missing
/// SMTP or a missing seed account are recoverable/expected in some
/// deployments (e.g. an admin already exists from a prior deploy), the
/// point is making the gap visible in the logs rather than invisible.
/// </summary>
public static class StartupValidation
{
    public static void Run(IServiceProvider services, IWebHostEnvironment env, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        CheckAdminAccess(userManager, config, env, logger).GetAwaiter().GetResult();
        CheckSmtp(db, env, logger);
    }

    private static async Task CheckAdminAccess(UserManager<ApplicationUser> userManager, IConfiguration config, IWebHostEnvironment env, ILogger logger)
    {
        if (await userManager.Users.AnyAsync())
        {
            return; // At least one admin account already exists, panel is reachable.
        }

        var hasSeedConfig = !string.IsNullOrWhiteSpace(config["Seed:SuperAdminEmail"])
            && !string.IsNullOrWhiteSpace(config["Seed:SuperAdminPassword"]);

        if (!hasSeedConfig)
        {
            if (env.IsProduction())
            {
                logger.LogCritical(
                    "STARTUP CONFIG: no admin account exists and no SuperAdmin seed credentials are configured " +
                    "(Seed__SuperAdminEmail / Seed__SuperAdminPassword environment variables). " +
                    "/admin is unreachable until these are set and the app restarts.");
            }
            else
            {
                logger.LogWarning(
                    "STARTUP CONFIG: no admin account exists and no SuperAdmin seed credentials are configured. " +
                    "/admin will be unreachable, set Seed:SuperAdminEmail / Seed:SuperAdminPassword (user-secrets in dev).");
            }
        }
    }

    private static void CheckSmtp(AppDbContext db, IWebHostEnvironment env, ILogger logger)
    {
        string? Get(string key) => db.SiteSettings.AsNoTracking().FirstOrDefault(s => s.Key == key)?.Value;

        var host = Get(SiteSettingKeys.SmtpHost);
        var username = Get(SiteSettingKeys.SmtpUsername);
        var password = Get(SiteSettingKeys.SmtpPassword);
        var fromEmail = Get(SiteSettingKeys.SmtpFromEmail);

        var configured = new[] { host, username, password, fromEmail };
        var anySet = configured.Any(v => !string.IsNullOrWhiteSpace(v));
        var allSet = configured.All(v => !string.IsNullOrWhiteSpace(v));

        if (!allSet)
        {
            var level = env.IsProduction() && anySet
                ? LogLevel.Warning // partially configured in prod: likely a real mistake
                : LogLevel.Information; // unconfigured entirely: probably deliberate, don't alarm
            logger.Log(level,
                "STARTUP CONFIG: SMTP is not fully configured (Host/Username/Password/FromEmail in /admin/settings). " +
                "New-enquiry email notifications will not be sent until all four are set.");
        }
    }
}
