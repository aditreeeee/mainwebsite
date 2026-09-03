// Re-compresses everything in assets/img/ in place, without resizing.
// Files whose extension is .webp are re-encoded as real WebP (quality 88);
// .png files are optimized with palette reduction. A file is only
// overwritten if the recompressed version is actually smaller, so this is
// safe to run repeatedly (won't degrade quality pass after pass).
const sharp = require("sharp");
const fs = require("fs");
const path = require("path");

const dir = path.join(__dirname, "..", "assets", "img");

async function main() {
  const files = fs.readdirSync(dir).filter((f) => /\.(webp|png)$/i.test(f));
  let before = 0;
  let after = 0;

  for (const f of files) {
    const fp = path.join(dir, f);
    const originalSize = fs.statSync(fp).size;
    before += originalSize;

    const isWebp = f.toLowerCase().endsWith(".webp");
    const buf = isWebp
      ? await sharp(fp).webp({ quality: 88, effort: 6 }).toBuffer()
      : await sharp(fp).png({ compressionLevel: 9, palette: true, quality: 90 }).toBuffer();

    if (buf.length < originalSize) {
      fs.writeFileSync(fp, buf);
      after += buf.length;
      console.log(`  ${f}: ${originalSize} -> ${buf.length} bytes`);
    } else {
      after += originalSize;
    }
  }

  console.log(`Images: ${before} -> ${after} bytes (${files.length} files)`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
