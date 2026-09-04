using System.Threading.RateLimiting;
using eGlobeSolutions.Infrastructure.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using Serilog;

// Structured logging replaces the bare console logger everywhere: request
// path/status/duration on every request (UseSerilogRequestLogging below),
// plus every ILogger<T> call elsewhere in the app (DbInitializer's startup
// validation, EmailSender failures, etc.) now goes through the same
// structured pipeline instead of being invisible in production. Configured
// up front, before WebApplication.CreateBuilder, so startup failures during
// host build are captured too, not just failures after DI is ready.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    // Daily rolling file, 31 days retained, under the app's own directory so
    // it works the same in any hosting environment without extra config.
    .WriteTo.File("logs/eglobe-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31)
    .CreateLogger();

try
{

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ---------- Services ----------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IEnquiryService, EnquiryService>();
builder.Services.AddScoped<ICalculatorPricingService, CalculatorPricingService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Public, unauthenticated POST endpoints (contact/submit, reseller/submit,
// calculator/calculate) had no defense against being hammered. A fixed
// window per client IP is enough to stop casual abuse/scraping without
// affecting a real visitor filling out one form.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("PublicForms", opt =>
    {
        opt.PermitLimit = 8;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    // The calculator recalculates on every field edit (200ms debounce), a
    // real visitor configuring a quote can legitimately fire this dozens of
    // times in a minute, so it gets a much higher ceiling than a one-shot
    // lead form, just enough to stop a scripted hammering loop.
    options.AddFixedWindowLimiter("CalculatorCalculate", opt =>
    {
        opt.PermitLimit = 60;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks();

// Public content caching: every controller marked with [OutputCache] below
// serves cached HTML/JSON for a short window instead of hitting the DB on
// every request. Deliberately NOT applied to Home or Contact, both embed
// @Html.AntiForgeryToken() forms, caching that HTML would serve one
// visitor's antiforgery token to every other visitor, and their POSTs
// would then fail CSRF validation. Nothing under /admin is cached, admin
// content must always reflect the latest edit immediately after save.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("PublicContent", policy => policy
        .Expire(TimeSpan.FromMinutes(5))
        .SetVaryByHost(true));
});

builder.Services.AddControllersWithViews()
    // Requirement: Razor views must not be precompiled, so admins/devs can
    // edit .cshtml files and see changes without a rebuild.
    .AddRazorRuntimeCompilation()
    // Calculator DTOs use enums (CalculatorPlanType, ...) serialized as strings
    // so the JSON payloads stay readable; this lets [FromBody] bind them back.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddAuthorization(options =>
{
    // "AdminOnly" was previously used on every admin controller, collapsing
    // the three documented roles (see ApplicationRole.Names / DbInitializer's
    // role descriptions: SuperAdmin "full access", ContentEditor "manages
    // site content and pricing", SalesAgent "manages Contact Sales and
    // Reseller enquiries") into one undifferentiated tier. In practice that
    // meant a SalesAgent account, whose whole job is the Enquiries queue,
    // could also edit Settings (SMTP credentials, theme colors) and inject
    // arbitrary HTML/JS into any public page via Pages/BlogPosts (Body is
    // rendered with Html.Raw). Split into policies that actually match the
    // documented boundaries; "AdminOnly" now means "authenticated admin of
    // any role", kept only for genuinely role-agnostic read/overview pages
    // (Dashboard, ActivityLog).
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SuperAdmin,
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.ContentEditor,
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SalesAgent));

    // Site content: pages, blog, homepage/reseller content blocks, pricing
    // plans, navigation menus, SEO metadata, media library, FAQs, the
    // calculator's pricing catalog. SalesAgent has no business here.
    options.AddPolicy("ContentManage", policy =>
        policy.RequireRole(
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SuperAdmin,
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.ContentEditor));

    // The Contact Sales / Reseller enquiries queue, SalesAgent's actual job.
    // ContentEditor has no legitimate reason to see lead data.
    options.AddPolicy("EnquiriesManage", policy =>
        policy.RequireRole(
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SuperAdmin,
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SalesAgent));

    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireRole(eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SuperAdmin));
});

var app = builder.Build();

// ---------- Pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Renders a branded page for any response that reaches the end of the
// pipeline with an error status and no body yet (404s from routing/CmsPage
// lookups, 403s from an authorization policy, etc.), instead of the blank
// status-only response ASP.NET Core sends by default. Re-executes the
// request against /error/{code}, so the original status code is preserved.
app.UseStatusCodePagesWithReExecute("/error/{0}");

// One structured log line per request (method, path, status, elapsed ms),
// this is what actually makes production traffic/errors visible instead of
// only whatever ILogger calls individual controllers happen to make.
app.UseSerilogRequestLogging();

// Baseline security response headers on every request. CSP is intentionally
// permissive on 'unsafe-inline' for style/script, this site relies heavily
// on inline style="" attributes and inline JSON-LD <script> blocks
// throughout, tightening that needs a real refactor (moving inline styles
// to CSS, nonce-ing scripts), not a one-line change, tracked separately.
// The other headers carry no such tradeoff and are unconditionally safe.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "img-src 'self' data: https:; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "script-src 'self' 'unsafe-inline'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'");
    await next();
});

// Serve the existing static site (index.html, pricing.html, css/, js/, ...)
// unchanged from wwwroot, per the "preserve current frontend design"
// requirement. UseDefaultFiles makes "/" resolve to index.html.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.GetTypedHeaders();
        // Every <link>/<script> tag for our own CSS/JS carries
        // asp-append-version="true", so its URL's "?v=" query string changes
        // the instant the file's content changes, safe to cache for a year.
        // Everything else served from wwwroot (unversioned images, the
        // static legal/about pages) gets a much shorter cache instead, since
        // its URL never changes even when its content does.
        if (ctx.Context.Request.Query.ContainsKey("v"))
        {
            var cc = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(365) };
            cc.Extensions.Add(new NameValueHeaderValue("immutable"));
            headers.CacheControl = cc;
        }
        else
        {
            headers.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromHours(1) };
        }
    }
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache();

// Admin area routed under /admin/{controller}/{action}, kept separate from
// the public site so no admin route can be reached accidentally.
app.MapControllerRoute(
    name: "admin",
    pattern: "admin/{controller=Dashboard}/{action=Index}/{id?}",
    defaults: new { area = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Uptime/load-balancer probe. No DB check, deliberately cheap and fast.
app.MapHealthChecks("/health");

// ---------- DB migrate + seed on startup ----------
await DbInitializer.InitializeAsync(app.Services);

// ---------- Startup config validation ----------
// Fails loudly (in the structured log, not silently) if required production
// config is missing, rather than the previous behaviour of silently skipping
// seeding and leaving /admin unreachable with no signal why. Doesn't throw:
// a missing SuperAdmin seed is fine on a redeploy where an admin already
// exists, but it must show up in the logs either way.
StartupValidation.Run(app.Services, app.Environment, app.Logger);

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the top-level Program for WebApplicationFactory<Program> in the
// integration test project (standard ASP.NET Core pattern).
public partial class Program { }
