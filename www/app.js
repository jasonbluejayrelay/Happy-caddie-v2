'use strict';

// ─── Haversine distance (yards) ──────────────────────────────────────────────
function haversineYards(lat1, lon1, lat2, lon2) {
  const R = 6371000; // meters
  const φ1 = lat1 * Math.PI / 180, φ2 = lat2 * Math.PI / 180;
  const Δφ = (lat2 - lat1) * Math.PI / 180;
  const Δλ = (lon2 - lon1) * Math.PI / 180;
  const a = Math.sin(Δφ/2)**2 + Math.cos(φ1)*Math.cos(φ2)*Math.sin(Δλ/2)**2;
  return 2 * R * Math.asin(Math.sqrt(a)) * 1.09361;
}

function bearingDeg(lat1, lon1, lat2, lon2) {
  const φ1 = lat1 * Math.PI / 180, φ2 = lat2 * Math.PI / 180;
  const Δλ = (lon2 - lon1) * Math.PI / 180;
  const y = Math.sin(Δλ) * Math.cos(φ2);
  const x = Math.cos(φ1)*Math.sin(φ2) - Math.sin(φ1)*Math.cos(φ2)*Math.cos(Δλ);
  return (Math.atan2(y, x) * 180 / Math.PI + 360) % 360;
}

// Smallest absolute difference between two compass bearings (0–180°).
function angleDiff(a, b) {
  const d = Math.abs(a - b) % 360;
  return d > 180 ? 360 - d : d;
}

// Move a point `yards` along `bearing` (deg). Used to derive green front/back from center.
function destinationPoint(lat, lon, bearing, yards) {
  const R = 6371000;
  const d = (yards / 1.09361) / R; // angular distance
  const br = bearing * Math.PI / 180;
  const φ1 = lat * Math.PI / 180, λ1 = lon * Math.PI / 180;
  const φ2 = Math.asin(Math.sin(φ1) * Math.cos(d) + Math.cos(φ1) * Math.sin(d) * Math.cos(br));
  const λ2 = λ1 + Math.atan2(Math.sin(br) * Math.sin(d) * Math.cos(φ1), Math.cos(d) - Math.sin(φ1) * Math.sin(φ2));
  return { lat: φ2 * 180 / Math.PI, lon: ((λ2 * 180 / Math.PI) + 540) % 360 - 180 };
}

// ─── Storage (localStorage) ────────────────────────────────────────────────
const Store = {
  get(k, def = null) {
    try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : def; } catch { return def; }
  },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch {} },
  del(k)    { localStorage.removeItem(k); }
};

// ─── GPS ──────────────────────────────────────────────────────────────────
const GPS = {
  pos: null,
  watchId: null,
  callbacks: [],

  start() {
    if (!navigator.geolocation) return;
    this.watchId = navigator.geolocation.watchPosition(
      pos => {
        this.pos = { lat: pos.coords.latitude, lon: pos.coords.longitude, acc: pos.coords.accuracy };
        this.callbacks.forEach(fn => fn(this.pos));
      },
      () => {},
      { enableHighAccuracy: true, maximumAge: 2000, timeout: 10000 }
    );
  },

  stop() { if (this.watchId != null) navigator.geolocation.clearWatch(this.watchId); },

  onChange(fn) { this.callbacks.push(fn); },

  distanceTo(lat, lon) {
    if (!this.pos) return null;
    return haversineYards(this.pos.lat, this.pos.lon, lat, lon);
  },

  bearingTo(lat, lon) {
    if (!this.pos) return null;
    return bearingDeg(this.pos.lat, this.pos.lon, lat, lon);
  }
};

// ─── Compass (device heading, for the rangefinder reticle) ─────────────────
const Compass = {
  heading: null,      // degrees, 0 = true north, increasing clockwise
  _hasAbsolute: false,
  _started: false,

  async start() {
    if (this._started) return;
    this._started = true;
    // iOS 13+ gates orientation behind a permission prompt that must be
    // triggered from a user gesture (we call this from the rangefinder tap).
    try {
      const DOE = window.DeviceOrientationEvent;
      if (DOE && typeof DOE.requestPermission === 'function') {
        const res = await DOE.requestPermission().catch(() => 'denied');
        if (res !== 'granted') { this._started = false; return; }
      }
    } catch {}
    window.addEventListener('deviceorientationabsolute', e => { this._hasAbsolute = true; this._read(e); }, true);
    window.addEventListener('deviceorientation', e => {
      // Prefer the absolute feed on Android; only use this for iOS' webkit heading.
      if (this._hasAbsolute && e.webkitCompassHeading == null) return;
      this._read(e);
    }, true);
  },

  _read(e) {
    if (e.webkitCompassHeading != null) {
      this.heading = e.webkitCompassHeading;          // iOS: already true heading (CW)
    } else if (e.alpha != null) {
      this.heading = (360 - e.alpha) % 360;           // absolute alpha → compass heading
    }
  }
};

// ─── Text-to-speech (spoken confirmations) ────────────────────────────────
const Speak = {
  on: true,
  say(text) {
    if (!this.on || !text) return;
    try {
      if (!window.speechSynthesis) return;
      const u = new SpeechSynthesisUtterance(text);
      u.rate = 1.0; u.pitch = 1.0; u.volume = 1.0;
      window.speechSynthesis.cancel();
      window.speechSynthesis.speak(u);
    } catch {}
  }
};

// ─── Screen wake lock (keep display on during a round) ────────────────────
const WakeLock = {
  sentinel: null,
  async on() {
    try { if (navigator.wakeLock && !this.sentinel) this.sentinel = await navigator.wakeLock.request('screen'); } catch {}
  },
  async off() {
    try { await this.sentinel?.release(); } catch {}
    this.sentinel = null;
  }
};

// ─── Voice Recognition ───────────────────────────────────────────────────
const Voice = {
  recog: null,
  active: false,
  continuous: false,
  onCommand: null,

  init() {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) return false;
    this.recog = new SR();
    this.recog.lang = 'en-US';
    this.recog.interimResults = false;
    this.recog.maxAlternatives = 3;
    this.recog.onresult = e => {
      const alts = Array.from(e.results[e.results.length - 1]).map(a => a.transcript.toLowerCase().trim());
      this._handle(alts);
    };
    this.recog.onend = () => {
      this.active = false;
      UI.setVoiceStatus(false);
      if (this.continuous) setTimeout(() => this.listen(), 300);
    };
    this.recog.onerror = () => { this.active = false; UI.setVoiceStatus(false); };
    return true;
  },

  // Speech engines routinely mishear "golf" as "gulf"/"gold", so accept close
  // homophones and a couple of natural prefixes as the wake word.
  WAKE: /\b(?:golf|gulf|gold|caddie|caddy)\b/i,
  WAKE_G: /\b(?:golf|gulf|gold|caddie|caddy)\b/gi,

  listen() {
    if (!this.recog || this.active) return;
    try {
      // In always-on mode keep the mic open so the wake word isn't lost during
      // the stop/restart gap; push-to-talk grabs a single phrase.
      this.recog.continuous = this.continuous;
      this.recog.start();
      this.active = true;
      UI.setVoiceStatus(true);
    } catch {}
  },

  stop() { this.continuous = false; if (this.recog && this.active) { try { this.recog.stop(); } catch {} } },

  setContinuous(on) {
    this.continuous = on;
    if (on) this.listen();
    else this.stop();
  },

  _handle(alts) {
    const hasWake = alts.some(a => this.WAKE.test(a));
    const looksLikeCmd = alts.some(a => this._isCommand(a));

    // In always-on mode, ignore background speech that is neither the wake word
    // nor a recognizable command.
    if (this.continuous && !hasWake && !looksLikeCmd) return;

    // Prefer the alternative that actually carries the wake word or a command.
    const text = alts.find(a => this.WAKE.test(a) || this._isCommand(a)) || alts[0];
    const cmd = text.replace(this.WAKE_G, ' ').replace(/\s+/g, ' ').trim();

    // Wake word heard with nothing after it — acknowledge so the user knows it
    // registered (the only signal you get when not looking at the phone).
    if (hasWake && !cmd) {
      UI.toast('Listening… say a command', 'info', 1500);
      Speak.say('Go ahead');
      return;
    }

    // Otherwise hand the full phrase to the command parser, which knows the
    // complete vocabulary.
    if (this.onCommand) this.onCommand(cmd || text, alts);
  },

  _isCommand(t) {
    return /next shot|new shot|made it|in the hole|holed out|score \d|par|bogey|birdie|eagle|double|triple|club |driver|iron|wood|hybrid|wedge|putter|yardage|how far|distance|rangefinder|camera|scorecard|the card|match|standings|winning|commands|what can i say|help/.test(t);
  }
};

// ─── State ──────────────────────────────────────────────────────────────
const State = {
  // From localStorage
  clubs: Store.get('clubs', DEFAULT_CLUBS),
  rounds: Store.get('rounds', []),
  settings: Store.get('settings', { playerName: 'Jason', tee: 'blue', units: 'yards' }),
  clubStats: Store.get('clubStats', {}), // { clubId: [distance, ...] }

  // Active round (in memory + localStorage backup)
  round: null,  // { courseId, tee, date, players, teams, holes: [{scores:{}, shots:[]}] }
  holeIdx: 0,   // 0-based index
  shotStart: null, // { lat, lon }
  currentClub: null,
  pendingShot: false,

  load() {
    const saved = Store.get('activeRound');
    if (saved) {
      this.round = saved.round;
      this.holeIdx = saved.holeIdx;
      // Restore the in-progress shot so backgrounding/reloading mid-hole doesn't
      // lose the current shot (which would make tracking appear to get stuck).
      this.shotStart = saved.shotStart ?? null;
      this.pendingShot = saved.pendingShot ?? false;
      this.currentClub = saved.currentClub ?? null;
    }
  },

  saveActive() {
    if (this.round) Store.set('activeRound', {
      round: this.round,
      holeIdx: this.holeIdx,
      shotStart: this.shotStart,
      pendingShot: this.pendingShot,
      currentClub: this.currentClub
    });
    else Store.del('activeRound');
  },

  get hole() { return this.round?.holes[this.holeIdx]; },
  get holeData() {
    const c = COURSES.find(c => c.id === this.round?.courseId);
    return c?.holes[this.holeIdx];
  },
  get course() { return COURSES.find(c => c.id === this.round?.courseId); },

  // Front / Center / Back of the green. The stored `pin` is treated as center;
  // front/back are offset ~16 yds along the tee→green line (≈32 yd deep green).
  greenPoints(hd = this.holeData) {
    if (!hd) return null;
    const b = bearingDeg(hd.tee.lat, hd.tee.lon, hd.pin.lat, hd.pin.lon);
    const half = 16;
    return {
      front:  destinationPoint(hd.pin.lat, hd.pin.lon, (b + 180) % 360, half),
      center: { lat: hd.pin.lat, lon: hd.pin.lon },
      back:   destinationPoint(hd.pin.lat, hd.pin.lon, b, half)
    };
  },

  // A point `yards` from the tee along the tee→green line.
  pointFromTee(hd, yards) {
    const b = bearingDeg(hd.tee.lat, hd.tee.lon, hd.pin.lat, hd.pin.lon);
    return destinationPoint(hd.tee.lat, hd.tee.lon, b, yards);
  },

  // Carry targets declared on the hole (yards from tee).
  carryTargets(hd = this.holeData) {
    if (!hd || !hd.carries) return [];
    return hd.carries.map(c => ({ label: c.label, point: this.pointFromTee(hd, c.fromTee) }));
  },

  // Layup markers: points 150 / 100 yds short of green center (par 4/5 only).
  layupTargets(hd = this.holeData) {
    if (!hd || hd.par < 4) return [];
    const b = bearingDeg(hd.tee.lat, hd.tee.lon, hd.pin.lat, hd.pin.lon);
    const teeToGreen = haversineYards(hd.tee.lat, hd.tee.lon, hd.pin.lat, hd.pin.lon);
    return [150, 100].filter(y => teeToGreen > y + 40).map(y => ({
      label: `Lay up to ${y}`,
      point: destinationPoint(hd.pin.lat, hd.pin.lon, (b + 180) % 360, y)
    }));
  },

  // Aim-able points for the rangefinder reticle. The green's front/center/back
  // sit on your line of play (same bearing), so they can't be told apart by aim
  // — the green is a single "Pin" target. Off-axis points (carries, hazards,
  // layups) get their own entries so panning the phone can lock onto them.
  rangefinderTargets() {
    const targets = [];
    const hd = this.holeData;
    if (hd) targets.push({ label: 'Pin', point: { lat: hd.pin.lat, lon: hd.pin.lon }, primary: true });
    for (const c of this.carryTargets()) targets.push({ label: c.label, point: c.point });
    for (const l of this.layupTargets()) targets.push({ label: l.label, point: l.point });
    return targets;
  },

  // ── Shot editing (keeps club averages correct) ──────────────────────────
  _statRemove(club, dist) {
    const arr = this.clubStats[club];
    if (!arr) return;
    const i = arr.indexOf(dist);
    if (i >= 0) arr.splice(i, 1);
    if (!arr.length) delete this.clubStats[club];
    Store.set('clubStats', this.clubStats);
  },
  _statAdd(club, dist) {
    if (!club || dist <= 10) return;
    if (!this.clubStats[club]) this.clubStats[club] = [];
    this.clubStats[club].push(dist);
    Store.set('clubStats', this.clubStats);
  },
  removeShot(idx) {
    const shot = this.hole?.shots[idx];
    if (!shot) return;
    if (shot.club && shot.dist > 10) this._statRemove(shot.club, shot.dist);
    this.hole.shots.splice(idx, 1);
    this.saveActive();
  },
  editShot(idx, club, dist) {
    const shot = this.hole?.shots[idx];
    if (!shot) return;
    if (shot.club && shot.dist > 10) this._statRemove(shot.club, shot.dist);
    shot.club = club;
    shot.dist = dist;
    this._statAdd(club, dist);
    this.saveActive();
  },

  // ── Handicaps / net best-ball / skins ───────────────────────────────────
  strokesReceived(courseHcp, holeHandicapIndex) {
    const h = Math.round(courseHcp || 0);
    if (h <= 0) return 0;
    let s = Math.floor(h / 18);
    if (holeHandicapIndex <= (h % 18)) s += 1;
    return s;
  },

  matchState() {
    if (!this.round) return null;
    const teams = this.round.teams;
    if (!teams.length) return null;
    const course = this.course;
    const result = teams.map(t => ({ id: t.id, name: t.name, gross: 0, net: 0, skins: 0, holesPlayed: 0 }));
    const byId = Object.fromEntries(result.map(r => [r.id, r]));
    let carry = 0;

    course.holes.forEach((hd, i) => {
      const hole = this.round.holes[i];
      const teamHole = {};
      teams.forEach(t => {
        const grosses = [], nets = [];
        t.playerIds.forEach(pid => {
          const g = hole.scores[pid];
          if (g != null) {
            grosses.push(g);
            const p = this.round.players.find(pp => pp.id === pid);
            nets.push(g - this.strokesReceived(p?.hcp || 0, hd.handicap));
          }
        });
        if (grosses.length) teamHole[t.id] = { gross: Math.min(...grosses), net: Math.min(...nets) };
      });
      teams.forEach(t => {
        if (teamHole[t.id]) {
          byId[t.id].gross += teamHole[t.id].gross;
          byId[t.id].net += teamHole[t.id].net;
          byId[t.id].holesPlayed++;
        }
      });
      // Skins (net best ball) only when every team has a score on the hole
      if (teams.length >= 2 && teams.every(t => teamHole[t.id])) {
        let bestNet = Infinity, winners = [];
        teams.forEach(t => {
          const n = teamHole[t.id].net;
          if (n < bestNet) { bestNet = n; winners = [t.id]; }
          else if (n === bestNet) winners.push(t.id);
        });
        if (winners.length === 1) { byId[winners[0]].skins += 1 + carry; carry = 0; }
        else carry += 1;
      }
    });
    return { teams: result, carry };
  },

  recordShotEnd() {
    if (!this.shotStart || !GPS.pos) return 0;
    const dist = Math.round(haversineYards(
      this.shotStart.lat, this.shotStart.lon, GPS.pos.lat, GPS.pos.lon
    ));
    if (dist < 5) return 0; // ignore accidental taps
    // The club belongs to the shot that was *started* here — not whatever club
    // is now selected for the next shot. (Selecting the next club before tapping
    // the button must not re-attribute the completed shot.)
    const club = this.shotStart.club ?? this.currentClub;
    const shot = { club, dist, lat: GPS.pos.lat, lon: GPS.pos.lon, ts: Date.now() };
    this.hole.shots.push(shot);
    // Update club stats
    if (club && dist > 10) {
      if (!this.clubStats[club]) this.clubStats[club] = [];
      this.clubStats[club].push(dist);
      Store.set('clubStats', this.clubStats);
    }
    this.saveActive();
    return dist;
  },

  // Yards moved since the in-progress shot started (null if none / no GPS).
  pendingDistance() {
    if (!this.shotStart || !GPS.pos) return null;
    return Math.round(haversineYards(
      this.shotStart.lat, this.shotStart.lon, GPS.pos.lat, GPS.pos.lon
    ));
  },

  startShot() {
    if (!GPS.pos) return false;
    this.shotStart = { lat: GPS.pos.lat, lon: GPS.pos.lon, club: this.currentClub };
    this.pendingShot = true;
    this.saveActive();
    return true;
  },

  nextShot(club) {
    const dist = this.recordShotEnd();
    this.currentClub = club || this.currentClub;
    this.shotStart = GPS.pos ? { lat: GPS.pos.lat, lon: GPS.pos.lon, club: this.currentClub } : null;
    this.pendingShot = !!GPS.pos;
    this.saveActive();
    return dist;
  },

  madeIt() {
    const dist = this.recordShotEnd();
    this.shotStart = null;
    this.pendingShot = false;
    this.saveActive();
    return dist;
  },

  setScore(playerIdx, score, holeIdx = this.holeIdx) {
    const hole = this.round?.holes[holeIdx];
    if (!hole) return;
    const player = this.round.players[playerIdx];
    if (!player) return;
    hole.scores[player.id] = score;
    // Compute best ball per team
    for (const team of this.round.teams) {
      const scores = team.playerIds.map(pid => hole.scores[pid]).filter(s => s != null);
      if (scores.length) hole.teamScores[team.id] = Math.min(...scores);
    }
    this.saveActive();
  },

  finishHole() {
    if (this.holeIdx < 17) {
      this.holeIdx++;
      this.shotStart = null;
      this.pendingShot = false;
      this.currentClub = null;
      this.saveActive();
      return true;
    }
    return false; // round over
  },

  finishRound() {
    const rounds = Store.get('rounds', []);
    rounds.unshift({ ...this.round, finishedAt: Date.now() });
    Store.set('rounds', rounds);
    Store.del('activeRound');
    this.round = null;
    this.holeIdx = 0;
  },

  avgDistance(clubId) {
    const shots = this.clubStats[clubId];
    if (!shots || !shots.length) return null;
    return Math.round(shots.reduce((a, b) => a + b, 0) / shots.length);
  },

  suggestClub(yards) {
    let best = null, bestDiff = Infinity;
    for (const club of this.clubs) {
      const avg = this.avgDistance(club.id);
      if (avg == null) continue;
      const diff = Math.abs(avg - yards);
      if (diff < bestDiff) { best = club; bestDiff = diff; }
    }
    return best;
  },

  topSuggestions(yards) {
    return this.clubs
      .map(c => ({ club: c, avg: this.avgDistance(c.id) }))
      .filter(x => x.avg != null)
      .map(x => ({ ...x, diff: Math.abs(x.avg - yards) }))
      .sort((a, b) => a.diff - b.diff)
      .slice(0, 3);
  }
};

// Summarize a finished round for the history view.
function summarizeRound(r) {
  const course = COURSES.find(c => c.id === r.courseId);
  const coursePar = course?.par ?? 72;
  const me = r.players.find(p => p.isMe) || r.players[0];
  const myId = me?.id;
  let total = 0, holesScored = 0;
  let firHit = 0, firTot = 0, girHit = 0, girTot = 0, putts = 0, puttHoles = 0;

  r.holes.forEach((h, i) => {
    const sc = myId ? h.scores[myId] : null;
    if (sc != null) { total += sc; holesScored++; }
    const st = h.stats || {};
    const par = course?.holes[i]?.par ?? 4;
    if (par > 3 && st.fir != null) { firTot++; if (st.fir) firHit++; }
    if (st.gir != null) { girTot++; if (st.gir) girHit++; }
    if (st.putts != null) { putts += st.putts; puttHoles++; }
  });

  return {
    date: r.date || (r.finishedAt ? new Date(r.finishedAt).toISOString().slice(0, 10) : ''),
    courseName: course?.name ?? 'Course',
    myName: me?.name ?? 'Me',
    total: holesScored ? total : null,
    complete: holesScored === 18,
    holesScored,
    vsPar: holesScored ? total - coursePar : null,
    coursePar,
    fir: firTot ? Math.round(firHit / firTot * 100) : null,
    gir: girTot ? Math.round(girHit / girTot * 100) : null,
    putts: puttHoles ? putts : null,
    puttHoles
  };
}

// ─── UI helpers ─────────────────────────────────────────────────────────
const UI = {
  activeTab: 'hole',
  rangefinderOpen: false,
  cameraStream: null,
  compassHeading: null,

  $: id => document.getElementById(id),
  show: id => { const el = document.getElementById(id); if (el) el.style.display = ''; },
  hide: id => { const el = document.getElementById(id); if (el) el.style.display = 'none'; },

  setVoiceStatus(on) {
    const btn = this.$('btn-voice');
    if (btn) btn.classList.toggle('voice-active', on);
  },

  showView(name) {
    document.querySelectorAll('.view').forEach(v => v.style.display = 'none');
    const el = this.$('view-' + name);
    if (el) el.style.display = 'flex';
  },

  toast(msg, type = 'info', ms = 2500) {
    const t = this.$('toast');
    t.textContent = msg;
    t.className = 'toast show toast-' + type;
    clearTimeout(t._tid);
    t._tid = setTimeout(() => t.classList.remove('show'), ms);
  },

  // ── Home ──────────────────────────────────────────────────────────────
  renderHome() {
    const el = this.$('home-active-banner');
    if (State.round) {
      const hd = State.holeData;
      el.innerHTML = `<div class="active-banner" onclick="App.resumeRound()">
        <span>⛳ Resume Round — Hole ${State.holeIdx + 1} (Par ${hd?.par})</span>
        <span class="btn-sm">Continue →</span>
      </div>`;
    } else {
      el.innerHTML = '';
    }
  },

  // ── Round Setup ────────────────────────────────────────────────────────
  renderSetup() {
    const course = COURSES[0];
    const s = this.$('setup-tee');
    s.innerHTML = course.tees.map(t =>
      `<option value="${t.id}" ${State.settings.tee === t.id ? 'selected' : ''}>${t.name}</option>`
    ).join('');
    this.$('setup-player0').value = State.settings.playerName || 'Jason';
  },

  setPlayerTeam(playerIdx, teamIdx) {
    const tog = this.$(`teamtog-${playerIdx}`);
    if (!tog) return;
    tog.dataset.team = teamIdx;
    tog.querySelectorAll('.tt-opt').forEach((b, i) => b.classList.toggle('tt-on', i === teamIdx));
  },

  getSetupData() {
    const teamNames = [
      this.$('setup-team0')?.value.trim() || 'Team 1',
      this.$('setup-team1')?.value.trim() || 'Team 2'
    ];
    const players = [];
    const playerTeamIdx = [];
    for (let i = 0; i < 4; i++) {
      const name = this.$(`setup-player${i}`)?.value.trim();
      if (name) {
        const hcp = parseInt(this.$(`setup-hcp${i}`)?.value);
        players.push({ id: `p${i}`, name, isMe: i === 0, hcp: isNaN(hcp) ? 0 : hcp });
        playerTeamIdx.push(parseInt(this.$(`teamtog-${i}`)?.dataset.team ?? '0'));
      }
    }
    // Build teams from manual assignments — only include teams that have players
    const teams = [];
    [0, 1].forEach(ti => {
      const ids = players.filter((_, idx) => playerTeamIdx[idx] === ti).map(p => p.id);
      if (ids.length) teams.push({ id: `t${ti + 1}`, name: teamNames[ti], playerIds: ids });
    });
    return {
      courseId: 'north-hampton',
      tee: this.$('setup-tee').value,
      date: new Date().toISOString().slice(0, 10),
      players,
      teams,
      holes: Array.from({ length: 18 }, (_, i) => ({
        number: i + 1,
        scores: {},
        teamScores: {},
        shots: [],
        stats: { fir: null, gir: null, putts: null } // my fairway-hit / green-in-reg / putts
      }))
    };
  },

  // ── Hole View ──────────────────────────────────────────────────────────
  renderHole() {
    if (!State.round) return;
    const hd = State.holeData;
    const hole = State.hole;
    if (!hd || !hole) return;

    // Header
    this.$('hole-num').textContent = `Hole ${hd.number}`;
    this.$('hole-par').textContent = `Par ${hd.par}`;
    this.$('hole-hdcp').textContent = `Hdcp ${hd.handicap}`;

    const teeYards = hd.yardages[State.round.tee] || hd.yardages.blue;
    this.$('hole-yards').textContent = `${teeYards} yds`;
    this.$('hole-desc').textContent = hd.description;

    // Distance to pin
    this.updatePinDistance();

    // Shot status
    const shotEl = this.$('shot-status');
    shotEl.textContent = State.pendingShot
      ? `Shot ${hole.shots.length + 1} in progress — ${State.clubs.find(c => c.id === (State.shotStart?.club ?? State.currentClub))?.name || 'No club'}`
      : `${hole.shots.length} shot${hole.shots.length !== 1 ? 's' : ''} this hole`;

    // Shot log
    this.renderShotLog();

    // Club selector
    this.renderClubSelector();

    // Club suggestion
    this.updateClubSuggestion();

    // Hazards & layups
    this.renderHazards();
  },

  updatePinDistance() {
    const gp = State.greenPoints();
    const accEl = this.$('gps-acc');
    const fEl = this.$('dist-front'), cEl = this.$('dist-center'), bEl = this.$('dist-back');
    if (!fEl || !cEl || !bEl) return;
    if (gp && GPS.pos) {
      const center = Math.round(GPS.distanceTo(gp.center.lat, gp.center.lon));
      fEl.textContent = Math.round(GPS.distanceTo(gp.front.lat, gp.front.lon));
      cEl.textContent = center;
      bEl.textContent = Math.round(GPS.distanceTo(gp.back.lat, gp.back.lon));
      accEl.textContent = GPS.pos.acc ? `GPS ±${Math.round(GPS.pos.acc)}m` : '';
      this.updateClubSuggestion(center);
      this.renderHazards();
    } else {
      fEl.textContent = cEl.textContent = bEl.textContent = '—';
      accEl.textContent = 'No GPS';
    }
  },

  updateClubSuggestion(yards) {
    const el = this.$('club-suggestion');
    if (!el) return;
    if (!yards) {
      if (!GPS.pos || !State.holeData) { el.textContent = ''; return; }
      yards = Math.round(GPS.distanceTo(State.holeData.pin.lat, State.holeData.pin.lon));
    }
    const suggestions = State.topSuggestions(yards);
    if (!suggestions.length) {
      el.textContent = 'Hit more shots to get club suggestions';
      return;
    }
    el.innerHTML = `For ${yards} yds: ` + suggestions.map((s, i) =>
      `<span class="sug ${i === 0 ? 'sug-best' : ''}" onclick="App.selectClub('${s.club.id}')">${s.club.name} (${s.avg}y avg)</span>`
    ).join(' · ');
  },

  renderShotLog() {
    const shots = State.hole?.shots || [];
    const el = this.$('shot-log');
    if (!shots.length) { el.innerHTML = '<div class="no-shots">No shots yet</div>'; return; }
    el.innerHTML = shots.map((s, i) => {
      const club = State.clubs.find(c => c.id === s.club);
      return `<div class="shot-item" onclick="UI.openShotEdit(${i})">
        <span class="shot-num">${i + 1}</span>
        <span class="shot-club">${club?.name || '?'}</span>
        <span class="shot-dist">${s.dist} yds</span>
        <span class="shot-edit">✎</span>
      </div>`;
    }).join('');
  },

  // ── Shot editing ────────────────────────────────────────────────────────
  openShotEdit(idx) {
    const shot = State.hole?.shots[idx];
    if (!shot) return;
    this._editShotIdx = idx;
    this.$('shot-edit-club').innerHTML = State.clubs.map(c =>
      `<option value="${c.id}" ${c.id === shot.club ? 'selected' : ''}>${c.name}</option>`
    ).join('');
    this.$('shot-edit-dist').value = shot.dist;
    this.$('shot-modal').style.display = 'flex';
  },
  closeShotEdit() { this.$('shot-modal').style.display = 'none'; },
  saveShotEdit() {
    const dist = parseInt(this.$('shot-edit-dist').value);
    if (isNaN(dist) || dist < 0) { UI.toast('Enter a valid distance', 'error'); return; }
    State.editShot(this._editShotIdx, this.$('shot-edit-club').value, dist);
    this.closeShotEdit();
    UI.renderHole();
    UI.toast('Shot updated', 'success');
  },
  deleteShotEdit() {
    if (!confirm('Delete this shot?')) return;
    State.removeShot(this._editShotIdx);
    this.closeShotEdit();
    UI.renderHole();
    UI.toast('Shot deleted', 'info');
  },

  // ── Hazards & layups ────────────────────────────────────────────────────
  renderHazards() {
    const el = this.$('hazards-list');
    if (!el) return;
    const items = [...State.carryTargets(), ...State.layupTargets()];
    if (!items.length) { el.innerHTML = '<div class="haz-hint">No marked hazards or layups on this hole</div>'; return; }
    if (!GPS.pos) { el.innerHTML = '<div class="haz-hint">Waiting for GPS…</div>'; return; }
    el.innerHTML = items.map(it => {
      const d = Math.round(GPS.distanceTo(it.point.lat, it.point.lon));
      return `<div class="haz-item"><span>${it.label}</span><span class="haz-d">${d} y</span></div>`;
    }).join('');
  },

  // ── Live match (net best ball + skins) ──────────────────────────────────
  renderMatch() {
    const el = this.$('match-body');
    const m = State.matchState();
    if (!m) { el.innerHTML = '<p class="empty-state">No teams in this round.</p>'; return; }

    let lead = '';
    const scored = m.teams.filter(t => t.holesPlayed > 0);
    if (m.teams.length >= 2 && scored.length) {
      const sorted = [...m.teams].sort((a, b) => a.net - b.net);
      const diff = sorted[1].net - sorted[0].net;
      lead = diff === 0 ? 'All square (net)' : `${sorted[0].name} up by ${diff} (net)`;
    }

    el.innerHTML = `
      ${lead ? `<div class="card"><div class="match-lead">${lead}</div></div>` : ''}
      <div class="card">
        <div class="card-title">Best Ball — Net &amp; Gross</div>
        <table class="match-table">
          <thead><tr><th>Team</th><th>Thru</th><th>Gross</th><th>Net</th><th>Skins</th></tr></thead>
          <tbody>
            ${m.teams.map(t => `<tr>
              <td style="text-align:left">${t.name}</td>
              <td>${t.holesPlayed}</td>
              <td>${t.gross || '·'}</td>
              <td class="match-net">${t.net || '·'}</td>
              <td>${t.skins}</td>
            </tr>`).join('')}
          </tbody>
        </table>
        ${m.carry ? `<div style="font-size:12px;color:var(--muted);margin-top:8px">${m.carry} skin${m.carry > 1 ? 's' : ''} carried over (tie)</div>` : ''}
        <div style="font-size:11px;color:var(--muted);margin-top:8px">Net uses each player's handicap, allocated by hole. Skins go to the lowest team net; ties carry over.</div>
      </div>`;
  },

  renderClubSelector() {
    const el = this.$('club-selector');
    el.innerHTML = State.clubs.map(c => {
      const avg = State.avgDistance(c.id);
      const sel = c.id === State.currentClub;
      return `<div class="club-btn ${sel ? 'club-sel' : ''}" onclick="App.selectClub('${c.id}')">
        <div class="club-name">${c.name}</div>
        ${avg ? `<div class="club-avg">${avg}y</div>` : ''}
      </div>`;
    }).join('');
  },

  // ── Scorecard ─────────────────────────────────────────────────────────
  renderScorecard() {
    if (!State.round) return;
    const course = State.course;
    const { players, teams, holes } = State.round;
    const tee = State.round.tee;
    const el = this.$('scorecard-table');

    let front9Par = 0, back9Par = 0;

    const rows = course.holes.map((hd, i) => {
      const hole = holes[i];
      const yds = hd.yardages[tee];
      if (i < 9) front9Par += hd.par; else back9Par += hd.par;

      const playerCells = players.map(p => {
        const score = hole.scores[p.id];
        const diff = score != null ? score - hd.par : null;
        const cls = diff === null ? '' : diff <= -2 ? 'score-eagle' : diff === -1 ? 'score-birdie' : diff === 0 ? 'score-par' : diff === 1 ? 'score-bogey' : 'score-dbl';
        return `<td class="${cls}" onclick="UI.editScore(${i}, '${p.id}')">${score ?? '·'}</td>`;
      }).join('');

      const teamCells = teams.map(t => {
        const ts = hole.teamScores[t.id];
        const diff = ts != null ? ts - hd.par : null;
        const cls = diff === null ? '' : diff <= -2 ? 'score-eagle' : diff === -1 ? 'score-birdie' : diff === 0 ? 'score-par' : diff === 1 ? 'score-bogey' : 'score-dbl';
        return `<td class="${cls} team-score">${ts ?? '·'}</td>`;
      }).join('');

      const isCurrent = i === State.holeIdx;
      return `<tr class="${isCurrent ? 'current-hole-row' : ''}">
        <td>${hd.number}</td><td>${hd.par}</td><td>${yds}</td>
        ${playerCells}${teamCells}
      </tr>` + (i === 8 ? `<tr class="subtotal-row"><td colspan="3">OUT ${front9Par}</td>${
        players.map(p => `<td>${holes.slice(0,9).reduce((s,h) => s + (h.scores[p.id] || 0), 0) || '·'}</td>`).join('')
      }${teams.map(t => `<td>${holes.slice(0,9).reduce((s,h) => s + (h.teamScores[t.id] || 0), 0) || '·'}</td>`).join('')}</tr>` : '');
    }).join('');

    const totalPar = front9Par + back9Par;
    const totals = `<tr class="subtotal-row"><td colspan="3">IN ${back9Par}</td>${
      players.map(p => `<td>${holes.slice(9).reduce((s,h) => s + (h.scores[p.id] || 0), 0) || '·'}</td>`).join('')
    }${teams.map(t => `<td>${holes.slice(9).reduce((s,h) => s + (h.teamScores[t.id] || 0), 0) || '·'}</td>`).join('')}</tr>
    <tr class="total-row"><td colspan="3">TOT ${totalPar}</td>${
      players.map(p => {
        const total = holes.reduce((s,h) => s + (h.scores[p.id] || 0), 0);
        return `<td>${total || '·'}</td>`;
      }).join('')
    }${teams.map(t => {
      const total = holes.reduce((s,h) => s + (h.teamScores[t.id] || 0), 0);
      return `<td>${total || '·'}</td>`;
    }).join('')}</tr>`;

    el.innerHTML = `<table class="scorecard">
      <thead><tr>
        <th>#</th><th>Par</th><th>Yds</th>
        ${players.map(p => `<th>${p.name.split(' ')[0]}</th>`).join('')}
        ${teams.map(t => `<th class="team-hdr">${t.name}</th>`).join('')}
      </tr></thead>
      <tbody>${rows}${totals}</tbody>
    </table>`;
  },

  editScore(holeIdx, playerId) {
    const hd = State.course?.holes[holeIdx];
    const playerIdx = State.round.players.findIndex(p => p.id === playerId);
    const current = State.round.holes[holeIdx].scores[playerId] || hd?.par || '';
    const score = prompt(`${State.round.players[playerIdx]?.name} — Hole ${holeIdx + 1} score:`, current);
    const n = parseInt(score);
    if (!isNaN(n) && n > 0) {
      State.setScore(playerIdx, n, holeIdx);
      UI.renderScorecard();
    }
  },

  // ── Score Modal ────────────────────────────────────────────────────────
  showScoreModal() {
    const hd = State.holeData;
    const hole = State.hole;
    const el = this.$('score-modal-body');
    const myShots = hole.shots.length + (State.pendingShot ? 1 : 0);
    this._statPick = { fir: hole.stats?.fir ?? null, gir: hole.stats?.gir ?? null };

    const scoreRows = State.round.players.map((p, i) => {
      const def = i === 0 ? myShots || hd.par : (hole.scores[p.id] || hd.par);
      return `<div class="score-entry">
        <label>${p.name}</label>
        <div class="score-controls">
          <button onclick="this.nextElementSibling.value = Math.max(1, +this.nextElementSibling.value - 1)">-</button>
          <input type="number" id="mscore-${p.id}" value="${def}" min="1" max="15">
          <button onclick="this.previousElementSibling.value = Math.min(15, +this.previousElementSibling.value + 1)">+</button>
        </div>
      </div>`;
    }).join('');

    const fir = this._statPick.fir, gir = this._statPick.gir;
    const statRows = `
      <div class="divider"></div>
      <div class="card-title">My Stats (optional)</div>
      ${hd.par > 3 ? `
      <div class="score-entry">
        <label>Fairway hit?</label>
        <div class="team-toggle">
          <button type="button" class="tt-opt ${fir === true ? 'tt-on' : ''}" id="fir-yes" onclick="UI.pickStat('fir', true)">Yes</button>
          <button type="button" class="tt-opt ${fir === false ? 'tt-on' : ''}" id="fir-no" onclick="UI.pickStat('fir', false)">No</button>
        </div>
      </div>` : ''}
      <div class="score-entry">
        <label>Green in reg?</label>
        <div class="team-toggle">
          <button type="button" class="tt-opt ${gir === true ? 'tt-on' : ''}" id="gir-yes" onclick="UI.pickStat('gir', true)">Yes</button>
          <button type="button" class="tt-opt ${gir === false ? 'tt-on' : ''}" id="gir-no" onclick="UI.pickStat('gir', false)">No</button>
        </div>
      </div>
      <div class="score-entry">
        <label>Putts</label>
        <div class="score-controls">
          <button onclick="this.nextElementSibling.value = Math.max(0, +this.nextElementSibling.value - 1)">-</button>
          <input type="number" id="stat-putts" value="${hole.stats?.putts ?? 2}" min="0" max="10">
          <button onclick="this.previousElementSibling.value = Math.min(10, +this.previousElementSibling.value + 1)">+</button>
        </div>
      </div>`;

    el.innerHTML = scoreRows + statRows;
    this.$('score-modal').style.display = 'flex';
  },

  pickStat(key, val) {
    this._statPick = this._statPick || {};
    this._statPick[key] = val;
    this.$(`${key}-yes`)?.classList.toggle('tt-on', val === true);
    this.$(`${key}-no`)?.classList.toggle('tt-on', val === false);
  },

  saveScoreModal() {
    State.round.players.forEach((p, i) => {
      const el = this.$(`mscore-${p.id}`);
      if (el) State.setScore(i, parseInt(el.value));
    });
    // Capture my optional stats on the current hole before advancing
    const putts = parseInt(this.$('stat-putts')?.value);
    State.hole.stats = {
      fir: this._statPick?.fir ?? null,
      gir: this._statPick?.gir ?? null,
      putts: isNaN(putts) ? null : putts
    };
    State.saveActive();
    this.$('score-modal').style.display = 'none';
    const more = State.finishHole();
    if (more) {
      UI.renderHole();
      UI.renderScorecard();
      UI.toast(`Hole ${State.holeIdx + 1} — Let's go!`, 'info');
      Speak.say(`Hole ${State.holeIdx + 1}, par ${State.holeData?.par ?? ''}`);
      UI.switchTab('hole');
    } else {
      State.finishRound();
      WakeLock.off();
      UI.toast('Round complete! Great game!', 'success', 4000);
      Speak.say('Round complete. Great game!');
      UI.showView('home');
      UI.renderHome();
    }
  },

  // ── Stats View ─────────────────────────────────────────────────────────
  // Build the club-averages markup shared by the standalone Stats view and the
  // in-round Stats tab.
  _clubStatsHtml() {
    const clubs = State.clubs.map(c => {
      const shots = State.clubStats[c.id] || [];
      if (!shots.length) return null;
      const avg = Math.round(shots.reduce((a, b) => a + b) / shots.length);
      return { club: c, avg, max: Math.max(...shots), min: Math.min(...shots), count: shots.length };
    }).filter(Boolean).sort((a, b) => b.avg - a.avg);

    if (!clubs.length) {
      return '<p class="empty-state">No shot data yet.<br>Track shots during a round to see averages here.</p>';
    }
    return clubs.map(d => `
      <div class="stat-row">
        <div class="stat-club">${d.club.name}</div>
        <div class="stat-avg">${d.avg}<small>y avg</small></div>
        <div class="stat-range">${d.min}–${d.max}y · ${d.count} shots</div>
        <div class="stat-bar"><div class="stat-fill" style="width:${(d.avg / clubs[0].avg * 100).toFixed(0)}%"></div></div>
      </div>`).join('');
  },

  _suggestionHtml(yards) {
    if (!yards || isNaN(yards)) return '';
    const suggestions = State.topSuggestions(yards);
    if (!suggestions.length) return 'Need more shot data';
    return suggestions.map((s, i) =>
      `<div class="sug-row ${i === 0 ? 'sug-top' : ''}">
        ${i === 0 ? '✓ ' : ''}<strong>${s.club.name}</strong> — ${s.avg}y avg (off by ${s.diff}y)
      </div>`
    ).join('');
  },

  // Standalone Stats view (view-stats).
  renderStats() {
    const el = this.$('stats-clubs');
    if (el) el.innerHTML = this._clubStatsHtml();
    if (this.$('suggest-dist')) this.updateStatSuggestion();
  },

  // In-round Stats tab (separate element ids so both can coexist).
  renderStatsRound() {
    const el = this.$('stats-clubs-round');
    if (el) el.innerHTML = this._clubStatsHtml();
    this.updateStatSuggestionInline(this.$('suggest-dist-round')?.value);
  },

  updateStatSuggestion() {
    const el = this.$('suggest-result');
    if (!el) return;
    el.innerHTML = this._suggestionHtml(parseInt(this.$('suggest-dist')?.value));
  },

  updateStatSuggestionInline(val) {
    const el = this.$('suggest-result-round');
    if (!el) return;
    el.innerHTML = this._suggestionHtml(parseInt(val));
  },

  // ── Tab switching ──────────────────────────────────────────────────────
  switchTab(tab) {
    this.activeTab = tab;
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.toggle('tab-active', b.dataset.tab === tab));
    document.querySelectorAll('.tab-pane').forEach(p => p.style.display = p.dataset.tab === tab ? 'block' : 'none');
    if (tab === 'scorecard') this.renderScorecard();
    if (tab === 'match') this.renderMatch();
    if (tab === 'stats') this.renderStatsRound();
    if (tab === 'hole') { this.renderHole(); }
  },

  // ── Rangefinder ────────────────────────────────────────────────────────
  // Point the phone like a laser rangefinder: a center reticle locks onto the
  // mapped target (pin, green front/back, carries, layups) you're aiming at and
  // reads out its distance. Falls back to the pin when no compass is available.
  _rfLockedTarget: null,

  async openRangefinder() {
    this.$('rangefinder-overlay').style.display = 'flex';
    this.rangefinderOpen = true;
    Compass.start(); // user gesture → ok to request orientation permission on iOS
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
      this.cameraStream = stream;
      const video = this.$('rf-video');
      video.srcObject = stream;
      video.play();
    } catch {
      this.$('rf-no-cam').style.display = 'block';
    }
    this.updateRangefinder();
  },

  closeRangefinder() {
    this.rangefinderOpen = false;
    this._rfLockedTarget = null;
    this.$('rangefinder-overlay').style.display = 'none';
    if (this.cameraStream) { this.cameraStream.getTracks().forEach(t => t.stop()); this.cameraStream = null; }
  },

  // Speak the locked target's distance (rangefinder "trigger").
  rangefinderShoot() {
    const t = this._rfLockedTarget;
    if (!t) { Speak.say('No target'); UI.toast('Aim at a target first', 'error'); return; }
    UI.toast(`${t.label}: ${t.dist} yds`, 'success', 2500);
    Speak.say(`${t.dist} yards to ${t.label}`);
  },

  updateRangefinder() {
    if (!this.rangefinderOpen) return;
    const overlay = this.$('rangefinder-overlay');
    const distEl = this.$('rf-distance');
    const labelEl = this.$('rf-label');
    const bearEl = this.$('rf-bearing');
    const targets = State.rangefinderTargets();
    const aim = Compass.heading;

    if (GPS.pos && targets.length) {
      // Range every mapped point from the current position…
      for (const t of targets) {
        t.dist = Math.round(GPS.distanceTo(t.point.lat, t.point.lon));
        t.bearing = GPS.bearingTo(t.point.lat, t.point.lon);
      }
      // Default to the pin, then let the reticle steal the lock only when it is
      // pointing clearly closer to another (off-axis) target — this keeps
      // near-collinear targets from flickering.
      let pick = targets.find(t => t.primary) || targets[0];
      let bestDiff = aim != null ? angleDiff(aim, pick.bearing) : 0;
      if (aim != null) {
        for (const t of targets) {
          const diff = angleDiff(aim, t.bearing);
          if (diff < bestDiff - 2) { bestDiff = diff; pick = t; }
        }
      }
      const locked = aim != null && bestDiff <= 12;
      this._rfLockedTarget = pick;
      overlay.classList.toggle('rf-locked', locked);

      distEl.textContent = pick.dist + ' yds';
      labelEl.textContent = pick.label.toUpperCase();
      if (aim != null) {
        const compass = ['N','NE','E','SE','S','SW','W','NW'][Math.round(aim / 45) % 8];
        bearEl.textContent = locked
          ? `🔒 Locked · ${Math.round(aim)}° ${compass}`
          : `Pan to a target · ${Math.round(aim)}° ${compass}`;
      } else {
        bearEl.textContent = 'Compass unavailable — showing the pin';
      }
    } else {
      this._rfLockedTarget = null;
      overlay.classList.remove('rf-locked');
      distEl.textContent = '— yds';
      labelEl.textContent = 'TO TARGET';
      bearEl.textContent = GPS.pos ? 'No targets mapped on this hole' : 'Acquiring GPS…';
    }
    if (this.rangefinderOpen) requestAnimationFrame(() => this.updateRangefinder());
  },

  // ── Settings ───────────────────────────────────────────────────────────
  renderSettings() {
    this.$('setting-name').value = State.settings.playerName;
    const course = COURSES[0];
    this.$('setting-tee').innerHTML = course.tees.map(t =>
      `<option value="${t.id}" ${State.settings.tee === t.id ? 'selected' : ''}>${t.name}</option>`
    ).join('');
    this.$('setting-voice').checked = State.settings.voiceConfirm !== false;
    this.$('setting-autoadvance').checked = State.settings.autoAdvance !== false;
  },

  saveSettings() {
    State.settings.playerName = this.$('setting-name').value.trim() || 'Jason';
    State.settings.tee = this.$('setting-tee').value;
    State.settings.voiceConfirm = this.$('setting-voice').checked;
    State.settings.autoAdvance = this.$('setting-autoadvance').checked;
    Speak.on = State.settings.voiceConfirm;
    Store.set('settings', State.settings);
    UI.toast('Settings saved', 'success');
    UI.showView('home');
  },

  // ── Round History & Trends ──────────────────────────────────────────────
  renderHistory() {
    const rounds = Store.get('rounds', []);
    const el = this.$('history-body');
    if (!rounds.length) {
      el.innerHTML = '<p class="empty-state">No finished rounds yet.<br>Complete a round to see your history and trends.</p>';
      return;
    }

    const summaries = rounds.map(summarizeRound);
    const scored = summaries.filter(s => s.total != null);

    const avg = arr => arr.length ? (arr.reduce((a, b) => a + b, 0) / arr.length) : null;
    const avgScore = avg(scored.map(s => s.total));
    const best = scored.length ? Math.min(...scored.map(s => s.total)) : null;
    const avgGir = avg(summaries.filter(s => s.gir != null).map(s => s.gir));
    const avgFir = avg(summaries.filter(s => s.fir != null).map(s => s.fir));
    const avgPutts = avg(summaries.filter(s => s.putts != null).map(s => s.putts));
    // Rough handicap: average of (score − par) over the most recent (complete) rounds.
    const diffs = summaries.filter(s => s.complete).slice(0, 20).map(s => s.vsPar);
    const hcp = diffs.length ? (avg(diffs) * 0.96) : null;

    const fmt = (v, d = 0) => v == null ? '—' : v.toFixed(d);
    const sign = v => v == null ? '' : (v > 0 ? '+' + v : '' + v);

    el.innerHTML = `
      <div class="card">
        <div class="card-title">Trends · ${rounds.length} round${rounds.length !== 1 ? 's' : ''}</div>
        <div class="trend-grid">
          <div class="trend"><div class="trend-val">${fmt(avgScore, 1)}</div><div class="trend-lbl">Avg Score</div></div>
          <div class="trend"><div class="trend-val">${best ?? '—'}</div><div class="trend-lbl">Best</div></div>
          <div class="trend"><div class="trend-val">${hcp == null ? '—' : sign(Math.round(hcp))}</div><div class="trend-lbl">Est. Hcp*</div></div>
          <div class="trend"><div class="trend-val">${fmt(avgGir)}%</div><div class="trend-lbl">GIR</div></div>
          <div class="trend"><div class="trend-val">${fmt(avgFir)}%</div><div class="trend-lbl">Fairways</div></div>
          <div class="trend"><div class="trend-val">${fmt(avgPutts, 1)}</div><div class="trend-lbl">Putts/Rd</div></div>
        </div>
        <div style="font-size:11px;color:var(--muted);margin-top:8px">*Rough estimate from score vs. par — not an official handicap.</div>
      </div>
      ${summaries.map(s => `
        <div class="round-card">
          <div class="round-main">
            <div class="round-score">${s.total ?? '—'} ${s.vsPar != null ? `<small>(${sign(s.vsPar)})</small>` : ''}</div>
            <div class="round-meta">
              <div class="round-date">${s.date}${s.complete ? '' : ` · ${s.holesScored} holes`}</div>
              <div class="round-course">${s.courseName}</div>
            </div>
          </div>
          <div class="round-stats">
            ${s.gir != null ? `<span>GIR ${s.gir}%</span>` : ''}
            ${s.fir != null ? `<span>FIR ${s.fir}%</span>` : ''}
            ${s.putts != null ? `<span>${s.putts} putts</span>` : ''}
          </div>
        </div>`).join('')}
      <button class="btn btn-danger btn-sm btn-block" style="margin-top:8px"
        onclick="if(confirm('Delete all round history? (Club averages are kept.)')){Store.set('rounds',[]);UI.renderHistory()}">
        Clear Round History
      </button>`;
  },

  // ── My Bag (club editor) ────────────────────────────────────────────────
  renderBag() {
    const el = this.$('bag-list');
    el.innerHTML = State.clubs.map(c => {
      const avg = State.avgDistance(c.id);
      return `<div class="bag-item">
        <span class="bag-name" onclick="App.renameClub('${c.id}')">${c.name}</span>
        ${avg ? `<span class="bag-avg">${avg}y avg</span>` : '<span class="bag-avg" style="opacity:.5">no data</span>'}
        <button class="bag-del" onclick="App.removeClub('${c.id}')">✕</button>
      </div>`;
    }).join('') || '<p class="empty-state">Your bag is empty. Add some clubs below.</p>';
  },

  // ── Backup / Restore ────────────────────────────────────────────────────
  openBackup() {
    this.$('backup-export').value = Backup.buildJson();
    this.$('backup-import').value = '';
    this.$('backup-modal').style.display = 'flex';
  },

  closeBackup() { this.$('backup-modal').style.display = 'none'; },

  async copyBackup() {
    const text = this.$('backup-export').value;
    try {
      await navigator.clipboard.writeText(text);
      UI.toast('Backup copied — paste it somewhere safe', 'success', 3000);
    } catch {
      // Fallback: select the textarea so the user can long-press copy
      const ta = this.$('backup-export');
      ta.focus(); ta.select();
      UI.toast('Select all & copy the highlighted text', 'info', 3000);
    }
  },

  async shareBackup() {
    const text = this.$('backup-export').value;
    const filename = `happy-caddie-backup-${new Date().toISOString().slice(0, 10)}.json`;
    // Best: share as a real file (lets you save to Drive, email, Files, etc.)
    try {
      const file = new File([text], filename, { type: 'application/json' });
      if (navigator.canShare && navigator.canShare({ files: [file] })) {
        await navigator.share({ files: [file], title: 'Happy Caddie Backup' });
        return;
      }
    } catch {}
    // Fallback: trigger a download (works in desktop browsers)
    try {
      const blob = new Blob([text], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = filename;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
      UI.toast('Backup file saved', 'success');
    } catch {
      UI.copyBackup();
    }
  },

  doRestore() {
    const text = this.$('backup-import').value.trim();
    if (!text) { UI.toast('Paste your backup text first', 'error'); return; }
    if (Backup.import(text)) {
      this.closeBackup();
      UI.renderSettings();
      UI.renderHome();
    }
  }
};

// ─── Backup data model ────────────────────────────────────────────────────
const Backup = {
  buildJson() {
    return JSON.stringify({
      app: 'golf-tracker',
      schema: 1,
      exportedAt: new Date().toISOString(),
      settings: State.settings,
      clubs: State.clubs,
      clubStats: State.clubStats,
      rounds: Store.get('rounds', []),
      activeRound: Store.get('activeRound', null)
    }, null, 2);
  },

  import(jsonText) {
    let data;
    try { data = JSON.parse(jsonText); } catch { UI.toast('That text is not valid backup data', 'error'); return false; }
    if (!data || data.app !== 'golf-tracker') {
      if (!confirm("This doesn't look like a Happy Caddie backup. Restore anyway?")) return false;
    }
    if (data.settings)  { State.settings  = data.settings;  Store.set('settings', data.settings); }
    if (data.clubs)     { State.clubs     = data.clubs;     Store.set('clubs', data.clubs); }
    if (data.clubStats) { State.clubStats = data.clubStats; Store.set('clubStats', data.clubStats); }
    if (data.rounds)    Store.set('rounds', data.rounds);
    if (data.activeRound) { Store.set('activeRound', data.activeRound); State.load(); }
    else { Store.del('activeRound'); }
    UI.toast('Backup restored!', 'success', 3000);
    return true;
  }
};

// ─── App controller ──────────────────────────────────────────────────────
const App = {
  init() {
    State.load();
    Speak.on = State.settings.voiceConfirm !== false;
    // Ask the OS to keep our data durable (no eviction)
    if (navigator.storage?.persist) navigator.storage.persist().catch(() => {});
    // Re-acquire the screen wake lock when returning to the app mid-round
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible' && State.round) WakeLock.on();
    });
    if (State.round) WakeLock.on();
    GPS.start();
    GPS.onChange(() => {
      if (UI.activeTab === 'hole') UI.updatePinDistance();
      App.checkAutoAdvance();
    });

    if (Voice.init()) {
      Voice.onCommand = (cmd, alts) => App.handleVoice(cmd, alts);
    }

    // Register service worker
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.register('./sw.js').catch(() => {});
    }

    // Device compass — start now where allowed (Android); iOS waits for the
    // rangefinder tap so it can prompt for permission from a user gesture.
    const DOE = window.DeviceOrientationEvent;
    if (DOE && typeof DOE.requestPermission !== 'function') Compass.start();

    UI.renderHome();
    UI.showView('home');
  },

  // ── Navigation ─────────────────────────────────────────────────────────
  goHome()     { WakeLock.off(); UI.showView('home'); UI.renderHome(); },
  goSetup()    { UI.showView('setup'); UI.renderSetup(); },
  goStats()    { UI.showView('stats'); UI.renderStats(); },
  goSettings() { UI.showView('settings'); UI.renderSettings(); },
  goHistory()  { UI.showView('history'); UI.renderHistory(); },
  goBag()      { UI.showView('bag'); UI.renderBag(); },
  goHelp()     { UI.showView('help'); },

  // ── Auto-advance to the next hole by GPS ────────────────────────────────
  checkAutoAdvance() {
    if (!State.round || State.settings.autoAdvance === false) return;
    if (State.holeIdx >= 17) return;
    if (!GPS.pos || (GPS.pos.acc && GPS.pos.acc > 30)) return; // need a decent fix
    const nextTee = State.course?.holes[State.holeIdx + 1]?.tee;
    if (!nextTee) return;
    // If the next tee sits essentially on top of this hole's green (some course
    // coordinates loop each green into the next tee), GPS can't tell "on the
    // green" from "at the next tee" — so auto-advance would fire mid-hole as you
    // approach the green. Skip it; use the "Made It!" button to finish the hole.
    const pin = State.holeData?.pin;
    if (pin && haversineYards(pin.lat, pin.lon, nextTee.lat, nextTee.lon) < 60) return;
    const dToNext = GPS.distanceTo(nextTee.lat, nextTee.lon);
    if (dToNext == null || dToNext > 35) {
      // moved away from the next tee — re-arm so a future arrival can trigger
      if (this._autoArmedHole !== State.holeIdx) this._autoArmedHole = State.holeIdx;
      return;
    }
    if (this._autoHandledHole === State.holeIdx) return; // already handled this arrival
    this._autoHandledHole = State.holeIdx;

    const myId = (State.round.players.find(p => p.isMe) || State.round.players[0])?.id;
    const scored = myId && State.hole.scores[myId] != null;
    const nextNum = State.holeIdx + 2;

    if (State.hole.shots.length && !scored) {
      // Played the hole but didn't tap "Made it" — prompt for the score.
      UI.toast(`Reached hole ${nextNum} — enter your last hole's score`, 'info', 3500);
      Speak.say(`You're on hole ${nextNum}. Enter your score.`);
      UI.showScoreModal();
    } else if (!scored) {
      // No tracking on the hole — just advance.
      State.finishHole();
      UI.renderHole(); UI.renderScorecard();
      UI.toast(`Auto-advanced to Hole ${State.holeIdx + 1}`, 'success');
      Speak.say(`Hole ${State.holeIdx + 1}, par ${State.holeData?.par ?? ''}`);
    }
  },

  // ── My Bag editing ──────────────────────────────────────────────────────
  addClub() {
    const input = UI.$('bag-add-name');
    const name = input.value.trim();
    if (!name) { UI.toast('Enter a club name', 'error'); return; }
    const base = name.toLowerCase().replace(/[^a-z0-9]+/g, '') || 'club';
    let id = base, n = 1;
    while (State.clubs.some(c => c.id === id)) id = base + (++n);
    State.clubs.push({ id, name });
    Store.set('clubs', State.clubs);
    input.value = '';
    UI.renderBag();
  },

  removeClub(id) {
    const club = State.clubs.find(c => c.id === id);
    if (!club) return;
    if (!confirm(`Remove ${club.name} from your bag? (Its shot history is kept.)`)) return;
    State.clubs = State.clubs.filter(c => c.id !== id);
    Store.set('clubs', State.clubs);
    UI.renderBag();
  },

  renameClub(id) {
    const club = State.clubs.find(c => c.id === id);
    if (!club) return;
    const name = prompt('Rename club:', club.name);
    if (name && name.trim()) {
      club.name = name.trim();
      Store.set('clubs', State.clubs);
      UI.renderBag();
    }
  },

  resumeRound() {
    if (!State.round) return;
    WakeLock.on();
    UI.showView('round');
    UI.switchTab('hole');
    UI.renderHole();
  },

  // ── Round management ───────────────────────────────────────────────────
  startRound() {
    const data = UI.getSetupData();
    if (!data.players.length) { UI.toast('Add at least one player', 'error'); return; }
    State.round = data;
    State.holeIdx = 0;
    State.currentClub = null;
    State.shotStart = null;
    State.pendingShot = false;
    State.saveActive();
    UI.showView('round');
    UI.switchTab('hole');
    UI.renderHole();
    UI.toast('Round started! Hole 1 — good luck!', 'success');
    Speak.say('Round started. Hole 1, par ' + (State.holeData?.par ?? ''));
    WakeLock.on();
    GPS.start();
  },

  abandonRound() {
    if (!confirm('Abandon current round?')) return;
    State.round = null;
    Store.del('activeRound');
    WakeLock.off();
    UI.showView('home');
    UI.renderHome();
  },

  // ── Shot management ─────────────────────────────────────────────────────
  selectClub(id) {
    State.currentClub = id;
    if (!State.pendingShot && GPS.pos) {
      State.startShot();
      UI.toast(`${State.clubs.find(c => c.id === id)?.name} — Shot ${(State.hole?.shots.length || 0) + 1} started`, 'info', 1500);
    } else {
      State.saveActive(); // persist the club chosen for the next shot
    }
    UI.renderHole();
  },

  newShot() {
    if (!State.round) return;
    if (!GPS.pos) { UI.toast('Waiting for GPS…', 'error'); return; }
    if (State.pendingShot) {
      // A shot only counts once you've walked to the ball (≥5 yds). If you tap
      // again without moving, tell you how far you've gone and keep the shot
      // pending — don't silently reset it (that's what looked like "stuck").
      const moved = State.pendingDistance();
      if (moved != null && moved < 5) {
        UI.toast(`Only ${moved} yd from the last shot — walk to your ball, then tap New Shot`, 'info', 3000);
        return;
      }
      const dist = State.nextShot(State.currentClub);
      if (dist > 0) { UI.toast(`Shot recorded: ${dist} yds`, 'success', 2000); Speak.say(`Shot recorded, ${dist} yards`); }
      else UI.toast('New shot started', 'info', 1500);
    } else {
      if (!State.currentClub) { UI.toast('Select a club first', 'error'); return; }
      State.startShot();
      UI.toast('Shot started — walk to your ball', 'info', 2000);
    }
    UI.renderHole();
  },

  madeIt() {
    if (!State.round) return;
    if (State.pendingShot) {
      const dist = State.madeIt();
      if (dist > 0) { UI.toast(`Last shot: ${dist} yds — in the hole!`, 'success', 2500); Speak.say(`Last putt, ${dist} yards. In the hole!`); }
      else Speak.say('In the hole!');
    }
    UI.showScoreModal();
  },

  // ── Voice commands ─────────────────────────────────────────────────────
  handleVoice(cmd, alts) {
    const t = cmd.toLowerCase();

    if (/next shot|new shot/.test(t)) { App.newShot(); return; }
    if (/made it|in the hole|holed out/.test(t)) { App.madeIt(); return; }
    // While the rangefinder is open, "range/shoot/lock" reads the aimed target.
    if (UI.rangefinderOpen && /\b(range|shoot|fire|lock)\b/.test(t)) { UI.rangefinderShoot(); return; }
    if (/yardage|how far|distance to (the )?pin|read distance/.test(t)) { App.speakYardage(); return; }
    if (/commands|what can i say|^help|show help/.test(t)) { App.goHelp(); Speak.say('Here are all the commands'); return; }
    if (/rangefinder|camera/.test(t)) { UI.openRangefinder(); return; }
    if (/scorecard|the card/.test(t)) { App.resumeRound(); UI.switchTab('scorecard'); return; }
    if (/match|who.?s winning|standings/.test(t)) { App.resumeRound(); UI.switchTab('match'); return; }

    // "score 5" / "score bogey" etc.
    const scoreMatch = t.match(/score (\d+)/);
    if (scoreMatch) {
      const score = parseInt(scoreMatch[1]);
      State.setScore(0, score);
      UI.renderScorecard();
      UI.toast(`Score set: ${score}`, 'success');
      return;
    }
    // Ordered most-specific first: "bogey" is a substring of "double bogey",
    // and "par" is a substring of many words, so they must be matched last.
    const parWords = { eagle: -2, birdie: -1, 'double bogey': 2, 'triple': 3, 'double': 2, bogey: 1, par: 0 };
    for (const [word, diff] of Object.entries(parWords)) {
      if (t.includes(word) && State.holeData) {
        const score = State.holeData.par + diff;
        State.setScore(0, score);
        UI.renderScorecard();
        UI.toast(`Score set: ${score} (${word})`, 'success');
        return;
      }
    }

    // Club selection
    for (const club of State.clubs) {
      if (alts.some(a => a.includes(club.name.toLowerCase()) || a.includes(club.id))) {
        App.selectClub(club.id);
        return;
      }
    }
  },

  toggleVoice() {
    if (Voice.continuous) {
      Voice.setContinuous(false);
      UI.toast('Always-on voice OFF', 'info');
    } else {
      if (!Voice.recog) { UI.toast('Voice not supported on this browser', 'error'); return; }
      Voice.setContinuous(true);
      UI.toast('Always-on voice ON — say "golf [command]"', 'success');
    }
  },

  pushToTalk() {
    if (!Voice.recog) { UI.toast('Voice not supported', 'error'); return; }
    Voice.listen();
    UI.toast('Listening…', 'info', 1500);
  },

  speakYardage() {
    const gp = State.greenPoints();
    if (!gp || !GPS.pos) { UI.toast('No GPS signal', 'error'); Speak.say('No G P S signal yet'); return; }
    const f = Math.round(GPS.distanceTo(gp.front.lat, gp.front.lon));
    const c = Math.round(GPS.distanceTo(gp.center.lat, gp.center.lon));
    const b = Math.round(GPS.distanceTo(gp.back.lat, gp.back.lon));
    UI.toast(`F ${f} · C ${c} · B ${b}`, 'info', 2500);
    Speak.say(`${c} to the center. Front ${f}, back ${b}.`);
  },

  // ── Update pin position ─────────────────────────────────────────────────
  updatePinHere() {
    if (!GPS.pos) { UI.toast('No GPS signal', 'error'); return; }
    const hd = State.holeData;
    if (!hd) return;
    hd.pin = { lat: GPS.pos.lat, lon: GPS.pos.lon };
    UI.toast(`Pin position updated for Hole ${hd.number}`, 'success');
    UI.updatePinDistance();
  }
};

window.addEventListener('DOMContentLoaded', () => App.init());
