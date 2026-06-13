# TODO

Dinge die nicht direkt angegangen werden, aber nicht vergessen werden sollen.

## Periodisch
- [ ] Code Review — letzter: 2026-06-08 → 2 QUALITY-Fixes (stockfish toter Ternary + BulkImport Seed/ChainPuzzleIds) in 0.97.11; keine Bugs gefunden
- [ ] Übersetzungen prüfen (en/de/hr vollständig + korrekt) — letzter: 2026-06-08 → alle 840 Keys vorhanden, kein leerer String, kein JSON-Fehler; keine echten Lücken gefunden
- [ ] Security Review — letzter: 2026-06-08 → 2 Fixes (Swagger nur Dev + RememberMe 1 Jahr) in 0.97.12; keine kritischen Lücken gefunden
- [ ] Logs prüfen (Kibana: Errors/Warnings/Anomalien) — letzter: 2026-06-08 → engine_analysis_crash als einziger aktiver Fehler gefunden, in 0.97.10 behoben
- [ ] Dependency-Updates prüfen (NuGet + npm) — letzter: 2026-06-08 → npm-Patches eingespielt (Angular 19.2.25/cli 19.2.27); NuGet + Angular/TS-Major-Sprünge bewusst ausgelassen

## Bugs
- [x] Engine-Hang bei Puzzle→Analyse-Wechsel → behoben in 0.97.5 (engine.destroy() statt stop())
- [x] BookPuzzle: Ladefehler → endloser Spinner → behoben in 0.97.6 (loadError-Flag + Retry-Button)
- [x] FriendController: return Forbid(ex.Message) → 500 → war bereits behoben in 0.40.9
- [x] Friendship TOCTOU-Race → war bereits behoben (PairLow/PairHigh computed columns + Self-Friend-Check)
- [x] CrawlJob bleibt bei Enqueue-Fehler dauerhaft Queued → behoben in Crawler (Job auf Failed setzen)
- [x] StockfishService in ngOnDestroy terminate() → war bereits behoben (kein terminate()-Aufruf mehr)
- [x] RecordAttemptAsync ohne Idempotenz/Limit → behoben in 0.97.8 (30s-Idempotenz + Elo-Guard)
- [x] RoundMonitorService: ein SaveChanges nach ganzer Schleife → behoben in 0.97.9 (pro Iteration)

## Geparkt
- [ ] Google Play / TWA fertigstellen (Branches 0.78.1–0.78.5 bereits in master 0.83.0):
  - [ ] Impressum/Betreiberdaten in `src/frontend/app/src/environments/operator.ts` eintragen (Name, Anschrift, UID, Kontakt-E-Mail)
  - [ ] Google-Play-Developer-Account prüfen/anlegen (25 $; neue Accounts: 12 Tester / 14 Tage Closed-Test vor Production)
  - [ ] Upload-Keystore erzeugen (`keytool -genkeypair … -alias rookhub`) + Play App Signing aktivieren
  - [ ] CI-Secrets setzen: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD`
  - [ ] AAB bauen: GH-Action „Build Android TWA" (manuell) oder `bubblewrap build`
  - [ ] Play-Listing: Beschreibung, Icon 512, Feature-Graphic 1024×500, ≥2 Screenshots
  - [ ] Datenschutz-URL in Play Console: `https://rookhub.oberschmid.homes/privacy`
  - [ ] Data-Safety-Formular ausfüllen (gemäß Datenschutzerklärung)

## Refactoring / Qualität
_Sortiert: sinnvoll/einfach → aufwändig/marginal. Stand der Sichtung: 2026-06-13 (gegen Code geprüft)._

- [ ] CI: Docker-Push an grüne Tests koppeln (`needs:`-Gate) — `docker.yml` hat `push: true` ohne Abhängigkeit von `test.yml`; läuft parallel → Image wird auch bei roten Tests gepusht. Gilt für RookHub + Crawler (CRIT)
- [x] Crawler-Standalone-Compose: Default-Passwörter entfernt (0.114.2) — `docker-compose.yml` nutzt jetzt `${...:?}` (required, fail-closed) für Root-/DB-Passwort inkl. Connection-String; `.env.example` hat Platzhalter statt echter Passwörter
- [ ] Crawler: `CancellationToken` an der einen fehlenden Stelle durchziehen — `SearchPlayerTournamentsAsync` (`CrawlerService.cs:~654`) hat keinen CT, Schwester-Methode `SearchPlayersAsync` schon (rest ist abgedeckt)
- [ ] gluetun-Control-Server (IP-Rotation) auf API-Key-Auth härten statt `auth = "none"` (HIGH; Aufwand M, nur intern erreichbar) — `gluetun-auth/config.toml` im rookhub-schach-dev-Stack gibt `GET /v1/publicip/ip` + `GET|PUT /v1/vpn/status` unauthentifiziert frei (nur intern via FIREWALL_INPUT_PORTS=8000 im Bridge-Netz). Härtung: `auth = "apikey"` + `apikey = "<secret>"`, Secret in beide `.env` (`rookhub-schach`/`-dev`), dann `X-API-Key`-Header senden in **piratechess-api** (`VpnRotationService`, `Gluetun__ApiKey`-Env) UND **chessresults_crawler** (`CrawlerService.RotateVpnAsync`/`TryGetPublicIpAsync`); beide Images neu bauen + deployen. Betrifft prod + dev. Liegt im Deploy-Stack (piratechess_docker), nicht im Repo.
- [ ] Tournament-Detail-Komponente aufteilen (~545 Zeilen; Favoriten-/Polling-/Daten-Logik auslagern — `TeamPlayersDialogComponent` ist bereits ausgelagert). Reiner Wartbarkeits-Gewinn
- [ ] JWT `ClockSkew` explizit auf ≤1 min setzen (`Program.cs:~92`, aktuell Default 5 min) — 1-Zeilen-Härtung, niedriger Nutzen
- [ ] Retry-Interceptor erweitern — existiert (`retry.interceptor.ts`: 502/503/0, GET/HEAD, X-Retry-Guard), aber nur **1 Retry ohne Backoff**; ggf. auf Exponential-Backoff + mehr Versuche. Marginal
- [ ] Endless-Puzzle-Komponente: State-Management in dedizierten Service auslagern (`endless-puzzle.component.ts` ~1211 Zeilen). Großer Umbau, mittleres Regressionsrisiko, nur Wartbarkeit
- [ ] `takeUntilDestroyed` durchgängig einsetzen — ~228 `.subscribe(`-Stellen, nur 6 Komponenten nutzen es heute; viele mit manuellem `ngOnDestroy`/`clearInterval`. Flächiger Sweep, eher opportunistisch beim Anfassen erledigen als als eigenes Projekt

### Bewusste Entscheidung — kein Bug (nur falls gewünscht umbauen)
- [ ] Crawler-`API_KEY` ist fail-open (leerer Key = Gate offen, `ApiKeyMiddleware.cs:22-26`) — gewollter Dev-Fallback; allenfalls dokumentieren oder optional fail-closed schalten
- [ ] Token-Refresh im Frontend — `auth.interceptor.ts` macht bei 401 harten `logout()` (fail-closed, sicher). Refresh-Flow wäre reines Komfort-Feature bei aktivem Polling (Monitor 30 s / Crawl-Job 2 s)

### Bei der Sichtung 2026-06-13 als bereits erledigt verifiziert (entfernt)
- AdminSeeder setzt PW nur beim ersten Start (`AdminSeeder.cs:35`, `AnyAsync(...) return`)
- BCrypt Work Factor ist bereits 12 (`AuthService.cs:21`, auch AdminSeeder)
- Crawler `HtmlParserService` ist durch Tests abgedeckt (`HtmlParserServiceTests.cs`, ~448 Z.)
- Crawler `RoundDetectionService` cacht bereits 60 s (`:50`)

## Features
- [ ] Trainersystem mit eigenen Gruppen einführen — Konzept noch offen. Idee: Trainer-Rolle, die eigene Gruppen anlegen/verwalten und Mitglieder zuweisen kann (heute nur Admin via `/api/admin/groups`), inkl. Trainingsziel-Vorlagen + ggf. Kurs-Freigaben für die eigenen Gruppen. Aufbauen auf bestehender Gruppen-/`GroupTrainingGoals`-/`BookGroupAccess`-Infrastruktur; offene Fragen: Rollenmodell (neue Rolle vs. Flag), Sichtbarkeits-/Berechtigungsgrenzen Trainer ↔ Mitglieder, Einladungsfluss.
- [ ] Passwort vergessen / E-Mail-Reset
- [ ] Push-Benachrichtigungen (PWA) — z.B. „Dein Tagespuzzle wartet"
- [ ] E-Mail-Benachrichtigung bei neuen Turnierblättchen
- [ ] Puzzle-Streaks / Achievements
- [ ] Dinge für Freunde — Stats (Idee: Freunde-bezogene Statistiken / Vergleiche; Konzept noch offen)
- [ ] Dinge für Freunde — Revenge a Friend (Idee: einem Freund eine Revanche-Aufgabe / Herausforderung stellen)
- [ ] Admin-Dashboard: User-Übersicht + Aktionen
- [x] Schach-Bot auf Elasticsearch umbauen (Logging/Events) → umgesetzt im Bot-Repo v2.60.0/2.60.1 (`core/es_client.py`, ESHandler in `log_setup.py`, Events `reaction`+`stat_inc`); Index `schach-bot-logs-*` ist live in Prod. Weitere Event-Typen (Daily-Post, DMs, Webhooks, Commands, Buttons) bei Bedarf später ergänzen.
