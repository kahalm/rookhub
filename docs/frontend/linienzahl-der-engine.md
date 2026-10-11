# Linienzahl der Engine (0.738.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`AnalysisEngineService.setMultiPv` nimmt bis `MaxLocalMultiPv` (10) — Stockfish im Browser rechnet so viele; an eine externe
Engine geht höchstens `MaxRemoteMultiPv` (5, Protokoll `work.multiPv`). `LiveEngineSession` hat als dritten Parameter die
Linienzahl (Vorgabe `LIVE_LINES` 3); die Formular-Prüfseite in LeagueHub (`scan-engine.component.ts`, `SCAN_ENGINE_LINES`)
will 10 und zeigt mit externer Engine 5 Zeilen.
