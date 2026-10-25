import { Color } from 'chessground/types';

/** Eval-String aus Weiß-Sicht ("+1.5"/"#3"/"#-2") → Bauerneinheiten aus Sicht von `orientation`
 *  (Matt = ±100). null, wenn leer/unparsbar. */
export function whiteEvalToPlayerPawns(s: string, orientation: Color): number | null {
  if (!s) return null;
  let white: number;
  if (s.includes('#')) {
    const m = parseInt(s.replace(/[#+]/g, ''), 10);   // "#3"→3, "#-2"→-2
    if (isNaN(m)) return null;
    white = (m >= 0 ? 1 : -1) * 100;
  } else {
    white = parseFloat(s);
    if (isNaN(white)) return null;
  }
  return orientation === 'white' ? white : -white;
}

/** Eval (Weiß-Sicht) aus SPIELER-Sicht als kurzer Anzeige-String ("+1.5"/"-0.8"/"#3"/"#-2"), oder null, wenn
 *  keine vorliegt. Für die Klammer im Off-Path-Hinweis. */
export function playerEvalText(s: string, orientation: Color): string | null {
  if (!s) return null;
  if (s.includes('#')) {
    const m = parseInt(s.replace(/[#+]/g, ''), 10);
    if (isNaN(m)) return null;
    const signed = orientation === 'white' ? m : -m;
    return signed >= 0 ? `#${signed}` : `#-${Math.abs(signed)}`;
  }
  const white = parseFloat(s);
  if (isNaN(white)) return null;
  const p = orientation === 'white' ? white : -white;
  const v = Math.abs(p).toFixed(1);
  return p > 0 ? `+${v}` : p < 0 ? `-${v}` : '0.0';
}

/**
 * Off-Path-Episode eines Lösungsversuchs (Zerlegung Z23), rein: Weicht der User von der Lösung ab und spielt gegen
 * Stockfish weiter, wird — sobald die Eval nicht mehr klar für den Spieler ist — ab dem N-ten off-path-Zug EINMAL
 * gewarnt (evtl. falsch abgebogen → besser zurücksetzen/aufgeben). Dazu die Zugnummer der ersten Abzweigung
 * (Stufe 3 der Off-Path-Tipps) und die Eval der Startstellung (ausgeglichen → anderer Warntext und andere Grenze).
 */
export class OffPathPolicy {
  /** Anzahl der bisher off-path gespielten Züge des Users (seit dem Abweichen). */
  userPlies = 0;
  /** In dieser Episode bereits gewarnt (einmalig). */
  warned = false;
  /** True, sobald gewarnt wurde (für eine optionale Dauer-Anzeige im Template). */
  warning = false;
  /** 1-basierte Nummer des eigenen Löserzugs, bei dem ZUERST abgebogen wurde; null solange auf dem Pfad. */
  userMoveNumber: number | null = null;
  /** Eval der Trainings-Startstellung aus Spieler-Sicht (Bauern); null = (noch) unbekannt. */
  startEvalPawns: number | null = null;

  /** Zurück auf dem Lösungsweg (neues Puzzle, Mausrutscher): Episode vergessen (die Start-Eval bleibt). */
  reset(): void {
    this.userPlies = 0;
    this.warned = false;
    this.warning = false;
    this.userMoveNumber = null;
  }

  /** Erste Abzweigung merken: der wievielte eigene Löserzug war es (aus dem Index des erwarteten Zugs und dem
   *  Vorspiel)? Liefert true nur beim ersten Mal je Episode. */
  markDeviation(moveIndex: number, startPly: number): boolean {
    if (this.userMoveNumber != null) return false;   // nur die ERSTE Abzweigung zählt
    const firstUserPly = startPly < 0 ? 0 : startPly + 1;
    const n = Math.floor((moveIndex - firstUserPly) / 2) + 1;
    this.userMoveNumber = n > 0 ? n : 1;
    return true;
  }

  /** War die Startstellung ausgeglichen (Spieler-Sicht |x| < 1)? */
  get startWasBalanced(): boolean {
    return this.startEvalPawns != null && Math.abs(this.startEvalPawns) < 1;
  }

  /** Nach einem off-path-Zug: ab dem Schwellwert-Zug (0 = nie) und wenn die Eval nicht mehr reicht → warnen.
   *  War die Startstellung ausgeglichen (Halten/Remis-Aufgabe), erst wenn der Spieler KLAR schlechter steht
   *  (< −1); war sie gewonnen, sobald es nicht mehr klar gewonnen ist (< +2). Liefert true genau einmal. */
  checkWarn(threshold: number, playerPawns: number | null): boolean {
    if (threshold <= 0 || this.warned || this.userPlies < threshold) return false;
    if (playerPawns == null) return false;
    const limit = this.startWasBalanced ? -1 : 2;
    if (playerPawns >= limit) return false;
    this.warned = true;
    this.warning = true;
    return true;
  }
}
