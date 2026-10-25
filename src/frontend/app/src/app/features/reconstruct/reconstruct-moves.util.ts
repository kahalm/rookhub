import { Chess } from 'chess.js';
import { START_FEN } from '../../shared/pgn-viewer/pgn-parser';
import { whiteToMove } from '../../shared/chess/fen-side';
import { GapResult, PartKind, ReconstructionPart } from './reconstruct.service';

/**
 * Reine Zug-, Teile- und Lücken-Hilfen der Rekonstruktion (Zerlegung Z25, aus
 * `ReconstructDetailComponent`). Kein Zustand, kein Angular — die Seite reicht Text, Teile und
 * Indizes herein.
 */

/**
 * Seite am Zug in einer FEN umstellen. Das en-passant-Feld fällt dabei weg: es beschreibt den Zug
 * DAVOR, und der gehört nach dem Wechsel der anderen Seite.
 */
export function withSideToMove(fen: string, black: boolean): string {
  const f = fen.trim().split(/\s+/);
  if (f.length < 4) return fen;
  f[1] = black ? 'b' : 'w';
  f[3] = '-';
  return f.join(' ');
}

/** Ergebnis-Token, die im eingetippten Zugtext wegfallen — „1/2" (Kurzform) gilt wie „1/2-1/2" als Remis. */
const RESULT_TOKENS: readonly string[] = ['1-0', '0-1', '1/2-1/2', '1/2', '*'];

/** Die Züge eines eingetippten Textes: Zugnummern und Ergebnis fallen weg, „12.e4" wird „e4". */
export function moveTokens(text: string): string[] {
  return text
    .split(/\s+/)
    .map(t => t.trim())
    .filter(t => t && !/^\d+\.*$/.test(t) && !RESULT_TOKENS.includes(t))
    .map(t => t.replace(/^\d+\.+/, ''));
}

/** Die Züge bis zum Blätter-Stand `ply` (alle, solange `ply` null ist). */
export function tokensUpTo(tokens: string[], ply: number | null): string[] {
  return ply === null ? tokens : tokens.slice(0, Math.max(0, Math.min(ply, tokens.length)));
}

/** Züge lokal nachspielen — für Brettanzeige und Sofort-Hinweis; die Wahrheit sagt der Server. */
export function replayMoves(startFen: string, moves: string[]): { fen: string; bad: string | null } {
  let chess: Chess;
  try { chess = new Chess(startFen); } catch { return { fen: START_FEN, bad: null }; }
  for (const san of moves) {
    try {
      if (!chess.move(san)) return { fen: chess.fen(), bad: san };
    } catch { return { fen: chess.fen(), bad: san }; }
  }
  return { fen: chess.fen(), bad: null };
}

/** Zugnummer aus dem Halbzug (0 → Zug 1). */
export function moveNo(ply: number): number { return Math.floor(ply / 2) + 1; }

/** Halbzüge eines Teils — bei Vorschlägen rechnet sie der Server nicht mit (sie zählen nicht zur Partie). */
export function plyCountOf(part: ReconstructionPart): number {
  if (!part.generated) return part.plyCount;
  return (part.moves ?? '').split(/\s+/).filter(t => t).length;
}

/** Kurzfassung eines Teils für die Liste. */
export function partSummary(part: ReconstructionPart): string {
  if (part.kind === PartKind.Position) return part.fen ?? '';
  const moves = (part.moves ?? '').split(' ').filter(m => m);
  return moves.length > 12 ? `${moves.slice(0, 12).join(' ')} …` : moves.join(' ');
}

/** Der Zustand eines Teils als Schlüssel für Text und Farbe. */
export function partState(part: ReconstructionPart): 'ok' | 'floating' | 'broken' | 'mismatch' {
  if (part.mismatch) return 'mismatch';
  if (!part.anchored) return 'floating';
  return part.valid ? 'ok' : 'broken';
}

/** Ausrichtung der Mini-Vorschau: aus Sicht der Seite am Zug. */
export function positionOrientation(part: ReconstructionPart): 'white' | 'black' {
  return whiteToMove(part.fen) ? 'white' : 'black';
}

/** Zugnummer einer Stellung aus ihrer FEN (fehlt sie, Zug 1). */
export function fenMoveNumber(fen: string | null | undefined): number {
  const f = (fen ?? '').trim().split(/\s+/);
  return Number(f[5]) > 0 ? Number(f[5]) : 1;
}

/** Die aufgezeichneten Teile — Vorschläge zählen nicht zur Partie. */
export function recordedParts(parts: ReconstructionPart[]): ReconstructionPart[] {
  return parts.filter(p => !p.generated);
}

/**
 * Lässt sich vor diesem Teil überhaupt suchen? Nur wenn eine Lücke davor liegt UND das Teil eine
 * STELLUNG ist: eine Zugfolge nach einer Lücke hat selbst keine bekannte Ausgangsstellung, und
 * genau die wäre das Ziel der Suche.
 */
export function canCloseGap(part: ReconstructionPart, index: number): boolean {
  return index > 0 && !part.continuesPrevious && part.kind === PartKind.Position;
}

/** Das aufgezeichnete Teil NACH diesem Vorschlag — darauf beziehen sich Übernehmen und Verwerfen. */
export function targetAfter(parts: ReconstructionPart[], part: ReconstructionPart): ReconstructionPart | null {
  return parts.find(p => p.ordinal > part.ordinal && !p.generated) ?? null;
}

/** Die Stellung, an der ein Vorschlag hängt: das Ende des letzten AUFGEZEICHNETEN Teils davor. */
export function recordedEndBefore(parts: ReconstructionPart[], part: ReconstructionPart): string | null {
  for (let i = parts.findIndex(p => p.id === part.id) - 1; i >= 0; i--)
    if (!parts[i].generated) return parts[i].endFen ?? null;
  return null;
}

/** Stehen vor diesem Teil Vorschläge? (Dann gibt es an der Lücke ein „Verwerfen".) */
export function hasProposalsBefore(parts: ReconstructionPart[], part: ReconstructionPart): boolean {
  const index = parts.findIndex(p => p.id === part.id);
  return index > 0 && parts[index - 1].generated;
}

/**
 * Zu welchem Teil gehört die Lücke VOR der Zeile `index` der sichtbaren Liste — oder null, wenn dort
 * keine ist? Die Zeile steht vor dem ERSTEN Eintrag der Lücke, und das ist bei vorhandenen
 * Vorschlägen der erste Vorschlag: sie liegen ja IN der Lücke, nicht davor.
 */
export function gapTargetAt(visible: ReconstructionPart[], all: ReconstructionPart[], index: number): ReconstructionPart | null {
  const part = visible[index];
  if (!part || index === 0) return null;
  if (visible[index - 1]?.generated) return null;          // mitten in den Vorschlägen derselben Lücke
  if (part.generated) return targetAfter(all, part);      // erster Vorschlag → die Lücke gehört dem Teil danach
  return part.continuesPrevious ? null : part;
}

/** Die erste Lücke, die sich schließen lässt (Ziel ist eine Stellung). */
export function firstGap(visible: ReconstructionPart[], all: ReconstructionPart[]): ReconstructionPart | null {
  for (let i = 1; i < visible.length; i++) {
    const target = gapTargetAt(visible, all, i);
    if (target?.kind === PartKind.Position) return target;
  }
  return null;
}

/** Vorschau ohne gewähltes Teil (d3): die Stellung am Ende des letzten aufgezeichneten Teils; ohne Teile keine. */
export function previewFen(parts: ReconstructionPart[]): string | null {
  const recorded = recordedParts(parts);
  return recorded.length ? (recorded[recorded.length - 1].endFen ?? null) : null;
}

/**
 * Der Satz zum leeren Ergebnis. „Nicht gefunden" und „gibt es nicht" sind verschiedene Aussagen,
 * und die Antwort trennt sie — deshalb hat jeder Grund einen eigenen Text statt eines
 * gemeinsamen „keine Lösung".
 */
export function gapMessageKey(result: GapResult | null): string | null {
  // Kein Grund = es hat geklappt; die Wege stehen dann als Vorschläge in der Liste.
  if (!result || result.reason == null || result.solutions.length > 0) return null;
  switch (result.reason) {
    case 'unreachable': return 'reconstruct.gap.unreachable';
    case 'too-far': return 'reconstruct.gap.tooFar';
    case 'same-position': return 'reconstruct.gap.samePosition';
    case 'budget': return 'reconstruct.gap.budget';
    case 'no-anchor': return 'reconstruct.gap.noAnchor';
    case 'no-gap': return 'reconstruct.gap.noGap';
    case 'no-previous': return 'reconstruct.gap.noPrevious';
    case 'target-not-a-position': return 'reconstruct.gap.targetNotAPosition';
    case 'invalid-from':
    case 'invalid-to': return 'reconstruct.gap.invalidPosition';
    default: return 'reconstruct.gap.none';
  }
}
