# TODO

Dinge die nicht direkt angegangen werden, aber nicht vergessen werden sollen.

## Periodisch
- [ ] Code Review — letzter: noch keiner
- [ ] Übersetzungen prüfen (en/de/hr vollständig + korrekt) — letzter: noch keiner
- [ ] Security Review — letzter: noch keiner
- [ ] Logs prüfen (Kibana: Errors/Warnings/Anomalien) — letzter: noch keiner
- [ ] Dependency-Updates prüfen (NuGet + npm) — letzter: noch keiner

## Bugs
- [ ] Engine-Hang bei Puzzle→Analyse-Wechsel (tritt auf beim Wechsel von Puzzle- in Analyse-Modus)
- [ ] BookPuzzle: Ladefehler → `state='LOADING'` bleibt → endloser Spinner, kein Retry (`book-puzzle.component.ts:284-288`)
- [ ] FriendController: `return Forbid(ex.Message)` → 500 statt 403 (`FriendController.cs:57,70,83`)
- [ ] Friendship TOCTOU-Race: Unique-Index nur auf geordnetes Paar → gespiegelte Doppel-Beziehung möglich + fehlender Self-Friend-Ausschluss (`AppDbContext.cs:53`)
- [ ] CrawlJob bleibt bei Enqueue-Fehler dauerhaft `Queued` → Duplikat-Check blockiert Turnier für immer (`CrawlController.cs:56-73`)
- [ ] StockfishService-Singleton wird von 3 Komponenten in `ngOnDestroy` `terminate()`t → Worker-Konflikt (`stockfish.service.ts:8`)
- [ ] `RecordAttemptAsync` ohne Idempotenz/Limit → Stats fälschbar, unbegrenztes Tabellenwachstum (`PuzzleService.cs:81`)
- [ ] `RoundMonitorService`: ein `SaveChanges` nach ganzer Schleife → bei Exception alle Updates verloren (`RoundMonitorService.cs:137`)

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
- [ ] Schach-Bot auf Elasticsearch umbauen (Logging/Events)
