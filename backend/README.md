# eGlobe Solutions — Backend / Admin CMS

.NET 8 / ASP.NET Core MVC backend for the existing static eGlobe Solutions
site, plus a WordPress-style admin panel covering every module from the
original scope: enquiries, pricing, homepage/reseller/contact content, blog
posts, FAQs, media, navigation, SEO metadata, site settings, users/roles
and the activity log. Every public page is now a database-backed Razor
view, none of the site is served as static HTML anymore except `404.html`.

## Stack

- .NET 8 / ASP.NET Core MVC, Razor views with **runtime compilation**
  (`RazorCompileOnBuild=false`, `AddRazorRuntimeCompilation()`) — views are
  not precompiled, matching the requirement.
- EF Core 8 + SQL Server, `Microsoft.AspNetCore.Identity` for admin auth/roles.
- Clean-ish layering: `Domain` (entities/enums, no framework deps) →
  `Infrastructure` (EF Core, Identity, DI wiring) → `Web` (MVC, controllers,
  views, `/admin` area).

```
backend/
  eGlobeSolutions.sln
  src/
    eGlobeSolutions.Domain/          entities, enums (Enquiry, BlogPost, ActivityLog, ...)
    eGlobeSolutions.Infrastructure/  AppDbContext, Identity, migrations, DI
    eGlobeSolutions.Web/
      Controllers/                  public site: Home, Pricing, Reseller, Contact, Blog
      ViewComponents/                TopNav, NavDock, SiteFooter (shared chrome)
      Areas/Admin/                  the admin panel (Dashboard, Enquiries, Pricing, Content,
                                     BlogPosts, Faqs, Media, Menus, Seo, Settings, Users, ActivityLog)
      wwwroot/                      404.html, css/, js/ — the only static leftovers
```

## What's built and working

- **Solution builds clean** (`dotnet build`, 0 errors); two EF Core
  migrations (`InitialCreate`, `AddCmsModules`) have been generated and
  reviewed. Every admin module below was also verified by temporarily
  flipping `RazorCompileOnBuild` on and rebuilding, catching real `.cshtml`
  compile errors (a couple of `@page` vs. `?page=` ambiguities), then
  flipping it back off since views must not be precompiled at runtime.
- **Admin auth**: cookie-based login at `/admin/account/login`, ASP.NET
  Identity with three seeded roles (`SuperAdmin`, `ContentEditor`,
  `SalesAgent`), lockout after 5 failed attempts, role-based authorization
  policies (`AdminOnly`, `SuperAdminOnly`).
- **Enquiries** (`/admin/enquiries`): Contact Sales + Reseller submissions in
  one table (`EnquiryType` distinguishes them), search/filter/sort/paginate,
  status workflow (`New → Contacted → Qualified → ProposalSent →
  Won/Lost/Spam`) with notes and follow-up date, soft delete + Trash/restore.
  Public endpoints `POST /contact/submit`, `POST /reseller/submit` validate
  server-side and store to SQL Server, with a honeypot field.
- **Pricing** (`/admin/pricing`): plans (name, badge, unit description,
  featured flag, CTA label/URL, sort order), each with an ordered feature
  bullet list, plus the module-comparison table rows (included/add-on/none
  per plan). This is **live**: `pricing.html` is now a database-backed Razor
  view (see below), so editing a plan here changes the public page immediately.
- **Page Content** (`/admin/content`): ordered, publishable content blocks
  (kicker/title/subtitle/body/CTA/image) grouped by page (`home`,
  `reseller`) and a section key, covering homepage-section management and
  reseller content management in one screen.
- **FAQs** (`/admin/faqs`): question/answer pairs grouped by page, feeds both
  the visible FAQ list and the page's FAQPage JSON-LD schema on Pricing.
- **Media Library** (`/admin/media`): real file upload (jpg/png/webp/gif/svg/pdf,
  10 MB cap) into `wwwroot/uploads`, tracked in `MediaAssets` with alt text,
  soft delete.
- **Navigation** (`/admin/menus`): menu items grouped by location (top bar,
  nav dock, footer columns), label/URL/new-tab/sort/publish.
- **SEO Metadata** (`/admin/seo`): per-page title/description/keywords/canonical/OG
  image, one row per page key (unique-constrained). Pricing's `<head>` reads
  from this table live; other pages are ready for the same once converted.
- **Site Settings** (`/admin/settings`): one typed form (not a raw key/value
  editor) over contact info, social links, business hours, app store links
  and footer copyright, stored as key/value rows so new settings can be
  added later without a migration. `CallUsNumbers` holds the landline
  numbers shown as a separate "Call Us: ..." line from the main mobile
  `Phone` number, slash-separated, each rendered as its own `tel:` link, in
  both the footer and Contact's "Get In Touch" sidebar.
- **Users & Roles** (`/admin/users`, SuperAdmin only): list, create, edit
  (name, active flag, role, password reset) admin accounts against the three
  fixed roles.
- **Activity Log** (`/admin/activitylog`): full paginated view of every
  `ActivityLog` row (enquiry views/status changes/deletes/restores today,
  and the pattern is ready for other modules to log into the same table).
- **`index.html`, `pricing.html` and `reseller.html` are all real
  database-backed Razor views now**, not static files:
  - `Controllers/HomeController.cs` + `Views/Home/Index.cshtml` — the exact
    original markup, CSS, JS and interactive dashboard/department-tab/product-modal
    widgets are untouched, but the announcement bar, hero copy + CTA, and
    every major section's kicker/heading/subheading/CTA (department
    workspaces, product ecosystem intro, ecosystem network, mobile app,
    testimonials intro, pricing teaser, final CTA) now come from
    `ContentBlock` rows (`PageKey="home"`), editable at `/admin/content?page=home`.
  - `Controllers/ResellerController.cs` + `Views/Reseller/Index.cshtml` — hero,
    all three partner-plan cards (badge/title/unit/bullets/CTA), the
    statement banner, the "who should apply" copy, all three benefit items
    and the final CTA are `ContentBlock` rows (`PageKey="reseller"`), editable
    at `/admin/content?page=reseller`. Feature bullets are stored as
    newline-separated text in `Body` and split into `<li>` items in the view,
    the same block shape as `home`, no new entity needed for a 3-bullet list.
  - `Controllers/PricingController.cs` + `Views/Pricing/Index.cshtml` — plans,
    comparison rows and FAQs, as before.
  - All three pages' `<title>`/meta description/keywords/canonical/OG image
    read from `SeoMetadata` (`PageKey` = `home`/`pricing`/`reseller`),
    editable at `/admin/seo`.
  - `Controllers/ContactController.cs` + `Views/Contact/Index.cshtml` — hero
    copy and the "What happens next" sidebar bullets are `ContentBlock` rows
    (`PageKey="contact"`), and the "Get In Touch" card (phone, email,
    business hours, socials) now reads from `SiteSettings` instead of being
    hardcoded a second time (previously the same contact info was
    hand-typed separately in this sidebar, in the footer, and in
    `#sales-form`'s honeypot markup, three places to update by hand).
  - The contact form now has a real antiforgery token: `@@Html.AntiForgeryToken()`
    renders a hidden `__RequestVerificationToken` field, `js/main.js` reads
    it and includes it in the `fetch()` body, and `ContactController.Submit`
    has `[ValidateAntiForgeryToken]` back on it. This closes the CSRF gap
    that existed while `contact.html` was still a static file (see the old
    "Known Phase-1 limitation" note below, now resolved).
  - Every block has a hardcoded fallback in the view (`?? "original copy"`),
    so if an admin deletes or unpublishes a block, the page still renders
    with the original static copy instead of a blank section.
- **`blog.html` and every article page (e.g. `article-ai-tools.html`) are
  database-backed Razor views too**, backed by a new `BlogPost` entity
  (`/admin/blog`), not another `ContentBlock` reuse, a real blog needs
  title/slug/category/excerpt/body/author/publish-date/read-time/featured/
  cover image/its own SEO fields, which don't fit the generic content-block
  shape:
  - `Controllers/BlogController.cs`: `GET /blog.html` lists the featured
    post plus a grid (same client-side category filter buttons as before);
    `GET /{slug}.html` renders any post that has both a `Slug` and a `Body`
    at its own URL, e.g. `article-ai-tools.html` now resolves to the
    `BlogPost` row with `Slug="article-ai-tools"` instead of a static file.
  - That `{slug}.html` route is a catch-all pattern, but ASP.NET Core's
    routing gives fully-literal routes (`pricing.html`, `contact.html`, the
    `blog.html` list page itself, etc.) higher precedence than a
    parameterized one, by design, not by registration order, so it only
    ever catches genuine post slugs, never shadows another page.
  - Posts without a `Slug`/`Body` render as non-clickable teaser cards in
    the grid (`href="#"`), matching what the original 6 placeholder cards
    on the static page already did, they never linked anywhere real either.
  - `/admin/blog` is full CRUD: title, slug (auto-lowercased, uniqueness
    checked), category, excerpt, HTML body, author, read time, publish
    date, featured/published flags, cover image, and its own optional SEO
    title/description/keywords (falls back to Title/Excerpt if left blank).
  - `wwwroot/index.html`, `wwwroot/pricing.html`, `wwwroot/reseller.html`,
    `wwwroot/contact.html`, `wwwroot/blog.html` and
    `wwwroot/article-ai-tools.html` were all removed so these routes aren't
    shadowed by the static file middleware; `UseDefaultFiles()`/`UseStaticFiles()`
    still serve `404.html`, `css/`, `js/` unchanged. Every page on the site
    is now a real Razor view except the 404 page.
- **Header/footer navigation and contact info are now database-driven on all
  three converted pages**, via three ViewComponents (each with a hardcoded
  fallback if the DB has no rows, same pattern as content blocks):
  - `TopNavViewComponent` — the topbar's Home/Pricing/Resellers links, from
    `MenuItems` where `Location="topbar"`.
  - `NavDockViewComponent` — same for the mobile nav-dock (`Location="nav-dock"`).
  - `SiteFooterViewComponent` — the entire `<footer>`: phone/email/business
    hours/copyright from `SiteSettings`, Facebook/YouTube/LinkedIn/WhatsApp
    links (WhatsApp is derived from the `WhatsAppNumber` setting as a
    `wa.me` link) from `SiteSettings`, App Store/Google Play links from
    `SiteSettings`, and the footer's Product/Company link columns from
    `MenuItems` (`Location="footer-product"` / `"footer-company"`).
  - Editing any of these in `/admin/menus` or `/admin/settings` now changes
    every page's header/footer at once, no per-page editing needed.
  - Fixed a real bug while wiring this up: the original `topbar`/`nav-dock`
    seed data pointed at `/pricing` and `/reseller`, which aren't routes
    (`PricingController`'s actual route is `pricing.html`, an attribute
    route, not MVC-conventional `/pricing`). That seed data was written but
    never actually rendered anywhere until this pass, so the mismatch never
    surfaced, fixed to `pricing.html`/`reseller.html`/`index.html` to match
    how the rest of the site links.
- **Removed the duplicate contact info block from the footer** on every
  page (static and converted): `footer__cta` had its own phone/email/website/hours
  lines directly below the "Talk to Sales" button, which duplicated the
  identical info already shown in `footer__strip` further down the same
  footer. Removed the `footer__cta` copy, kept `footer__strip` (now
  database-driven via `SiteFooterViewComponent`) as the single source.
- **Fixed a genuine contrast bug in `footer__cta p`**: `css/style.css` has a
  global `p{color:var(--ink-soft)}` rule (line 177) intended for body copy
  on light backgrounds, `var(--ink-soft)` is `#4B5563`, a dark slate grey.
  `.footer__cta p` never set its own `color`, so the "Talk to our team
  about the PMS..." paragraph inherited that dark grey against the navy
  (`#101935`) footer background, unreadably low contrast. Added
  `color:rgba(255,255,255,.7)` to `.footer__cta p` to match the rest of the
  footer's translucent-white text. This was a pre-existing bug in the
  original static CSS, not something introduced by the backend conversion.

## What's still not wired up (honest scope)

- **The interactive dashboard demo, department tabs, 16-card product grid +
  modal system, client/OTA logo strips and testimonial cards on `index.html`
  are still static markup**, not database content. These are UI/interaction
  structure driven by `js/main.js` (the product modal content, for instance,
  is a hardcoded lookup keyed by `data-modal` attributes), not editorial
  copy a WordPress-style admin would naturally expose. Turning them into
  admin-editable content would need a proper product-catalog entity (icon,
  description, bullet points, FAQ, demo widget per product) and a
  testimonials/media-list entity, neither of which was in the original module
  list, so it's flagged here rather than bolted on as a rushed extra.
- **The blog article's "On this page" table of contents was dropped** during
  conversion. The original static page hardcoded three anchor links
  (`#`, with `onclick="return false;"`, so they never actually scrolled
  anywhere even before conversion) matching that one article's three `<h2>`
  headings. That doesn't generalize to arbitrary `Body` HTML from the
  admin, so it was removed rather than shipped as another dead link. A real
  TOC would need to parse `<h2>` tags out of `Body` at render time, doable
  later if wanted.

## A real conflict I hit and how it's resolved: compat level 100 vs. pagination

You asked for **database compatibility level 100** (SQL Server 2008). It's
enforced as the first statement in the `InitialCreate` migration
(`ALTER DATABASE ... SET COMPATIBILITY_LEVEL = 100`), applied automatically
on `dotnet ef database update` / app startup.

The catch: EF Core 8's `Skip()`/`Take()` compiles to SQL Server's
`OFFSET ... FETCH NEXT`, which needs compatibility level **110+**. At level
100 that throws a SQL syntax error at runtime, so naive pagination would
have broken the Enquiries list page the first time it ran against a real
database.

**Resolved in `EnquiriesController.Index`**: instead of `Skip/Take` on the
entity query, it selects just the ordered `Id` column (one narrow query),
pages that id list in memory, then loads only that page's full rows by id.
No `OFFSET/FETCH` involved, compatible with level 100 as-is. Trade-off: it
reads one `int` per matching row to compute the page, even on rows outside
the current page — cheap at the scale this table will realistically reach.
If the enquiries table ever grows into the millions, the fix is to raise
compatibility level to 110+ and switch back to `Skip/Take`, not to keep
piling workarounds onto level 100.

This pattern (order → page ids → fetch by id) is now reused in
`ActivityLogController.Index` too. The other new admin list screens
(Pricing, Content, FAQs, Media, Menus, SEO) don't paginate server-side yet
since their row counts are naturally small (a handful of plans, sections,
FAQs); if any of them grow large enough to need it, follow the same pattern.

**Second round of the same problem, caught later against a real SQL Server:**
the "fetch that page's rows by id" step used `.Where(e => pageIds.Contains(e.Id))`.
EF Core 8 translates `List<int>.Contains()` to
`OPENJSON(@param) WITH ([value] int '$')`, and `OPENJSON` itself requires
compatibility level **130+**. At level 100 that throws `Incorrect syntax
near '$'`, which is exactly what broke both `/admin/enquiries` and
`/admin/activitylog` at runtime, both used the same `.Contains()` pattern.
This one couldn't be caught by `dotnet build` or the Razor compile-on-build
check, it only surfaces when the query actually runs against a real SQL
Server (this sandbox had none until a working local instance was found).

Fixed with `Extensions/QueryableExtensions.cs`, a `WhereIdIn` helper that
builds `e => e.Id == a || e.Id == b || ...` as an explicit expression tree
instead of calling `.Contains()` on a list. That compiles to a plain SQL OR
chain, which has no compatibility-level floor. Both controllers now use
`.WhereIdIn(e => e.Id, pageIds)` instead of `.Where(e => pageIds.Contains(e.Id))`.
Reuse this helper for any future "fetch this page's rows by a small id
list" code rather than reaching for `.Contains()` again, it's the same trap.

## Local setup

1. Install SQL Server / LocalDB and the EF tool: `dotnet tool install --global dotnet-ef`
2. Set credentials for the first admin account (don't commit real ones):
   ```
   cd src/eGlobeSolutions.Web
   dotnet user-secrets init
   dotnet user-secrets set "Seed:SuperAdminEmail" "you@eglobe-solutions.com"
   dotnet user-secrets set "Seed:SuperAdminPassword" "SomeStrongPassword!1"
   ```
   (or edit `appsettings.Development.json`, already pre-filled with a dev
   default, `admin@eglobe-solutions.com` / `ChangeMe!2026`, change before
   using this anywhere real).
3. Update `ConnectionStrings:Default` in `appsettings.json` to point at your
   SQL Server instance.
4. `dotnet run --project src/eGlobeSolutions.Web` — migrations apply and the
   SuperAdmin seeds automatically on startup.
5. Public site: `https://localhost:<port>/`
   Admin panel: `https://localhost:<port>/admin/account/login`

## CSRF on the public forms

`contact.html` is now a real Razor view, so `#sales-form` has a genuine
antiforgery token (`@@Html.AntiForgeryToken()`), `js/main.js` sends it with
the submission, and `ContactController.Submit` enforces it with
`[ValidateAntiForgeryToken]`.

`ResellerController.Submit` (`POST /reseller/submit`) still relies on the
honeypot field only, no page currently posts to it, the "Reseller /
Partnership" interest is captured through `#sales-form` on `contact.html`
instead (see the `EnquiryType` on the model it saves). If a dedicated
reseller-only form is ever added to `reseller.html`, give it the same
antiforgery token treatment as `contact.html` before wiring it up.

The admin panel itself (`/admin/...`) **is** fully CSRF-protected, every
POST there goes through a real Razor-rendered form with
`@Html.AntiForgeryToken()`.
