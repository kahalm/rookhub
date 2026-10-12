import { ParsedGame } from '../../shared/pgn-viewer/pgn-parser';
import { pgnHeaderText } from '../../shared/pgn-viewer/pgn-header.util';
import { prettyMoveLabel, startNumbering } from './repertoire-move-format.util';
import { lineChapter } from './repertoire-trainer.util';

/**
 * Anzeige der aktuellen Linie im Repertoire-Trainer (Linienname, Zugleiste, Kommentar) — reine
 * Funktionen ohne Angular. Aus `RepertoireTrainerComponent` herausgezogen (Hausregel „große Dateien
 * beim Anfassen verkleinern"); die Getter der Komponente reichen nur noch durch.
 */

/** Ein Halbzug der Zugleiste. `num` ist beim ersten Halbzug eines Zugpaares gesetzt. */
export interface LineMoveToken {
  num: number | null;
  san: string;
  ply: number;
  state: 'past' | 'current';
  comment: string;
}

/**
 * Name der Linie (White-Header) für die Anzeige. UI-Sweep 2026-10-12 (v1-rk-trainer-learn-platzhalter-fragezeichen):
 * ein PGN-Platzhalter („?", „-", leer) zählt als fehlend — die Zeile unter „Linie 1 von N" entfällt dann.
 */
export function lineLabel(line: ParsedGame | undefined): string {
  if (!line) return '';
  return pgnHeaderText(line.headers['White']);
}

/** Kapitel der Linie (Black-Header) für den Tooltip am Liniennamen; Platzhalter = kein Tooltip. Nur Anzeige — als
 * Filterschlüssel bleibt der rohe Wert (`lineChapter`). */
export function lineChapterTitle(line: ParsedGame | undefined): string {
  if (!line) return '';
  return pgnHeaderText(lineChapter(line));
}

/**
 * Züge der Linie als flache Tokens fürs Template: bis `currentPly - 1`, mit `revealCurrent` bis
 * einschließlich `currentPly`. Die Zukunft wird bewusst NICHT vorweggenommen. Die Zugnummern
 * kommen aus dem Start-FEN (fullmove counter + turn).
 */
export function lineMoveTokens(line: ParsedGame | undefined, currentPly: number, revealCurrent: boolean): LineMoveToken[] {
  if (!line || line.moves.length === 0) return [];
  const { side, fullMove } = startNumbering(line.fens[0]);
  const out: LineMoveToken[] = [];
  let curSide = side;
  let curMove = fullMove;
  const upto = revealCurrent ? Math.min(currentPly, line.moves.length - 1) : currentPly - 1;
  for (let ply = 0; ply <= upto; ply++) {
    const isFirstBlackHalfmove = ply === 0 && curSide === 'b';
    const num = curSide === 'w' || isFirstBlackHalfmove ? curMove : null;
    const state: 'past' | 'current' = ply < currentPly ? 'past' : 'current';
    const comment = (line.comments?.[ply] || '').trim();
    out.push({ num, san: line.moves[ply].san, ply, state, comment });
    if (curSide === 'b') curMove++;
    curSide = curSide === 'w' ? 'b' : 'w';
  }
  return out;
}

/** PGN-Kommentar zum Halbzug `ply` (getrimmt, '' wenn keiner). */
export function plyComment(line: ParsedGame | undefined, ply: number): string {
  if (!line) return '';
  return (line.comments?.[ply] || '').trim();
}

/** Kommentar in Absätze gesplittet (leere Zeile trennt). */
export function commentParagraphs(comment: string): string[] {
  if (!comment) return [];
  return comment.split(/\n\s*\n/).map(p => p.trim()).filter(Boolean);
}

/** Menschenlesbares Label des Halbzugs `ply`, z. B. „3. exd5" (Weiß) oder „2… d6" (Schwarz). */
export function plyPrettyLabel(line: ParsedGame | undefined, ply: number): string {
  if (!line || ply < 0 || ply >= line.moves.length) return '';
  return prettyMoveLabel(line.fens[0], line.moves[ply].san, ply);
}
