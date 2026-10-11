# Turnier-Proxy (leitet an Crawler weiter; lesen und Turnier holen ohne Anmeldung)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Turnier-Proxy (leitet an Crawler weiter; lesen und Turnier holen ohne Anmeldung)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Crawler-Route | Auth |
|---------|----------|---------------|------|
| GET | `/api/tournaments` | `/api/tournaments` | Auth |
| GET | `/api/tournaments/{id}` | `/api/tournaments/{id}` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/players?team=&sortBy=` | `/api/tournaments/{id}/players` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/teams[/{snr}]` | `/api/tournaments/{id}/teams` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/pairings?round=` | `/api/tournaments/{id}/pairings` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/players/{snr}/results` | `/api/tournaments/{id}/players/{snr}/results` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/rounds/check` | `/api/tournaments/{id}/rounds/check` | Auth |
| GET/POST | `/api/tournaments/{id}/clubs` | Vereine nachtragen | Auth (POST `user-crawl`) |
| POST | `/api/tournaments/crawl` | `/api/crawl` | **AllowAnonymous** (`user-crawl`, seit 0.643.0) |
| GET | `/api/tournaments/crawl/{jobId}` | Stand eines Holen-Auftrags | **AllowAnonymous** (`directory-read`, seit 0.643.0) |
| POST | `/api/tournaments/crawl/player-details` | `/api/crawl/player-details` | Auth |

**Turnier holen ohne Konto** (0.643.0, Wunsch „Turnierseite voll ohne Anmeldung benutzbar"): ein Gast kann ein noch
nicht geholtes Turnier über „Teilnehmer und Ergebnisse" bzw. „Aktualisieren" anfordern. `user-crawl` zählt dann je IP
(`RateLimitPartitions.UserOrIp`), dieselben 10/min wie je Konto; die Zuordnung hält `AnonymousRateLimitTests` fest,
die Liste der anonymen Endpunkte `EndpointAuthInventoryTests`.

**Crawler-Aufträge je Konto gedrosselt** (Codereview 2026-09-29, A5-004): Policy `user-crawl` (10/min je Konto,
`RateLimitPartitions.CrawlerRequest`, ein gemeinsames Fenster) auf `POST /api/tournaments/crawl`,
`/crawl/player-details`, `/{id}/clubs` und `POST /api/tournament-monitors/{id}` — zusätzlich zum globalen Deckel je IP;
die Crawler-Warteschlange (500 Plätze) und der chess-results-Takt gehören auch den Hintergrunddiensten.
