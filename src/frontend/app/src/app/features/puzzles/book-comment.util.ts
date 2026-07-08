import { isGameCitationComment } from './game-citation.util';

/**
 * Reine (state-freie) Kommentar-/Review-Rendering-Logik des Buch-/Kurs-/Wochenpost-/Tagespuzzle-
 * Solvers. Aus `book-puzzle.component` herausgelöst; die Komponente ruft diese Funktionen mit ihren
 * aktuellen Feld-Werten auf — Semantik + Reihenfolge sind unverändert (siehe die dortigen Getter-Docs).
 */

/** Pro-Zug-Kommentare eines Buch-Puzzles: 0-basierter Halbzug-Index (als String) → Text; -1 = Einleitung. */
export type MoveComments = { [ply: string]: string } | null | undefined;

/** Kommentar, der zum zuletzt gespielten Halbzug gehört (`plyPlayed` 0-basiert, -1 = Einleitung). */
export function commentForPlyPlayed(moveComments: MoveComments, plyPlayed: number): string | null {
  if (!moveComments) return null;
  return moveComments[String(plyPlayed)] ?? null;
}

/** Zuletzt nicht-leerer Kommentar im Bereich [start .. plyPlayed] (rückwärts); null wenn keiner. */
export function latestCommentUpTo(moveComments: MoveComments, start: number, plyPlayed: number): string | null {
  for (let ply = plyPlayed; ply >= start; ply--) {
    const c = commentForPlyPlayed(moveComments, ply);
    if (c) return c;
  }
  return null;
}

/**
 * Einzel-Kommentar für Review-/Info-Modus (Kommentar zum aktuell durchgespielten Zug, Fallback
 * Einleitung nur vor dem 1. Zug); außerhalb des Reviews die Einleitung des Puzzles.
 */
export function displayComment(reviewMode: boolean, reviewIndex: number,
    moveComment: string | null, puzzleComment: string | null | undefined): string | null {
  if (reviewMode) {
    if (reviewIndex === 0) return moveComment ?? puzzleComment ?? null;
    return moveComment ?? null;
  }
  return puzzleComment ?? null;
}

/** Eingaben für {@link buildCommentLines} — genau die von der Komponente gelesenen Felder. */
export interface CommentLinesState {
  reviewMode: boolean;
  reviewIndex: number;
  moveComment: string | null;
  puzzleComment: string | null | undefined;
  moveComments: MoveComments;
  onSolutionPath: boolean;
  moveIndex: number;
  /** state === 'AWAITING_USER_MOVE' || state === 'THINKING' */
  solving: boolean;
  startPly: number;
}

/**
 * Die im Kontext-Block angezeigten Kommentar-Absätze (gestapelt).
 * - Review/Info: EIN Absatz (der zum durchgespielten Zug bzw. die Einleitung vor dem 1. Zug).
 * - WÄHREND des Lösens: die Kommentare ALLER bereits gespielten Lösungszüge in Reihenfolge; Züge
 *   ohne Kommentar fügen nichts hinzu, KEIN Rückfall auf die Einleitung. AUSNAHME: solange der
 *   Spieler seinen ersten Lösungszug noch nicht gemacht hat, bleibt die Einleitung oben stehen
 *   (damit sie bei Kurslinien mit Aufbauzügen einen Halbzug länger lesbar ist).
 * - Vor dem ersten Zug (auf dem Lösungspfad): die Einleitung des Puzzles.
 */
export function buildCommentLines(s: CommentLinesState): string[] {
  if (s.reviewMode) {
    const c = s.reviewIndex === 0
      ? (s.moveComment ?? s.puzzleComment ?? null)
      : (s.moveComment ?? null);
    return c ? [c] : [];
  }
  if (s.onSolutionPath && s.moveIndex > 0 && s.solving) {
    const start = Math.max(0, s.startPly);
    const lines: string[] = [];
    // Einleitung einen Halbzug länger stehen lassen: bei Kurslinien mit Aufbauzügen (`startPly >= 0`)
    // steht `moveIndex` beim Lösestart bereits auf dem ersten Löserzug (`startPly + 1`), sodass die
    // Einleitung sonst genau dann verschwände, wenn der Aufbauzug fertig ist — zu kurz zum Lesen.
    // Solange der Spieler seinen ERSTEN Lösungszug noch nicht gemacht hat, bleibt sie oben stehen.
    const firstUserPly = s.startPly < 0 ? 0 : s.startPly + 1;
    if (s.puzzleComment && s.moveIndex <= firstUserPly) lines.push(s.puzzleComment);
    // `moveIndex` ist bereits ABSOLUT → der zuletzt gespielte Halbzug ist `moveIndex - 1`.
    for (let ply = start; ply <= s.moveIndex - 1; ply++) {
      const c = commentForPlyPlayed(s.moveComments, ply);
      if (c) lines.push(c);
    }
    return lines;
  }
  return (s.onSolutionPath && s.puzzleComment) ? [s.puzzleComment] : [];
}

/**
 * Gibt es nach dem letzten Zug der Linie noch einen LEHRREICHEN Abschlusskommentar? Reine Partie-/
 * Studien-Angaben (Zitate) zählen nicht — dann wird wie ohne Kommentar auto-weitergerückt.
 */
export function hasTrailingSolutionComment(moves: string | null | undefined, moveComments: MoveComments): boolean {
  const allMoves = (moves ?? '').split(' ').filter(m => m);
  if (allMoves.length === 0) return false;
  const c = commentForPlyPlayed(moveComments, allMoves.length - 1);
  if (!c) return false;
  if (isGameCitationComment(c)) return false;
  return true;
}
