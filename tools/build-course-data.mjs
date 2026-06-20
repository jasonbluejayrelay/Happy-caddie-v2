#!/usr/bin/env node
// Regenerate www/course-data.js coordinates from OpenStreetMap (Overpass).
// Keeps par/handicap/yardages/description from the existing file; replaces
// tee / green (front-center-back) / hazards with real mapped coordinates,
// matched to holes by the `ref` tag on golf=hole ways.
//
// Usage: node tools/build-course-data.mjs "North Hampton Golf Club, Fernandina Beach, FL"
// Requires network (run in CI or locally). Non-destructive: only writes if it
// confidently matched most holes; otherwise prints findings and exits 1.

import fs from 'node:fs';

const QUERY = process.argv[2] || 'North Hampton Golf Club, Fernandina Beach, FL';
const UA = 'happy-caddie-course-builder/1.0 (github actions)';
const OVERPASS = 'https://overpass-api.de/api/interpreter';

const R = 6371000;
const toRad = d => d * Math.PI / 180;
function meters(aLat, aLon, bLat, bLon) {
  const dLat = toRad(bLat - aLat), dLon = toRad(bLon - aLon);
  const s = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(aLat)) * Math.cos(toRad(bLat)) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(s));
}
const M_TO_YD = 1.09361;
function bearing(aLat, aLon, bLat, bLon) {
  const y = Math.sin(toRad(bLon - aLon)) * Math.cos(toRad(bLat));
  const x = Math.cos(toRad(aLat)) * Math.sin(toRad(bLat)) - Math.sin(toRad(aLat)) * Math.cos(toRad(bLat)) * Math.cos(toRad(bLon - aLon));
  return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
}
function centroid(geom) {
  let lat = 0, lon = 0;
  for (const p of geom) { lat += p.lat; lon += p.lon; }
  return { lat: +(lat / geom.length).toFixed(6), lon: +(lon / geom.length).toFixed(6) };
}
const round6 = n => +n.toFixed(6);

async function overpassRaw(query) {
  const r = await fetch(OVERPASS, { method: 'POST', headers: { 'User-Agent': UA, 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(query) });
  if (!r.ok) throw new Error('Overpass HTTP ' + r.status);
  return (await r.json()).elements || [];
}

function bboxOf(geom) {
  let s = 90, w = 180, n = -90, e = -180;
  for (const p of geom) { s = Math.min(s, p.lat); n = Math.max(n, p.lat); w = Math.min(w, p.lon); e = Math.max(e, p.lon); }
  return [s, w, n, e];
}

// Find the golf course by name directly in OSM (no geocoder dependency).
async function findCourse(query) {
  const kw = (query.split(',')[0] || query).replace(/\b(golf|club|course|cc|g\.?c\.?)\b/ig, '').trim() || query;
  const region = '30.20,-82.25,31.05,-81.25'; // NE Florida (Nassau/Amelia area)
  console.log(`Searching OSM for golf course matching "${kw}" in NE Florida…`);
  const q = `[out:json][timeout:90];
    (
      way[leisure=golf_course][name~"${kw}",i](${region});
      relation[leisure=golf_course][name~"${kw}",i](${region});
      way[leisure=golf_course][name~"North Hampton",i](${region});
      relation[leisure=golf_course][name~"North Hampton",i](${region});
    );
    out tags geom;`;
  const els = await overpassRaw(q);
  if (!els.length) return null;
  const el = els.find(e => (e.geometry && e.geometry.length) || (e.members && e.members.length)) || els[0];
  let geom = el.geometry || (el.members ? el.members.flatMap(m => m.geometry || []) : []);
  if (!geom.length) return null;
  console.log(`  Found: "${el.tags?.name}" (osm ${el.type}/${el.id})`);
  return { name: el.tags?.name, bbox: bboxOf(geom) };
}

async function overpass(bbox) {
  // bbox = [s,w,n,e]
  const q = `[out:json][timeout:60];
    (
      way["golf"="hole"](${bbox});
      way["golf"="green"](${bbox});
      way["golf"="tee"](${bbox});
      way["golf"="bunker"](${bbox});
      way["natural"="sand"](${bbox});
      way["golf"="water_hazard"](${bbox});
      way["golf"="lateral_water_hazard"](${bbox});
      relation["natural"="water"](${bbox});
      way["natural"="water"](${bbox});
    );
    out geom tags;`;
  const r = await fetch(OVERPASS, { method: 'POST', headers: { 'User-Agent': UA, 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
  if (!r.ok) throw new Error('Overpass HTTP ' + r.status);
  return (await r.json()).elements || [];
}

// front/center/back of a green polygon relative to the approach (tee→center) line
function greenFCB(geom, tee) {
  const c = centroid(geom);
  const brg = bearing(tee.lat, tee.lon, c.lat, c.lon);
  let front = c, back = c, fMin = Infinity, bMax = -Infinity;
  for (const p of geom) {
    // signed distance of vertex along the approach bearing, relative to center
    const along = meters(c.lat, c.lon, p.lat, p.lon) * Math.cos(toRad(bearing(c.lat, c.lon, p.lat, p.lon) - brg));
    if (along < fMin) { fMin = along; front = p; }
    if (along > bMax) { bMax = along; back = p; }
  }
  return {
    front: { lat: round6(front.lat), lon: round6(front.lon) },
    center: c,
    back: { lat: round6(back.lat), lon: round6(back.lon) }
  };
}

function nearestFeature(feats, lat, lon) {
  let best = null, bd = Infinity;
  for (const f of feats) {
    const c = f._c;
    const d = meters(lat, lon, c.lat, c.lon);
    if (d < bd) { bd = d; best = f; }
  }
  return { feat: best, dist: bd };
}

async function main() {
  const course0 = await findCourse(QUERY);
  if (!course0) {
    console.error('Could not locate the golf course in OpenStreetMap by name. ' +
      'It may not be mapped, or the name differs. Try a different query, or map it on the Satellite view.');
    process.exit(1);
  }
  const [s, w, n, e] = course0.bbox;
  const pad = 0.0015; // small margin around the course polygon
  const bbox = [s - pad, w - pad, n + pad, e + pad].map(x => x.toFixed(6)).join(',');
  console.log('Course bbox:', bbox);
  const els = await overpass(bbox);
  const ways = els.filter(e => e.geometry && e.geometry.length);
  ways.forEach(w => { w._c = centroid(w.geometry); });
  const holes = ways.filter(w => w.tags?.golf === 'hole');
  const greens = ways.filter(w => w.tags?.golf === 'green');
  const tees = ways.filter(w => w.tags?.golf === 'tee');
  const bunkers = ways.filter(w => w.tags?.golf === 'bunker' || w.tags?.natural === 'sand');
  const water = ways.filter(w => w.tags?.golf === 'water_hazard' || w.tags?.golf === 'lateral_water_hazard' || w.tags?.natural === 'water');
  console.log(`Found: holes=${holes.length} greens=${greens.length} tees=${tees.length} bunkers=${bunkers.length} water=${water.length}`);
  if (holes.length < 9 || greens.length < 9) {
    console.error('Not enough hole/green data in OSM for a confident rebuild. Aborting (no file written).');
    process.exit(1);
  }

  // Load existing course-data to preserve par/handicap/yardages/description.
  const srcPath = 'www/course-data.js';
  const src = fs.readFileSync(srcPath, 'utf8');
  const COURSES = (new Function(src + '; return COURSES;'))();
  const course = COURSES[0];

  const byNum = {};
  for (const h of holes) {
    const ref = parseInt(h.tags.ref);
    if (!isNaN(ref)) byNum[ref] = h;
  }

  let matched = 0;
  for (const hd of course.holes) {
    const hw = byNum[hd.number];
    if (!hw) { console.warn(`  hole ${hd.number}: no OSM hole way (ref)`); continue; }
    const a = hw.geometry[0], z = hw.geometry[hw.geometry.length - 1];
    // green nearer the hole's last node, tee nearer the first node
    const gN = nearestFeature(greens, z.lat, z.lon);
    const tN = nearestFeature(tees, a.lat, a.lon);
    if (!gN.feat || gN.dist > 60) { console.warn(`  hole ${hd.number}: no green within 60m`); continue; }
    const tee = tN.feat && tN.dist < 60 ? tN.feat._c : { lat: round6(a.lat), lon: round6(a.lon) };
    hd.tee = tee;
    hd.green = greenFCB(gN.feat.geometry, tee);
    hd.pin = hd.green.center;
    // hazards within ~45m of the hole line centroid->green span
    const hz = [];
    const near = (f) => {
      // distance from feature centroid to the straight tee->green segment (approx via endpoints)
      const dTee = meters(f._c.lat, f._c.lon, tee.lat, tee.lon);
      const dGreen = meters(f._c.lat, f._c.lon, hd.green.center.lat, hd.green.center.lon);
      const span = meters(tee.lat, tee.lon, hd.green.center.lat, hd.green.center.lon) + 40;
      return (dTee + dGreen) < span + 60; // rough "near the corridor"
    };
    for (const b of bunkers) if (near(b)) hz.push({ type: 'bunker', label: 'Bunker', lat: round6(b._c.lat), lon: round6(b._c.lon) });
    for (const w of water) if (near(w)) hz.push({ type: 'water', label: 'Water', lat: round6(w._c.lat), lon: round6(w._c.lon) });
    if (hz.length) hd.hazards = hz.slice(0, 6);
    delete hd.carries; // superseded by real hazards
    matched++;
    console.log(`  hole ${hd.number}: tee+green set, hazards=${hz.length}`);
  }
  console.log(`Matched ${matched}/${course.holes.length} holes`);
  if (matched < 12) { console.error('Too few holes matched; aborting.'); process.exit(1); }

  const header = `// ${course.name} — coordinates from OpenStreetMap (© OpenStreetMap contributors, ODbL)\n// Generated ${new Date().toISOString().slice(0, 10)} by tools/build-course-data.mjs\n\n`;
  const out = header + 'const COURSES = ' + JSON.stringify(COURSES, null, 2) + ';\n\n'
    + 'const DEFAULT_CLUBS = ' + (new Function(src + '; return JSON.stringify(DEFAULT_CLUBS,null,2);'))() + ';\n';
  fs.writeFileSync(srcPath, out);
  console.log('Wrote', srcPath);
}

main().catch(e => { console.error(e); process.exit(1); });
