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
- [ ] CI: Docker-Push an grüne Tests koppeln (`needs:`-Gate) — aktuell wird ohne Test-Gate gebaut (CRIT)
- [ ] Crawler-Standalone-Compose: Default-Passwörter aus `chessresults_crawler/docker-compose.yml` + `.env.example` entfernen (CRIT)
- [ ] Crawler-`API_KEY` im Standalone-Compose verpflichtend setzen / Middleware fail-closed machen (HIGH)
- [ ] Token-Refresh implementieren — bei aktivem Polling (Monitor 30 s / Crawl-Job 2 s) plötzlicher `/login`-Redirect bei Ablauf
- [ ] AdminSeeder: Passwort nur beim ersten Start setzen, nicht bei jedem Deploy (überschreibt UI-Änderungen) (`AdminSeeder.cs:31-36`)
- [ ] BCrypt Work Factor auf ≥12 erhöhen + `EnhancedHashPassword` verwenden (`AuthService.cs:37`)
- [ ] JWT `ClockSkew` explizit auf ≤1 min setzen (Default = 5 min Toleranz über `exp` hinaus) (`Program.cs:63`)
- [ ] Tournament-Detail-Komponente aufteilen (900+ Zeilen; `TeamPlayersDialogComponent` auslagern)
- [ ] Endless-Puzzle-Komponente: State-Management in dedizierten Service auslagern
- [ ] `takeUntilDestroyed` durchgängig einsetzen (aktuell nur in DashboardComponent vorbildlich)
- [ ] Crawler: `HtmlParserService` mit Unit-Tests abdecken
- [ ] Crawler: `CancellationToken` durch alle Crawl-Methoden durchziehen (`CrawlerService.cs:655`)
- [ ] Crawler: `RoundDetectionService` kurzzeitigen Cache (60s TTL) ergänzen statt bei jedem Check chess-results.com zu treffen
- [ ] Retry-Interceptor für 502/503/0 mit Exponential-Backoff im Frontend

## Features
- [ ] Passwort vergessen / E-Mail-Reset
- [ ] Push-Benachrichtigungen (PWA) — z.B. „Dein Tagespuzzle wartet"
- [ ] E-Mail-Benachrichtigung bei neuen Turnierblättchen
- [ ] Puzzle-Streaks / Achievements
- [ ] Admin-Dashboard: User-Übersicht + Aktionen
- [x] Schach-Bot auf Elasticsearch umbauen (Logging/Events) → umgesetzt im Bot-Repo v2.60.0/2.60.1 (`core/es_client.py`, ESHandler in `log_setup.py`, Events `reaction`+`stat_inc`); Index `schach-bot-logs-*` ist live in Prod. Weitere Event-Typen (Daily-Post, DMs, Webhooks, Commands, Buttons) bei Bedarf später ergänzen.
