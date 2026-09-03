// Regression guard: fails the build if any HTML page in the repo has a
// heading level skip (e.g. an <h2> followed directly by an <h4>), which
// breaks screen-reader navigation and accessibility audits. Run via
// `npm run check:headings`, wired into CI.
const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const root = path.join(__dirname, "..");

const tracked = execSync('git ls-files "*.html"', { cwd: root })
  .toString()
  .split("\n")
  .filter(Boolean)
  .filter((f) => (f.startsWith("backend") ? f.includes("wwwroot") : true));

// Includes any *.html not yet committed at the repo root or in
// blog-articles/ (e.g. freshly added blog articles), so this check still
// covers them pre-commit.
const untrackedHtml = [
  ...fs.readdirSync(root).filter((f) => f.endsWith(".html") && !tracked.includes(f)),
  ...(fs.existsSync(path.join(root, "blog-articles"))
    ? fs
        .readdirSync(path.join(root, "blog-articles"))
        .filter((f) => f.endsWith(".html"))
        .map((f) => "blog-articles/" + f)
        .filter((f) => !tracked.includes(f))
    : []),
];

const files = [...tracked, ...untrackedHtml];

let hadIssues = false;

for (const rel of files) {
  const fp = path.join(root, rel);
  if (!fs.existsSync(fp)) continue; // tracked at its old path but moved on disk, not yet committed
  const html = fs.readFileSync(fp, "utf8");
  const heads = [...html.matchAll(/<h([1-6])[ >]/g)].map((m) => +m[1]);

  let prev = 0;
  for (const h of heads) {
    if (prev > 0 && h > prev + 1) {
      console.error(`${rel}: heading skip ${prev} -> ${h}`);
      hadIssues = true;
    }
    prev = h;
  }
}

if (hadIssues) {
  console.error("\nHeading hierarchy check failed.");
  process.exit(1);
}

console.log(`Heading hierarchy OK across ${files.length} pages.`);
