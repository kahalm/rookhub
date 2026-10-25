import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import type { AnalysisLine } from './analysis-engine.service';

/**
 * Reine Anzeige-Regeln des Analysebretts (Zerlegung Z22): Partie-Ende, Zugziele, Bewertungsleiste, Engine-Pfeile und
 * die Suchuhr. Ohne Angular — der Container `AnalysisComponent` setzt nur die Ergebnisse.
 */

/** Partie-Ende in einer Stellung ohne legale Züge (dort rechnet keine Engine). */
export type TerminalState = 'mate-white-wins' | 'mate-black-wins' | 'stalemate' | null;

/** Matt oder Patt? Nur aufrufen, wenn es keine legalen Züge gibt. Wirft nicht: bei einer illegalen Stellung
 *  (Buch-Diagramme ohne König) liefert chess.js keinen Zustand — dann lieber gar keine Aussage als eine falsche. */
export function terminalStateOf(c: Chess): TerminalState {
  try {
    if (c.isStalemate()) return 'stalemate';
    // Matt heißt: die Seite AM ZUG hat verloren.
    if (c.isCheckmate()) return c.turn() === 'w' ? 'mate-black-wins' : 'mate-white-wins';
  } catch { /* illegale Stellung */ }
  return null;
}

/** Legale Zugziele je Ausgangsfeld (für chessground). */
export function legalDests(c: Chess): Map<Key, Key[]> {
  const map = new Map<Key, Key[]>();
  for (const m of c.moves({ verbose: true })) {
    const arr = map.get(m.from as Key) || [];
    arr.push(m.to as Key);
    map.set(m.from as Key, arr);
  }
  return map;
}

/** Bewertungsleiste: Text und Höhe des weißen Teils (0–100). Ohne Linie zeigt sie beim Partie-Ende das ERGEBNIS,
 *  sonst die ausgeglichene Mitte; Zentibauern laufen über eine Sigmoide und bleiben zwischen 2 und 98. */
export function evalBar(best: AnalysisLine | null, terminal: TerminalState): { evalText: string; whiteHeight: number } {
  if (!best) {
    switch (terminal) {
      case 'mate-white-wins': return { evalText: '1-0', whiteHeight: 100 };
      case 'mate-black-wins': return { evalText: '0-1', whiteHeight: 0 };
      case 'stalemate': return { evalText: '½-½', whiteHeight: 50 };
    }
    return { evalText: '0.00', whiteHeight: 50 };
  }
  if (best.scoreType === 'mate') return { evalText: best.evalText, whiteHeight: best.score > 0 ? 100 : 0 };
  const cp = best.score;
  return { evalText: best.evalText, whiteHeight: Math.max(2, Math.min(98, 50 + 50 * (2 / (1 + Math.exp(-0.004 * cp)) - 1))) };
}

/** Pfeil je Engine-Linie für ihren ersten Zug; die Nummer auf dem Pfeil = Marke in der Linienliste
 *  (d5-rk-analyse-pfeile-nur-farbe: nicht nur über die Farbe). */
export function engineArrows(lines: AnalysisLine[], brush: (i: number) => string): DrawShape[] {
  return lines.map((l, i) => {
    const u = l.pvUci[0];
    return u ? { orig: u.substring(0, 2) as Key, dest: u.substring(2, 4) as Key, brush: brush(i),
      label: { text: String(i + 1) } } as DrawShape : null;
  }).filter((s): s is DrawShape => !!s);
}

/** Erste Züge der Linien samt Bewertung — „Züge vergleichen" wählt sie vor. */
export function engineCandidates(lines: AnalysisLine[]): { uci: string; evalText: string }[] {
  return lines.filter(l => !!l.pvUci[0]).map(l => ({ uci: l.pvUci[0], evalText: l.evalText }));
}

/**
 * Suchuhr der laufenden Stellung: Funkstille seit der letzten Engine-Zeile und die Suchzeit — sie startet mit dem
 * ersten „läuft" einer Stellung, läuft über eine Fortsetzung nach Abriss weiter und friert beim Ende ein.
 */
export class SearchClock {
  running = false;
  private lastUpdateAt: number;
  private searchStartedAt: number;
  private searchEndedAt: number | null = null;
  private searchFen = '';

  constructor(now = Date.now()) {
    this.lastUpdateAt = now;
    this.searchStartedAt = now;
  }

  /** Eine Statusmeldung der Engine. */
  onStatus(running: boolean, fen: string, now: number): void {
    const wasRunning = this.running;
    this.running = running;
    this.lastUpdateAt = now;
    if (running && (!wasRunning || fen !== this.searchFen)) {
      this.searchStartedAt = now; this.searchEndedAt = null; this.searchFen = fen;
    } else if (!running && wasRunning) {
      this.searchEndedAt = now;
    }
  }

  /** Sekunden ohne neue Zeile (0, wenn nichts läuft) und Suchzeit der Stellung. */
  read(now: number): { sinceUpdateSec: number; searchElapsedSec: number } {
    return {
      sinceUpdateSec: this.running ? Math.floor((now - this.lastUpdateAt) / 1000) : 0,
      searchElapsedSec: Math.max(0, Math.floor(((this.searchEndedAt ?? now) - this.searchStartedAt) / 1000)),
    };
  }
}
