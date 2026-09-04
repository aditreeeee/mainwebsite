// Regression guard: fails if the static-mirror files listed in
// sync-wwwroot.js differ from their wwwroot copy, i.e. someone edited one
// side (root frontend or wwwroot) and forgot to run `npm run build`. This
// is exactly the class of bug this project hit repeatedly: two copies of
// the same page/asset drifting apart. Run via `npm run check:wwwroot-sync`.
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const wwwroot = path.join(root, "backend", "src", "eGlobeSolutions.Web", "wwwroot");

const pairs = [
  ["css/style.css", "css/style.css"],
  ["css/style.min.css", "css/style.min.css"],
  ["js/main.js", "js/main.js"],
  ["js/main.min.js", "js/main.min.js"],
  ["js/calculator.js", "js/calculator.js"],
  ["js/calculator.min.js", "js/calculator.min.js"],
  ["404.html", "404.html"],
];

// products/*.html and about.html/privacy-policy.html/terms-of-use.html/
// refund-and-cancellation.html at the repo root are archival: their content
// now lives in CmsPages (seeded via DbInitializer) and is served dynamically,
// not mirrored into wwwroot, so they're deliberately not tracked here.

let outOfSync = [];

for (const [src, dest] of pairs) {
  const srcPath = path.join(root, src);
  const destPath = path.join(wwwroot, dest);
  if (!fs.existsSync(srcPath)) continue; // nothing to compare yet
  if (!fs.existsSync(destPath)) {
    outOfSync.push(`${dest} is missing from wwwroot`);
    continue;
  }
  const a = fs.readFileSync(srcPath);
  const b = fs.readFileSync(destPath);
  if (!a.equals(b)) outOfSync.push(`${src} differs from wwwroot/${dest}`);
}

if (outOfSync.length) {
  console.error("wwwroot is out of sync with the root frontend:\n");
  for (const msg of outOfSync) console.error("  - " + msg);
  console.error("\nRun `npm run build:sync-wwwroot` and commit the result.");
  process.exit(1);
}

console.log(`wwwroot is in sync (${pairs.length} files checked).`);
