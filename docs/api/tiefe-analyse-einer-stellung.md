# „Tiefe Analyse" einer Stellung (0.686.0) — `DeepAnalysisService`, `POST /api/deep-analysis`

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → „Tiefe Analyse" einer Stellung (0.686.0) — `DeepAnalysisService`, `POST /api/deep-analysis`“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-10-06: Vereinsmitglieder (`league.view`, live; sonst 403) rechnen über das ⋮-Menü der Partieseite die
Stellung auf dem Brett tiefer: `{ fen }` → `{ stockfish, lc0, stockfishDepth: 40, lc0Nodes: 500000 }` = zwei gewöhnliche
Aufträge DES NUTZERS (`AnalysisJobService.CreateAsync`, `remember: false`, MultiPv 3): Stockfish auf der Engine aus
`EngineOwnerResolver` (eigene, sonst Haus-Engine), Lc0 auf der Registrierung namens `ClubSecondEngine:EngineName` (fehlt sie:
`lc0: null`). Normale Aufträge → verdrängen die Stapelarbeit ihrer Engine. Erkannt am Titel (`DeepAnalysisService.StockfishTitle`
/`Lc0Title`): dieselbe Stellung = dieselben Aufträge (auch fertige), eine andere löscht die noch offenen der vorigen — eine
tiefe Analyse je Nutzer. `/api/analysis-jobs/live` trägt dafür `nodes`. Oberfläche: `features/games/deep-analysis-dialog.component.ts`
(Regeln rein in `deep-analysis.util.ts`): je Engine ein Balken (Tiefe bzw. Knoten), Linien = die HINTERLEGTEN der Partie-Analyse
(`GameReviewComponent.storedChange`), bis der Auftrag weiter ist (`deepAhead`), dann seine; der Menüpunkt erscheint nur, wo die
Seite `[deep]` an `app-position-menu` reicht (Partieseite) und das Konto `league.view` hat.
**Unter der Partie (0.690.0):** `GET /api/deep-analysis` = die eigenen tiefen Aufträge (ohne Recht leer). Der Rückblick
(`GameReviewComponent.deepJobs`, mit `withAlternatives`) holt sie beim Start, alle 5 s solange einer offen ist, sonst alle 30 s;
passt einer zur Stellung auf dem Brett (`deepJobFor`, erste vier FEN-Felder, Lc0 am Knotenziel) und ist weiter als das
Hinterlegte (`deepAhead`), ersetzen seine Linien den Stockfish- bzw. Lc0-Block, mit Etikett „Tiefe Analyse · Tiefe n".
**Live-Analyse mit Lc0 (0.693.0):** `AnalysisEngineService` schickt für eine Lc0-Engine (`isLc0Engine`) `nodes: Lc0LiveNodes` (500 000) statt der Tiefe — Lc0s Tiefe ist die mittlere Baumtiefe, `go depth 12` war nach ~6k Knoten fertig; ein Stream-Ende gilt dann nie als Abriss.
**Ganze Partie auf Lc0 (0.692.0):** `POST /api/deep-analysis/game { pgn }` (ebenfalls `league.view`) → eigene Analyse
(`Origin.Manual`) auf der Lc0-Registrierung mit `ClubSecondEngine:TargetNodes`; dasselbe PGN noch einmal = dieselbe Analyse.
Partieseite: ⋮ → „Mit Lc0 analysieren", solange der Rückblick keine Lc0-Alternative hat (`GameReviewComponent.hasLc0`), danach
`refreshAlternatives()`.
