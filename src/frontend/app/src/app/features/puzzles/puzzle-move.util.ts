/**
 * Weiterleitung: die Zug-Helfer (`applyUci`, `tryFreeMove`, `calcDests`, `tryLoadFen` …) liegen seit dem
 * F8-013-Nachlauf in `shared/chess/puzzle-move.util.ts` (der geteilte `line-solver` braucht sie). Nur noch für die
 * Löser in `features/puzzles/` und `features/courses/calc/` — die baut in der laufenden Zerlegungs-Welle eine andere
 * Sitzung um; danach dort auf `shared/chess/puzzle-move.util` umstellen und diese Datei löschen.
 */
export * from '../../shared/chess/puzzle-move.util';
