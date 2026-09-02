using eGlobeSolutions.Infrastructure.DependencyInjection;
using eGlobeSolutions.Infrastructure.Persistence;
using eGlobeSolutions.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IEnquiryService, EnquiryService>();
builder.Services.AddScoped<ICalculatorPricingService, CalculatorPricingService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

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

// Serve the existing static site (index.html, pricing.html, css/, js/, ...)
// unchanged from wwwroot, per the "preserve current frontend design"
// requirement. UseDefaultFiles makes "/" resolve to index.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

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

// ---------- DB migrate + seed on startup ----------
await DbInitializer.InitializeAsync(app.Services);

app.Run();
