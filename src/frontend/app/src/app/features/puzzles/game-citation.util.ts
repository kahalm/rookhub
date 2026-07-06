/**
 * Erkennt einen Abschlusskommentar, der NUR eine Partie-/Studien-Angabe ist — z. B.
 *   "Bayer - Kuenitz, Wiesbaden, 2015."   "Maedler-Stahl, Magdeburg 1964."
 *   "Black resigned in Blalock-Francisco, Evora 2008."
 * Solche Zitate tragen keine Lehr-Information; der Buch-/Kurs-Solver soll bei ihnen automatisch
 * weiterrücken (statt wie bei echten Erklärungen zum Lesen anzuhalten).
 *
 * Heuristik — ALLE drei Bedingungen (validiert gegen Buch 252 „1001 Endgame Exercises": 36 Treffer,
 * alle echte Zitate; kein lehrreicher Kommentar fälschlich getroffen):
 *  1. Enthält eine Namenspaarung „Nachname-Nachname" bzw. „Name - Name" (Großbuchstabe–Bindestrich–
 *     Großbuchstabe). Grenzt Partie-Zitate von Studien-Prosa wie „…composed by Troitzky in 1914." ab.
 *  2. KEINE Satz-/Zug-Interpunktion (`.` `!` `?`) im ganzen Text (nur der optionale Schlusspunkt) —
 *     schließt Prosa UND Zugnotation ("50.Nd7", "1...Qf5+ 2.Ke7 .") aus.
 *  3. Endet auf eine 4-stellige Jahreszahl (optional mit Schlusspunkt).
 * Zusätzlich längenbegrenzt, damit lange kommalastige Prosa nicht durchrutscht.
 */
const NAME_PAIR = /[A-ZÀ-Þ][\wÀ-ÿ.'’]*\s*[-–—]\s*[A-ZÀ-Þ]/;
const NO_PROSE_ENDS_YEAR = /^[^.!?]*\b(1[5-9]\d\d|20\d\d)\b\.?\s*$/;

export function isGameCitationComment(text: string | null | undefined): boolean {
  const t = (text ?? '').trim();
  if (t.length === 0 || t.length > 100) return false;
  return NAME_PAIR.test(t) && NO_PROSE_ENDS_YEAR.test(t);
}
