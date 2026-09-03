using System.Threading.RateLimiting;
using eGlobeSolutions.Infrastructure.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Services;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllersWithViews()
    // Requirement: Razor views must not be precompiled, so admins/devs can
    // edit .cshtml files and see changes without a rebuild.
    .AddRazorRuntimeCompilation()
    // Calculator DTOs use enums (CalculatorPlanType, ...) serialized as strings
    // so the JSON payloads stay readable; this lets [FromBody] bind them back.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.SuperAdmin,
            eGlobeSolutions.Infrastructure.Identity.ApplicationRole.Names.ContentEditor,
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
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

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

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in the
// integration test project (standard ASP.NET Core pattern).
public partial class Program { }
