import type { LiveMove } from '../live-engine-session';
import type { InlineBoardJump } from './summary-inline-board.component';
import { SummaryBlock } from './summary-markdown.util';

/**
 * Bretter im Fließtext (Wunsch 2026-11-28, schmales Layout ohne sichtbares Seitenbrett): hinter welchen Absatz kommt eins und
 * welche Stellung zeigt es? Index = Block in der gezeigten Reihenfolge (`visibleBlocks`), Wert = Partiezugfolge + Halbzug.
 *
 * - Nur Absätze (`paragraph`) und Absätze des verschmolzenen Abschnitts „Die Partie“ (`gameText`) mit mindestens einem
 *   PARTIEzug. Ein Absatz nur mit Varianten bekommt keins — er spricht über eine Stellung, die das Brett davor schon zeigt.
 * - Gezeigt wird die Stellung nach dem ersten Partiezug mit Zugzeichen („25…Kd7?“, „19…Sc3!!“ — der Moment, um den es
 *   geht); hat keiner eins, nach dem letzten Partiezug des Absatzes (dort steht die Erzählung, wenn man weiterliest).
 * - Keins direkt vor einem ```diagram-Block (der zeigt die Stellung gleich selbst) und keins direkt hinter einem Diagramm
 *   derselben Stellung; zwei Bretter hintereinander mit derselben Stellung entfallen ebenfalls (das zweite).
 */
export function inlineBoardPlan(blocks: readonly SummaryBlock[], game: readonly LiveMove[]): Map<number, InlineBoardJump> {
  const plan = new Map<number, InlineBoardJump>();
  let lastPly = -1;
  blocks.forEach((b, i) => {
    const moves = gameMovesOf(b, game);
    if (!moves.length) return;
    const ply = (moves.find(m => m.glyph) ?? moves[moves.length - 1]).ply;
    const next = blocks[i + 1], prev = blocks[i - 1];
    if (next?.kind === 'diagram') return;
    if (prev?.kind === 'diagram' && prev.spec.ply === ply) return;
    if (ply === lastPly) return;
    lastPly = ply;
    plan.set(i, { line: game, at: ply });
  });
  return plan;
}

/** Ist die Folge bis einschließlich Halbzug `at` die Partie? */
export function followsGame(line: readonly LiveMove[], at: number, game: readonly LiveMove[]): boolean {
  if (at > game.length) return false;
  for (let k = 0; k < at; k++) if (line[k]?.uci !== game[k].uci) return false;
  return true;
}

/** Partiezüge eines Absatzes in Lesereihenfolge: Halbzug (Stellung danach) und ob ein Zugzeichen dran steht. */
function gameMovesOf(b: SummaryBlock, game: readonly LiveMove[]): { ply: number; glyph: boolean }[] {
  if (b.kind === 'paragraph') {
    return b.parts.flatMap(p => p.line && p.at && followsGame(p.line, p.at, game) ? [{ ply: p.at, glyph: /[!?]$/.test(p.text) }] : []);
  }
  if (b.kind === 'gameText') {
    return b.segments.flatMap(s => s.parts.flatMap(p => p.move?.ply !== undefined && p.move.ply > 0 ? [{ ply: p.move.ply, glyph: !!p.glyph }] : []));
  }
  return [];
}
