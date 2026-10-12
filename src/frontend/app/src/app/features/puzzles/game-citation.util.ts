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

/**
 * Inhaltsleere Abschluss-Floskeln OHNE Quellenangabe, bei denen der Solver ebenfalls weiterrücken soll
 * (Wunsch 2026-10-12, ausgewertet über die Endkommentare aller Kurse auf Prod):
 *  - nur Zeichen ohne Buchstaben: „✔" (Woodpecker), „1-0", „0-1 .", „=", „." …
 *  - Partie-Ausgang: „and White won.", „Resigns.", „Draw agreed.", „Checkmate!", „Black is mated." …
 *  - Lob: „CORRECT! Well done.", „Great job!" …
 *  - Punktwertung: „(1 point)", „Another 1 point for this variation." …
 *  - Verweise: „Go to the lesson", „____ Full Game", „Continued in the next variation." …
 */
const FILLERS = new Set([
  'white won', 'black won', 'white soon won', 'black soon won',
  'white went on to win', 'black went on to win', 'white won the game', 'black won the game',
  'resigns', 'resigned', 'draw agreed', 'a draw was soon agreed', 'draw by perpetual',
  'checkmate', 'black is mated', 'white is mated', 'etc', "it's all over", 'the rest is easy',
  'correct! well done', 'correct', 'well done', 'very well done', 'great job', 'good job', 'excellent',
  'go to the lesson', 'continued in the next variation', 'see the relevant theme',
]);
const POINTS = /^\(?(?:another )?\d+ points?(?: for this variation)?\)?\.?$/i;
const FULL_GAME = /^_+\s*full game$/i;
const LETTER = /\p{L}/u;

export function isFillerEndComment(text: string | null | undefined): boolean {
  const t = (text ?? '').trim();
  if (t.length === 0 || t.length > 60) return false;
  if (!LETTER.test(t)) return true;   // ✔, 1-0, 0-1 ., =, ½-½ …
  if (POINTS.test(t) || FULL_GAME.test(t)) return true;
  if (FILLERS.has(resultCore(t))) return true;
  // Kurze Schluss-Etiketten (Wunsch 2026-10-12, zweite Runde) — nur ohne Zugnotation, damit eine
  // knappe Varianten-Erklärung („Not 2.Qxe5? Rxe5") weiterhin anhält:
  if (MOVE_NOTATION.test(t)) return false;
  return MATE_NAME.test(t) || THEME_LABEL.test(t) || MATERIAL.some(r => r.test(t)) || EVALUATION.some(r => r.test(t));
}

/** Zugnotation: „2.Qxe5", „Nf3", „exd5", „e4", „O-O". */
const MOVE_NOTATION = /\d+\.\s*\S|\b[KQRBN]x?[a-h][1-8]|\b[a-h]x?[a-h]?[1-8]\b|O-O/;
/** Mattbild-Name: „Greco's mate", „Railroad into Triangle mate", „Black is mated", „and mate next move". */
const MATE_NAME = /\bmate[ds]?\b/i;
/** Themen-Label: „Theme: domination.", „(1 point) Theme: mate." */
const THEME_LABEL = /^(?:\(\d+ points?\)\s*)?themes?:\s*[^.!?]{1,50}\.?$/i;
const PIECE = String.raw`(?:piece|pawns?|queen|rook|bishop|knight|exchange|material)`;
/** Materialgewinn: „White wins a piece.", „You won the queen.", „Winning the queen.", „The queen is trapped." */
const MATERIAL = [
  new RegExp(String.raw`^(?:and\s+)?(?:(?:white|black|you)\s+)?(?:wins?|won|winning|traps?|trapping|nets?|gains?|picks? up)\b.*\b${PIECE}\b`, 'i'),
  new RegExp(String.raw`\b(?:queen|rook|bishop|knight) is (?:trapped|lost)\b`, 'i'),
  new RegExp(String.raw`\b(?:is|are) (?:a |an |two )?${PIECE}s? up\b`, 'i'),
];
/** Kurze Bewertung: „White is clearly winning.", „Black has no problems.", „With equality.", „Zugzwang." */
const EVALUATION = [
  /^(?:and\s+)?(?:white|black)\s+(?:is|has|stands|remains|holds|keeps)\b[^.!?]{0,30}\b(?:winning|better|worse|equal|level|fine|ok|advantage|edge|problems?|attack|compensation|pressure|initiative|lost|drawn|draw|holds?)\b[^.!?]{0,15}[.!]?$/i,
  /^(?:and\s+)?with\s+(?:a |an )?(?:decisive |winning |clear |big |large |slight |small |huge )?(?:advantage|edge|equality|attack|extra \w+)\b[^.!?]{0,15}[.!]?$/i,
  /^(?:and\s+)?(?:winning|zugzwang|equal|unclear|black holds|white holds)(?: for (?:white|black))?[.!]?$/i,
];
