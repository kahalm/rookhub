import { Chess } from 'chess.js';
import { LiveEval, LiveGame } from './live-boards.models';

/** Engine-Balken an/aus — Gerätevorliebe (`core/local-storage-keys.ts`, Klasse `device`). */
export const LIVE_EVAL_KEY = 'rookhub_live_eval';

export function readLiveEval(): boolean {
  try { return localStorage.getItem(LIVE_EVAL_KEY) === '1'; } catch { return false; }
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
const EXT_KEY = /^[A-Za-z0-9]{8}\.[A-Za-z0-9]{8}$/;
const LICHESS_ID = /^[A-Za-z0-9]{8}$/;

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
    const r = x.slice(0, 8);
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

/**
 * Link einer Lichess-Übertragung für „Brett hinzufügen“ prüfen (wie `LichessBroadcasts.BoardLinkOf` im Server):
 * nur lichess.org/broadcast, Turnier- (`/slug/tourId`), Runden- (`/slug/runde/roundId`) oder Partie-Link
 * (`/slug/runde/roundId/gameId`), jede Kennung acht Buchstaben/Ziffern. `null` = ungültig.
 */
export function parseBroadcastLink(input: string): { tourId: string | null; roundId: string | null; gameId: string | null } | null {
  let url: URL;
  const s = input.trim();
  try { url = new URL(s.includes('://') ? s : 'https://' + s); } catch { return null; }
  if (!/^(www\.)?lichess\.org$/i.test(url.hostname)) return null;
  const parts = url.pathname.split('/').filter(Boolean);
  if (parts[0] !== 'broadcast') return null;
  const p = parts.slice(1);
  if (p.length === 2 && LICHESS_ID.test(p[1])) return { tourId: p[1], roundId: null, gameId: null };
  if (p.length === 3 && LICHESS_ID.test(p[2])) return { tourId: null, roundId: p[2], gameId: null };
  if (p.length === 4 && LICHESS_ID.test(p[2]) && LICHESS_ID.test(p[3])) return { tourId: null, roundId: p[2], gameId: p[3] };
  return null;
}

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

/** „Besten Zug zeigen“ an/aus (1.030.0) — Gerätevorliebe (`device`), bewusst NICHT in der Adresse/geteilten Ansicht: jeder Zuschauer schaltet selbst. */
export const LIVE_BEST_KEY = 'rookhub_live_best';

export function readLiveBest(): boolean {
  try { return localStorage.getItem(LIVE_BEST_KEY) === '1'; } catch { return false; }
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
