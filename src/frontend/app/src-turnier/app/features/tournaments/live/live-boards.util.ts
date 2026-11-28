import { Chess } from 'chess.js';
import { LiveEval, LiveGame } from './live-boards.models';

/**
 * Engine-Balken an/aus — Gerätevorliebe (`core/local-storage-keys.ts`, Klasse `device`), jeder Zuschauer selbst (auch in der
 * geteilten Ansicht, 1.039.0). Standard AN: ohne gemerkte Wahl (oder bei gesperrtem Speicher) zeigt die Seite den Balken.
 */
export const LIVE_EVAL_KEY = 'rookhub_live_eval';

export function readLiveEval(): boolean {
  return readOnByDefault(LIVE_EVAL_KEY);
}

/** Gemerkter Schalter, Standard an: nur `'0'`/`'false'` heißt aus (ui-prefs-sync kann den Wert vom Konto übernehmen). */
function readOnByDefault(key: string): boolean {
  try {
    const v = localStorage.getItem(key);
    return v !== '0' && v !== 'false';
  } catch { return true; }
}

export function writeLiveEval(on: boolean): void {
  try { localStorage.setItem(LIVE_EVAL_KEY, on ? '1' : '0'); } catch { /* gesperrter Speicher: gilt dann nur bis zum Neuladen */ }
}

/**
 * Weißanteil des Balkens in Prozent (0–100), dieselbe Kurve wie Lichess (Gewinnwahrscheinlichkeit aus Centipawns).
 * Matt → 100 bzw. 0; keine Bewertung → `null` (Balken leer).
 */
export function whiteShare(ev: LiveEval | null | undefined): number | null {
  if (!ev) return null;
  if (ev.mate !== null && ev.mate !== undefined) return ev.mate > 0 ? 100 : ev.mate < 0 ? 0 : 50;
  if (ev.cp === null || ev.cp === undefined) return null;
  const cp = Math.max(-1000, Math.min(1000, ev.cp));
  return Math.round(50 + 50 * (2 / (1 + Math.exp(-0.00368208 * cp)) - 1));
}

/** „+0.4", „−1.2", „#3", „#−2" (Weiß-Sicht). */
export function evalText(ev: LiveEval | null | undefined): string {
  if (!ev) return '';
  if (ev.mate !== null && ev.mate !== undefined) return ev.mate < 0 ? `#−${-ev.mate}` : `#${ev.mate}`;
  if (ev.cp === null || ev.cp === undefined) return '';
  const p = (Math.abs(ev.cp) / 100).toFixed(1);
  return ev.cp > 0 ? `+${p}` : ev.cp < 0 ? `−${p}` : '0.0';
}

/** Letzter Zug als Chessground-Paar (Rochade „e1g1" bleibt der Königszug). */
export function lastMovePair(uci: string | null | undefined): [string, string] | undefined {
  return uci && uci.length >= 4 ? [uci.slice(0, 2), uci.slice(2, 4)] : undefined;
}

/** Punkte je Seite aus dem Ergebnis. */
export function points(result: string): [number, number] {
  switch (result) {
    case '1-0': return [1, 0];
    case '0-1': return [0, 1];
    case '1/2-1/2': return [0.5, 0.5];
    default: return [0, 0];
  }
}

/** „1½" statt „1.5" ist hübscher, aber die Turnierseite zeigt Ergebnisse überall als Dezimalzahl (`localScore`). */
export function scoreText(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1);
}

/**
 * Restzeit der Seite am Zug läuft seit dem Eintreffen der Partie weiter (die Übertragung kennt den Zeitpunkt des Zugs
 * nicht — Näherung). Fertige Partien und die wartende Seite stehen.
 */
export function shownClock(game: LiveGame, white: boolean, receivedAt: number, now: number): number | null {
  const base = white ? game.white.clockSeconds : game.black.clockSeconds;
  if (base === null || base === undefined) return null;
  if (game.finished || game.whiteToMove !== white || game.plies < 2) return base;
  return Math.max(0, base - Math.floor(Math.max(0, now - receivedAt) / 1000));
}

/**
 * So lange bleibt bei einem Abriss „Live“ stehen (Bretter mit letztem Stand, kein Spinner), bevor „Verbindung
 * unterbrochen“ erscheint (1.025.0, Wunsch 2026-11-26: das Blinken live ↔ unterbrochen störte). Der Neuaufbau läuft still.
 */
export const LIVE_LOST_GRACE_MS = 10000;

/** So viele Bretter derselben Runde nimmt `?add=` höchstens (wie `OgMetaService.MaxAddedBoards`). */
export const LIVE_MAX_ADDED = 24;
/** Fremde Bretter je Seite: höchstens 12 aus höchstens 4 Runden (wie `ExternalRoundRegistry` im Server). */
export const LIVE_MAX_EXT = 12;
export const LIVE_MAX_EXT_ROUNDS = 4;

const ADD_KEY = /^(\d{1,3}(\.\d{1,3})?|[A-Za-z0-9]{8})$/;
const EXT_KEY = /^([A-Za-z0-9]{8}\.[A-Za-z0-9]{8}|cc~[A-Za-z0-9][A-Za-z0-9-]{0,99}~[A-Za-z0-9_-]{1,40}\.[A-Za-z0-9_-]{1,80})$/;
const LICHESS_ID = /^[A-Za-z0-9]{8}$/;
/** chess.com (1.040.0, wie `ChessComEvents` im Server): Runde `cc~{event}~{runde}`, Partie = chess.com-Slug. */
const CC_ROUND = /^cc~([A-Za-z0-9][A-Za-z0-9-]{0,99})~([A-Za-z0-9_-]{1,40})$/;
const CC_TOUR = /^cc~([A-Za-z0-9][A-Za-z0-9-]{0,99})$/;
const CC_SLUG = /^[A-Za-z0-9][A-Za-z0-9-]{0,99}$/;
const CC_ROUND_SLUG = /^[A-Za-z0-9_-]{1,40}$/;
const CC_GAME = /^[A-Za-z0-9_-]{1,80}$/;
const CC_TABS = new Set(['games', 'results', 'schedule', 'info', 'news', 'players', 'pairings', 'standings', 'dashboard', 'bracket', 'teams']);
const CC_RESERVED = new Set(['current', 'finished', 'favorites', 'results', 'pgn', 'v1', 'tab', 'embed', 'graphics', 'upcoming', 'past']);

/** Runde eines `?ext=`-Schlüssels (`roundId.gameId`; keine der Kennungen enthält einen Punkt). */
export function extRoundOf(key: string): string { return key.slice(0, key.indexOf('.')); }

/** Lichess-Runde (acht Zeichen) oder chess.com-Runde (`cc~event~runde`)? */
export function isLiveRoundId(id: string | null | undefined): boolean { return !!id && (LICHESS_ID.test(id) || CC_ROUND.test(id)); }

/** Passt die Partie-Kennung zur Quelle der Runde (Lichess acht Zeichen, chess.com Slug)? */
export function isLiveGameIdFor(roundId: string, gameId: string): boolean {
  return CC_ROUND.test(roundId) ? CC_GAME.test(gameId) : LICHESS_ID.test(gameId);
}

/** Link einer Runde bei ihrer Quelle: Lichess-Runde, bei chess.com die Partien-Seite des Events (wie `ExternalRoundRegistry.RoundUrl`). */
export function broadcastRoundUrl(roundId: string): string {
  const cc = CC_ROUND.exec(roundId);
  return cc ? `https://www.chess.com/events/${cc[1]}/games` : `https://lichess.org/broadcast/-/-/${roundId}`;
}

/** Kurzname der Quelle eines Übertragungs-Links für die Liste „Übertragungen“ (`chess.com` bzw. `lichess.org`). */
export function broadcastHost(url: string | null | undefined): string {
  return /^https?:\/\/([a-z0-9-]+\.)*chess\.com(\/|$)/i.test(url ?? '') ? 'chess.com' : 'lichess.org';
}

/**
 * `?add=` — hinzugefügte Bretter DERSELBEN Runde (1.025.0). Schlüssel nach der chess-results-Paarung, damit der Link auch
 * vor Rundenbeginn (noch keine Lichess-Kennung) und nach einem Neuaufsetzen der Übertragung stimmt: Mannschaftsturnier
 * `Begegnung.Brett` („3.2“), Einzelturnier `Brett` („7“), übertragene Partie ohne Brett ihre Lichess-Kennung.
 */
export function parseAddParam(v: string | null): string[] {
  return distinct((v ?? '').split(',').map(x => x.trim()).filter(x => ADD_KEY.test(x))).slice(0, LIVE_MAX_ADDED);
}

/** `?ext=roundId.gameId,…` — Bretter aus fremden Übertragungen (Lichess-Kennungen sind dort ab dem Einfügen bekannt). */
export function parseExtParam(v: string | null): string[] {
  const out: string[] = [];
  const rounds = new Set<string>();
  for (const x of distinct((v ?? '').split(',').map(s => s.trim()).filter(s => EXT_KEY.test(s)))) {
    const r = extRoundOf(x);
    if (!rounds.has(r) && rounds.size >= LIVE_MAX_EXT_ROUNDS) continue;
    rounds.add(r);
    out.push(x);
    if (out.length >= LIVE_MAX_EXT) break;
  }
  return out;
}

/** Leere Liste → `null` (der Parameter fällt aus der Adresse). */
export function joinParam(list: readonly string[]): string | null {
  return list.length ? list.join(',') : null;
}

/** Was ein Übertragungs-Link nennt. chess.com: `tourId` = `cc~event` (bei einem Gruppen-Link `/events/info/…` `null`, der
 *  Server wählt den Raum), `roundId` = `cc~event~runde`, `gameId` = Partie-Slug. */
export interface BroadcastLink { source: 'lichess' | 'chesscom'; tourId: string | null; roundId: string | null; gameId: string | null }

/**
 * Link einer Übertragung für „Brett hinzufügen“ / „Verknüpfen“ prüfen (wie `LichessBroadcasts.BoardLinkOf` und
 * `ChessComEvents.LinkOf` im Server):
 * - Lichess: nur lichess.org/broadcast, Turnier- (`/slug/tourId`), Runden- (`/slug/runde/roundId`) oder Partie-Link
 *   (`/slug/runde/roundId/gameId`), jede Kennung acht Buchstaben/Ziffern;
 * - chess.com (1.040.0): `chess.com/events/{event}` (auch `/games`, `/results` …), `/events/{event}/{runde}[/{partie}]`,
 *   `/events/{event}/dashboard/{runde}/{partie}`, `/events/info/{gruppe}`; Sprachpräfix (`/de/events/…`) und `*.chess.com` erlaubt.
 * `null` = ungültig.
 */
export function parseBroadcastLink(input: string): BroadcastLink | null {
  let url: URL;
  const s = input.trim();
  if (!s || s.length > 400) return null;
  try { url = new URL(s.includes('://') ? s : 'https://' + s); } catch { return null; }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') return null;
  if (/^([a-z0-9-]+\.)*chess\.com$/i.test(url.hostname)) return parseChessComLink(url);
  if (!/^(www\.)?lichess\.org$/i.test(url.hostname)) return null;
  const parts = url.pathname.split('/').filter(Boolean);
  if (parts[0] !== 'broadcast') return null;
  const p = parts.slice(1);
  if (p.length === 2 && LICHESS_ID.test(p[1])) return { source: 'lichess', tourId: p[1], roundId: null, gameId: null };
  if (p.length === 3 && LICHESS_ID.test(p[2])) return { source: 'lichess', tourId: null, roundId: p[2], gameId: null };
  if (p.length === 4 && LICHESS_ID.test(p[2]) && LICHESS_ID.test(p[3])) return { source: 'lichess', tourId: null, roundId: p[2], gameId: p[3] };
  return null;
}

function parseChessComLink(url: URL): BroadcastLink | null {
  let parts: string[];
  try { parts = url.pathname.split('/').filter(Boolean).map(decodeURIComponent); } catch { return null; }
  if (parts.length && /^[a-z]{2,3}(-[A-Za-z]{2})?$/.test(parts[0]) && parts[0] !== 'events') parts.shift();
  if (parts.length < 2 || parts[0].toLowerCase() !== 'events') return null;
  parts.shift();
  const cc = (room: string, round: string | null, game: string | null): BroadcastLink =>
    ({ source: 'chesscom', tourId: 'cc~' + room, roundId: round ? `cc~${room}~${round}` : null, gameId: game });
  if (parts[0].toLowerCase() === 'info') {
    return parts.length >= 2 && CC_SLUG.test(parts[1]) ? { source: 'chesscom', tourId: null, roundId: null, gameId: null } : null;
  }
  const room = parts[0];
  if (CC_RESERVED.has(room.toLowerCase()) || !CC_SLUG.test(room)) return null;
  const rest = parts.slice(1);
  if (rest.length >= 3 && rest[0].toLowerCase() === 'dashboard') rest.shift();
  if (rest.length === 0 || (rest.length === 1 && CC_TABS.has(rest[0].toLowerCase()))) return cc(room, null, null);
  if (rest.length === 1) return CC_ROUND_SLUG.test(rest[0]) ? cc(room, rest[0], null) : null;
  if (CC_TABS.has(rest[0].toLowerCase())) return cc(room, null, null);
  return CC_ROUND_SLUG.test(rest[0]) && CC_GAME.test(rest[1]) ? cc(room, rest[0], rest[1]) : null;
}

/** chess.com-Turnierkennung (`cc~event`)? */
export function isChessComTour(id: string | null | undefined): boolean { return !!id && CC_TOUR.test(id); }

function distinct(list: string[]): string[] { return [...new Set(list)]; }

/** „Mittal, Aditya" = „Aditya Mittal": Wörter sortiert, ohne Komma, klein. */
export function samePlayer(a: string, b: string): boolean {
  const key = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').split(/[\s,]+/).filter(Boolean).sort().join(' ');
  return key(a) === key(b);
}

/** Zugnummer einer Live-Partie für die Karte (Wunsch 2026-11-26 „Zuganzahl anzeigen“): läuft sie, der Zug, der gerade
 *  gespielt wird (`floor(plies/2) + 1`, z. B. 33 Halbzüge → Zug 17, Schwarz am Zug); fertig der letzte gespielte
 *  (`ceil(plies/2)`); vor dem ersten Zug `null`. */
export function liveMoveNumber(game: Pick<LiveGame, 'plies' | 'finished'>): number | null {
  if (!game.plies || game.plies < 1) return null;
  return game.finished ? Math.ceil(game.plies / 2) : Math.floor(game.plies / 2) + 1;
}

/** Bearbeitungsschlüssel geteilter Ansichten, die dieser Browser angelegt hat (`token → key`, Gerätevorliebe `device`:
 *  ein Gast soll seine Ansicht weiter ändern können; höchstens {@link LIVE_VIEW_KEYS_MAX}, älteste raus). */
export const LIVE_VIEW_KEYS = 'rookhub_live_view_keys';
export const LIVE_VIEW_KEYS_MAX = 50;

function readViewKeys(): [string, string][] {
  try {
    const v = JSON.parse(localStorage.getItem(LIVE_VIEW_KEYS) ?? '[]') as unknown;
    return Array.isArray(v) ? v.filter((x): x is [string, string] => Array.isArray(x) && typeof x[0] === 'string' && typeof x[1] === 'string') : [];
  } catch { return []; }
}

export function readViewKey(token: string): string | null {
  return readViewKeys().find(([t]) => t === token)?.[1] ?? null;
}

/** Merkt den Schlüssel; `false` = Speicher gesperrt/voll (die Ansicht lässt sich dann nur bis zum Neuladen ändern). */
export function writeViewKey(token: string, key: string): boolean {
  const list = [[token, key] as [string, string], ...readViewKeys().filter(([t]) => t !== token)].slice(0, LIVE_VIEW_KEYS_MAX);
  try { localStorage.setItem(LIVE_VIEW_KEYS, JSON.stringify(list)); return true; } catch { return false; }
}

/** Besitzer einer geteilten Ansicht: so lange nach der letzten Änderung, bis sie gespeichert wird (mehrere Klicks = ein PUT). */
export const LIVE_VIEW_SAVE_MS = 400;

/** „Besten Zug zeigen“ an/aus (1.030.0) — Gerätevorliebe (`device`), bewusst NICHT in der Adresse/geteilten Ansicht: jeder Zuschauer schaltet selbst. Standard AN (1.039.0). */
export const LIVE_BEST_KEY = 'rookhub_live_best';

export function readLiveBest(): boolean {
  return readOnByDefault(LIVE_BEST_KEY);
}

export function writeLiveBest(on: boolean): void {
  try { localStorage.setItem(LIVE_BEST_KEY, on ? '1' : '0'); } catch { /* gesperrter Speicher: gilt bis zum Neuladen */ }
}

/**
 * Bester Zug (UCI) in der Stellung `fen` → Pfeil + SAN; `null`, wenn er dort nicht legal ist (Bewertung gehört dann zu einer
 * anderen Stellung — lieber kein Pfeil als ein falscher). Rochade auch als „König schlägt Turm“ (`e1h1`), wie manche Engines.
 */
export function liveBestMove(fen: string, uci: string | null | undefined): { from: string; to: string; san: string } | null {
  if (!uci || !/^[a-h][1-8][a-h][1-8][qrbn]?$/.test(uci)) return null;
  let c: Chess;
  try { c = new Chess(fen); } catch { return null; }
  const tryMove = (from: string, to: string) => {
    try { return c.move({ from, to, promotion: uci.length > 4 ? uci[4] : undefined }); } catch { return null; }
  };
  let from = uci.slice(0, 2);
  let to = uci.slice(2, 4);
  const piece = c.get(from as Parameters<Chess['get']>[0]);
  if (piece?.type === 'k' && (from === 'e1' || from === 'e8') && (to[0] === 'h' || to[0] === 'a') && to[1] === from[1]) {
    to = (to[0] === 'h' ? 'g' : 'c') + from[1];
  }
  const mv = tryMove(from, to);
  return mv ? { from, to, san: mv.san } : null;
}

/** Von Hand gedrehte Bretter (1.031.0): Partie-Kennungen, nur diese Sitzung (`session`), höchstens {@link LIVE_TURNED_MAX}. */
export const LIVE_TURNED_KEY = 'rookhub_live_turned';
export const LIVE_TURNED_MAX = 100;

export function readTurned(): ReadonlySet<string> {
  try {
    const v = JSON.parse(sessionStorage.getItem(LIVE_TURNED_KEY) ?? '[]') as unknown;
    return new Set(Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string').slice(0, LIVE_TURNED_MAX) : []);
  } catch { return new Set(); }
}

export function writeTurned(ids: ReadonlySet<string>): void {
  try { sessionStorage.setItem(LIVE_TURNED_KEY, JSON.stringify([...ids].slice(-LIVE_TURNED_MAX))); } catch { /* gesperrt: gilt bis zum Neuladen */ }
}

/**
 * Fenster für „Auf dem Analysebrett öffnen“: in der installierten App (PWA `display-mode: standalone` bzw. Android-TWA,
 * Referrer `android-app://…`) im selben Fenster — ein neuer Tab risse dort aus der App heraus; im Browser ein neuer Tab.
 */
export function analysisLinkTarget(win: Pick<Window, 'matchMedia'> | null = typeof window === 'undefined' ? null : window,
  referrer: string = typeof document === 'undefined' ? '' : document.referrer): '_self' | '_blank' {
  let standalone = false;
  try { standalone = !!win?.matchMedia?.('(display-mode: standalone)').matches; } catch { /* alter Browser */ }
  return standalone || referrer.startsWith('android-app://') ? '_self' : '_blank';
}

/**
 * Beendete Partien ans Ende (Wunsch 2026-11-28: „wenn ein Spiel fertig ist, dann wandert es ans Ende“): laufende behalten
 * ihre Reihenfolge vorne, beendete folgen in ihrer bisherigen Reihenfolge (stabil). `groupOf` hält Gruppen zusammen
 * (Begegnungen der Übersicht): jede Gruppe bleibt ein Block an der Stelle ihres ersten Bretts, sortiert wird nur darin.
 * Reine Darstellung — die gespeicherte Auswahl (Adresse, geteilte Ansicht) bleibt unberührt.
 */
export function finishedLast<T>(boards: readonly T[], isFinished: (b: T) => boolean, groupOf: (b: T) => string = () => ''): T[] {
  const groups = new Map<string, { running: T[]; done: T[] }>();
  for (const b of boards) {
    const key = groupOf(b);
    let g = groups.get(key);
    if (!g) { g = { running: [], done: [] }; groups.set(key, g); }
    (isFinished(b) ? g.done : g.running).push(b);
  }
  const out: T[] = [];
  for (const g of groups.values()) out.push(...g.running, ...g.done);
  return out;
}

/** Positionen der Karten im Raster (relativ zum Raster, also unabhängig vom Scrollen) für die FLIP-Animation. */
export type CardPositions = ReadonlyMap<string, { x: number; y: number }>;

/**
 * Welche Karten beim Wechsel ans Ende gleiten (FLIP, 1.043.0): nur wenn in DIESEM Bild eine Partie fertig wurde, die
 * vorher schon lief (`wasFinished` false) — beim Erstladen, Rundenwechsel oder neuen Bretter-Set gibt es keine Vorgänger,
 * also keinen Sprung. Ergebnis: Schlüssel → Versatz (alte minus neue Lage) der Karten, die sich bewegt haben.
 */
export function flipDeltas(prev: CardPositions, next: CardPositions, wasFinished: ReadonlyMap<string, boolean>,
  finishedNow: ReadonlyMap<string, boolean>): Map<string, { dx: number; dy: number }> {
  const out = new Map<string, { dx: number; dy: number }>();
  const justFinished = [...finishedNow].some(([k, f]) => f && wasFinished.get(k) === false);
  if (!justFinished) return out;
  for (const [k, p] of next) {
    const old = prev.get(k);
    if (!old) continue;
    const dx = old.x - p.x, dy = old.y - p.y;
    if (Math.abs(dx) >= 1 || Math.abs(dy) >= 1) out.set(k, { dx, dy });
  }
  return out;
}
