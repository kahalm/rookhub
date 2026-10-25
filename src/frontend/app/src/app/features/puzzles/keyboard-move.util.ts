/**
 * Weiterleitung: die Tastatur-Züge und die Figurenbuchstaben (`parseKeyboardMove`, `localizeSan` …) liegen seit dem
 * F8-013-Nachlauf in `shared/chess/keyboard-move.util.ts` (shared/ darf nicht aus features/ importieren). Nur noch für
 * `puzzle-board.component` und `features/reconstruct/` — die baut in der laufenden Zerlegungs-Welle eine andere Sitzung
 * um; danach dort auf `shared/chess/keyboard-move.util` umstellen und diese Datei löschen.
 */
export * from '../../shared/chess/keyboard-move.util';
