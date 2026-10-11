# Ähnliche Meisterpartien: anschauen + Fortschritt (0.567.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`features/games/similar-games.component.ts` (Auswahl serverseitig, `SimilarGamesService`): je Zeile angemeldet ein
Augen-Knopf „Mit Anmerkungen anschauen" → `GET /api/library-games/{id}/view?lang=<Oberfläche>` → der Nachspiel-Dialog
(`PgnViewerComponent`, erst beim Klick per `import()` nachgeladen). Daneben `state(s)`: `play` (im Bestand oder die EIGENE
Anforderung ist `done`), `computing` („Wird gerechnet … n %", aus `GameAnalysisService.list()`, alle `PollMs` 10 s, solange
eine eigene Anforderung weder `done` noch `failed` ist), `request` (auch „Erneut anfordern" nach `failed`), `login`. Vorher
sprang eine angeforderte Partie sofort auf „Spielen" — das Spiel stand dann vor ungerechneten Stellungen.
