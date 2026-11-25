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
