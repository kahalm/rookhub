#!/usr/bin/env node
// UI-Sweep: rendert alle Seiten aller fünf Oberflächen in einem echten Browser, prüft jede automatisch und
// vergleicht mit dem vorigen Lauf. Aufruf und Optionen: README.md bzw. `node sweep.mjs --help`.

import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import https from 'node:https';
import os from 'node:os';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';
import { PNG } from 'pngjs';
import pixelmatch from 'pixelmatch';
import { APPS, AREAS, ROUTES, areaOf, routeId } from './routes.mjs';
import { login, resolveParams } from './seed.mjs';
import { pageChecks } from './checks.mjs';
import { writeIndex } from './report.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '../..');
const RUNS = path.join(HERE, 'runs');

const VIEWPORTS = {
  mobile: { width: 390, height: 844, isMobile: true, hasTouch: true },
  laptop: { width: 1366, height: 768 },
  wide: { width: 1920, height: 1080 },
};
const MAX_SHOT_HEIGHT = 4000;
const PUZZLE_AREAS = new Set(['puzzles', 'kurse', 'wochenpost']);
const SETTLE_MAX_MS = 60000;   // hoch, weil Anfragen in der eigenen Bremse warten können
const QUIET_MS = 800;
// Die API drosselt je Adresse (global 100 Anfragen/min, anonyme Wege teils 60/min). Alle Aufnahmen kommen von
// EINER Adresse — ohne eigene Bremse lief der erste Lauf in hunderte 429 und fotografierte Fehlerzustände.
const DEFAULT_RATE = 80;
// Wie lange eine Anfrage in der Bremse warten darf, bevor das Bild als „vom Werkzeug verzögert" gilt.
const DELAY_FLAG_MS = 3000;

// ------------------------------------------------------------------------------------------- Optionen
const HELP = `UI-Sweep — Screenshots aller Seiten + automatische Prüfung + Vergleich

  node sweep.mjs [Optionen]          (oder ./sweep.sh, installiert vorher die Abhängigkeiten)

  --env dev|prod         Ziel (Vorgabe dev). prod nur abgemeldet und nur lesend — es wird dort nichts angelegt.
  --app a,b              nur diese Oberflächen (rookhub, turnier, kidhub, leaguehub, clubhub)
  --area a,b             nur diese Bereiche, z. B. kurse, repertoire, partien (Liste: --areas)
  --route text           nur Routen, deren Pfad oder Name den Text enthält (mehrere mit Komma)
  --viewport v,w         mobile (390), laptop (1366), wide (1920)       Vorgabe: mobile,wide
  --theme t,u            dark, light                                       Vorgabe: dark
  --auth a,b             anon, user (normales Konto), admin (claude-dev)    Vorgabe: anon,user
  --concurrency n        gleichzeitige Seiten (Vorgabe 2 — mehr läuft in die Drossel der API)
  --rate n               höchstens n API-Anfragen je Minute (Vorgabe 70; die API drosselt bei 100 je Adresse)
  --compare run|none     mit diesem Lauf vergleichen (Ordnername unter runs/); Vorgabe: der vorige Lauf
  --local                die gebauten Oberflächen aus src/frontend/app/dist/<app>/browser ausliefern
                         (API-Aufrufe gehen an die gewählte Umgebung) — einen Umbau ansehen, bevor er deployt ist
  --list                 Katalog mit aufgelösten Adressen zeigen, keinen Browser starten
  --name text            Zusatz am Ordnernamen des Laufs

Zugang: ~/.config/rookhub/dev-claude.env (ROOKHUB_DEV_USER/ROOKHUB_DEV_PASSWORD) oder UI_SWEEP_USER/UI_SWEEP_PASSWORD.
Ergebnis: runs/<Zeitpunkt>/index.html (Übersicht), report.json, shots/*.png, diff/*.png.`;

function parseArgs(argv) {
  const o = { env: 'dev', viewport: ['mobile', 'wide'], theme: ['dark'], auth: ['anon', 'user'], concurrency: 2, rate: DEFAULT_RATE };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]; const v = () => argv[++i];
    const csv = () => v().split(',').map(s => s.trim()).filter(Boolean);
    switch (a) {
      case '--help': case '-h': console.log(HELP); process.exit(0);
      case '--env': o.env = v(); break;
      case '--app': o.app = csv(); break;
      case '--area': o.area = csv(); break;
      case '--areas': console.log(AREAS.map(([n]) => n).join(', ') + ', sonstiges'); process.exit(0);
      case '--route': o.route = csv(); break;
      case '--viewport': o.viewport = csv(); break;
      case '--theme': o.theme = csv(); break;
      case '--auth': o.auth = csv(); break;
      case '--concurrency': o.concurrency = Number(v()); break;
      case '--rate': o.rate = Number(v()); break;
      case '--compare': o.compare = v(); break;
      case '--local': o.local = true; break;
      case '--list': o.list = true; break;
      case '--name': o.name = v(); break;
      default: throw new Error(`Unbekannte Option ${a} (--help)`);
    }
  }
  for (const vp of o.viewport) if (!VIEWPORTS[vp]) throw new Error(`Unbekannte Breite ${vp}`);
  if (o.env === 'prod' && o.auth.some(a => a !== 'anon')) {
    console.log('Hinweis: auf prod nur abgemeldet — --auth user/admin entfällt.');
    o.auth = o.auth.filter(x => x === 'anon');
  }
  return o;
}

const opts = parseArgs(process.argv.slice(2));
const suffix = opts.env === 'prod' ? '' : '-dev';
const remoteBase = app => `https://${APPS[app].host}${suffix}.oberschmid.homes`;
const apiBase = remoteBase('rookhub');

function credentials() {
  if (process.env.UI_SWEEP_USER) return { user: process.env.UI_SWEEP_USER, password: process.env.UI_SWEEP_PASSWORD };
  const file = path.join(os.homedir(), '.config/rookhub/dev-claude.env');
  const env = Object.fromEntries(fs.readFileSync(file, 'utf8').split('\n').filter(l => l.includes('='))
    .map(l => [l.slice(0, l.indexOf('=')).trim(), l.slice(l.indexOf('=') + 1).trim()]));
  return { user: env.ROOKHUB_DEV_USER, password: env.ROOKHUB_DEV_PASSWORD };
}

/** Normales Konto ohne Sonderrechte (Rolle „user"): ROOKHUB_DEV_PLAIN_USER/_PASSWORD in derselben Datei. Fehlt es,
 *  wird es auf Dev registriert und dort eingetragen. Das Admin-Konto (claude-dev) ist die Rolle „admin". */
async function plainCredentials(base) {
  if (process.env.UI_SWEEP_PLAIN_USER) return { user: process.env.UI_SWEEP_PLAIN_USER, password: process.env.UI_SWEEP_PLAIN_PASSWORD };
  const file = path.join(os.homedir(), '.config/rookhub/dev-claude.env');
  const text = fs.readFileSync(file, 'utf8');
  const env = Object.fromEntries(text.split('\n').filter(l => l.includes('='))
    .map(l => [l.slice(0, l.indexOf('=')).trim(), l.slice(l.indexOf('=') + 1).trim()]));
  if (env.ROOKHUB_DEV_PLAIN_USER) return { user: env.ROOKHUB_DEV_PLAIN_USER, password: env.ROOKHUB_DEV_PLAIN_PASSWORD };
  const user = 'claude-dev-user';
  const password = 'Sw-' + crypto.randomBytes(12).toString('base64url') + '!7';
  const res = await fetch(`${base}/api/auth/register`, { method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ username: user, email: null, password }) });
  if (!res.ok) throw new Error(`Normales Konto anlegen gescheitert: HTTP ${res.status} ${await res.text()}`);
  fs.appendFileSync(file, (text.endsWith('\n') ? '' : '\n') + `ROOKHUB_DEV_PLAIN_USER=${user}\nROOKHUB_DEV_PLAIN_PASSWORD=${password}\n`);
  console.log(`Normales Konto ${user} auf Dev angelegt (Zugang in ${file}).`);
  return { user, password };
}

/** Parameter, die an den EIGENEN Daten des Admin-Kontos hängen — für das normale Konto wären das fremde 404-Seiten. */
const OWNED_PARAMS = new Set(['gameId', 'analyzedGameId', 'repertoireId', 'worksheetId', 'reconstructionId', 'guessId',
  'analysisId', 'comparisonId', 'friendId', 'bookId', 'calcBookId']);
/** Für abgemeldet ohne Sinn: die Punktepartie gehört einem Konto (anonym gäbe es nur ein 404). */
const ANON_SKIP = new Set(['guessId']);

function chromePath() {
  if (process.env.CHROME_BIN) return process.env.CHROME_BIN;
  const cache = path.join(os.homedir(), '.cache/ms-playwright');
  const dirs = fs.existsSync(cache) ? fs.readdirSync(cache).filter(d => /^chromium-\d+$/.test(d)).sort().reverse() : [];
  for (const d of dirs) {
    const p = path.join(cache, d, 'chrome-linux64/chrome');
    if (fs.existsSync(p)) return p;
  }
  throw new Error('Kein Chromium gefunden — `npx playwright-core install chromium` oder CHROME_BIN setzen.');
}

// ------------------------------------------------------------------------- lokale Auslieferung (--local)
function startLocalServers(apps) {
  const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.json': 'application/json',
    '.svg': 'image/svg+xml', '.png': 'image/png', '.wasm': 'application/wasm', '.woff2': 'font/woff2', '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json' };
  const bases = {};
  let port = 18300;
  for (const app of apps) {
    const root = path.join(REPO, 'src/frontend/app/dist', app === 'rookhub' ? 'app' : app, 'browser');
    if (!fs.existsSync(path.join(root, 'index.html'))) throw new Error(`--local: ${root} fehlt — vorher \`npx ng build ${app === 'rookhub' ? 'app' : app}\``);
    const api = new URL(apiBase);
    const server = http.createServer((req, res) => {
      if (req.url.startsWith('/api/')) {
        const up = https.request({ host: api.host, path: req.url, method: req.method, headers: { ...req.headers, host: api.host } }, r => {
          res.writeHead(r.statusCode, r.headers); r.pipe(res);
        });
        up.on('error', e => { res.writeHead(502); res.end(String(e)); });
        req.pipe(up); return;
      }
      let file = path.join(root, decodeURIComponent(req.url.split('?')[0]));
      if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(root, 'index.html');
      res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' });
      fs.createReadStream(file).pipe(res);
    });
    const p = port++;
    server.listen(p);
    bases[app] = `http://localhost:${p}`;
  }
  return bases;
}

// ------------------------------------------------------------------------------------------- Ablauf
async function main() {
  if (opts.area) for (const a of opts.area) if (a !== 'sonstiges' && !AREAS.some(([n]) => n === a)) throw new Error(`Unbekannter Bereich ${a} (--areas)`);
  const routes = ROUTES.filter(r => (!opts.app || opts.app.includes(r.app)) && (!opts.area || opts.area.includes(areaOf(r)))
    && (!opts.route || opts.route.some(t => r.url.includes(t) || routeId(r).includes(t))));

  let session = null; let plainSession = null; let params = {}; let userParams = {};
  const cachePath = path.join(RUNS, `params-${opts.env}.json`);
  const previousParams = fs.existsSync(cachePath) ? JSON.parse(fs.readFileSync(cachePath, 'utf8')) : {};
  const resolveLog = [];
  if (opts.env === 'prod') {
    console.log('prod: keine Anmeldung, keine Testdaten — Routen mit Platzhaltern entfallen.');
  } else {
    const { user, password } = credentials();
    session = await login(apiBase, user, password);
    console.log(`Angemeldet als ${session.username} an ${apiBase}`);
    if (opts.auth.includes('user')) {
      const plain = await plainCredentials(apiBase);
      plainSession = await login(apiBase, plain.user, plain.password);
      console.log(`Normales Konto: ${plainSession.username} — eigene Daten:`);
      const userCache = path.join(RUNS, `params-${opts.env}-user.json`);
      const prevUser = fs.existsSync(userCache) ? JSON.parse(fs.readFileSync(userCache, 'utf8')) : {};
      userParams = await resolveParams(apiBase, plainSession.token, (name, value) =>
        OWNED_PARAMS.has(name) && console.log(`  ${name.padEnd(22)} ${value instanceof Error ? 'Fehler: ' + value.message : value ?? '—'}`), prevUser);
      fs.writeFileSync(userCache, JSON.stringify(userParams, null, 2));
    }
    params = await resolveParams(apiBase, session.token, (name, value) => {
      const text = value instanceof Error ? `Fehler: ${value.message}` : value ?? '— (nichts gefunden)';
      resolveLog.push({ name, value: value instanceof Error ? null : value, note: text });
      console.log(`  ${name.padEnd(22)} ${text}`);
    }, previousParams);
    fs.mkdirSync(RUNS, { recursive: true });
    fs.writeFileSync(cachePath, JSON.stringify(params, null, 2));
  }

  // Konto-eigene Parameter (Partie, Repertoire …) kommen für die Rolle „user" aus dessen eigenen Daten, der Rest ist geteilt.
  const fill = (url, auth) => {
    const missing = [];
    const src = n => (auth === 'user' && OWNED_PARAMS.has(n) ? userParams : params)[n];
    const out = url.replace(/\{(\w+)\}/g, (_, n) => { if (src(n) == null) missing.push(n); return src(n) ?? ''; });
    return { url: out, missing };
  };

  const jobs = []; const unresolved = [];
  for (const route of routes) {
    const wanted = route.auth === 'any' ? opts.auth
      : route.auth === 'user' ? opts.auth.filter(a => a !== 'anon') : opts.auth.filter(a => a === route.auth);
    for (const auth of wanted) {
      if (auth === 'anon' && [...route.url.matchAll(/\{(\w+)\}/g)].some(m => ANON_SKIP.has(m[1]))) continue;
      const { url, missing } = fill(route.url, auth);
      if (missing.length) { unresolved.push({ id: `${routeId(route)} (${auth})`, url: route.url, missing }); continue; }
      for (const viewport of opts.viewport) for (const theme of opts.theme) jobs.push({ route, id: routeId(route), url, auth, viewport, theme });
    }
  }


  if (opts.list) {
    for (const r of routes) { const { url, missing } = fill(r.url, 'admin'); console.log(`${routeId(r).padEnd(42)} ${r.auth.padEnd(5)} ${missing.length ? `[fehlt: ${missing}]` : url}`); }
    console.log(`\n${jobs.length} Aufnahmen, ${unresolved.length} Routen nicht auflösbar.`);
    return;
  }

  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '_').slice(0, 13);
  const runName = `${stamp}${opts.env === 'prod' ? '_prod' : ''}${opts.local ? '_local' : ''}${opts.name ? '_' + opts.name : ''}`;
  const runDir = path.join(RUNS, runName);
  fs.mkdirSync(path.join(runDir, 'shots'), { recursive: true });

  const appBases = opts.local ? startLocalServers([...new Set(jobs.map(j => j.route.app))]) : null;
  const base = app => appBases?.[app] ?? remoteBase(app);
  const i18n = Object.keys(JSON.parse(fs.readFileSync(path.join(REPO, 'src/frontend/app/public/i18n/en.json'), 'utf8')));

  // Gleitendes Fenster über 60 s für ALLE Seiten zusammen.
  const stamps = [];
  let pauseUntil = 0;   // nach einem 429 des Servers: alle warten, bis seine Minute um ist
  const apiSlot = async () => {
    const t0 = Date.now();
    for (;;) {
      if (Date.now() < pauseUntil) { await new Promise(res => setTimeout(res, pauseUntil - Date.now())); continue; }
      const now = Date.now();
      while (stamps.length && now - stamps[0] > 60000) stamps.shift();
      if (stamps.length < opts.rate) { stamps.push(now); return now - t0; }
      await new Promise(res => setTimeout(res, 60000 - (now - stamps[0]) + 20));
    }
  };
  const browser = await chromium.launch({ executablePath: chromePath(), args: ['--no-sandbox', '--disable-dev-shm-usage'] });
  const shots = [];
  let done = 0;
  const started = Date.now();

  async function shoot(job) {
    const vp = VIEWPORTS[job.viewport];
    const origin = base(job.route.app);
    const ctx = await browser.newContext({
      viewport: { width: vp.width, height: vp.height }, isMobile: !!vp.isMobile, hasTouch: !!vp.hasTouch,
      deviceScaleFactor: 1, colorScheme: job.theme, reducedMotion: 'reduce', serviceWorkers: 'block',
      locale: 'de-DE', timezoneId: 'Europe/Vienna', ignoreHTTPSErrors: true,
    });
    let toolDelayMs = 0; const apiCalls = [];
    const userJson = job.auth === 'admin' ? JSON.stringify(session) : job.auth === 'user' ? JSON.stringify(plainSession) : null;
    await ctx.addInitScript(([user, theme]) => {
      try {
        if (user) localStorage.setItem('rookhub_user', user); else localStorage.removeItem('rookhub_user');
        localStorage.setItem('rookhub_app_theme', theme);
        localStorage.setItem('rookhub_lang', 'de');
        // Offline-Vorräte nicht vorladen: jede Seite holte sonst ~8 zusätzliche Puzzle-Anfragen (Drossel!).
        localStorage.setItem('rookhub_offline_settings', JSON.stringify({ puzzleCount: 0, endlessRuns: 0 }));
      } catch { /* kein Speicher */ }
    }, [userJson, job.theme]);
    const host = new URL(origin).hostname;
    if (host.endsWith('oberschmid.homes')) {
      await ctx.addCookies([
        { name: 'rookhub_theme', value: job.theme, domain: '.oberschmid.homes', path: '/' },
        { name: 'rookhub_lang', value: 'de', domain: '.oberschmid.homes', path: '/' },
      ]);
    }

    // Diagnose-Meldungen der App nicht an die API schicken (kein Bild hängt daran, und sie zählen gegen die Drossel);
    // alle übrigen API-Aufrufe durch die gemeinsame Bremse.
    await ctx.route(u => u.pathname.startsWith('/api/'), async route => {
      const reqUrl = route.request().url();
      if (reqUrl.includes('/api/client-log')) return route.fulfill({ status: 204, body: '' });
      // Der Endlos-Vorrat lädt auch mit endlessRuns 0 (App ignoriert die Einstellung dort) — außerhalb der
      // Puzzle-Bereiche leer beantworten, dort braucht die Seite die Puzzles selbst.
      if (reqUrl.includes('/api/puzzles/random-batch') && !PUZZLE_AREAS.has(areaOf(job.route))) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' });
      }
      apiCalls.push(new URL(reqUrl).pathname);
      const waited = await apiSlot();
      if (waited > DELAY_FLAG_MS) toolDelayMs = Math.max(toolDelayMs, waited);
      return route.continue();
    });
    const page = await ctx.newPage();
    const consoleErrors = []; const failed = [];
    let inflight = 0; let lastActivity = Date.now();
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300)); });
    page.on('pageerror', e => consoleErrors.push(`Ausnahme: ${String(e.message).slice(0, 300)}`));
    page.on('request', () => { inflight++; lastActivity = Date.now(); });
    const finish = () => { inflight = Math.max(0, inflight - 1); lastActivity = Date.now(); };
    page.on('requestfinished', finish);
    page.on('requestfailed', req => { finish(); if (!/favicon|\.woff/.test(req.url())) failed.push(`${req.method()} ${shortUrl(req.url())} — ${req.failure()?.errorText}`); });
    page.on('response', res => { if (res.status() >= 400 && res.url().includes('/api/')) failed.push(`${res.request().method()} ${shortUrl(res.url())} → ${res.status()}`); });

    const record = { area: areaOf(job.route), id: job.id, app: job.route.app, path: job.route.url, url: job.url, auth: job.auth, viewport: job.viewport, theme: job.theme };
    try {
      await page.goto(origin + job.url, { waitUntil: 'load', timeout: 30000 });
      const t0 = Date.now();
      // „Ruhig": keine offenen Anfragen seit QUIET_MS — Poller (Glocke, Analyse) halten das Netz nie ganz still,
      // deshalb höchstens SETTLE_MAX_MS.
      while (Date.now() - t0 < SETTLE_MAX_MS && !(inflight === 0 && Date.now() - lastActivity > QUIET_MS)) await page.waitForTimeout(150);
      await page.addStyleTag({ content: '*,*::before,*::after{transition:none!important;animation:none!important;caret-color:transparent!important}' });
      await page.waitForTimeout(300);
      record.finalUrl = page.url().replace(origin, '');
      record.findings = await page.evaluate(pageChecks, i18n);
      const h = await page.evaluate(() => document.documentElement.scrollHeight);
      const file = `shots/${job.id}__${job.auth}__${job.viewport}__${job.theme}.png`;
      await page.screenshot({ path: path.join(runDir, file), fullPage: true, clip: { x: 0, y: 0, width: vp.width, height: Math.min(h, MAX_SHOT_HEIGHT) } });
      record.file = file;
      record.truncated = h > MAX_SHOT_HEIGHT;
    } catch (e) {
      record.error = String(e.message).split('\n')[0];
      record.findings = record.findings ?? [];
    }
    record.apiCalls = apiCalls.length;
    if (toolDelayMs) record.toolDelayMs = toolDelayMs;   // Bild evtl. vor den Daten — vom Werkzeug, nicht von der App
    record.console = [...new Set(consoleErrors)].slice(0, 10);
    record.failedRequests = [...new Set(failed)].slice(0, 10);
    if (record.finalUrl && stripQuery(record.finalUrl) !== stripQuery(job.url)) record.redirected = true;
    await ctx.close();
    // Hat der Server trotzdem gedrosselt (sein Fenster zählt auch Anfragen von vorher), zeigt das Bild einen
    // Fehlerzustand der Drossel, nicht der App: eine Minute Pause für alle, dann diese Aufnahme noch einmal.
    if (record.failedRequests.some(r => r.endsWith('→ 429')) && !job.retried) {
      pauseUntil = Math.max(pauseUntil, Date.now() + 61000);
      process.stdout.write(`\n429 vom Server bei ${job.id} — eine Minute Pause, dann neu\n`);
      queue.push({ ...job, retried: true });
      return;
    }
    shots.push(record);
    done++;
    const marks = record.error ? 'FEHLER' : record.findings.length ? `${record.findings.length} Funde` : '';
    process.stdout.write(`\r[${done}/${jobs.length}] ${job.id} ${job.auth}/${job.viewport} ${marks}`.padEnd(110));
  }

  const queue = [...jobs];
  await Promise.all(Array.from({ length: Math.max(1, opts.concurrency) }, async () => {
    while (queue.length) await shoot(queue.shift());
  }));
  await browser.close();
  process.stdout.write('\n');

  // ------------------------------------------------------------------------------------- Vergleich
  const compareName = opts.compare === 'none' ? null : opts.compare ?? previousRun(runName);
  if (compareName) compareWith(path.join(RUNS, compareName), runDir, shots);

  shots.sort((a, b) => jobs.findIndex(j => j.id === a.id) - jobs.findIndex(j => j.id === b.id)
    || a.auth.localeCompare(b.auth) || a.viewport.localeCompare(b.viewport) || a.theme.localeCompare(b.theme));
  const report = {
    run: runName, env: opts.env, local: !!opts.local, startedAt: new Date(started).toISOString(),
    seconds: Math.round((Date.now() - started) / 1000), matrix: { viewport: opts.viewport, theme: opts.theme, auth: opts.auth },
    comparedWith: compareName, params: resolveLog, unresolved, shots,
  };
  fs.writeFileSync(path.join(runDir, 'report.json'), JSON.stringify(report, null, 2));
  writeIndex(runDir, report);

  const withFindings = shots.filter(hasProblems).length;
  const changed = shots.filter(s => s.diff && s.diff.status !== 'same').length;
  console.log(`\nFertig in ${report.seconds}s: ${shots.length} Aufnahmen, ${withFindings} mit Auffälligkeiten`
    + (compareName ? `, ${changed} verändert gegenüber ${compareName}` : '') + `, ${unresolved.length} Routen nicht auflösbar.`);
  console.log(`Übersicht: ${path.join(runDir, 'index.html')}`);
}

export const hasProblems = s => !!(s.findings?.length || s.error || s.console?.length || s.failedRequests?.length);
const shortUrl = u => u.replace(/^https?:\/\/[^/]+/, '').slice(0, 140);
const stripQuery = u => u.replace(/[?#].*$/, '').replace(/\/$/, '');

function previousRun(current) {
  if (!fs.existsSync(RUNS)) return null;
  // Nur Läufe derselben Art (dev/prod, lokal/deployt) — sonst vergliche man Äpfel mit Birnen.
  const kind = d => `${d.includes('_prod') ? 'prod' : 'dev'}${d.includes('_local') ? '-local' : ''}`;
  const runs = fs.readdirSync(RUNS).filter(d => d !== current && kind(d) === kind(current)
    && fs.existsSync(path.join(RUNS, d, 'report.json'))).sort();
  return runs.at(-1) ?? null;
}

function compareWith(oldDir, runDir, shots) {
  if (!fs.existsSync(oldDir)) { console.log(`Vergleich: ${oldDir} gibt es nicht.`); return; }
  fs.mkdirSync(path.join(runDir, 'diff'), { recursive: true });
  for (const s of shots) {
    if (!s.file) continue;
    const old = path.join(oldDir, s.file);
    if (!fs.existsSync(old)) { s.diff = { status: 'new' }; continue; }
    const a = PNG.sync.read(fs.readFileSync(old));
    const b = PNG.sync.read(fs.readFileSync(path.join(runDir, s.file)));
    if (a.width !== b.width || a.height !== b.height) {
      s.diff = { status: 'changed', note: `Größe ${a.width}×${a.height} → ${b.width}×${b.height}`, old: path.relative(runDir, old) };
      continue;
    }
    const out = new PNG({ width: a.width, height: a.height });
    const n = pixelmatch(a.data, b.data, out.data, a.width, a.height, { threshold: 0.1 });
    const ratio = n / (a.width * a.height);
    if (ratio < 0.0005) { s.diff = { status: 'same', ratio }; continue; }
    const diffFile = s.file.replace('shots/', 'diff/');
    fs.writeFileSync(path.join(runDir, diffFile), PNG.sync.write(out));
    s.diff = { status: 'changed', ratio, file: diffFile, old: path.relative(runDir, old) };
  }
}

// Im lokalen Modus halten die Auslieferungs-Server den Prozess offen — nach dem Bericht ausdrücklich beenden.
main().then(() => process.exit(0), e => { console.error(e); process.exit(1); });
