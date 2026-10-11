# „Warum war das ein Fehler?" (0.534.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Unter dem Abzeichen des aktuellen Zugs steht bei Fehlern ein, zwei Sätze Erklärung (💬), geschrieben vom Sprachmodell auf
eigener Hardware (Server: `GameMoveExplanationService`, siehe Haupt-CLAUDE.md). Adresse = die der Bewertungen mit
`/explanations` statt `/evals` (`GamesService.explanationsUrl`), Sprache = `translate.currentLang()`. Geladen, sobald die
Analyse `done` ist und bei jedem Sprachwechsel (`effect`, Schlüssel URL|Sprache); der Knopf „Fehler erklären lassen" erscheint
nur mit `canGenerate` (Besitzer + Modell da), ohne vorhandene Erklärung und nur, wenn die Partie Fehler hat; solange es läuft,
alle 5 s nachfragen (`ExplainPollMs`). Im Fehler-Training (`engineHidden`) weder Text noch Knopf — der Text nennt den besseren
Zug. Fehler beim Laden sind still (ohne Modell gibt es schlicht keinen Text).
