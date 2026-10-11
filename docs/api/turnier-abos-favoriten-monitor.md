# Turnier-Abos + Favoriten + Monitor (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Turnier-Abos + Favoriten + Monitor (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET/POST/DELETE | `/api/subscriptions[/{id:int}]` | Abonnierte Turniere verwalten |
| DELETE | `/api/subscriptions/by-tournament/{crawlerTournamentId}` | Abo ueber die TURNIER-Nummer loesen statt ueber die Abo-Id — die Kurzansicht auf Karte, Liste und Kalender kennt das Turnier, nicht das Abo. **Idempotent** (204 auch ohne Abo): der Merken-Knopf ist ein Umschalter, und „war schon nicht gemerkt" ist kein Fehlerfall. Literal-Route VOR `{id:int}` |
| GET/POST/DELETE | `/api/tournament-favorites[/{id}]` | Favoriten verwalten |
| GET/POST/DELETE | `/api/tournament-monitors/{tournamentId}` | Per-Turnier-User-Einstellungen + Runden-Monitor (Round-Watch, Auto-Subscribe). POST: Rate-Limit `user-crawl`; höchstens 10 aktive Monitore je Konto (`MaxActiveMonitorsPerUser`, sonst 409 — auch beim Wiederbeleben eines abgelaufenen Monitors; einen aktiven verlängern geht immer, abgelaufene zählen nicht) |

**Eine Kennung je Turnier** (Codereview 2026-09-29, A5-001): `POST /api/subscriptions` speichert IMMER die
chess-results-Nummer — eine Crawler-DB-Id löst der Crawler auf, ein Alt-Abo unter der DB-Id wird umgeschlüsselt.
Runden-Monitor und Abo-Refresh lösen die Kennung vor jedem Crawl über `CrawlQueueClient` auf.
**Runden-Monitor** (A5-007): `RoundMonitorService` prüft je Turnier (`CrawlerTournamentDbId`) einmal je Durchlauf und
meldet nur Runden über `LastKnownRounds` hinaus — jede Runde genau einmal. Crawl-Aufträge der Hintergrunddienste
laufen über `CrawlQueueClient.RequestAsync`; 409 vom Crawler heißt „läuft schon", kein Fehler.
