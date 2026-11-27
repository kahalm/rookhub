import { LiveEval, LiveGame } from './live-boards.models';

/**
 * Vibration der Live-Bretter (Wunsch 2026-11-27): das Handy meldet sich, wenn eine Partie entschieden ist oder die Bewertung
 * eines Bretts um mindestens {@link LIVE_SWING_PAWNS} Bauern springt — auch schleichend über die letzten
 * {@link LIVE_SWING_WINDOW_PLIES} Halbzüge. Die Erkennung ist rein ({@link LiveAlertTracker}), der Aufruf von
 * `navigator.vibrate` steht getrennt ({@link vibrate}).
 */

/** Vibration an/aus — Gerätevorliebe (`core/local-storage-keys.ts`, Klasse `device`). */
export const LIVE_VIBRATE_KEY = 'rookhub_live_vibrate';

export function readLiveVibrate(): boolean {
  try { return localStorage.getItem(LIVE_VIBRATE_KEY) === '1'; } catch { return false; }
}

export function writeLiveVibrate(on: boolean): void {
  try { localStorage.setItem(LIVE_VIBRATE_KEY, on ? '1' : '0'); } catch { /* gesperrter Speicher: gilt bis zum Neuladen */ }
}

/** Ab diesem Ausschlag (Bauern, Weiß-Sicht) meldet sich das Handy. */
export const LIVE_SWING_PAWNS = 1.0;
/** Fenster für schleichende Änderungen: die letzten 8 Halbzüge (= 4 Züge) vor der aktuellen Stellung. */
export const LIVE_SWING_WINDOW_PLIES = 8;
/** Matt und große Werte werden hierauf gekappt — „+12 → Matt“ ist kein neuer Sprung. */
export const LIVE_EVAL_CAP = 10;

/** Muster: Ergebnis lang-kurz-lang, Bewertungssprung drei kurze Stöße, Bestätigung beim Einschalten ein kurzer. */
export const VIBRATE_RESULT: readonly number[] = [400, 150, 400];
export const VIBRATE_SWING: readonly number[] = [120, 80, 120, 80, 120];
export const VIBRATE_CONFIRM: readonly number[] = [60];

export type LiveAlert = 'result' | 'swing';

/** Bewertung in Bauern aus Weiß-Sicht, gekappt auf ±{@link LIVE_EVAL_CAP}; `null` ohne Wert. Matt = ±Kappe. */
export function evalPawns(ev: LiveEval | null | undefined): number | null {
  if (!ev) return null;
  if (ev.mate !== null && ev.mate !== undefined) return ev.mate > 0 ? LIVE_EVAL_CAP : ev.mate < 0 ? -LIVE_EVAL_CAP : 0;
  if (ev.cp === null || ev.cp === undefined) return null;
  return Math.max(-LIVE_EVAL_CAP, Math.min(LIVE_EVAL_CAP, ev.cp / 100));
}

function decided(result: string | null | undefined): boolean {
  return result === '1-0' || result === '0-1' || result === '1/2-1/2';
}

interface Track { decided: boolean; evals: { ply: number; v: number }[] }

/**
 * Merkt je Partie Ergebnis und die Bewertungen der letzten Halbzüge und sagt, ob eine Änderung eine Meldung wert ist.
 * `seed` = Anfangsstand (Snapshot nach Laden/Wiederverbinden) — merkt sich alles, meldet nie.
 * Sprung: neue Bewertung gegen Minimum/Maximum der Werte im Fenster (frühere Halbzüge, ≥ {@link LIVE_SWING_PAWNS});
 * nach einer Meldung beginnt das Fenster neu bei der aktuellen Bewertung (sonst vibrierte jeder weitere Zug).
 * Eine neue Bewertung derselben Stellung (tiefer gerechnet) ersetzt die alte und wird ebenso gegen das Fenster geprüft.
 */
export class LiveAlertTracker {
  private readonly games = new Map<string, Track>();

  /** Anfangsstand übernehmen, ohne zu melden. */
  seed(game: LiveGame): void {
    const t: Track = { decided: decided(game.result) || game.finished, evals: [] };
    const v = evalPawns(game.eval);
    if (v !== null && !t.decided) t.evals.push({ ply: game.plies, v });
    this.games.set(game.id, t);
  }

  /** Live-Änderung einer Partie; unbekannte Partie = erster Stand (ohne Meldung). */
  observe(game: LiveGame): LiveAlert | null {
    const t = this.games.get(game.id);
    if (!t) { this.seed(game); return null; }
    if (t.decided) return null;
    if (decided(game.result) || game.finished) {
      t.decided = true;
      t.evals = [];
      return decided(game.result) ? 'result' : null;
    }
    const v = evalPawns(game.eval);
    if (v === null) return null;
    const ply = game.plies;
    // Frühere Halbzüge im Fenster; dieselbe Stellung (oder eine zurückgenommene spätere) fällt heraus.
    const prior = t.evals.filter(e => e.ply < ply && e.ply >= ply - LIVE_SWING_WINDOW_PLIES);
    let swing = false;
    if (prior.length > 0) {
      const min = Math.min(...prior.map(e => e.v));
      const max = Math.max(...prior.map(e => e.v));
      swing = v - min >= LIVE_SWING_PAWNS - 1e-9 || max - v >= LIVE_SWING_PAWNS - 1e-9;
    }
    t.evals = swing ? [{ ply, v }] : [...prior, { ply, v }];
    return swing ? 'swing' : null;
  }

  /** Alles vergessen (Rundenwechsel). */
  clear(): void { this.games.clear(); }
}

/** Kann dieses Gerät vibrieren? (iOS Safari und Desktop-Firefox kennen `navigator.vibrate` nicht.) */
export function canVibrate(): boolean {
  return typeof navigator !== 'undefined' && typeof (navigator as Navigator & { vibrate?: unknown }).vibrate === 'function';
}

/** Vibrieren, wenn möglich; still sonst (Chrome ignoriert es ohne vorherige Nutzerinteraktion). */
export function vibrate(pattern: readonly number[]): void {
  if (!canVibrate()) return;
  try { navigator.vibrate([...pattern]); } catch { /* manche Browser werfen in Rahmen/ohne Fokus */ }
}
