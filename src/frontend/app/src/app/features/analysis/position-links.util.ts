/**
 * Adressen rund um EINE Stellung — für das ⋮-Menü von Analysebrett und Partieseite (`PositionMenuComponent`).
 * Rein, ohne Angular: die Schreibweisen sind Verträge mit fremden Seiten und stehen deshalb mit literalen Vektoren
 * in der Spec.
 */

/**
 * Die FEN-Suche von chess.com-Schwester Chessable über ALLE Kurse (`/courses/fen/…/`). Chessable schreibt die FEN
 * dort mit „U" statt „/" und `%20` statt Leerzeichen — dieselbe Regel wie der „Search FEN"-Knopf der
 * RepCheck-Erweiterung (`chessableSearchUrl` in repcheck/extension/chessable-fen.js, Rückfall ohne Kurs-Id). Die
 * übrigen FEN-Zeichen sind URL-sicher; `encodeURIComponent` ist bewusst NICHT im Spiel.
 */
export function chessableFenSearchUrl(fen: string): string {
  const encoded = fen.trim().replace(/\//g, 'U').replace(/ /g, '%20');
  return `https://www.chessable.com/courses/fen/${encoded}/`;
}

/**
 * Link zum Weitergeben: das Analysebrett mit dieser Stellung (`/analysis?fen=…&orientation=…`). Die Route ist
 * öffentlich, der Empfänger braucht kein Konto; `AnalysisComponent` liest beide Parameter beim Start.
 */
export function positionShareUrl(origin: string, fen: string, orientation: 'white' | 'black' = 'white'): string {
  const q = new URLSearchParams({ fen: fen.trim() });
  if (orientation === 'black') q.set('orientation', 'black');
  return `${origin}/analysis?${q.toString()}`;
}
