import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { GameEvals, reviewGame } from './game-review.util';
import { SavedGame } from './games.service';
import { Mistake, collectMistakes, mistakesOf, sideWithMoreMistakes } from './mistakes.util';
import { uciOf } from './move-tactics.util';
import { NO_CLASSIFIER } from './classifier.util';

/**
 * „Fehler aus Liga/Saison nachspielen" (0.748.0) — die Aufgaben MEHRERER eigener Partien in einer Sitzung. Rein: kein
 * Angular, kein HTTP. Je Partie dieselbe Auswahl wie auf der Partieseite (`reviewGame` + `collectMistakes`), nur die
 * Seite des Besitzers; ohne festgelegte Seite die mit den meisten Fehlern (wie `trainingSide`).
 */

/** Der Filterwert „ohne Klassifizierer" in der Adresse — `NO_CLASSIFIER` selbst ist ein Steuerzeichen. */
export const NO_CLASSIFIER_PARAM = '-';

export function classifierToParam(v: string): string | null {
  return !v ? null : v === NO_CLASSIFIER ? NO_CLASSIFIER_PARAM : v;
}

export function classifierFromParam(v: string | null | undefined): string {
  return !v ? '' : v === NO_CLASSIFIER_PARAM ? NO_CLASSIFIER : v;
}

/** Die Aufgaben einer Partie für die Sammlung, und wie viele die Partie insgesamt hat (für die Meldung des Fortschritts). */
export interface GameMistakes {
  /** Abzufragen: ohne ausgeblendete, und ohne schon gefundene, solange `includeSolved` aus ist. */
  list: Mistake[];
  /** Alle Aufgaben der Seite des Besitzers — `total` der Fortschrittsmeldung, wie auf der Partieseite. */
  total: number;
}

/** Kopfzeile einer Partie in der Sammlung: „Weiß – Schwarz · 12.10.2026". */
export function gameLabel(g: Pick<SavedGame, 'white' | 'black' | 'playedAt'>): string {
  const names = `${g.white || '?'} – ${g.black || '?'}`;
  const m = (g.playedAt ?? '').match(/^(\d{4})-(\d{2})-(\d{2})/);
  return m ? `${names} · ${m[3]}.${m[2]}.${m[1]}` : names;
}

export function gameMistakes(
  game: SavedGame, pgn: string, ownerSide: 'white' | 'black' | null | undefined, evals: GameEvals | null,
  includeSolved: boolean,
): GameMistakes {
  if (!evals || !pgn) return { list: [], total: 0 };
  const parsed = parsePgnText(pgn)[0];
  if (!parsed) return { list: [], total: 0 };
  const review = reviewGame(evals, parsed.fens, parsed.moves.map(uciOf));
  const bySide = collectMistakes(review, evals, parsed.fens, parsed.moves);
  const all = mistakesOf(bySide, ownerSide ?? sideWithMoreMistakes(bySide));
  const skip = new Set<number>([
    ...(game.mistakes?.dismissedPlies ?? []),
    ...(includeSolved ? [] : game.mistakes?.solvedPlies ?? []),
  ]);
  const label = gameLabel(game);
  const list = all.filter(m => !skip.has(m.ply)).map(m => ({ ...m, gameId: game.id, gameLabel: label }));
  return { list, total: all.length };
}

/** Reihenfolge der Partien: wie gespielt (älteste zuerst, ohne Datum nach dem Speichern) — eine Saison Runde für Runde. */
export function byPlayedAt(a: SavedGame, b: SavedGame): number {
  const ka = a.playedAt || a.createdAt || '';
  const kb = b.playedAt || b.createdAt || '';
  return ka < kb ? -1 : ka > kb ? 1 : a.id - b.id;
}
