# Endspiel-Datenbank (Tablebase, offen, 0.729.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Endspiel-Datenbank (Tablebase, offen, 0.729.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Wunsch 2026-10-10: „bei der Live-Analyse ab 7 Steinen Lichess um die Wahrheit fragen — parallel Stockfish, sollte ich keine
Antwort bekommen". `GET /api/tablebase?fen=` (`TablebaseController`, `[AllowAnonymous]`, Rate-Limit `anonymous-read`) →
`TablebaseService` fragt `tablebase.lichess.ovh/standard` (Syzygy, bis `MaxPieces` = 7 Steine; Basis `Lichess:TablebaseUrl`).
Immer 200, `status` ∈ `ok`/`tooManyPieces`/`invalid`/`unavailable`/`rateLimited` — mehr als 7 Steine und unlesbare FEN gehen
gar nicht erst raus. EINE Leitung (`TablebaseGate`, Singleton): eine Anfrage zur Zeit, ≥ 250 ms Abstand, nach einem 429
60 s Pause für alle; Antworten 24 h im `IMemoryCache` (Schlüssel = FEN ohne Zugzähler — der Halbzugzähler zählt wegen der
50-Züge-Regel), Fehlschläge NICHT. **Die Zug-Ergebnisse dreht der Dienst** auf die Sicht der ziehenden Seite (Lichess meldet
die Stellung nach dem Zug; `Flip`, DTZ/DTM mit umgekehrtem Vorzeichen). Der Browser fragt nicht direkt bei Lichess (CSP
`connect-src 'self'`, gemeinsamer Speicher).

Frontend `features/analysis/tablebase-panel.component.ts` (+ `tablebase.service.ts`): im Analysebrett (Engine-Karte, nicht
im Maia-Sparring) und in der Live-Engine der Partieseite (`LiveEnginePanelComponent`). Fragt 200 ms nach dem letzten
Stellungswechsel, zeigt Ergebnis (Weiß gewinnt / Remis / Gewinn mit 50-Züge-Remis), Matt in n bzw. DTZ und jeden Zug mit
Bewertung; Klick spielt ihn. Die Bewertungsleiste des Analysebretts übernimmt das Ergebnis (`verdictOf`: 100/50/0,
„1-0 (TB)"; verflucht/gesegnet = Remis). Stockfish rechnet immer weiter — antwortet Lichess nicht, steht „Stockfish rechnet
weiter" und sonst ändert sich nichts.
