# TODO

Dinge die nicht direkt angegangen werden, aber nicht vergessen werden sollen.

## Periodisch
- [ ] Code Review — letzter: 2026-06-18 → Frontend-Fan-out-Review (6 Dimensionen); alle [Hoch]-Funde + 2 [Mittel] gefixt (v0.155.4–0.155.12), Rest geparkt unter „## Audit-Funde 2026-06-18 (Frontend Code Review)". (vorher 2026-06-16 alle Repos; 2026-06-13 erstmals alle Repos)
- [ ] Übersetzungen prüfen (en/de/hr vollständig + korrekt) — letzter: 2026-06-13 → alle 25 Sprachdateien JSON-valide. en+de vollständig (1028 Keys); **hr hatte 73 Lücken → in 0.115.1 ergänzt** (Impersonation/Menü/Chessable). `weekly.oClock` überall leer = Absicht. Die 22 Weltsprachen (ar,cs,el,…) sind je 174 Keys hinter en + 24 veraltet (i18n-worldwide-Drift) → fallen auf en zurück, Massen-Übersetzung offen (siehe Audit-Funde)
- [ ] Security Review — letzter: 2026-06-13 → alle Repos (siehe „## Audit-Funde 2026-06-13"). Auth/Ownership/HMAC/Injection durchweg solide. Echte Funde v. a. im Crawler (SSRF via Auto-Redirect, Body-Logging→ES behoben) + piratechess (curl-Arg-Injektion via bid, gluetun auth=none). Keine sofort-kritische rookhub-Lücke
- [ ] Logs prüfen (Kibana: Errors/Warnings/Anomalien) — letzter: 2026-06-13 → ES lokal auf :9200 (nicht 9201/9202). **Prod 0 Errors über 7 Tage** ✓. 24h: 34382 Info / 91 Warn / 0 Error. Top-Warns: VPN-Rotation (27× „rotation failed/incomplete → forcing restart" — deckt sich mit Audit-Fund Crawler/piratechess), Chessable curl/Import-Retries (transient), 2× ASP.NET DataProtection-Key-Warnung (s. Audit). engine_analysis_crash NICHT wieder aufgetreten. log-watcher: 37 Alerts am 06-12 (nur Warn-Volumen-Spikes, keine Errors), 0 heute. Bot: 0 Warn/Error
- [ ] Dependency-Updates prüfen (NuGet + npm) — letzter: 2026-06-13 → npm Angular auf 19.2.25/cli 19.2.27 aktualisiert (0.115.1, Build+289 Tests grün). NuGet: alle Updates sind 9→10-Major (.NET-10) → bewusst ausgelassen; Swashbuckle 6.9.0 bleibt gepinnt. Bot (pip `>=`-Floors) aktuell. npm-audit-Vulns (12) nur in Dev-Deps (webpack-dev-server/sockjs) — nicht im Prod-Bundle

## Bugs
- [ ] Bauernumwandlung (Pawn Promotion) auf Mobile — Auswahl-Dialog/Interaktion auf dem Handy prüfen & fixen (Promotion-Picker schwer/nicht bedienbar auf Touch/kleinen Screens). Betrifft alle Puzzle-Modi (gemeinsamer `PuzzleBoardComponent`/chessground).
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

- [x] CI: Docker-Push an grüne Tests koppeln (`needs:`-Gate) — bereits behoben (war nach dem Audit gefixt, aber nicht abgehakt). RookHub: `docker.yml` hat `tests`-Job (`uses: ./.github/workflows/test.yml`, `workflow_call`), `build-api`/`build-frontend` mit `needs: tests` (Commit e26f44a, 0.114.1). Crawler: `test`-Job + `build-crawler: needs: test` (Commit 9b8804c). Verifiziert 2026-06-14: kein ungated Push-Pfad mehr, beide committed + in sync.
- [x] Crawler-Standalone-Compose: Default-Passwörter entfernt (0.114.2) — `docker-compose.yml` nutzt jetzt `${...:?}` (required, fail-closed) für Root-/DB-Passwort inkl. Connection-String; `.env.example` hat Platzhalter statt echter Passwörter
- [x] Crawler: `CancellationToken` durchgezogen (0.114.3) — `SearchPlayersAsync` UND `SearchPlayerTournamentsAsync` (beiden fehlte er) reichen ct jetzt an Fetch/RateLimit/PostAsync/ReadAsStringAsync; PlayerSearchController bindet `RequestAborted`. 2 Tests (cancelled token → wirft)
- [ ] gluetun-Control-Server (IP-Rotation) auf API-Key-Auth härten statt `auth = "none"` (HIGH; Aufwand M, nur intern erreichbar) — `gluetun-auth/config.toml` im rookhub-schach-dev-Stack gibt `GET /v1/publicip/ip` + `GET|PUT /v1/vpn/status` unauthentifiziert frei (nur intern via FIREWALL_INPUT_PORTS=8000 im Bridge-Netz). Härtung: `auth = "apikey"` + `apikey = "<secret>"`, Secret in beide `.env` (`rookhub-schach`/`-dev`), dann `X-API-Key`-Header senden in **piratechess-api** (`VpnRotationService`, `Gluetun__ApiKey`-Env) UND **chessresults_crawler** (`CrawlerService.RotateVpnAsync`/`TryGetPublicIpAsync`); beide Images neu bauen + deployen. Betrifft prod + dev. Liegt im Deploy-Stack (piratechess_docker), nicht im Repo. — **Status:** piratechess-Seite (X-API-Key) erledigt (b398963, DEV deployed); OFFEN = chessresults_crawler-Seite + die eigentliche Aktivierung (auth="apikey"+Secret+koordinierter Restart, s. „## Audit-Funde / piratechess_docker").
- [x] Tournament-Detail-Komponente aufgeteilt (0.114.4) — HTTP-Calls → `TournamentDetailService`, reine Favoriten-Logik → `tournament-favorites.util.ts` (+Spec). Komponente 545→513 Z., Verantwortung getrennt. Polling-Logik bewusst in der Komponente belassen (UI-State-nah). Nebenbei kaputten Navbar-Spec repariert (289 FE-Tests grün)
- [ ] JWT `ClockSkew` explizit auf ≤1 min setzen (`Program.cs:~92`, aktuell Default 5 min) — 1-Zeilen-Härtung, niedriger Nutzen
- [ ] Retry-Interceptor erweitern — existiert (`retry.interceptor.ts`: 502/503/0, GET/HEAD, X-Retry-Guard), aber nur **1 Retry ohne Backoff**; ggf. auf Exponential-Backoff + mehr Versuche. Marginal
- [ ] Endless-Puzzle-Komponente: State-Management in dedizierten Service auslagern (`endless-puzzle.component.ts` ~1211 Zeilen). Großer Umbau, mittleres Regressionsrisiko, nur Wartbarkeit
- [ ] `takeUntilDestroyed` durchgängig einsetzen — ~228 `.subscribe(`-Stellen, nur 6 Komponenten nutzen es heute; viele mit manuellem `ngOnDestroy`/`clearInterval`. Flächiger Sweep, eher opportunistisch beim Anfassen erledigen als als eigenes Projekt
- [ ] Puzzle-Board auf den gemeinsamen `PromotionPickerComponent` (`shared/promotion-picker/`, seit 0.152.0 vom Analysebrett genutzt) migrieren — `puzzle-board.component.ts` hat noch seine eigene Inline-Umwandlungs-Overlay (Normal- + Viz-Pfad) mit identischer Guard-/Positionslogik. Zusammenführen vermeidet Doppelpflege; Risiko = Viz-Pfad (eigene Farb-/FEN-Erkennung) + frisch gefixter Ghost-Tap-Guard, daher bewusst getrennt belassen bis zum nächsten Anfassen

### Bewusste Entscheidung — kein Bug (nur falls gewünscht umbauen)
- [ ] Crawler-`API_KEY` ist fail-open (leerer Key = Gate offen, `ApiKeyMiddleware.cs:22-26`) — gewollter Dev-Fallback; allenfalls dokumentieren oder optional fail-closed schalten
- [ ] Token-Refresh im Frontend — `auth.interceptor.ts` macht bei 401 harten `logout()` (fail-closed, sicher). Refresh-Flow wäre reines Komfort-Feature bei aktivem Polling (Monitor 30 s / Crawl-Job 2 s)

### Bei der Sichtung 2026-06-13 als bereits erledigt verifiziert (entfernt)
- AdminSeeder setzt PW nur beim ersten Start (`AdminSeeder.cs:35`, `AnyAsync(...) return`)
- BCrypt Work Factor ist bereits 12 (`AuthService.cs:21`, auch AdminSeeder)
- Crawler `HtmlParserService` ist durch Tests abgedeckt (`HtmlParserServiceTests.cs`, ~448 Z.)
- Crawler `RoundDetectionService` cacht bereits 60 s (`:50`)

## Audit-Funde 2026-06-18 (Frontend Code Review)
Fan-out-Review des Angular-Frontends (6 Dimensionen: Security, State/RxJS, Performance, Robustheit/TS, A11y/i18n, Wartbarkeit). **Alle [Hoch]-Funde + 2 [Mittel] direkt gefixt** (v0.155.4–0.155.12, committet+gepusht, 429 FE-Tests grün, Prod-Build sauber):
- hr-Übersetzung vervollständigt (39 fehlende Keys, ganzer `messages`-Namespace) — 0.155.4
- JWT nur noch an `/api` (kein Token-Leak an Dritt-URLs) — 0.155.5
- Wochenpost-Upload Client-Validierung (.pgn ≤10 MB) — 0.155.6
- ENDLESS_POOL_KEY geteilt (war 2× definiert) — 0.155.7
- LOCALE_ID/Datums-Lokalisierung (war immer en-US; de-DE-Hardcode in endless-history weg) — 0.155.8
- User-Suche entkoppelt (friends switchMap, admin debounce+switchMap) gegen Out-of-order — 0.155.9
- Puzzle-Lade-Races (puzzle/book/endless: loadEpoch + runGeneration-Guard) — 0.155.10
- Analyse: kein doppeltes analyze() bei Linien-/Tiefenwechsel — 0.155.11
- A11y: aria-labels für Icon-Only-Buttons (friends/gear/back) — 0.155.12

**Geparkt (bewusst NICHT angefasst — Aufwand/Regressionsrisiko, brauchen Laufzeit-Verifikation):**
- [ ] **`OnPush` ausrollen** (0/59 Komponenten nutzen es) — mit den 1-s/2,5-s-Timern + Engine-Stream systemische CD-Last. Start: Puzzle-Solver/Analyse/Turnier-Tabellen; Timer via `NgZone.runOutsideAngular`. (Hoch Wert, mittel Risiko)
- [ ] **God-Components entzerren**: `endless-puzzle.component` (1359 LOC), `admin.component` (732, 6 Domänen). Plus 14 Komponenten rufen `HttpClient` direkt statt über Service (friends/tournament-list/repertoire-list/profile/dashboard …) → `FriendsService`/`TournamentService`/`RepertoireService`/`ProfileService` einziehen.
- [ ] **Cross-Solver-Duplikation in `BasePuzzleSolver` hochziehen**: timer/formatTime/eval/keyboard/theme-setter (2–3× kopiert in puzzle/book/endless).
- [ ] **Test-Lücke**: `admin.component`/`admin.service`/`chessable.service`/`profile.component`/`menu.service`/`preferences.service` ohne Spec (größter Gap: Admin).
- [ ] **Singleton-Engine-Lifecycle** (Mittel): `AnalysisEngineService` ist `providedIn:'root'`, aber `AnalysisComponent.ngOnDestroy` ruft `engine.destroy()` — component-scopen oder nur `stop()`.
- [ ] **endless-history**: `JSON.parse` in template-gebundenen `formatConfig/formatMistakes` → beim Zuweisen von `sessions` in ein View-Model vorberechnen (Mittel, Perf).
- [ ] **Custom-Overlays** (app.component Changelog/Quickstart, endless-Hilfe): kein Focus-Trap/Escape/role=dialog → auf `MatDialog` migrieren (Mittel, A11y).
- [ ] **Klickbare `<div>`/`<span>`/`<mat-icon>`** ohne Tastatur (tournament-favoriten, version-link, theme-chips …) → `<button>` bzw. role/tabindex/keydown (Mittel, A11y).
- [ ] Kleinkram: untypisierte DTOs in `endless-storage.service` (`any`), `Repertoire.kind:number` statt Enum, `AppNotification.type:string` als Union, nicht-gecancelte Einmal-Timer/RAF (chess-board, api-tokens), verschachtelte Subscribes (friends `getNotifications→markSeen`, api-tokens 3-fach).

## Audit-Funde 2026-06-16 (Code-Review aller Repos)
Read-only-Review über rookhub (API+Frontend), chessresults_crawler, schach-bot, piratechess_docker. **5 Top-Funde direkt gefixt** (in v0.149.2 / piratechess): #1 Revenge-`solved` serverseitig hergeleitet+Dedupe, #3 Job-Feld-Data-Race (Gate/Complete/Snapshot), #4 Per-Bid-Lock gegen Doppel-Fetch, #5 Admin-Deep-Link via queryParamMap-Abo, #8 `GetThreadsAsync` auf GROUP-BY/bounded umgebaut. Rest hier geparkt (priorisiert; vieles intern/VPN-geschützt → Risiko realistisch einordnen):

### rookhub API
- [ ] HIGH `EncryptionService`: AES-Key aus `PadRight('0')` statt KDF, CBC ohne MAC, kein Längen-Guard in `Decrypt` (Key-Rotation → 500 auf jeder Credentials-Seite). → `AesGcm` + `SHA256(key)`/Längenvalidierung + `TryDecrypt`. (Gleiche Klasse dupliziert in piratechess `EncryptionService`.)
- [x] HIGH `AdminMessageService.EnsureThreadAsync`: PK-Race bei gleichzeitiger Erst-Nachricht → behoben (0.152.5): EnsureThreadAsync legt die Thread-Zeile jetzt in EINEM eigenen SaveChanges an und fängt `DbUpdateException` (PK-Konflikt) ab → eigene Add-Entry detachen + existierende Zeile nachladen. Idempotenz-Test ergänzt (3× EnsureThread → 1 Thread-Zeile + Claim bleibt). Hinweis: der echte Concurrency-Pfad ist mit InMemory nicht deterministisch nachstellbar → gegen MariaDB verifizieren.
- [ ] HIGH ChessableImport: kein atomarer Claim beim Job-Picking (`RunNextAsync`+`RunDetached`) — bei Skalierung/Resume-Sturm Doppelverarbeitung möglich. → RowVersion/`ExecuteUpdate`-Claim der Phase.
- [ ] MED Challenge-`ResolveAsync`: `solved`/`timeSpentSeconds` clientseitig geglaubt (wie Revenge, aber auf eigene Challenge begrenzt). Serverseitig herleiten erwägen.
- [x] MED N+1 im Challenge-Batch → behoben (0.152.7): `FriendService.GetAcceptedFriendIdsAsync` (eine Abfrage statt N× `AreFriendsAsync`) + Duplikat-Check für alle Kandidaten in EINER Abfrage; Benachrichtigung via `CreateManyAsync` (ein Save). Vorher teilerledigt (0.152.3): `NotificationService.CreateManyAsync` für die Admin-Schleife. (+1 Test: nur erstellte Empfänger werden benachrichtigt; 16 ChallengeControllerTests grün.)
- [ ] MED `FriendService.SearchUsersAsync`: `LIKE %q%` über 6 Spalten ohne Index (Full-Scan, MariaDB-Profil); Auth-Rate-Limiter IP- statt account-basiert (Credential-Stuffing über viele IPs).
- [x] LOW (0.152.6): `GetUserCoursesAdmin` prüft jetzt User-Existenz → 404 statt irreführender 400; `Mask` zeigt nur noch die letzten 4 Zeichen (Anfang nicht mehr preisgegeben). `RunDetached` existiert nicht mehr (Import-Service = `RunNextAsync`/`RunAsync`) → Fund obsolet. (+2 Controller-Tests, Mask-Test angepasst.)

### rookhub Frontend
- [x] HIGH Test-Lücke: `InAppNotificationService`, `notification-text.ts`, `messages.component`, `notifications.component` ohne Spec → behoben (0.152.4): 4 neue Specs, 22 Tests (Service: Count/markSeen-Clamp/markAllSeen/reset/Query-Params; notification-text: Key-Wahl inkl. _solved/_failed + Chessable-Suffix + Icon-Map; beide Components direkt instanziiert: loadMore-Pagination/open-markSeen+navigate bzw. load+markUserSeen/send-trim/Fehlerpfade).
- [x] MED `/messages` Refresh-on-focus (0.154.1): `MessagesComponent` lädt den Thread bei `window:focus` neu (still, kein Spinner, nicht während Senden) → neue Admin-Antwort + Read-State sofort aktuell. +2 Specs.
- [~] MED Tab-Index: **Teilerledigt (0.154.2)** — `messagesTabIndex=6` Magic Number ersetzt durch `admin-tabs.ts` (`ADMIN_TAB_KEYS` + `adminTabIndex()`, Deep-Link auf BELIEBIGEN Tab-Key generalisiert, Guard-Test hält die Reihenfolge mit dem HTML konsistent). OFFEN: Deep-Link schreibt `tab` noch nicht in die URL zurück (Reload/Back verliert den Tab) — bräuchte Router-Write + TestBed.
- [ ] MED Label-Methoden im Template (`translate.instant` je CD-Zyklus während Polling) in chessable/admin/dashboard → beim Update einmal berechnen/cachen.
- [ ] MED Badge-Flackern: optimistisches `markSeen`-Dekrement vs. 60-s-`refreshCount` (server-getrieben vs. optimistisch).
- [ ] LOW `dlImport`-Polling stoppt bei `paused` (Fortschritt friert ein) — Stop-Bedingung an Haupt-Component angleichen; `loadAllUsers` ohne Error-Callback + 500er-Limit; `availableUsers()`/`acceptDisclaimer`-Doppelsubmit; `bypassSecurityTrustUrl`-Bookmarklet-Kommentar/Guard.

### piratechess_docker
- [ ] HIGH „Chessable"-HttpClient nie in `Program.cs` registriert → `WaitForProxyReadyAsync`/VPN-Statusfallback laufen am Proxy vorbei (Readiness-Probe nach Rotation wirkungslos, Status meldet Host-IP). → `AddChessableHttpClient` registrieren.
- [ ] HIGH `ServiceKeyAuth`: nicht-timing-safer Vergleich → `CryptographicOperations.FixedTimeEquals` + `StringValues.Count==1`-Guard.
- [ ] MED globaler Rotations-Zähler von Parallel-Fetches geteilt (RotateAfter=10 verwässert); Job-Store-Leak (nie abgeholte Jobs bleiben mit MB-PGN im RAM → TTL/Reaper + Obergrenze); `RunFetchAsync` ohne CancellationToken (Shutdown hängt in Linien-Retries); `course/{bid}/cached` dekomprimiert riesige Blobs nur für ein bool → billige `AnyAsync`-Variante.
- [ ] LOW `.Wait()` auf SignalR-Send in Export-Progress (sync-over-async); `int.Parse(claim)` ohne Guard; Upsert ohne Unique-Index (`CachedCourse`/`GeneratedPgn`); `ChessableRawResponses` append-on-every-retry (Wachstumstreiber).

### chessresults_crawler
- [ ] HIGH Voll-HTML-Body (bis 500 KB) auf `Information` → ES-Bloat + personenbez. Daten in unauth. ES/Kibana → nur Größe/Status auf Info loggen.
- [ ] HIGH VPN-Rotation läuft IM gehaltenen Semaphor → blockiert alle Parallel-Crawls bis ~8 s (Timeout-Risiko); 429/5xx von chess-results.com lösen kein Backoff aus (harter Job-Fail) → `Retry-After`/Polly.
- [ ] MED `ExtractHiddenField` per Regex (bricht bei Markup-Drift) → AngleSharp; kein Response-Größenlimit (`zeilen=99999`→Heap); Encoding-Annahme (windows-1252-Umlaute → Datenkorruption); Player/Team-Upsert ohne Transaktion/normalisiertes Matching.
- [ ] LOW `ApiKeyMiddleware` offen ohne Key (Fail-Fast in Prod); `/api/health/ip` unauth + externer Call; Phantom-Runden aus beliebigen `rd=`-Links (gegen TotalRounds clampen).

### schach-bot
- [ ] HIGH Webhook ohne Replay-/Timestamp-Schutz (Port `0.0.0.0:9000` exponiert) + `daily-regenerate` kann Daily-Posts wiederholt auslösen (puzzleId nur geloggt, nicht validiert) → Timestamp signieren + Idempotenz über puzzleId + Port nicht veröffentlichen.
- [ ] HIGH `asyncio.create_task`-Schwarm (Reinforcement-/Slacker-DMs) ohne Referenz/Drossel → Discord-429/Claude-Limits, GC-Risiko → Tasks sammeln + Semaphore.
- [ ] MED KI-Chat für ALLE DM-User offen (kein Tages-/Token-Cap → Claude-Kosten); `analyze_move` `fen_override` erlaubt Engine-Analyse beliebiger Stellungen; `_check_rate_limit`-Dict wächst unbegrenzt; Motivations-Loop ohne Claude-Timeout.
- [ ] LOW SFTPGo-Share-Passwort im Klartext in DM; Webhook ohne `client_max_size`; Help-Definitionen aus `bot.py` auslagern (zyklische Kopplung mit `chat_tools`).

## Audit-Funde 2026-06-13 (Code- + Security-Review aller Repos)
Read-only-Audit über rookhub (API+Frontend), chessresults_crawler, schach-bot, piratechess_docker, repcheck. Zwei sichere Fixes direkt erledigt (s. u.), Rest geparkt — priorisiert. Adressraum-Hinweis: vieles davon ist intern/VPN-geschützt; Risiko realistisch einordnen.

### chessresults_crawler
- [x] **Body-Logging nach ES** — `LogCrawlRequest` loggte bei jedem erfolgreichen Fetch bis 500 KB Roh-HTML (Spieler-PII + ES-Bloat). In 0.115.1 entfernt (nur noch Größe). (`CrawlerService.cs:700`)
- [ ] **CRIT SSRF: Host-Guard greift erst NACH dem Request** — `HttpClient.AllowAutoRedirect=true` folgt chess-results-Redirects automatisch, `EnsureChessResultsHost` prüft erst die finale URL → Redirect auf interne Ziele (gluetun :8000, 169.254.169.254) wird bereits ausgeführt. Betrifft `FetchWithRedirectAsync` (`:471`), `FetchHtmlAsync` (`:505`), `SearchPlayers*` POST (`:637/:678`). Fix: `AllowAutoRedirect=false` + Redirects manuell verfolgen, jede Location vor dem nächsten Hop validieren, nur `https`. **Risiko: bricht ggf. SNode-Erkennung → gegen echtes chess-results.com testen.**
- [ ] **HIGH `/api/health/ip` offen + triggert Outbound** (`HealthController.cs:21`, in `IsOpenPath` ohne Key/RateLimit) — exponiert VPN-Exit-IP unauth. + erlaubt beliebige externe Calls (ipify). Fix: hinter API-Key oder cachen/rate-limiten.
- [ ] **HIGH VPN-Rotation läuft im Request-Lock** (`RotateVpnAsync` innerhalb `RateLimitAsync`-Semaphore, `:719-723`) → Rotation (+5×1s IP-Poll) blockiert alle Crawls bis 60s-Timeout → TimeoutException-Kaskade unter Last. Fix: Rotation außerhalb des Request-Locks.
- [ ] MED verwaiste `Queued`-Jobs ohne Recovery (`CrawlController.cs:69` + DropWrite-Queue) — Crash zwischen SaveChanges und Enqueue blockiert via Unique-ActiveKey alle künftigen Crawls des Turniers. Fix: Startup-Cleanup `Queued/Running` ohne Worker → `Failed`.
- [ ] MED finaler Status-Save mit bereits gecanceltem Token (`:42/114/134`) → bei Cancellation wirft auch der `Failed`-Save → Status nie persistiert. Fix: finalen Save mit `CancellationToken.None`.
- [ ] MED Team-Upsert via `ToDictionaryAsync(t => t.Name)` (`:248`) wirft bei doppelten/leeren Teamnamen → ganzer Players-Crawl failt. Fix: `ToLookup`/per Snr matchen.
- [ ] LOW Retry-Pfad in `FetchWithRedirect`/`FetchHtml` ist copy-paste ohne Schleife, der eine Retry hat kein try/catch (`:486-502`).

### piratechess_docker
- [x] **HIGH curl-Arg-Injektion via `bid`** → behoben (piratechess b398963): Umstieg auf `ProcessStartInfo.ArgumentList` (jeder Wert ein escapetes argv-Token, content-agnostisch → schützt bid/uid/oid/bearer/url). `BuildGetArgs/BuildPostArgs` → `List<string>`, 3 Sicherheitstests. DEV deployed.
- [ ] **HIGH gluetun `auth = "none"`** — Code fertig (piratechess b398963, DEV deployed): GluetunControl-HttpClient sendet `X-API-Key`, WENN `Gluetun:ApiKey` gesetzt (rückwärtskompatibel: ohne Key kein Header). **OFFEN = Aktivierung (koordinierter Restart):** in `/opt/stacks/rookhub-schach{,-dev}/gluetun-auth.toml` `auth="apikey"` + `apikey=<secret>`, `GLUETUN_APIKEY` in beide `.env` → `Gluetun__ApiKey`-Env, dann **gluetun + piratechess-api ZUSAMMEN** neu starten (sonst Mismatch → Rotation bricht). Repo-`gluetun-auth.toml`-Template steht schon auf `apikey` (Platzhalter). Betrifft prod + dev.
- [ ] MED `GET /api/vpn/status` ohne Auth (`VpnController.cs:20`) — liefert reale Exit-IP unauth. (POST /rotate ist `[Authorize]`). Fix: `[ServiceKeyAuth]` auch auf status.
- [ ] MED Login-Response (enthält frisches Chessable-JWT) wird roh im Klartext persistiert (`ChessableRawResponse.RawJson`, `ChessableHttpService.cs:411`), kein TTL. Fix: Login-Response nicht roh speichern / Token redigieren + Retention.
- [ ] MED ServiceKey-Vergleich nicht zeitkonstant (`ServiceKeyAuthAttribute.cs:31`, `string.Equals`) — Auth ist aber fail-closed (gut). Fix: `FixedTimeEquals`.
- [ ] LOW DB-Port `3308:3306` auch in Prod auf Host gemappt; Prod-Compose fehlen `Service__ApiKey`/`Gluetun__*`/`Elasticsearch__*` ggü. dev (Config-Drift → /direct/* in Prod fail-closed 503).

### rookhub API
- [ ] **HIGH BotStats-HMAC ohne Timestamp/Nonce** (`BotStatsController.cs:64`) — Signatur nur über `discordId` → statisch + unbegrenzt replaybar (liest fremden Trainingsfortschritt, read-only, geringe Datensensibilität). **Cross-Repo-Fix**: Timestamp in HMAC + Header, ±300s-Fenster — in rookhub UND schach-bot (`puzzle/rookhub.py:170`) gleichzeitig.
- [ ] MED JWT wird bei Passwort-Reset/-Change + Account-Löschung nicht invalidiert (`AuthService.cs`, `Program.cs:90`) — alte Tokens bis 365 Tage gültig; gelöschter Account kann mit altem Token weiter API rufen. Fix: SecurityStamp/TokenVersion-Claim + DB-Check (mind. `DeletedAt` prüfen).
- [ ] MED AES-CBC ohne Auth-Tag + schwache Key-Ableitung (`EncryptionService.cs`, `PadRight(32,'0')`) für gespeicherten Chessable-Bearer. Fix: AES-GCM + 256-bit-Key verlangen. **Achtung: Datenmigration bestehender Ciphertexts.**
- [ ] MED Reset-Link inkl. Roh-Token wird bei deaktiviertem SMTP im Klartext geloggt (`PasswordResetService.cs:96`/`SmtpEmailSender.cs:43`) → ES. Fix: Link im Log-Fallback maskieren / Startup-Guard.
- [ ] LOW Anon-Sessions per erratener `sessionId` claim-/überschreibbar (IDOR, geringe Auswirkung: nur Puzzle-Stats) (`BookPuzzleController.cs:292`, `EndlessProgressService.cs:270`). Fix: Claim an serverseitig ausgegebenen Token binden.
- [ ] LOW Impersonation-`imp`-Claim wird nirgends ausgewertet — destruktive Aktionen (DeleteAccount/ChangePassword/Token-Create) sollten ihn ablehnen (`AdminController.cs:88`). LOW: `ValidateAsync` schreibt `LastUsedAt` synchron bei jedem Token-Request (Auth-Hot-Path, `ApiTokenService.cs:127`) → throtteln.

### rookhub Frontend
- [x] MED i18n-Verstoß behoben (0.117.1) — die tatsächlich gerenderten hartcodierten Strings lagen im **`puzzle-settings-dialog`** (`vizLevelOptions`-Beschreibungen + `difficultyInfoOptions`-Beschreibungen), nicht in `base-puzzle-solver`. Neue Keys `puzzles.viz.level{0..4}Name/Desc` + `puzzles.difficulty.*Desc` (en/de/hr), Template via `| translate`. Die in der Notiz genannten `base-puzzle-solver`-Getter + `book-puzzle`-Override + toter `VizCardComponent`-Import waren **toter Code** (nirgends gerendert) → entfernt. +Spec.
- [x] LOW Frontend-Kleinkram komplett erledigt: `rel="noopener noreferrer"` ergänzt (`tournament-detail`/`public-tournament` + `chessable` von `noopener` → `noopener noreferrer`) (0.117.1); `clipboard.writeText` mit Guard + `.catch()` (`api-tokens.component.ts`), `stopImpersonation()` parst vor dem Commit + loggt bei beschädigtem Backup sauber aus (`auth.service.ts`, +2 Specs), Crawler-Job-/Monitor-Responses typisiert (`CrawlJob`/`TournamentMonitorStatus` in `core/models.ts` statt `Observable<any>`) (0.117.2).

### schach-bot (Python) — sehr sauber, keine ≥MED-Funde
- [ ] LOW `isinstance(puzzle_id, int)` akzeptiert auch bool (`core/webhook_server.py:69`); `_id_cache` ohne TTL/Maxsize (`puzzle/rookhub.py:37`); DM-Chat-RateLimit nur prozesslokal (`commands/chat.py:82`). HMAC/Async/Secrets/Injection alle korrekt.

### repcheck (Browser-Extension, Kopie 1) — nicht in Kopie 2
- [ ] **HIGH `host_permissions: ["https://*/*","http://*/*"]`** massiv überbreit + Background-Worker ist ungebremster Fetch-Proxy ohne `sender`-/Ziel-Origin-Check (`extension/manifest.json:38`, `background.js:8`). Fix: Permissions einschränken (nur RookHub-Origin, kein `http`), `sender.id`-Check + URL-Allowlist gegen gespeicherte RookHub-URL.
- [ ] MED Chessable-Bearer-JWT dauerhaft unverschlüsselt in `chrome.storage.local` ohne TTL (`chessable-token.js:41`); `http`-URLs erlauben Token im Klartext. Versions-Drift `content.js`=1.5.1 vs Manifest 1.8.0.

### Aus den Live-Logs (24h Prod) zusätzlich aufgefallen
- [ ] ASP.NET **DataProtection-Keys nicht persistiert + unverschlüsselt** (Startup-Warnung „No XML encryptor configured" + „Storing keys in a directory that may not be persisted outside of the container") — bei Container-Neustart werden DataProtection-geschützte Daten (Antiforgery etc.) unlesbar. Fix: Key-Ring auf ein persistentes Volume legen + verschlüsseln (`PersistKeysToFileSystem` + `ProtectKeysWith*`). Betrifft rookhub + piratechess.
- [ ] **VPN-Rotation instabil** (live bestätigt: 27 Warns/24h „rotation failed (non-critical)" / „incomplete → forcing VPN restart") — verstärkt die Crawler/piratechess-Rotation-Funde oben; lohnt echte Ursachenanalyse (gluetun-Control-Timing).

### i18n-Weltsprachen (22 Stück)
- [ ] Massen-Übersetzung/Bereinigung der 22 erweiterten Sprachen (je ~174 Keys hinter en + 24 veraltete) — braucht Pipeline-/Tooling-Entscheidung (MT vs. manuell). Aktuell unkritisch (Fallback auf en). en/de/hr sind die gepflegten Sprachen und vollständig.

## Features
- [x] Start-ELO schneller einpendeln (0.123.0) — betraf den **Standard-/Random-Puzzle-Modus** (persönliche Puzzle-Elo), NICHT Endless. Umgesetzt im Backend `PuzzleService.ProvisionalKFactor`: K-Faktor **×4** (in beide Richtungen — K skaliert Gewinn wie Verlust) bis **≥5 gelöst UND ≥5 gescheitert** (je vizLevel), **×2** bis 10/10, danach normaler K (20). Ersetzt das alte `attemptCount<30?40:20`. Tests in `PuzzleServiceTests`.
- [ ] Trainersystem mit eigenen Gruppen einführen — Konzept noch offen. Idee: Trainer-Rolle, die eigene Gruppen anlegen/verwalten und Mitglieder zuweisen kann (heute nur Admin via `/api/admin/groups`), inkl. Trainingsziel-Vorlagen + ggf. Kurs-Freigaben für die eigenen Gruppen. Aufbauen auf bestehender Gruppen-/`GroupTrainingGoals`-/`BookGroupAccess`-Infrastruktur; offene Fragen: Rollenmodell (neue Rolle vs. Flag), Sichtbarkeits-/Berechtigungsgrenzen Trainer ↔ Mitglieder, Einladungsfluss.
- [ ] Push-Benachrichtigungen (PWA) — z.B. „Dein Tagespuzzle wartet"
- [ ] E-Mail-Benachrichtigung bei neuen Turnierblättchen
- [ ] Puzzle-Streaks / Achievements
- [ ] Admin-Dashboard: User-Übersicht + Aktionen
- [x] Schach-Bot auf Elasticsearch umbauen (Logging/Events) → umgesetzt im Bot-Repo v2.60.0/2.60.1 (`core/es_client.py`, ESHandler in `log_setup.py`, Events `reaction`+`stat_inc`); Index `schach-bot-logs-*` ist live in Prod. Weitere Event-Typen (Daily-Post, DMs, Webhooks, Commands, Buttons) bei Bedarf später ergänzen.
