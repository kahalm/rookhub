/**
 * Erkennt einen Abschlusskommentar, der keine Lehr-Information trägt, sondern nur die Quelle
 * (Partie/Studie) und/oder das Ergebnis nennt — z. B.
 *   "Bayer - Kuenitz, Wiesbaden, 2015."   "Kubbel (1916)."   "Black resigned in Blalock-Francisco, Evora 2008."
 *   "White wins."   "Black wins."   "Stalemate!"   "It is game over."   "White wins. Rinck (1928)."
 * Bei solchen Kommentaren soll der Buch-/Kurs-Solver automatisch weiterrücken (statt zum Lesen anzuhalten).
 *
 * Erkannt werden drei Formen (validiert gegen „1001 Endgame Exercises": 379 Treffer, 0 lehrreiche
 * Kommentare fälschlich getroffen):
 *  1. Reine Quellenangabe: eine (evtl. mit „in/based on/…" eingeleitete) Namens-/Studienangabe, die auf
 *     eine 4-stellige Jahreszahl endet. Zum Schutz gegen Prosa, die zufällig auf „… in JAHR." endet, muss
 *     die Angabe Klammern, ein Komma ODER eine Namenspaarung (Name-Name) enthalten.
 *  2. Reine Ergebnis-Floskel: der ganze Kommentar ist eine kurze Ergebnisaussage (White/Black wins,
 *     resigns, Stalemate, game over, draw, …) — ggf. mit vorangestelltem „And/It is/…".
 *  3. Ergebnis-Floskel + angehängte Quellenangabe (z. B. „White wins. Rinck (1928).").
 * Lehrreiche Kommentare (Taktik-/Technik-Erklärungen) bleiben unberührt.
 */

const YEAR = String.raw`(?:1[5-9]\d\d|20\d\d)`;
// Ein Namens-Token: Wort ODER Initiale (Großbuchstabe + Punkt); enthält KEINEN Satzpunkt.
const NAME = String.raw`(?:[A-ZÀ-Þ]\.[A-Za-zÀ-ÿ'’\-]*|[A-Za-zÀ-ÿ][A-Za-zÀ-ÿ'’\-]*)`;
const BODY = `${NAME}(?:[ ,&\\-–—]+${NAME})*`;
// Eine Quellenangabe am Ende — startet am Textanfang ODER nach Satzzeichen (damit sie nicht mitten in
// einem Satz beginnt und dessen Punkt „verschluckt").
const TRAILING_CITATION = new RegExp(
  `(?:^|(?<=[.!])\\s+)(?:(?:in|based on|inspired by|derived from|from)\\s+)?${BODY}\\s*,?\\s*\\(?${YEAR}\\)?\\.?\\s*$`,
);
// Namenspaarung „Name-Name" / „Name - Name" (auch für die alte Spiel-Zitat-Erkennung).
const NAME_PAIR = /[A-ZÀ-Þ][\wÀ-ÿ.'’]*\s*[-–—]\s*[A-ZÀ-Þ]/;
// Alt: ganzer Kommentar ohne Satz-/Zugzeichen, endet auf Jahr (klassische Spiel-Zitate).
const NO_PROSE_ENDS_YEAR = new RegExp(`^[^.!?]*\\b${YEAR}\\b\\.?\\s*$`);

/** Kurze Ergebnis-Floskeln (Kern nach Entfernen von Füllwörtern/Interpunktion, kleingeschrieben). */
const RESULTS = new Set([
  'white wins', 'black wins', 'white loses', 'black loses',
  'white resigns', 'black resigns', 'white resigned', 'black resigned',
  'white is winning', 'black is winning', 'white is fine', 'black is fine', 'white is ok', 'black is ok',
  'the game is level', 'the game is a draw', 'the game is drawn',
  'draw', 'a draw', 'with a draw', 'with a perpetual',
  'mate', 'stalemate', 'game over', 'mate is inevitable', 'mate is inevitable now',
]);

/** Kern einer möglichen Ergebnis-Floskel: führende Füllwörter + abschließende Punkte/Ausrufezeichen weg. */
function resultCore(text: string): string {
  let t = text.trim();
  for (const f of ['And it is ', 'And ', 'It is ', 'This is ']) {
    if (t.toLowerCase().startsWith(f.toLowerCase())) { t = t.slice(f.length); break; }
  }
  return t.replace(/[ .!]+$/, '').toLowerCase();
}

export function isGameCitationComment(text: string | null | undefined): boolean {
  const t = (text ?? '').trim();
  if (t.length === 0 || t.length > 200) return false;

  const m = TRAILING_CITATION.exec(t);
  if (m) {
    const cit = t.slice(m.index);
    const citeLike = (cit.includes('(') && cit.includes(')')) || cit.includes(',') || NAME_PAIR.test(cit);
    const head = t.slice(0, m.index).replace(/\s+$/, '');
    if (citeLike && head === '') return true;                   // reine Quellenangabe
    if (citeLike && RESULTS.has(resultCore(head))) return true; // Ergebnis + Quelle
  }
  if (RESULTS.has(resultCore(t))) return true;                  // reine Ergebnis-Floskel

  // Alt (v0.261.0): reines Spiel-Zitat = Namenspaarung + endet auf Jahr, keine Satz-/Zugzeichen.
  if (t.length <= 100 && NAME_PAIR.test(t) && NO_PROSE_ENDS_YEAR.test(t)) return true;
  return false;
}
