#!/usr/bin/env node
// Read-only probe: can GolfBert hole data be fetched automatically (no key)?
// Checks whether the public hole page embeds the data, and whether the API
// answers without credentials. Prints findings only — writes nothing.
// Usage: node tools/probe-golfbert.mjs [holeId]

const HOLE = process.argv[2] || '17764';
const UA = 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124 Safari/537.36';

async function get(url, headers = {}) {
  try {
    const r = await fetch(url, { headers: { 'User-Agent': UA, ...headers }, redirect: 'follow' });
    const text = await r.text();
    return { status: r.status, ct: r.headers.get('content-type') || '', text };
  } catch (e) { return { status: 0, ct: '', text: 'ERROR ' + e.message }; }
}

const KEYS = ['surfacetype', 'teeboxes', 'polygons', 'latitude', 'longitude', 'coordinates', 'dogleg', 'flag'];

function scan(label, text) {
  console.log(`\n--- ${label} (${text.length} bytes) ---`);
  for (const k of KEYS) {
    const n = (text.match(new RegExp(k, 'ig')) || []).length;
    if (n) console.log(`   contains "${k}" x${n}`);
  }
  // embedded JSON blobs commonly used by SPAs
  for (const marker of ['__NEXT_DATA__', '__NUXT__', '__INITIAL_STATE__', 'window.__', 'application/json']) {
    if (text.includes(marker)) console.log(`   has marker: ${marker}`);
  }
  // referenced golfbert endpoints / js bundles
  const urls = [...new Set((text.match(/https?:\/\/[a-z0-9._\/-]*golfbert[a-z0-9._\/-]*/ig) || []))].slice(0, 15);
  if (urls.length) { console.log('   golfbert URLs referenced:'); urls.forEach(u => console.log('     ' + u)); }
}

const page = await get(`https://www.golfbert.com/courses/holes/${HOLE}`);
console.log('PAGE status', page.status, page.ct);
scan('page html', page.text);
if (/surfacetype|polygons|teeboxes/i.test(page.text)) {
  const i = page.text.search(/surfacetype|polygons|teeboxes/i);
  console.log('\n   sample around first data hit:\n', page.text.slice(Math.max(0, i - 120), i + 240));
}

for (const path of [`v1/holes/${HOLE}`, `v1/holes/${HOLE}/polygons`, `v1/holes/${HOLE}/teeboxes`]) {
  const r = await get(`https://api.golfbert.com/${path}`, { Accept: 'application/json' });
  console.log(`\nAPI /${path} -> ${r.status} ${r.ct}`);
  console.log('   ' + r.text.slice(0, 200).replace(/\n/g, ' '));
}
console.log('\nDone.');
