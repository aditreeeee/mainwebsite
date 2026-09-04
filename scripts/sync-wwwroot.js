// Copies build output (minified CSS/JS, optimized images) from the repo
// root into the .NET backend's wwwroot, so the ASP.NET app serves the same
// assets as the standalone static frontend. Run after build:css/js/images.
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const wwwroot = path.join(root, "backend", "src", "eGlobeSolutions.Web", "wwwroot");

const copies = [
  ["css/style.css", "css/style.css"],
  ["css/style.min.css", "css/style.min.css"],
  ["js/main.js", "js/main.js"],
  ["js/main.min.js", "js/main.min.js"],
  ["js/calculator.js", "js/calculator.js"],
  ["js/calculator.min.js", "js/calculator.min.js"],

  // Pages with no controller/DB content, wwwroot serves these as plain
  // static files (see Program.cs UseStaticFiles). Keep this list in sync
  // with reality: a page belongs here only if it has NO matching
  // [HttpGet] route in a Controller and NO ContentBlock/SeoMetadata rows
  // driving it, i.e. genuinely static, not CMS-editable.
  ["404.html", "404.html"],
];

for (const [src, dest] of copies) {
  const srcPath = path.join(root, src);
  const destPath = path.join(wwwroot, dest);
  if (!fs.existsSync(srcPath)) continue;
  fs.mkdirSync(path.dirname(destPath), { recursive: true });
  fs.copyFileSync(srcPath, destPath);
  console.log(`  ${src} -> wwwroot/${dest}`);
}

// Images: copy every file in assets/img/ (mirrors whatever's currently there).
const imgSrcDir = path.join(root, "assets", "img");
const imgDestDir = path.join(wwwroot, "assets", "img");
fs.mkdirSync(imgDestDir, { recursive: true });
for (const f of fs.readdirSync(imgSrcDir)) {
  fs.copyFileSync(path.join(imgSrcDir, f), path.join(imgDestDir, f));
}
console.log(`  assets/img/* -> wwwroot/assets/img/* (${fs.readdirSync(imgSrcDir).length} files)`);

// products/*.html and solutions/*.html at the repo root are design-reference
// copies only (see backend/README.md), NOT synced into wwwroot: both are
// database-backed CmsPages served through BlogController.ProductPage /
// SolutionPage. wwwroot/products and wwwroot/solutions must stay empty, a
// static file there would silently shadow the CMS route (static-file
// middleware runs before MVC routing) and admin edits to those pages would
// stop taking effect, exactly the bug fixed for the product pages this
// session. Do not add a copy step for either folder here.

console.log("wwwroot sync complete.");
