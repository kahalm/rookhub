/**
 * Übersetzte Texte des Lös-Automaten, die Standard-, Endless- und Buch-Löser gleich zeigen (Zerlegung Z23):
 * Anarchy-Hinweise (e.p. Pflicht), die gestuften „falsch abgebogen"-Tipps und der Text der Off-Path-Warnung.
 * Rein — die Übersetzung kommt als Funktion herein (die Basis {@link BasePuzzleSolver} kennt keinen
 * TranslateService).
 */
export type SolverTranslate = (key: string, params?: object) => string;

/** Drei unterschiedlich formulierte „En passant ist Pflicht"-Hinweise (ersetzen die Tipps im e.p.-Zwang). */
export function epForcedHintTexts(t: SolverTranslate): string[] {
  return [1, 2, 3].map(i => t('puzzles.anarchyHint' + i));
}

/** Off-Path-Tipps in drei Stufen; Stufe 3 nennt den eigenen Zug, bei dem abgebogen wurde (`null` = unbekannt). */
export function offPathHintTexts(t: SolverTranslate, userMoveNumber: number | null): string[] {
  return [t('puzzles.offPath.h1'), t('puzzles.offPath.h2'),
    userMoveNumber != null ? t('puzzles.offPath.h3', { n: userMoveNumber }) : t('puzzles.offPath.h3any')];
}

/** Text der einmaligen Off-Path-Warnung: „nicht mehr ausgeglichen" bei ausgeglichener Startstellung, sonst
 *  „nicht mehr am Gewinnen"; die Eval aus Spieler-Sicht in Klammern, wenn bekannt. */
export function offPathWarningText(t: SolverTranslate, startWasBalanced: boolean, playerEval: string | null): string {
  const base = t(startWasBalanced ? 'puzzles.offPathWarningEqual' : 'puzzles.offPathWarning');
  return playerEval ? `${base} (${playerEval})` : base;
}
