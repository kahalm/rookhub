# Vereinspartien zusätzlich auf Lc0 (0.685.0) — `ClubSecondEngineScheduler`

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Vereinspartien zusätzlich auf Lc0 (0.685.0) — `ClubSecondEngineScheduler`“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-10-06: „alle Ligapartien, die neu dazukommen, automatisch mit 100k rechnen, und einmalig alle alten nachrechnen".
Jede `LeagueClubGame` bekommt eine ZWEITE Analyse: `Origin = Club` MIT `EngineId` (Registrierung namens
`ClubSecondEngine:EngineName`, Vorgabe „RookHub Spark Lc0" — per Name, die `rhe_`-Kennung wechselt beim Neuanmelden; leer
= aus) und `TargetNodes` (`ClubSecondEngine:TargetNodes`, Vorgabe 100 000 — gemessen 06.10. an 2 Partien: Lc0 stoppt per
Smart Pruning im Median bei ~88k, ab 50k bewegt sich die Bewertung nur um Hundertstel), MultiPv 3, höchstens
`ClubSecondEngine:MaxOpen` (1) offen, neueste Partie zuerst, minütlicher Takt. **Die Stockfish-Analyse bleibt die der
Partie**: jeder Leser der Club-Analyse fragt `EngineId == null` (LeagueClubService.ClubAnalyses/FillAnalysis,
SavedGameAnalysisLink.ClubAnalysisForMovesAsync, LeagueAnalysisQueue, MasterAnalysisScheduler.NextClubGameAsync + Zählung offener
Stellungen, Taktik-Ernte) — wer eine neue Club-Abfrage baut, nimmt die Bedingung mit. Sichtbar wird sie über
`POST /api/game-analyses/same-game` und `GET /api/game-analyses/{id}/evals`: Nutzer mit `league.view` sehen dort zusätzlich
Club-Analysen mit `EngineId` (`clubReader`). `GameEvalPlyDto.Nodes` = erreichte Knoten (letzte Stufe aus `NodeStepsJson`),
die Partieseite zeigt sie in den Ansichten Lc0/Beide.
