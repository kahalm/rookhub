import { Chess } from 'chess.js';
import { normalizeCastlingUci } from './castling-uci.util';
import type { AnalysisLine } from './analysis-engine.service';
import type { EngineAnalyseLine } from './external-engine.service';

/** Eine Engine-Linie so, wie die Karte sie zeigt: Bewertungstext, SAN-Zugfolge, Vorzeichen. */
export interface EngineDisplayLine { evalText: string; san: string; positive: boolean; }

/**
 * Broker-Zeile (ndjson des Lichess-External-Engine-Streams) → AnalysisLines. cp/mate kommen laut
 * Spezifikation bereits aus Weiß-Sicht (kein Vorzeichenwechsel wie beim WASM-Pfad). Rochaden
 * liefert der Broker als König-schlägt-Turm (`e1h1`) — `pvUci` trägt die Standardform, sonst
 * bräche der SAN-Nachbau an der Rochade ab. Genutzt vom Live-Pfad (AnalysisEngineService) UND
 * von der Auftragsseite, die gespeicherte Ergebnis-Zeilen ohne laufende Engine anzeigt.
 */
export function mapBrokerLine(fen: string, l: EngineAnalyseLine, multiPv: number): AnalysisLine[] {
  return (l.pvs ?? []).slice(0, multiPv).map((pv, i) => {
    const isMate = pv.mate !== undefined && pv.mate !== null;
    const score = isMate ? pv.mate! : (pv.cp ?? 0);
    let evalText: string;
    if (isMate) {
      evalText = '#' + score;
    } else {
      const v = score / 100;
      evalText = (v > 0 ? '+' : '') + v.toFixed(2);
    }
    return {
      multipv: i + 1,
      depth: pv.depth ?? l.depth ?? 0,
      scoreType: isMate ? 'mate' as const : 'cp' as const,
      score,
      evalText,
      pvUci: normalizeCastlingUci(fen, pv.moves ?? []),
    };
  });
}

/** UCI-Zugfolge ab `fromFen` als nummerierte SAN-Kette („12. Nf3 Nc6 13. Bb5"); bricht bei
 *  illegalem Zug ab, leer bei kaputter FEN — darf als Template-Getter niemals werfen. */
export function uciLineToSan(fromFen: string, uci: string[], maxPlies: number): string {
  let c: Chess;
  try { c = new Chess(fromFen); } catch { return ''; }
  const out: string[] = [];
  let moveNo = Math.floor((c.moveNumber?.() ?? 1));
  let white = c.turn() === 'w';
  for (let i = 0; i < uci.length && i < maxPlies; i++) {
    const u = uci[i];
    let mv;
    try { mv = c.move({ from: u.substring(0, 2), to: u.substring(2, 4), promotion: u.length > 4 ? u[4] : undefined }); }
    catch { break; }
    if (!mv) break;
    if (white) out.push(moveNo + '. ' + mv.san);
    else { if (out.length === 0) out.push(moveNo + '... ' + mv.san); else out.push(mv.san); moveNo++; }
    white = !white;
  }
  return out.join(' ');
}

export function toDisplayLines(fen: string, lines: AnalysisLine[], maxPlies = 12): EngineDisplayLine[] {
  return lines.map(l => ({
    evalText: l.evalText,
    positive: l.scoreType === 'mate' ? l.score > 0 : l.score >= 0,
    san: uciLineToSan(fen, l.pvUci, maxPlies),
  }));
}

/** Sekunden → „m:ss" bzw. „h:mm:ss" ab einer Stunde (Suchtimer, Auftrags-Rechenzeit). */
export function formatElapsed(totalSec: number): string {
  const s = Math.max(0, Math.floor(totalSec));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  const mm = h > 0 ? m.toString().padStart(2, '0') : String(m);
  return (h > 0 ? h + ':' : '') + mm + ':' + sec.toString().padStart(2, '0');
}
