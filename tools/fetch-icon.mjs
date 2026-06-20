#!/usr/bin/env node
// Fetch an app's icon from its Google Play listing and write it into the project
// as the Happy Caddie icon (used to generate Android launcher icons + PWA icons).
// Usage: node tools/fetch-icon.mjs <playStorePackageIdOrUrl>
// Requires network (run in CI). Only fetches a public listing image.
import fs from 'node:fs';

const arg = (process.argv[2] || '').trim();
if (!arg) { console.error('Provide a Play Store package id or URL'); process.exit(1); }
let pkg = arg;
const m = arg.match(/[?&]id=([^&]+)/);
if (m) pkg = m[1];
if (!/^[\w.]+$/.test(pkg)) { console.error('Could not parse a package id from:', arg); process.exit(1); }

const UA = 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36';

const pageUrl = `https://play.google.com/store/apps/details?id=${pkg}&hl=en&gl=US`;
console.log('Fetching listing:', pageUrl);
const res = await fetch(pageUrl, { headers: { 'User-Agent': UA } });
if (!res.ok) { console.error('Play listing HTTP', res.status, '— is the app still published?'); process.exit(1); }
const html = await res.text();

// The app icon is the listing's og:image (a play-lh.googleusercontent.com URL).
const og = html.match(/<meta\s+property="og:image"\s+content="([^"]+)"/i)
        || html.match(/"(https:\/\/play-lh\.googleusercontent\.com\/[^"]+)"/);
if (!og) { console.error('Could not find an icon image on the listing.'); process.exit(1); }
const base = og[1].split('=')[0]; // strip any =sN / =wN-hN size suffix
console.log('Icon base URL:', base);

async function dl(size, path) {
  const r = await fetch(`${base}=s${size}`, { headers: { 'User-Agent': UA } });
  if (!r.ok) throw new Error(`download ${size}px HTTP ${r.status}`);
  const buf = Buffer.from(await r.arrayBuffer());
  if (buf.length < 500 || buf.slice(0, 8).toString('hex') !== '89504e470d0a1a0a' && buf.slice(0, 3).toString() !== 'ÿØÿ') {
    // accept PNG or JPEG; warn otherwise
    console.log(`  (note: ${path} header ${buf.slice(0,4).toString('hex')})`);
  }
  fs.writeFileSync(path, buf);
  console.log('wrote', path, buf.length, 'bytes');
}

await dl(1024, 'resources/icon.png');     // drives Android launcher icons (capacitor-assets)
await dl(512, 'www/icon-512.png');        // PWA
await dl(192, 'www/icon-192.png');        // PWA / apple-touch-icon
console.log('Done. Review the images, then rebuild.');
