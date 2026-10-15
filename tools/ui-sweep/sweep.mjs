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
import { chromium, webkit, firefox } from 'playwright-core';
import { execFileSync } from 'node:child_process';
import { PNG } from 'pngjs';
import pixelmatch from 'pixelmatch';
import { APPS, AREAS, ROUTES, areaOf, routeId } from './routes.mjs';
import { login, resolveParams, resolveClubParams, ensureTrainer } from './seed.mjs';
import { pageChecks, foldTop, foldScroll, foldFindings, FOLD, FOLD_KINDS } from './checks.mjs';
import { writeIndex, foldSummary } from './report.mjs';
import { withStates, runSteps, writeDecision, stubBody } from './states.mjs';
import { themeSetup, storageFor } from './themes.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '../..');
const RUNS = path.join(HERE, 'runs');

const VIEWPORTS = {
  mobile: { width: 390, height: 844, isMobile: true, hasTouch: true },
  tablet: { width: 820, height: 1180, isMobile: true, hasTouch: true },
  laptop: { width: 1366, height: 768 },
  wide: { width: 1920, height: 1080 },
  // Ultrawide-Monitor des Betreibers (UWQHD 21:9). Nicht in der Vorgabe — nur mit --viewport ultrawide.
  ultrawide: { width: 3440, height: 1440 },
  // E-Ink-Tablet des Betreibers: Boox Tab X C (13,3", Kaleido 3, 3200 × 2400 s/w) — CSS-Viewport ≈ 960 × 1280 hoch.
  // Nicht in der Vorgabe; mit --viewport boox --theme eink. Aufnahme mit Pixeldichte 1 (gleich große Bilder wie die
  // übrigen Breiten), das Gerät selbst rendert mit ≈ 2,5.
  boox: { width: 960, height: 1280, isMobile: true, hasTouch: true },
};
const MAX_SHOT_HEIGHT = 4000;
/** Browser-Sprache je Oberflächensprache (Datums-/Zahlenformat der Seite kommt aus der App, das hier ist navigator.language). */
const BROWSER_LOCALE = { de: 'de-DE', en: 'en-US', hr: 'hr-HR', hu: 'hu-HU' };
const PUZZLE_AREAS = new Set(['puzzles', 'kurse', 'wochenpost', 'woodpecker']);
const SETTLE_MAX_MS = 60000;   // hoch, weil Anfragen in der eigenen Bremse warten können
const QUIET_MS = 800;
// Die API drosselt je Adresse (Prod global 100 Anfragen/min, anonyme Wege teils 60/min; Dev läuft mit
// RateLimiting__PermitScale=20, also 2000/min). Alle Aufnahmen kommen von EINER Adresse — ohne eigene Bremse lief der
// erste Lauf in hunderte 429 und fotografierte Fehlerzustände. Vorgaben je Umgebung, --rate/--concurrency gehen vor.
// Steht Dev noch auf PermitScale 5 (500/min), ausdrücklich `--rate 400 --concurrency 4` geben.
const ENV_DEFAULTS = { dev: { rate: 1600, concurrency: 8 }, prod: { rate: 70, concurrency: 2 } };
// Antwort-Zwischenspeicher (README „Zwischenspeicher"): GET-Antworten dieser Endpunkte sind innerhalb eines Laufs je
// Konto + Sprache gleich und hängen nicht an der fotografierten Seite — die erste 200-Antwort wird gemerkt, jede weitere
// Anfrage (auch auf anderen Seiten) direkt aus dem Speicher beantwortet, ohne die API und ohne die eigene Bremse.
// Nur Pfade (ohne Abfrage) prüfen; der Schlüssel enthält die volle Adresse samt Abfrage. Konservativ halten: nichts, was
// die Seite selbst ausmacht oder sich durch das Ansehen ändert.
// Gemessen 12.10. (runs/…_api-messung, 323 Seiten laptop, --no-cache): diese Endpunkte waren 1 600 von 2 348 Anfragen.
// Bewusst NICHT: Seiteninhalte (Kurs, Repertoire, Partie, Statistik …), Engine-Stand (game-analyses/guess/status),
// alles mit Schreibzugriff in der Nähe (view-state, kids/progress, rh-session).
const CACHEABLE = [
  /^\/api\/menu$/,                         // Menü-Sichtbarkeit (jede Seite)
  /^\/api\/auth\/permissions$/,             // Rechte (PermissionRefresher, jede Seite)
  /^\/api\/notifications\/count$/,          // Glocke (Poller)
  /^\/api\/messages\/unread-count$/,        // Nachrichten-Zähler (Poller)
  /^\/api\/catalog\/access$/,               // Katalog-Freischaltungen
  /^\/api\/courses\/access$/,               // Kurs-Zugang (Menü)
  /^\/api\/puzzles\/rating-range$/,         // Wertungsspanne des Puzzle-Bestands
  /^\/api\/league\/me$/,                    // LeagueHub-Mitgliedschaft
  /^\/api\/club\/link$/,                    // verknüpftes ClubHub-Karteiblatt
  /^\/api\/favorites\/count$/,              // Zähler Turnier-Favoriten
  /^\/api\/subscriptions$/,                  // Turnier-Abos
  /^\/api\/engine\/external$/,              // eingetragene externe Engine
  /^\/api\/repertoires\/explorer\/sources$/, // Explorer-Quellen (Konfiguration)
  /^\/api\/kids\/levels$/,                  // KidHub-Stufen (Katalog)
];
// --fast: nach dem Umstellen der Fenstergröße kurz warten (Breakpoint-Beobachter, Neuberechnung), dann wie nach dem Laden
// auf Netzruhe warten — aber kürzer, weil keine Navigation mehr kommt.
const RESIZE_WAIT_MS = 400;
const RESIZE_SETTLE_MAX_MS = 15000;
const STEP_SETTLE_MAX_MS = 8000;    // nach jedem Bedienschritt (states.mjs) höchstens so lange auf Netzruhe warten
// Wie lange eine Anfrage in der Bremse warten darf, bevor das Bild als „vom Werkzeug verzögert" gilt.
const DELAY_FLAG_MS = 3000;
/** Rollen: `anon` abgemeldet, `admin` = claude-dev, die übrigen sind eigene Dev-Konten (ACCOUNTS weiter unten). */
const ROLES = ['anon', 'user', 'admin', 'empty', 'trainer'];

// ------------------------------------------------------------------------------------------- Optionen
const HELP = `UI-Sweep — Screenshots aller Seiten + automatische Prüfung + Vergleich

  node sweep.mjs [Optionen]          (oder ./sweep.sh, installiert vorher die Abhängigkeiten)

  --env dev|prod         Ziel (Vorgabe dev). prod nur abgemeldet und nur lesend — es wird dort nichts angelegt.
  --app a,b              nur diese Oberflächen (rookhub, turnier, kidhub, leaguehub, clubhub)
  --area a,b             nur diese Bereiche, z. B. kurse, repertoire, partien (Liste: --areas)
  --route text           nur Routen, deren Pfad oder Name den Text enthält (mehrere mit Komma)
  --viewport v,w         mobile (390), tablet (820), laptop (1366), wide (1920), ultrawide (3440×1440)  Vorgabe: mobile,laptop,wide
  --theme t,u            dark, light                                                 Vorgabe: dark
  --lang l,m             Oberflächensprache (de, en, hr, …; Cookie + localStorage rookhub_lang)  Vorgabe: de
  --auth a,b             anon, user (normales Konto mit Bestand), admin (claude-dev),
                         empty (Konto ganz ohne Daten → Leerzustände), trainer (ClubHub-Trainer einer Gruppe)
                                                                                     Vorgabe: anon,user
  --concurrency n        gleichzeitige Seiten (Vorgabe dev 8, prod 2 — mehr läuft in die Drossel der API)
  --rate n               höchstens n API-Anfragen je Minute (Vorgabe dev 1600 — Dev drosselt bei 2000 je Adresse,
                         PermitScale 20; prod 70 — Prod drosselt bei 100). Antworten aus dem Zwischenspeicher zählen nicht
  --fast                 jede Seite (Route × Konto × Design × Sprache) nur EINMAL laden und alle Breiten durch Umstellen
                         der Fenstergröße fotografieren (schmalste zuerst). Grenze: Handy-Ansicht nur über die Breite,
                         ohne Touch — kein isMobile/hasTouch (lässt sich an einem offenen Kontext nicht umschalten),
                         also kein (pointer: coarse)/(hover: none) und keine Touch-Ereignisse. Für die Abnahme ohne --fast.
  --compare run|none     mit diesem Lauf vergleichen (Ordnername unter runs/); Vorgabe: der vorige Lauf
  --local                die gebauten Oberflächen aus src/frontend/app/dist/<app>/browser ausliefern
                         (API-Aufrufe gehen an die gewählte Umgebung) — einen Umbau ansehen, bevor er deployt ist
  --no-strips            keine „Gescrollt"-Streifen (schneller); Ganzseite + „Erster Blick" + Falz-Messung bleiben
  --no-cache             Antworten nicht zwischenspeichern (sonst: wiederkehrende GETs wie Profil, Menü, Zähler je
                         Konto + Sprache einmal holen, danach aus dem Speicher — zählen nicht gegen --rate)
  --list                 Katalog mit aufgelösten Adressen zeigen, keinen Browser starten
  --no-states            ohne die Zustände aus states.mjs (Menüs, Dialoge, Reiter … nach einer Bedienung)
  --states-only          NUR die Zustände (ohne die bloßen Seiten)
  --name text            Zusatz am Ordnernamen des Laufs
  --browser b            chromium (Vorgabe, lokal), webkit (Safari-Engine) oder firefox. webkit/firefox laufen im
                         offiziellen Playwright-Docker-Image (mcr.microsoft.com/playwright, passend zu playwright-core) —
                         lokal fehlen dafür Systembibliotheken. Firefox kennt keine Handy-Emulation (isMobile): dort nur
                         Breite + Touch. Bilder heißen gleich, der Ordner trägt _webkit/_firefox.

Zugang: ~/.config/rookhub/dev-claude.env (ROOKHUB_DEV_USER/ROOKHUB_DEV_PASSWORD) oder UI_SWEEP_USER/UI_SWEEP_PASSWORD;
die Konten user/empty/trainer werden beim ersten Gebrauch auf Dev registriert und dort eingetragen.
Je Aufnahme: Ganzseite (shots/<…>.png, Basis des Pixel-Vergleichs), „Erster Blick" (<…>__first.png, genau ein Fenster)
und bei Seiten über 2 Bildschirmen „Gescrollt"-Streifen (<…>__s1.png …, Fensterhöhe, 10 % Überlappung, höchstens 8 +
Seitenende). Falz-Prüfung (DESIGN.md 4.6): Hauptsache (routes.mjs \`main\`) über der Falz, Vorlauf ≤ 40 %, ≤ 1 Banner,
klebende Leisten ≤ 64/72 px, unten nichts verdeckt, Seitenlänge (≥ 6 Hinweis, ≥ 12 Befund).
Ergebnis: runs/<Zeitpunkt>/index.html (Übersicht), report.json, fold-summary.json, api-calls.json (jede API-Antwort:
Seite, Konto, Adresse, Status, aus dem Speicher ja/nein), shots/*.png, diff/*.png.`;

function parseArgs(argv) {
  const o = { browser: 'chromium', env: 'dev', viewport: ['mobile', 'laptop', 'wide'], theme: ['dark'], lang: ['de'], auth: ['anon', 'user'] };
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
      case '--ui-scale': o.uiScale = Number(v()); break;
      case '--lang': o.lang = csv(); break;
      case '--auth': o.auth = csv(); break;
      case '--concurrency': o.concurrency = Number(v()); break;
      case '--rate': o.rate = Number(v()); break;
      case '--compare': o.compare = v(); break;
      case '--local': o.local = true; break;
      case '--fast': o.fast = true; break;
      case '--no-strips': o.noStrips = true; break;
      case '--no-cache': o.noCache = true; break;
      case '--list': o.list = true; break;
      case '--no-states': o.states = 'none'; break;
      case '--states-only': o.states = 'only'; break;
      case '--name': o.name = v(); break;
      case '--browser': o.browser = v(); break;
      default: throw new Error(`Unbekannte Option ${a} (--help)`);
    }
  }
  if (!['chromium', 'webkit', 'firefox'].includes(o.browser)) throw new Error(`Unbekannter Browser ${o.browser} (chromium, webkit, firefox)`);
  for (const vp of o.viewport) if (!VIEWPORTS[vp]) throw new Error(`Unbekannte Breite ${vp}`);
  if (!ENV_DEFAULTS[o.env]) throw new Error(`Unbekannte Umgebung ${o.env} (dev, prod)`);
  o.rate ??= ENV_DEFAULTS[o.env].rate;
  o.concurrency ??= ENV_DEFAULTS[o.env].concurrency;
  for (const t of o.theme) themeSetup(t);
  o.uiScale ??= 100;
  storageFor(o.theme[0], o.uiScale);   // prüft die Stufe
  for (const a of o.auth) if (!ROLES.includes(a)) throw new Error(`Unbekannte Rolle ${a} (${ROLES.join(', ')})`);
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

const ACCOUNTS = {
  // normales Konto mit Bestand (seed-user.mjs) — die Rolle, mit der die meisten Seiten fotografiert werden
  user: { env: 'ROOKHUB_DEV_PLAIN', name: 'claude-dev-user', label: 'Normales Konto' },
  // frisch registriert, nie benutzt: zeigt die Leerzustände (keine Partie, kein Repertoire, kein Verein …)
  empty: { env: 'ROOKHUB_DEV_EMPTY', name: 'claude-dev-empty', label: 'Leeres Konto' },
  // Rolle mit club.trainer, einer ClubHub-Gruppe zugeteilt (seed.mjs ensureTrainer) — sonst ohne Daten
  trainer: { env: 'ROOKHUB_DEV_TRAINER', name: 'claude-dev-trainer', label: 'ClubHub-Trainer' },
};

/** Zugang eines Dev-Kontos aus ~/.config/rookhub/dev-claude.env (`<ENV>_USER`/`<ENV>_PASSWORD`). Fehlt es, wird es auf
 *  Dev registriert und in die Datei geschrieben (angehängt, das Kennwort wird nie ausgegeben). */
async function accountCredentials(base, role) {
  const acc = ACCOUNTS[role];
  if (role === 'user' && process.env.UI_SWEEP_PLAIN_USER) return { user: process.env.UI_SWEEP_PLAIN_USER, password: process.env.UI_SWEEP_PLAIN_PASSWORD };
  const file = path.join(os.homedir(), '.config/rookhub/dev-claude.env');
  const text = fs.readFileSync(file, 'utf8');
  const env = Object.fromEntries(text.split('\n').filter(l => l.includes('='))
    .map(l => [l.slice(0, l.indexOf('=')).trim(), l.slice(l.indexOf('=') + 1).trim()]));
  if (env[`${acc.env}_USER`]) return { user: env[`${acc.env}_USER`], password: env[`${acc.env}_PASSWORD`] };
  const user = acc.name;
  const password = 'Sw-' + crypto.randomBytes(12).toString('base64url') + '!7';
  const res = await fetch(`${base}/api/auth/register`, { method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ username: user, email: null, password }) });
  if (!res.ok) throw new Error(`${acc.label} anlegen gescheitert: HTTP ${res.status} ${await res.text()}`);
  // Zeilenende prüfen: ohne Umbruch am Dateiende verschmölze die neue Zeile mit der letzten.
  fs.appendFileSync(file, (text.endsWith('\n') ? '' : '\n') + `${acc.env}_USER=${user}\n${acc.env}_PASSWORD=${password}\n`);
  console.log(`${acc.label} ${user} auf Dev angelegt (Zugang in ${file}).`);
  return { user, password };
}

/** Parameter, die an den EIGENEN Daten eines Kontos hängen — mit denen des Admin-Kontos sähe ein anderes Konto nur fremde
 *  404-Seiten. Das normale Konto bekommt eigene (seed.mjs legt sie an), das leere und das Trainer-Konto haben keine:
 *  ihre Routen mit diesen Platzhaltern entfallen still (gezählt als „ohne eigene Daten", nicht als „nicht auflösbar"). */
const OWNED_PARAMS = new Set(['gameId', 'analyzedGameId', 'repertoireId', 'repertoireBlackId', 'repertoireEndgameId', 'worksheetId', 'reconstructionId', 'guessId',
  'analysisId', 'comparisonId', 'friendId', 'bookId', 'calcBookId', 'studyBookId', 'chapterIndex', 'bookPuzzleId', 'scanId']);
/** Parameter, die für das Trainer-Konto aus SEINER Sicht kommen (nur zugeteilte Gruppen/Kinder). */
const TRAINER_PARAMS = new Set(['clubGroupId', 'clubMemberId']);
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

/**
 * WebKit/Firefox im Playwright-Docker-Image (lokal fehlen GTK/GStreamer & Co., Installation bräuchte root): dort läuft
 * `playwright run-server`, wir verbinden uns per WebSocket. --network host, damit Dev-Adressen und --local erreichbar sind.
 * Das Image passt zur Version von playwright-core (Protokoll muss gleich sein).
 */
async function dockerBrowser(name) {
  const version = JSON.parse(fs.readFileSync(path.join(HERE, 'node_modules/playwright-core/package.json'), 'utf8')).version;
  const port = 30000 + (process.pid % 20000);
  const container = `ui-sweep-pw-${process.pid}`;
  execFileSync('docker', ['run', '-d', '--rm', '--init', '--ipc=host', '--network', 'host', '--name', container,
    `mcr.microsoft.com/playwright:v${version}-noble`, '/bin/sh', '-c',
    `cd /tmp && npx -y playwright@${version} run-server --port ${port} --host 127.0.0.1`], { stdio: 'ignore' });
  const stop = () => { try { execFileSync('docker', ['rm', '-f', container], { stdio: 'ignore' }); } catch { /* schon weg */ } };
  process.on('exit', stop);
  for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => { stop(); process.exit(130); });
  const type = { webkit, firefox }[name];
  for (let i = 0; i < 120; i++) {   // npx lädt beim ersten Mal das Paket: bis zu 2 min
    try {
      const b = await type.connect(`ws://127.0.0.1:${port}/`, { timeout: 2000 });
      const close = b.close.bind(b);
      b.close = async () => { await close().catch(() => {}); stop(); };
      return b;
    } catch { await new Promise(r => setTimeout(r, 1000)); }
  }
  stop();
  throw new Error(`${name} im Docker-Image nicht erreichbar (Port ${port}) — \`docker logs ${container}\``);
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
  // Zustände (states.mjs) hängen an Katalog-Routen und werden wie Routen gefiltert (--app, --area, --route auf ihre Id).
  const catalog = opts.states === 'only' ? withStates(ROUTES, routeId)
    : opts.states === 'none' ? ROUTES : [...ROUTES, ...withStates(ROUTES, routeId)];
  const routes = catalog.filter(r => (!opts.app || opts.app.includes(r.app)) && (!opts.area || opts.area.includes(areaOf(r)))
    && (!opts.route || opts.route.some(t => r.url.includes(t) || routeId(r).includes(t))));

  const sessions = {};   // Rolle → AuthResponse (genau das legt die App als `rookhub_user` ab)
  let params = {};
  const roleParams = { user: {}, empty: {}, trainer: {} };
  const cachePath = path.join(RUNS, `params-${opts.env}.json`);
  const previousParams = fs.existsSync(cachePath) ? JSON.parse(fs.readFileSync(cachePath, 'utf8')) : {};
  const resolveLog = [];
  const say = (name, value, why) => console.log(`  ${name.padEnd(22)} ${value instanceof Error ? 'Fehler: ' + value.message : value ?? `— ${why ?? '(nichts gefunden)'}`}`);
  if (opts.env === 'prod') {
    console.log('prod: keine Anmeldung, keine Testdaten — Routen mit Platzhaltern entfallen.');
  } else {
    const { user, password } = credentials();
    sessions.admin = await login(apiBase, user, password);
    console.log(`Angemeldet als ${sessions.admin.username} an ${apiBase}`);
    fs.mkdirSync(RUNS, { recursive: true });
    for (const role of Object.keys(ACCOUNTS).filter(r => opts.auth.includes(r))) {
      const c = await accountCredentials(apiBase, role);
      sessions[role] = await login(apiBase, c.user, c.password);
      if (role === 'user') {
        console.log(`${ACCOUNTS.user.label}: ${sessions.user.username} — eigene Daten:`);
        const userCache = path.join(RUNS, `params-${opts.env}-user.json`);
        const prevUser = fs.existsSync(userCache) ? JSON.parse(fs.readFileSync(userCache, 'utf8')) : {};
        roleParams.user = await resolveParams(apiBase, sessions.user.token, (n, v, why) => OWNED_PARAMS.has(n) && say(n, v, why), prevUser, OWNED_PARAMS);
        // Zusammenführen: ein Resolver, der diesmal scheiterte (429), soll den gemerkten Wert nicht löschen.
        fs.writeFileSync(userCache, JSON.stringify({ ...prevUser, ...roleParams.user }, null, 2));
      } else if (role === 'trainer') {
        const group = await ensureTrainer(apiBase, sessions.admin.token, sessions.trainer);
        console.log(`${ACCOUNTS.trainer.label}: ${sessions.trainer.username} — Gruppe „${group.name}" (${group.id}), sieht:`);
        roleParams.trainer = await resolveClubParams(apiBase, sessions.trainer.token, say);
      } else {
        console.log(`${ACCOUNTS[role].label}: ${sessions[role].username} (keine eigenen Daten)`);
      }
    }
    console.log('Admin-Konto (und geteilte Parameter):');
    params = await resolveParams(apiBase, sessions.admin.token, (name, value, why) => {
      const text = value instanceof Error ? `Fehler: ${value.message}` : value ?? `— ${why ?? '(nichts gefunden)'}`;
      resolveLog.push({ name, value: value instanceof Error ? null : value, note: text });
      say(name, value, why);
    }, previousParams);
    fs.writeFileSync(cachePath, JSON.stringify({ ...previousParams, ...params }, null, 2));
  }

  // Konto-eigene Parameter (Partie, Repertoire …) kommen je Rolle aus deren eigenen Daten, der Rest ist geteilt.
  // `noData` = ein konto-eigener Parameter fehlt bei einem Konto, das absichtlich keine Daten hat (empty/trainer).
  const fill = (url, auth) => {
    const missing = []; let noData = false;
    const src = n => {
      if (auth === 'trainer' && TRAINER_PARAMS.has(n)) return roleParams.trainer[n];
      if (auth !== 'admin' && auth !== 'anon' && OWNED_PARAMS.has(n)) return roleParams[auth][n];
      return params[n];
    };
    const out = url.replace(/\{(\w+)\}/g, (_, n) => {
      const v = src(n);
      if (v == null) { if ((auth === 'empty' || auth === 'trainer') && (OWNED_PARAMS.has(n) || TRAINER_PARAMS.has(n))) noData = true; else missing.push(n); }
      return v ?? '';
    });
    return { url: out, missing, noData };
  };

  const jobs = []; const unresolved = []; let noDataSkipped = 0;
  for (const route of routes) {
    const wanted = route.auth === 'any' ? opts.auth
      : route.auth === 'user' ? opts.auth.filter(a => a !== 'anon') : opts.auth.filter(a => a === route.auth);
    for (const auth of wanted) {
      if (route.stateAuth && !route.stateAuth.includes(auth)) continue;
      if (auth === 'anon' && [...route.url.matchAll(/\{(\w+)\}/g)].some(m => ANON_SKIP.has(m[1]))) continue;
      const { url, missing, noData } = fill(route.url, auth);
      if (missing.length) { unresolved.push({ id: `${routeId(route)} (${auth})`, url: route.url, missing }); continue; }
      if (noData) { noDataSkipped++; continue; }
      for (const viewport of opts.viewport) for (const theme of opts.theme) for (const lang of opts.lang)
        jobs.push({ route, id: routeId(route), url, auth, viewport, theme, lang });
    }
  }

  if (opts.list) {
    const roles = opts.auth;
    console.log(`\n${'Route'.padEnd(46)} ${'auth'.padEnd(5)} Adresse (Admin) / fehlende Parameter je Rolle`);
    for (const r of routes) {
      const { url, missing } = fill(r.url, 'admin');
      const per = roles.filter(a => a !== 'admin' && a !== 'anon' && r.auth !== 'anon').map(a => {
        const f = fill(r.url, a);
        return f.missing.length ? `${a}: fehlt ${f.missing}` : f.noData ? `${a}: (ohne Daten)` : f.url !== url ? `${a}: ${f.url}` : null;
      }).filter(Boolean);
      console.log(`${routeId(r).padEnd(46)} ${r.auth.padEnd(5)} ${missing.length ? `[fehlt: ${missing}]` : url}${per.length ? '   | ' + per.join(' · ') : ''}`);
    }
    console.log(`\n${jobs.length} Aufnahmen, ${unresolved.length} Routen nicht auflösbar${noDataSkipped ? `, ${noDataSkipped} ohne eigene Daten übersprungen` : ''}.`);
    for (const u of unresolved) console.log(`  nicht auflösbar: ${u.id} — ${u.missing.join(', ')}`);
    return;
  }

  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace('T', '_').slice(0, 13);
  const runName = `${stamp}${opts.env === 'prod' ? '_prod' : ''}${opts.local ? '_local' : ''}${opts.fast ? '_fast' : ''}${opts.browser === 'chromium' ? '' : '_' + opts.browser}${opts.name ? '_' + opts.name : ''}`;
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
  // Zwischenspeicher: Schlüssel → Promise<Antwort|null>. Die erste Anfrage legt das Promise an, gleichzeitige warten
  // darauf; endet sie nicht mit 200, wird der Eintrag entfernt und jede Wartende holt selbst.
  const apiCache = new Map();
  const cacheKeyOf = (job, req) => {
    if (opts.noCache || req.method() !== 'GET') return null;
    const u = new URL(req.url());
    if (!CACHEABLE.some(re => re.test(u.pathname))) return null;
    return `${job.auth}|${job.lang}|${u.host}${u.pathname}${u.search}`;
  };
  // Beim Wiedergeben: der Körper ist schon entpackt, Länge/Kodierung stimmen nicht mehr; Cookies nicht noch einmal setzen.
  const DROP_HEADERS = new Set(['content-encoding', 'content-length', 'transfer-encoding', 'set-cookie', 'connection']);
  const apiLog = [];   // → api-calls.json
  const onceCache = new Map(); // Zustände mit `once` (states.mjs): eine echte Antwort je Rolle/Sprache
  const browser = opts.browser === 'chromium'
    ? await chromium.launch({ executablePath: chromePath(), args: ['--no-sandbox', '--disable-dev-shm-usage'] })
    : await dockerBrowser(opts.browser);
  // Firefox kann keine Handy-Emulation (isMobile wirft) — dort nur Breite und Touch.
  const canMobile = opts.browser !== 'firefox';
  const shots = [];
  let done = 0;
  const started = Date.now();

  // Wartet, bis seit QUIET_MS keine Anfrage mehr läuft — Poller (Glocke, Analyse) halten das Netz nie ganz still,
  // deshalb höchstens maxMs.
  const settle = async (page, net, maxMs) => {
    const t0 = Date.now();
    while (Date.now() - t0 < maxMs && !(net.inflight === 0 && Date.now() - net.lastActivity > QUIET_MS)) await page.waitForTimeout(150);
  };

  // Eine Arbeitseinheit = eine Seite (Route × Konto × Design × Sprache) mit ihren Breiten `job.viewports`.
  // Normal ist das genau eine Breite; mit --fast alle, nacheinander durch Umstellen der Fenstergröße.
  async function shoot(job) {
    const first = VIEWPORTS[job.viewports[0]];
    const origin = base(job.route.app);
    const ctx = await browser.newContext({
      // --fast: ohne Handy-Emulation — isMobile/hasTouch lassen sich an einem offenen Kontext nicht umschalten.
      viewport: { width: first.width, height: first.height },
      isMobile: canMobile && !opts.fast && !!first.isMobile, hasTouch: !opts.fast && !!first.hasTouch,
      deviceScaleFactor: 1, colorScheme: themeSetup(job.theme).colorScheme, reducedMotion: 'reduce', serviceWorkers: 'block',
      locale: BROWSER_LOCALE[job.lang] ?? job.lang, timezoneId: 'Europe/Vienna', ignoreHTTPSErrors: true,
    });
    let toolDelayMs = 0; const apiCalls = []; let cacheHits = 0;
    const userJson = sessions[job.auth] ? JSON.stringify(sessions[job.auth]) : null;
    await ctx.addInitScript(([user, theme, lang, seedSolveMode, extra]) => {
      try {
        if (user) localStorage.setItem('rookhub_user', user); else localStorage.removeItem('rookhub_user');
        localStorage.setItem('rookhub_app_theme', theme);
        for (const [k, v] of Object.entries(extra ?? {})) localStorage.setItem(k, v);
        // Sprache: LocaleService liest erst das geteilte Cookie, dann diesen Wert (LeagueHub/ClubHub sind fest deutsch).
        localStorage.setItem('rookhub_lang', lang);
        // Offline-Vorräte nicht vorladen: jede Seite holte sonst ~8 zusätzliche Puzzle-Anfragen (Drossel!).
        localStorage.setItem('rookhub_offline_settings', JSON.stringify({ puzzleCount: 0, endlessRuns: 0 }));
        // Spielweise (Trainings-/Einfachmodus) schon gewählt: sonst deckte der Dialog, der beim ersten Betreten eines
        // Puzzle-/Kursbereichs kommt, JEDE Aufnahme dieser Seiten zu (jeder Lauf beginnt mit leerem Speicher). Den Dialog
        // selbst zeigt der Zustand „spielweise" (states.mjs, fresh).
        if (seedSolveMode && !localStorage.getItem('rookhub_solve_modes')) {
          localStorage.setItem('rookhub_solve_modes', JSON.stringify({ '*': { mode: 'easy', at: 1 }, 'course:*': { mode: 'easy', at: 1 } }));
        }
      } catch { /* kein Speicher */ }
    }, [userJson, themeSetup(job.theme).appTheme, job.lang, !job.route.fresh, storageFor(job.theme, opts.uiScale)]);
    const host = new URL(origin).hostname;
    if (host.endsWith('oberschmid.homes')) {
      await ctx.addCookies([
        { name: 'rookhub_theme', value: themeSetup(job.theme).appTheme, domain: '.oberschmid.homes', path: '/' },
        { name: 'rookhub_lang', value: job.lang, domain: '.oberschmid.homes', path: '/' },
      ]);
    }

    // Diagnose-Meldungen der App nicht an die API schicken (kein Bild hängt daran, und sie zählen gegen die Drossel);
    // alle übrigen API-Aufrufe durch die gemeinsame Bremse.
    // Wiederkehrende GETs (CACHEABLE) aus dem Zwischenspeicher — ohne Bremse, gezählt als cacheHits statt apiCalls.
    await ctx.route(u => u.pathname.startsWith('/api/'), async route => {
      const req = route.request();
      const reqUrl = req.url();
      if (reqUrl.includes('/api/client-log')) return route.fulfill({ status: 204, body: '', headers: { 'x-ui-sweep': 'stub' } });
      // Zustände (states.mjs) schreiben nie: jede schreibende Anfrage beantwortet das Werkzeug selbst (writeDecision) — außer
      // /api/auth/* (ein gestelltes „{}" auf refresh hielt die App für angemeldet) und Lese-POSTs (READ_POSTS).
      const decision = writeDecision(job.route, req.method(), new URL(reqUrl).pathname);
      if (decision === 'stub') {
        return route.fulfill({ status: 200, contentType: 'application/json', body: stubBody(req.postData()), headers: { 'x-ui-sweep': 'stub' } });
      }
      if (decision === 'once') {
        // Einmal echt je Rolle/Sprache, dann dieselbe Antwort (auch 4xx) für alle Varianten.
        const onceKey = `once|${job.auth}|${job.lang}|${req.method()}|${new URL(reqUrl).pathname}`;
        let pending = onceCache.get(onceKey);
        if (!pending) {
          pending = (async () => {
            await apiSlot();
            const res = await route.fetch({ timeout: 60000 });
            const headers = Object.fromEntries(Object.entries(res.headers()).filter(([h]) => !DROP_HEADERS.has(h.toLowerCase())));
            return { status: res.status(), headers, body: await res.body() };
          })().catch(() => null);
          onceCache.set(onceKey, pending);
        }
        const entry = await pending;
        if (!entry) { onceCache.delete(onceKey); return route.abort('failed').catch(() => {}); }
        return route.fulfill({ status: entry.status, headers: { ...entry.headers, 'x-ui-sweep': 'cache' }, body: entry.body });
      }
      // Der Endlos-Vorrat lädt auch mit endlessRuns 0 (App ignoriert die Einstellung dort) — außerhalb der
      // Puzzle-Bereiche leer beantworten, dort braucht die Seite die Puzzles selbst.
      if (reqUrl.includes('/api/puzzles/random-batch') && !PUZZLE_AREAS.has(areaOf(job.route))) {
        return route.fulfill({ status: 200, contentType: 'application/json', body: '[]', headers: { 'x-ui-sweep': 'stub' } });
      }
      const key = cacheKeyOf(job, req);
      for (let pending; key && (pending = apiCache.get(key));) {
        const entry = await pending;
        if (entry) {
          cacheHits++;
          return route.fulfill({ status: entry.status, headers: { ...entry.headers, 'x-ui-sweep': 'cache' }, body: entry.body });
        }
        // gescheitert: der Eintrag ist schon entfernt (oder neu angelegt) — nächste Runde
      }
      apiCalls.push(new URL(reqUrl).pathname);
      let settleEntry = null;
      if (key) { const p = new Promise(r => { settleEntry = r; }); apiCache.set(key, p); }
      const waited = await apiSlot();
      if (waited > DELAY_FLAG_MS) toolDelayMs = Math.max(toolDelayMs, waited);
      if (!key) return route.continue();
      try {
        const res = await route.fetch({ timeout: 60000 });
        const body = await res.body();
        if (res.status() === 200) {
          const headers = Object.fromEntries(Object.entries(res.headers()).filter(([h]) => !DROP_HEADERS.has(h.toLowerCase())));
          settleEntry({ status: 200, headers, body });
        } else {
          apiCache.delete(key); settleEntry(null);
        }
        return await route.fulfill({ response: res, body });
      } catch (e) {
        if (apiCache.get(key)) apiCache.delete(key);
        settleEntry(null);
        return route.abort('failed').catch(() => {});
      }
    });
    const page = await ctx.newPage();
    const consoleErrors = []; const failed = [];
    const net = { inflight: 0, lastActivity: Date.now() };
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 300)); });
    page.on('pageerror', e => consoleErrors.push(`Ausnahme: ${String(e.message).slice(0, 300)}`));
    page.on('request', () => { net.inflight++; net.lastActivity = Date.now(); });
    const finish = () => { net.inflight = Math.max(0, net.inflight - 1); net.lastActivity = Date.now(); };
    page.on('requestfinished', finish);
    page.on('requestfailed', req => { finish(); if (!/favicon|\.woff/.test(req.url())) failed.push(`${req.method()} ${shortUrl(req.url())} — ${req.failure()?.errorText}`); });
    page.on('response', res => {
      if (!res.url().includes('/api/')) return;
      if (res.status() >= 400) failed.push(`${res.request().method()} ${shortUrl(res.url())} → ${res.status()}`);
      const mark = res.headers()['x-ui-sweep'];
      if (mark !== 'stub') apiLog.push({ id: job.id, auth: job.auth, lang: job.lang, viewport: job.viewports.join(','),
        method: res.request().method(), url: shortUrl(res.url()), status: res.status(), cached: mark === 'cache' });
    });

    const records = [];
    let loadError = null;
    try {
      await page.goto(origin + job.url, { waitUntil: 'load', timeout: 30000 });
      await settle(page, net, SETTLE_MAX_MS);
      await page.addStyleTag({ content: '*,*::before,*::after{transition:none!important;animation:none!important;caret-color:transparent!important}' });
      await page.waitForTimeout(300);
    } catch (e) {
      loadError = String(e.message).split('\n')[0];
    }
    // Zustand (states.mjs): Schritte ausführen, dann prüfen, ob er erreicht ist. Die Aufnahme gibt es in jedem Fall.
    let stepError = null; let stateMissed = false;
    if (!loadError && job.route.steps) {
      stepError = await runSteps(page, job.route.steps, () => settle(page, net, STEP_SETTLE_MAX_MS));
      if (!stepError && job.route.expect) {
        stateMissed = !(await page.locator(job.route.expect).first().isVisible().catch(() => false));
      }
    }
    for (const [i, viewport] of job.viewports.entries()) {
      const vp = VIEWPORTS[viewport];
      const record = { area: areaOf(job.route), id: job.id, app: job.route.app, path: job.route.url, url: job.url, auth: job.auth, viewport, theme: job.theme, lang: job.lang };
      if (opts.fast) record.fast = true;
      if (job.route.state) record.state = job.route.state;
      const callsBefore = i === 0 ? 0 : apiCalls.length;   // die erste Breite trägt das Laden
      const hitsBefore = i === 0 ? 0 : cacheHits;
      try {
        if (loadError) throw new Error(loadError);
        if (i > 0) {
          // Gleiche Seite, neue Breite: keine Navigation — Fenster umstellen, Layout nachziehen lassen, dann wie
          // nach dem Laden auf Netzruhe warten (Breakpoints können nachladen) und wieder nach oben scrollen.
          await page.setViewportSize({ width: vp.width, height: vp.height });
          await page.waitForTimeout(RESIZE_WAIT_MS);
          await settle(page, net, RESIZE_SETTLE_MAX_MS);
          await page.evaluate(() => window.scrollTo(0, 0));
          await page.waitForTimeout(100);
        }
        record.finalUrl = page.url().replace(origin, '');
        record.findings = await page.evaluate(pageChecks, i18n);
        if (stepError) record.findings.push({ kind: 'state-step', detail: stepError });
        else if (stateMissed) record.findings.push({ kind: 'state-missed', detail: `nach den Schritten nicht sichtbar: ${job.route.expect}` });
        // Erster Blick (DESIGN.md 4.6): Messung oben, bevor irgendetwas gescrollt wird.
        const mainSel = job.route.main == null ? [] : [].concat(job.route.main);
        const fold = await page.evaluate(foldTop, { main: mainSel });
        const h = await page.evaluate(() => document.documentElement.scrollHeight);
        // Deutsch ohne Zusatz im Namen — so bleiben die Bilder mit Läufen von vor der Sprach-Achse vergleichbar.
        // Auch mit --fast dieselben Namen: Vergleich mit normalen Läufen greift.
        const stem = `shots/${job.id}__${job.auth}__${viewport}__${job.theme}${opts.uiScale !== 100 ? '__s' + opts.uiScale : ''}${job.lang === 'de' ? '' : '__' + job.lang}`;
        const file = `${stem}.png`;
        await page.screenshot({ path: path.join(runDir, file), fullPage: true, clip: { x: 0, y: 0, width: vp.width, height: Math.min(h, MAX_SHOT_HEIGHT) } });
        record.file = file;
        record.truncated = h > MAX_SHOT_HEIGHT;
        // „Erster Blick": genau ein Fenster, oben.
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.waitForTimeout(100);
        record.firstShot = `${stem}__first.png`;
        await page.screenshot({ path: path.join(runDir, record.firstShot) });
        // Nach dem Scrollen: klebende Leisten, Verdeckung unten.
        Object.assign(fold, await page.evaluate(foldScroll, { waitMs: FOLD.scrollWaitMs }));
        // „Gescrollt": fensterhohe Streifen mit 10 % Überlappung, nur bei Seiten über 2 Bildschirmen. Klebende Leisten
        // bleiben sichtbar — sie gehören zur Prüfung (Regel 9).
        record.strips = [];
        if (!opts.noStrips && fold.screens > FOLD.stripsMinScreens) {
          const step = Math.round(fold.vh * (1 - FOLD.stripOverlap));
          const maxY = Math.max(0, fold.scrollHeight - fold.vh);
          const ys = [];
          for (let y = step; y < maxY && ys.length < FOLD.maxStrips; y += step) ys.push(y);
          if (!ys.length || ys.at(-1) < maxY) ys.push(maxY);   // immer auch das Seitenende
          for (const [k, y] of ys.entries()) {
            await page.evaluate(yy => window.scrollTo(0, yy), y);
            await page.waitForTimeout(FOLD.scrollWaitMs);
            const strip = `${stem}__s${k + 1}.png`;
            await page.screenshot({ path: path.join(runDir, strip) });
            record.strips.push(strip);
          }
          await page.evaluate(() => window.scrollTo(0, 0));
          await page.waitForTimeout(100);
        }
        fold.exempt = !!job.route.foldExempt;
        record.fold = fold;
        // Weitergeleitet (Anmeldung, fehlendes Recht, andere Oberfläche): die Hauptsache dieser Route ist gar nicht da —
        // kein Katalog-Hinweis, „weitergeleitet" steht schon im Bericht.
        const movedAway = stripQuery(record.finalUrl) !== stripQuery(job.url);
        record.findings.push(...foldFindings(fold, viewport, { exempt: fold.exempt, mainDeclared: mainSel.length > 0 && !movedAway }));
      } catch (e) {
        record.error = String(e.message).split('\n')[0];
        record.findings = record.findings ?? [];
      }
      // Bei --fast: die Anfragen dieser Breite (die erste trägt das Laden); Konsole/Anfragen bis hierhin.
      // apiCalls = echte Anfragen an die API (durch die Bremse), cacheHits = aus dem Zwischenspeicher beantwortet.
      record.apiCalls = apiCalls.length - (opts.fast ? callsBefore : 0);
      record.cacheHits = cacheHits - (opts.fast ? hitsBefore : 0);
      if (toolDelayMs) record.toolDelayMs = toolDelayMs;   // Bild evtl. vor den Daten — vom Werkzeug, nicht von der App
      record.console = [...new Set(consoleErrors)].slice(0, 10);
      record.failedRequests = [...new Set(failed)].slice(0, 10);
      if (record.finalUrl && stripQuery(record.finalUrl) !== stripQuery(job.url)) record.redirected = true;
      records.push(record);
    }
    await ctx.close();
    // Hat der Server trotzdem gedrosselt (sein Fenster zählt auch Anfragen von vorher), zeigt das Bild einen
    // Fehlerzustand der Drossel, nicht der App: eine Minute Pause für alle, dann diese Seite (alle Breiten) noch einmal.
    if (records.some(r => r.failedRequests.some(f => f.endsWith('→ 429'))) && !job.retried) {
      pauseUntil = Math.max(pauseUntil, Date.now() + 61000);
      pauses429++;
      process.stdout.write(`\n429 vom Server bei ${job.id} — eine Minute Pause, dann neu\n`);
      queue.push({ ...job, retried: true });
      return;
    }
    for (const record of records) {
      shots.push(record);
      done++;
      const marks = record.error ? 'FEHLER' : record.findings.length ? `${record.findings.length} Funde` : '';
      process.stdout.write(`\r[${done}/${jobs.length}] ${job.id} ${job.auth}/${record.viewport}/${job.theme}/${job.lang} ${marks}`.padEnd(110));
    }
  }

  // Arbeitseinheiten: normal je Aufnahme eine, mit --fast je Seite eine mit allen Breiten. Reihenfolge der Breiten
  // bei --fast: schmalste zuerst (siehe README „--fast").
  const units = [];
  if (opts.fast) {
    const byPage = new Map();
    for (const j of jobs) {
      // Zustände nicht zusammenlegen: Umstellen der Breite schließt Menüs/Dialoge oder verschiebt sie.
      if (j.route.steps) { units.push({ ...j, viewports: [j.viewport] }); continue; }
      const key = `${j.id}|${j.auth}|${j.theme}|${j.lang}`;
      if (!byPage.has(key)) { const u = { ...j, viewports: [] }; delete u.viewport; byPage.set(key, u); units.push(u); }
      byPage.get(key).viewports.push(j.viewport);
    }
    for (const u of units) u.viewports.sort((a, b) => VIEWPORTS[a].width - VIEWPORTS[b].width);
  } else {
    for (const j of jobs) units.push({ ...j, viewports: [j.viewport] });
  }
  let pauses429 = 0;
  console.log(`${jobs.length} Aufnahmen${opts.fast ? ` aus ${units.length} Seitenladungen (--fast: Handy-Ansicht nur über die Breite, ohne Touch)` : ''}, `
    + `${opts.concurrency} gleichzeitig, höchstens ${opts.rate} API-Anfragen/min`);
  const queue = [...units];
  await Promise.all(Array.from({ length: Math.max(1, opts.concurrency) }, async () => {
    while (queue.length) await shoot(queue.shift());
  }));
  await browser.close();
  process.stdout.write('\n');

  // ------------------------------------------------------------------------------------- Vergleich
  const compareName = opts.compare === 'none' ? null : opts.compare ?? previousRun(runName);
  if (compareName) compareWith(path.join(RUNS, compareName), runDir, shots);

  shots.sort((a, b) => jobs.findIndex(j => j.id === a.id) - jobs.findIndex(j => j.id === b.id)
    || a.auth.localeCompare(b.auth) || a.viewport.localeCompare(b.viewport) || a.theme.localeCompare(b.theme) || (a.lang ?? '').localeCompare(b.lang ?? ''));
  const report = {
    run: runName, env: opts.env, local: !!opts.local, fast: !!opts.fast, rate: opts.rate, concurrency: opts.concurrency,
    cache: !opts.noCache, apiCalls: shots.reduce((n, s) => n + (s.apiCalls ?? 0), 0), cacheHits: shots.reduce((n, s) => n + (s.cacheHits ?? 0), 0),
    pauses429, startedAt: new Date(started).toISOString(),
    seconds: Math.round((Date.now() - started) / 1000), matrix: { viewport: opts.viewport, theme: opts.theme, lang: opts.lang, auth: opts.auth, uiScale: opts.uiScale },
    comparedWith: compareName, params: resolveLog, unresolved, noDataSkipped, shots,
  };
  fs.writeFileSync(path.join(runDir, 'report.json'), JSON.stringify(report, null, 2));
  fs.writeFileSync(path.join(runDir, 'api-calls.json'), '[\n' + apiLog.map(e => JSON.stringify(e)).join(',\n') + '\n]\n');
  writeIndex(runDir, report);
  const summary = foldSummary(report);
  fs.writeFileSync(path.join(runDir, 'fold-summary.json'), JSON.stringify(summary, null, 2));

  const withFindings = shots.filter(hasProblems).length;
  const changed = shots.filter(s => s.diff && s.diff.status !== 'same').length;
  console.log(`\nFertig in ${report.seconds}s: ${shots.length} Aufnahmen, ${withFindings} mit Auffälligkeiten`
    + (compareName ? `, ${changed} verändert gegenüber ${compareName}` : '') + `, ${unresolved.length} Routen nicht auflösbar, ${pauses429} Pausen wegen 429.`);
  console.log(`API: ${report.apiCalls} Anfragen an den Server, ${opts.noCache ? 'Zwischenspeicher aus (--no-cache)' : `${report.cacheHits} aus dem Zwischenspeicher`}.`);
  console.log(`Falz: ${Object.entries(summary.byKind).map(([k, n]) => `${FOLD_KINDS[k] ?? k} ${n}`).join(', ') || 'keine Befunde'}; `
    + `Bilder: ${summary.images.full} Ganzseite, ${summary.images.first} Erster Blick, ${summary.images.strips} Streifen.`);
  console.log(`Übersicht: ${path.join(runDir, 'index.html')}`);
}

/** Auffällig = ein Befund (Hinweise mit level 'hint' zählen nicht), ein Fehler, Konsole oder fehlgeschlagene Anfrage. */
export const hasProblems = s => !!(s.findings?.some(f => f.level !== 'hint') || s.error || s.console?.length || s.failedRequests?.length);
const shortUrl = u => u.replace(/^https?:\/\/[^/]+/, '').slice(0, 140);
const stripQuery = u => u.replace(/[?#].*$/, '').replace(/\/$/, '');

function previousRun(current) {
  if (!fs.existsSync(RUNS)) return null;
  // Nur Läufe derselben Art (dev/prod, lokal/deployt) — sonst vergliche man Äpfel mit Birnen.
  const kind = d => `${d.includes('_prod') ? 'prod' : 'dev'}${d.includes('_local') ? '-local' : ''}`;
  // Ein normaler Lauf vergleicht nicht gegen einen --fast-Lauf (ohne Touch, Layout-Reste) — umgekehrt schon.
  const runs = fs.readdirSync(RUNS).filter(d => d !== current && kind(d) === kind(current)
    && (current.includes('_fast') || !d.includes('_fast'))
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
