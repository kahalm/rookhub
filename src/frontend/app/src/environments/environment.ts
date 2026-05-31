export const environment = {
  production: false,
  version: '0.31.1',
  changelog: [
    { version: '0.31.1', date: '2026-05-31', changes: [
      'Fix: Filter-Felder Abstand — Hint-Text ueberlappt nicht mehr mit dem naechsten Feld',
    ]},
    { version: '0.31.0', date: '2026-05-31', changes: [
      'UI: Brett-/Figuren-Auswahl-Chips zeigen die echte Textur bzw. Figur als Vorschau.',
      'UI: Filter- und Darstellungs-Einstellungen (Schwierigkeit, Tiefe, Brett, Figuren) hinter ein Zahnrad/„Einstellungen"-Toggle gelegt (Default eingeklappt) — in Normal-, Endless- und Buch-Puzzle.',
    ]},
    { version: '0.30.0', date: '2026-05-31', changes: [
      'Feature: Figuren-Auswahl mit Mini-Vorschau (Springer) im Chip; zusätzliche Sets Celtic, Chessnut, RhosGFX (MIT/Apache/CC0).',
      'Feature: Zusätzliche Brett-Texturen Leder und Ahorn.',
    ]},
    { version: '0.29.0', date: '2026-05-31', changes: [
      'Feature: Figuren-Sets wählbar — Classic (cburnett), Merida, Fantasy, Spatial (lokal gevendort, MIT/GPL).',
      'Feature: Neue Brett-Texturen — Holz, Wasser, Marmor, Metall (echte Lichess-Texturen).',
      'Auswahl für Figuren + Brett in allen Puzzle-Modi (Normal, Endless, Buch), in localStorage gemerkt.',
    ]},
    { version: '0.28.2', date: '2026-05-31', changes: [
      'Fix: Buch-Puzzle-Import (Legacy-JSON) kürzt BookFileName auf 200 Zeichen und überspringt leere Dateinamen — verhindert DB-Fehler + Geister-Bücher (Code-Review).',
      'Fix: Schwierigkeits-Dropdown — erstes Puzzle erst nach Laden der Elo (nicht mehr Default 1500); Rating-Fenster wird auf den echten DB-Rating-Bereich geklemmt (keine leeren Ergebnisse/Retry-Schleife); ungültiger localStorage-Wert wird ignoriert (Code-Review).',
    ]},
    { version: '0.28.1', date: '2026-05-31', changes: [
      'Fix: Board Themes — Schachbrett korrekt als 8x8 statt 4 Quadranten gerendert (background-size: 25%)',
      'Fix: Board Theme Kontrast erhoeht — Blue, Green, Gray, Wood mit deutlich unterscheidbaren Feldfarben',
    ]},
    { version: '0.28.0', date: '2026-05-31', changes: [
      'Feature: Schwierigkeits-Dropdown bei den Puzzles — liefert Puzzles rund um die eigene Elo: Normal (±100), Leicht (−300), Sehr leicht (−600), Schwer (+300), Sehr schwer (+600).',
      'Feature: Rating-Fenster wird aus eigener Puzzle-Elo + Schwierigkeits-Offset berechnet (ersetzt die manuellen Min/Max-Rating-Felder); Auswahl wird in localStorage gemerkt.',
    ]},
    { version: '0.27.1', date: '2026-05-31', changes: [
      'Fix: Legacy-Buch-Puzzle-Import (POST /api/admin/book-puzzles/import) legt jetzt pro Datei ein Book an und setzt BookId — so erscheinen auch per Skript importierte Puzzles in den Pools (random/daily/blind) und im Admin-Bücher-Tab (Code-Review).',
    ]},
    { version: '0.27.0', date: '2026-05-31', changes: [
      'Feature: Puzzle Elo Rating System — dynamische Elo-Zahl basierend auf Puzzle-Schwierigkeit',
      'Feature: K-Faktor 40 fuer erste 30 Versuche (provisorisch), danach K=20',
      'Feature: Elo-Aenderung (+N gruen / -N rot) nach jedem Puzzle angezeigt',
      'Feature: Elo in Stats-Grid und Dashboard-Card sichtbar',
      'API: PuzzleElo auf AppUser, EloAfter/EloChange auf PuzzleAttempt',
      'Tests: 10 neue Tests fuer Elo-Berechnung, K-Faktor, Floor, Stats, History',
    ]},
    { version: '0.26.2', date: '2026-05-31', changes: [
      'Fix: Swagger-UI immer aktiv (nicht mehr nur in Development) — die API ist nur im internen Netz erreichbar.',
    ]},
    { version: '0.26.1', date: '2026-05-31', changes: [
      'Feature: Bauernumwandlungs-Dialog — bei Promotion erscheint ein Overlay mit 4 Figuren (Dame, Turm, Laeufer, Springer) statt automatischer Dame-Wahl',
      'Feature: Board Themes — 5 Brett-Farbschemata: Brown (Standard), Blue, Green, Gray, Wood',
      'Feature: Theme-Auswahl in allen Puzzle-Modi (Normal, Endless, Book) mit Farbvorschau-Chips',
      'Feature: Board-Theme wird in localStorage gespeichert und bleibt nach Refresh erhalten',
      'Fix: Promotion-Zuege werden korrekt mit UCI-Notation verglichen (inkl. Promotion-Buchstabe)',
    ]},
    { version: '0.26.0', date: '2026-05-30', changes: [
      'Feature: Buch-Puzzles als RookHub-Feature — Admin lädt PGN-Dateien als "Bücher" hoch (serverseitiges Parsing, SAN→UCI via Gera.Chess).',
      'Feature: Admin-Tab "Bücher" — Liste aller importierten Bücher mit Puzzle-Count und Pool-Schaltern Daily/Random/Blind pro Buch.',
      'API: GET /api/book-puzzles/random?pool=daily|random|blind&exclude=… — zufälliges Buch-Puzzle aus dem Pool; daily ist deterministisch pro UTC-Tag (gemeinsames Tagespuzzle).',
      'API: POST /api/admin/books/import (PGN-Upload), GET/PUT/DELETE /api/admin/books — Buch-Verwaltung inkl. Pool-Flags.',
      'DB: Neue Book-Entität (Flags ForDaily/ForRandom/ForBlind + Metadaten), BookPuzzle.BookId; Migration mit Backfill für Altbestand.',
      'Tests: 27 neue Tests (PGN-Parser inkl. Promotion/Rochade/Varianten/Skip-Regeln, random-Pool-Auswahl, Buch-Verwaltung).',
    ]},
    { version: '0.25.5', date: '2026-05-30', changes: [
      'UI: Mobile-Optimierung — Padding, Wrapping und Layout fuer alle Views angepasst',
      'UI: Endless History — Card-Layout statt Tabelle auf Mobilgeraeten',
      'UI: Friends — Suchfeld und Button stacken korrekt auf Mobile',
      'UI: Profile — Formularfelder stacken auf Mobile, Suchbutton volle Breite',
      'UI: Endless Puzzle — Config/GameOver/Help-Overlay mobile-optimiert',
      'Logging: Bildschirmaufloesung (ScreenWidth/ScreenHeight) wird bei Puzzle-Attempts nach Elasticsearch geloggt',
    ]},
    { version: '0.25.4', date: '2026-05-30', changes: [
      'Feature: Endless Run Resume — bei Seiten-Refresh wird ein laufender Run mit Continue-Button angeboten',
      'Feature: Archive & New Game — laufenden Run archivieren und neu starten',
      'Feature: Archive-Button auf Game-Over/Exhausted-Screen fuer sofortige Archivierung',
      'API: recordSessionToServer gibt jetzt Session-ID zurueck',
    ]},
    { version: '0.25.3', date: '2026-05-30', changes: [
      'Fix: Data Protection persistiert Keys auf gemountetes Volume (/keys, eigenes dataprotection-keys-Volume) statt In-Memory — keine "No XML encryptor / ephemeral key"-Warnings mehr beim API-Start.',
      'Fix: compose.dev.vpn.yml — API wartet auf gluetun (service_healthy); ES/Kibana Host-Port-Defaults 9201/5602 statt 9200/5601 (keine Kollision mit Prod-Stack auf demselben Host).',
      'Fix: .env.dev.vpn.example neu + .gitignore — dev-eigene Ports und getrennte Bind-Pfade (verhindert geteiltes ES-Datenverzeichnis/node.lock).',
      'Fix: Crawler-Dockerfile — redundantes ASPNETCORE_URLS entfernt (Bindung via Base-Image HTTP_PORTS=8080); entfernt "Overriding HTTP_PORTS"-Warning.',
    ]},
    { version: '0.25.2', date: '2026-05-30', changes: [
      'UI: Puzzle History im User-Menue (Profilbild oben rechts) verlinkt',
    ]},
    { version: '0.25.1', date: '2026-05-30', changes: [
      'Feature: Endless Session Archivierung — Sessions koennen archiviert/unarchiviert werden',
      'Feature: Archivierte Sessions fliessen nicht mehr in Sync-Response (Fasttrack/Session-Count)',
      'Feature: History-Filter: Alle / Aktive / Archivierte Sessions',
      'Feature: Mehrfachauswahl mit Checkboxen in der History-Tabelle',
      'API: Neuer Endpoint POST /api/endless/archive mit Bulk-Archivierung',
      'API: GET /api/endless/history unterstuetzt neuen Query-Parameter archived (bool)',
      'Tests: 5 neue Tests fuer Archive/Unarchive/Filter/Sync-Exclusion',
    ]},
    { version: '0.25.0', date: '2026-05-30', changes: [
      'Feature: Endless Puzzle History — paginierte Uebersicht aller vergangenen Sessions',
      'Feature: Authentifizierte User haben kein 50-Session-Limit mehr (unbegrenzte History)',
      'API: Neuer Endpoint GET /api/endless/history mit Pagination (page, pageSize)',
      'Tests: 5 neue Tests fuer History-Pagination und Trim-Verhalten',
    ]},
    { version: '0.24.1', date: '2026-05-30', changes: [
      'Observability: Puzzle-Attempts als strukturierte Serilog-Events nach Elasticsearch',
      'Kibana: 2 neue Dashboard-Panels (Puzzles Solved 24h, Puzzles per User)',
    ]},
    { version: '0.24.0', date: '2026-05-30', changes: [
      'Feature: Puzzle MoveLog — gespielte Zuege, erwartete Zuege und Denkzeit pro Zug werden getrackt',
      'Feature: Endless Mode sendet jetzt korrekte Puzzle-Dauer (timeSpentSeconds) statt 0',
      'API: MoveLog-Feld (JSON) auf PuzzleAttempt, in RecordAttempt und History-Endpoints',
    ]},
    { version: '0.23.3', date: '2026-05-30', changes: [
      'Fix: init-kibana.sh legt Data Views mit allowNoIndex:true an — Erstellung jetzt timing-unabhaengig (funktioniert auch, wenn der Log-Index beim init-Lauf noch nicht existiert). Behebt leeres Kibana (keine Data Views/Dashboard) bei frischem Stack-Start.',
      'Fix: kibana-init als Idle-Sidecar (restart: unless-stopped, idlet nach dem Init) statt One-Shot — Stack zeigt in Arcane/Portainer nicht mehr "partially running". Init laeuft idempotent bei jedem Stack-Start.',
    ]},
    { version: '0.23.2', date: '2026-05-29', changes: [
      'Fix: nginx PID-Pfad auf /tmp/nginx.pid geaendert (non-root Container Fix)',
    ]},
    { version: '0.23.1', date: '2026-05-29', changes: [
      'Fix: FriendController Search-Minimum von 3 auf 2 Zeichen (Frontend/API-Konsistenz)',
      'Fix: Email wird bei Registrierung normalisiert (lowercase, getrimmt)',
      'Fix: LIKE-Wildcards (%, _) in Friend-Suche bereinigt',
      'Fix: JWT base64url-Dekodierung in AuthService korrigiert',
      'Perf: RepertoireService.UpdateAsync — unnoetige Include(Files) durch CountAsync ersetzt',
    ]},
    { version: '0.23.0', date: '2026-05-29', changes: [
      'Security: SessionId-Validierung per Regex in EndlessController + DTOs (wie PuzzleController)',
      'Security: Repertoire-Limits — max 50 pro User, max 100 Files pro Repertoire',
      'Security: nginx laeuft als non-root User (Port 8080 intern)',
      'Security: init-db.sh — GRANT ALL durch spezifische Privileges ersetzt',
    ]},
    { version: '0.22.9', date: '2026-05-29', changes: [
      'Perf: TournamentDetail — Template-Getter durch gecachte Properties ersetzt (kein Array-Rebuild pro Change Detection)',
    ]},
    { version: '0.22.8', date: '2026-05-29', changes: [
      'Fix: Fehlende Error-Handler auf HTTP-Calls in RepertoireList, TournamentDetail — SnackBar-Feedback bei Fehlern',
    ]},
    { version: '0.22.7', date: '2026-05-29', changes: [
      'Fix: Puzzle-Laden zeigt ERROR-State mit Retry-Button statt endlosem Spinner',
    ]},
    { version: '0.22.6', date: '2026-05-29', changes: [
      'Security: GetCrawlerIp erfordert jetzt Authentifizierung (kein AllowAnonymous mehr)',
      'Security: ES+Kibana Ports in Production-Compose nicht mehr exponiert',
      'Cleanup: Irrelevanter CORS-Kommentar zu chrome-extension entfernt',
    ]},
    { version: '0.22.5', date: '2026-05-29', changes: [
      'Fix: CrawlerExceptionFilter faengt TaskCanceledException — Timeout ergibt 504 statt 500',
      'Fix: CrawlerProxyService — leere Response-Body sicher behandelt, CancellationToken durchgereicht',
    ]},
    { version: '0.22.4', date: '2026-05-29', changes: [
      'Security: ActiveGameState MaxLength 1MB im DTO (verhindert unbegrenztes Schreiben)',
      'Security: BookPuzzle Import — RequestSizeLimit 50MB + max 10000 Puzzles pro Import',
    ]},
    { version: '0.22.3', date: '2026-05-29', changes: [
      'Fix: FriendService Race Condition — Single SaveChanges + DbUpdateException Handling bei Re-Request nach Decline',
    ]},
    { version: '0.22.2', date: '2026-05-29', changes: [
      'Security: compose.dev.vpn.yml — Required-Variable-Checks fuer kritische Secrets',
    ]},
    { version: '0.22.1', date: '2026-05-29', changes: [
      'Security: Open Redirect in Login/Register behoben — returnUrl wird gegen Whitelist validiert',
    ]},
    { version: '0.22.0', date: '2026-05-29', changes: [
      'Endless Mode: Give Up zeigt "Gave Up" mit Flag-Icon statt "Wrong" (orange statt rot)',
      'Endless Mode: Show Eval und Give Up vor dem ersten Zug verfuegbar',
      'Infra: Kibana Logging-Dashboard wird automatisch erstellt (6 Panels: Volume, Levels, Apps, Errors, Timeline, Log-Tabelle)',
      'Fix: compose.dev.vpn.yml — kibana-init + Healthcheck + Service-Name korrigiert',
      'Fix: CRAWLER_API_KEY in .env.dev ergaenzt',
    ]},
    { version: '0.21.2', date: '2026-05-29', changes: [
      'Infra: Kibana Data Views werden beim Start automatisch erstellt (rookhub-logs, crawler-logs, alle-logs)',
      'Infra: kibana-init One-Shot-Container in beiden Docker-Compose-Files',
    ]},
    { version: '0.21.1', date: '2026-05-29', changes: [
      'Fix: Migration DropRequestLogs bereinigt — duplizierte Schema-Operationen entfernt die API-Start verhinderten',
    ]},
    { version: '0.21.0', date: '2026-05-29', changes: [
      'Logging: Elasticsearch + Kibana fuer zentrales Log-Management (ersetzt DB-Logging)',
      'Logging: Serilog mit Elasticsearch-Sink fuer strukturiertes Logging in beiden Projekten',
      'Cleanup: RequestLog DB-Tabellen, Middleware und Controller entfernt (RookHub + Crawler)',
      'Cleanup: LogRetentionService und CrawlRequestLog im Crawler entfernt',
      'Infra: ES 8.17 + Kibana 8.17 in allen Docker-Compose-Files',
    ]},
    { version: '0.20.1', date: '2026-05-28', changes: [
      'Fix: Stockfish-Timeout bei hoher Suchtiefe zeigt nicht mehr sofort "Incorrect" — User kann weiterspielen',
      'E2E Test: Neuer Testfall fuer Stockfish-Timeout-Szenario mit komplexer Stellung',
      'E2E: squareCenter/makeMove/dragMove unterstuetzen jetzt Black-Orientation',
    ]},
    { version: '0.20.0', date: '2026-05-28', changes: [
      'Feature: Server-Side Endless Puzzle Progress Sync — Config, Highscore und Active Game werden serverseitig gespeichert',
      'Feature: Endless Session History wird auf dem Server persistiert (max 50 Sessions)',
      'Feature: Anonyme und eingeloggte User koennen nahtlos auf anderen Geraeten weiterspielen',
      'Feature: Claim-Session uebertraegt anonyme Endless-Daten bei Login auf den Account',
      'Feature: Bulk-Import fuer localStorage-Migration (einmalig beim ersten Server-Sync)',
      'API: Neue Endpoints GET/PUT /api/endless/progress, POST /api/endless/sessions, POST /api/endless/claim-session',
      'API: Anonyme Varianten mit Rate-Limiting (anonymous-puzzle Policy)',
      'Tests: 19 neue Tests fuer EndlessProgressService (Progress, Sessions, Claim, BulkImport)',
    ]},
    { version: '0.19.4', date: '2026-05-28', changes: [
      'Refactor: UnitTest1.cs aufgeteilt in AuthServiceTests, ProfileServiceTests, FriendServiceTests, RepertoireServiceTests',
      'Refactor: TeamPlayersDialogComponent in eigene Datei extrahiert',
      'Refactor: Endless-Puzzle localStorage-Logik in EndlessStorageService ausgelagert',
      'Feature: Retry-Interceptor fuer 502/503/0 Fehler (1x Retry nach 1s)',
      'Feature: environment.prod.ts + Angular fileReplacements (kein sed im Dockerfile mehr)',
      'Crawler: RoundDetectionService mit IMemoryCache (60s pro Tournament)',
      'Crawler: CancellationToken durch alle Crawl-Methoden durchgereicht',
      'CI: Neuer Test-Workflow (.github/workflows/test.yml) fuer dotnet test + ng build',
    ]},
    { version: '0.19.3', date: '2026-05-28', changes: [
      'Code-Review: Crawler GetAllTournaments mit Pagination (page/pageSize) statt unbegrenzter Liste',
      'Code-Review: LogRetentionService fuer RookHub API und Crawler — loescht Request-Logs aelter als 30 Tage',
      'Code-Review: Frontend Tournament-Liste nutzt paginierte API-Response',
    ]},
    { version: '0.19.2', date: '2026-05-28', changes: [
      'Fix: E2E Test puzzle-moves.spec.ts erwartete deutschen Text statt englischem UI-Text',
    ]},
    { version: '0.19.1', date: '2026-05-28', changes: [
      'Crawler: Outgoing Request Logging — alle HTTP-Calls zu chess-results.com werden in DB persistiert (CrawlRequestLog)',
      'Crawler: Neuer Endpoint GET /api/crawl-request-logs mit Filter (URL, Status, Zeitraum, Success) und Pagination',
      'Crawler: Response-Body optional abrufbar (includeBody Parameter)',
    ]},
    { version: '0.19.0', date: '2026-05-28', changes: [
      'Anonyme Puzzle-Stats: Puzzle-Attempts werden auch ohne Login getrackt (localStorage SessionId)',
      'Anonyme Stats: Accuracy, Streak und Best Streak fuer nicht eingeloggte User',
      'Session-Claim: Bei Login/Register werden anonyme Puzzle-Daten automatisch auf den Account uebertragen',
      'Rate-Limiting: Eigene Policy fuer anonyme Puzzle-Endpoints (30 Requests/Minute)',
      'API: Neue Endpoints POST /api/puzzles/{id}/attempt/anonymous, GET /api/puzzles/stats/anonymous, POST /api/puzzles/claim-session',
    ]},
    { version: '0.18.5', date: '2026-05-28', changes: [
      'Code-Review: FriendService Suche mit determinisitscher Sortierung (OrderBy)',
      'Code-Review: AdminController ClearPuzzles in Transaktion gewrappt',
      'Code-Review: MaxFileSize Konstante dedupliziert (RepertoireService als Single Source)',
      'Code-Review: BackgroundTaskQueue DropOldest statt Wait (verhindert Request-Blockierung)',
      'Code-Review: Health-Endpoint prueft DB-Konnektivitaet (503 bei Fehler)',
      'Code-Review: PuzzleService Min/Max ID-Range mit MemoryCache (5min TTL)',
      'Code-Review: DB-Index auf TournamentSubscription.CrawlerTournamentId',
      'Code-Review: Dashboard takeUntilDestroyed fuer Subscription-Cleanup',
      'Crawler: ApiKey-Vergleich timing-safe (CryptographicOperations.FixedTimeEquals)',
      'Crawler: CrawlJob ErrorMessage Null-Check',
    ]},
    { version: '0.18.4', date: '2026-05-28', changes: [
      'Fix: Endless Mode letztes Puzzle erscheint jetzt in der Zusammenfassung (Reset auf letztem Leben)',
    ]},
    { version: '0.18.3', date: '2026-05-28', changes: [
      'Puzzle Mode: Einheitliche Controls in allen Zustaenden (kein UI-Hinweis ob am Loesungsweg)',
      'Puzzle Mode: Auto-Advance nach 3s bei geloestem Puzzle, Show Solution Button',
      'Puzzle Mode: Review-Button fuer letztes geloestes Puzzle',
    ]},
    { version: '0.18.2', date: '2026-05-27', changes: [
      'Endless Mode: Bei falschem Puzzle kein Auto-Advance, stattdessen Show Solution + Continue Buttons',
    ]},
    { version: '0.18.1', date: '2026-05-27', changes: [
      'Endless Mode: Stockfish Depth waehrend laufender Serie per Slider anpassbar',
    ]},
    { version: '0.18.0', date: '2026-05-27', changes: [
      'Security: CrawlerProxyService Error-Details durchreichen (CrawlerRequestException + ExceptionFilter)',
      'Security: JWT Key Minimum 32 Bytes fuer HMAC-SHA256',
      'Security: HSTS Header in nginx',
      'Security: Puzzle-Theme SQL-Wildcard Sanitization',
      'Security: Passwort-Validierung im Register-Formular (8+ Zeichen, Gross/Klein/Zahl)',
      'Performance: PuzzleService GetStats via DB-Level Aggregation statt ToListAsync',
      'Performance: Puzzle CSV Import mit File-Size-Limit (500 MB) und CancellationToken',
      'Fix: AdminSeeder kein Re-Hash bei unveraendertem Passwort',
      'Fix: AutoSubscriptionService Race-Condition bei SaveChanges (DbUpdateException Handling)',
      'Fix: TournamentMonitor per-User Scoping (UserId + FK + Migration)',
      'Fix: TournamentProxyController try/catch durch CrawlerExceptionFilter ersetzt',
      'Fix: AuthService localStorage Error Handling komplett in try/catch',
      'Fix: Dashboard typisierte HTTP-Calls (Repertoire[], Friend[], PuzzleStatsDto)',
      'Infra: compose.vpn.yml API depends_on Gluetun',
      'Crawler: BackgroundTaskQueue statt fire-and-forget Task.Run, 429 bei Queue-Full',
      'Crawler: RequestLoggingMiddleware ohne MemoryStream, Background-Queue statt Task.Run',
      'Crawler: VPN-Rotation Thread-Safety (Semaphore waehrend Rotation gehalten)',
      'Crawler: Separater HttpClient fuer Gluetun API (5s Timeout)',
      'Crawler: ResolveTournamentAsync auch fuer nicht-numerische IDs',
      'Tests: 16 neue Tests (CrawlerProxyService, BackgroundTaskQueue, AdminSeeder, PuzzleService, TournamentMonitor, AutoSubscription)',
    ]},
    { version: '0.17.0', date: '2026-05-27', changes: [
      'Book-Puzzles: Import von Puzzle-Buechern aus schach-bot (12 Buecher, 5000+ Puzzles)',
      'Book-Puzzles: Eigene Route /puzzles/book/:id mit Board, Zugloesung und Buch-Metadaten',
      'Book-Puzzles: Lookup-API GET /api/book-puzzles/by-line-id fuer schach-bot Integration',
      'Book-Puzzles: Admin-Import-Endpoint POST /api/admin/book-puzzles/import',
      'Book-Puzzles: Buch-Liste mit Puzzle-Counts GET /api/book-puzzles/books',
      'Book-Puzzles: Python-Import-Script scripts/import_books.py',
    ]},
    { version: '0.16.2', date: '2026-05-27', changes: [
      'Crawler: Migration-Upgrade-Doku fuer bestehende DBs (EnsureCreated → Migrate)',
      'Deploy: Crawler-Fix fuer leere __EFMigrationsHistory bei Bestandsdatenbanken',
    ]},
    { version: '0.16.1', date: '2026-05-27', changes: [
      'Security: Tournament-ID-Validierung gegen SSRF in Proxy- und Monitor-Controllern',
      'Security: .gitignore fuer Test-Artifacts (auth-state, playwright-report)',
      'Refactor: Fire-and-forget Task.Run ersetzt durch Channel+BackgroundService',
      'Refactor: TypeScript-Interfaces statt any fuer alle API-Responses',
      'Crawler: EnsureCreated durch EF Core Migrations ersetzt',
    ]},
    { version: '0.16.0', date: '2026-05-27', changes: [
      'Endless Mode: Puzzle-Review auf Game-Over-Screen zeigt alle gespielten Puzzles',
      'Endless Mode: Klick auf Puzzle im Review navigiert zu /puzzles/:id',
      'Endless Mode: Fehlgeschlagene Puzzles rot hervorgehoben',
    ]},
    { version: '0.15.9', date: '2026-05-27', changes: [
      'E2E Teststack: Isolierter Docker-Stack (compose.e2e.yml) mit eigenem DB, API, Frontend',
      'E2E Teststack: scripts/e2e.sh startet Stack, seedet Puzzles, fuehrt Tests aus, raeumt auf',
      'E2E: API_URL konfigurierbar in global-setup.ts und auth.fixture.ts',
    ]},
    { version: '0.15.8', date: '2026-05-27', changes: [
      'Fix: User-Seite bleibt fest — movable.color immer auf orientation statt turnColor',
      'Fix: Dests nur wenn User am Zug (verhindert Stockfish-Figuren ziehen nach Premove+Wrong Move)',
    ]},
    { version: '0.15.7', date: '2026-05-27', changes: [
      'Share-Puzzle: QR-Code + Link zum Teilen von Puzzles (Normal + Endless Mode)',
      'Direkt-Link /puzzles/:id laedt ein bestimmtes Puzzle',
    ]},
    { version: '0.15.6', date: '2026-05-27', changes: [
      'Fix: Premove-Orientierungsbug — illegale Premoves werden ignoriert statt Seitenwechsel auszulösen',
    ]},
    { version: '0.15.5', date: '2026-05-27', changes: [
      'E2E Tests: Falscher Zug + Premove in Puzzle Mode und Endless Mode',
    ]},
    { version: '0.15.4', date: '2026-05-27', changes: [
      'Puzzle Mode: Show Eval und Reset Buttons nach falschem Zug',
      'Fix: Endless Mode kein sofortiges Wrong bei Stockfish-Fehler',
    ]},
    { version: '0.15.3', date: '2026-05-27', changes: [
      'E2E Tests mit Playwright (Auth, Dashboard, Puzzles)',
    ]},
    { version: '0.15.2', date: '2026-05-27', changes: [
      'Tests: 110 neue Tests (122 → 232), alle Controller vollständig getestet',
      'Tests: TournamentProxyController, PuzzleController, AuthController, ProfileController, FriendController, RepertoireController, ExtensionController, RequestLogController, RoundMonitorService',
    ]},
    { version: '0.15.1', date: '2026-05-27', changes: [
      'Fix: Premoves im normalen Puzzle Mode funktionieren jetzt',
    ]},
    { version: '0.15.0', date: '2026-05-27', changes: [
      'Puzzle Mode: Stockfish übernimmt bei falschem Zug (wie im Endless Mode)',
      'Puzzle Mode: Schachmatt gegen Stockfish zählt als alternative Lösung',
      'Puzzle Mode: Mouseslip-Button (einmal pro Puzzle)',
      'Puzzle Mode: Stockfish Depth Einstellung (1–24, persistiert)',
      'Puzzle Mode: Premoves während Stockfish denkt',
    ]},
    { version: '0.14.5', date: '2026-05-27', changes: [
      'Fix: Premove funktioniert jetzt (Figuren während Gegnerzug anklickbar)',
    ]},
    { version: '0.14.4', date: '2026-05-27', changes: [
      'Fix: Premove-System komplett überarbeitet (Zug wird nach Gegnerzug automatisch ausgeführt)',
      'Fix: Hilfe-Overlay mit korrekten Umlauten',
      'Fix: Config-Grid Abstand zwischen Feldern korrigiert',
      'Hilfe: Stockfish Depth Einstellung dokumentiert',
    ]},
    { version: '0.14.3', date: '2026-05-27', changes: [
      'Endless Mode: Stockfish-Tiefe konfigurierbar (1–24, Standard 16)',
    ]},
    { version: '0.14.2', date: '2026-05-27', changes: [
      'Endless Mode: Premoves waehrend Gegner denkt (Zug vorausplanen wie auf Lichess/Chess.com)',
    ]},
    { version: '0.14.1', date: '2026-05-27', changes: [
      'Endless Mode: Hilfe-Overlay erklaert Spielablauf, Stockfish, Buttons und Einstellungen',
    ]},
    { version: '0.14.0', date: '2026-05-27', changes: [
      'Endless Mode: Mouseslip-Button macht letzten Zug gratis rueckgaengig (einmal pro Puzzle)',
    ]},
    { version: '0.13.9', date: '2026-05-27', changes: [
      'Endless Mode: Fasttrack standardmaessig aktiviert',
    ]},
    { version: '0.13.8', date: '2026-05-27', changes: [
      'Fix: Fehlende EF-Migration fuer Puzzles-Tabelle hinzugefuegt (Prod-Deployment)',
    ]},
    { version: '0.13.7', date: '2026-05-27', changes: [
      'Stockfish Suchtiefe von 12 auf 16 erhoeht (~2400 Elo)',
    ]},
    { version: '0.13.6', date: '2026-05-27', changes: [
      'Endless Mode: Puzzle Reset kostet ein Leben',
    ]},
    { version: '0.13.5', date: '2026-05-27', changes: [
      'Endless Mode: Alternative Loesung pausiert, Continue/Show Solution Buttons',
      'Endless Mode: Show Solution spielt die beabsichtigte Zugfolge animiert ab',
    ]},
    { version: '0.13.4', date: '2026-05-27', changes: [
      'Fasttrack: Auto-Thresholds mindestens startElo+400/+800 (keine sinnlosen niedrigen Werte mehr)',
      'Fasttrack: Step Size auf 10–200 geclampt',
    ]},
    { version: '0.13.3', date: '2026-05-27', changes: [
      'Endless Mode: Rating-Range aus DB geladen, alle Inputs validiert gegen tatsaechliche Puzzle-Range',
      'Endless Mode: Leere Rating-Bereiche werden automatisch uebersprungen',
      'Endless Mode: "Ausgespielt" Screen mit Stockfisch-Frage wenn Max-Rating ueberschritten',
      'Endless Mode: Step-Size Migration von altem Default 20 auf 40',
      'Endless Mode: Stale Fasttrack-Thresholds werden beim Laden bereinigt',
      'API: Neuer Endpoint GET /api/puzzles/rating-range',
    ]},
    { version: '0.13.2', date: '2026-05-27', changes: [
      'Fasttrack: Immer verfuegbar, Fallback startElo+400/+800 wenn keine History',
      'Fasttrack: Thresholds editierbar mit Auto-Wert-Anzeige und Klick-Reset',
      'Fasttrack: Manuelle Overrides werden persistiert',
      'Endless Mode: Range Width entfernt, ergibt sich aus Step Size',
      'Endless Mode: Puzzle-Tags standardmaessig ausgeblendet (Show/Hide toggle)',
    ]},
    { version: '0.13.0', date: '2026-05-27', changes: [
      'Endless Mode: Session-History trackt alle Spieldurchlaeufe (max 50, localStorage)',
      'Endless Mode: Fasttrack-Option ueberspringt leichte Puzzles basierend auf vergangenen Sessions',
      'Endless Mode: 3-Phasen Fasttrack-Algorithmus (Phase 1: bis 1. Fehler-Avg, Phase 2: bis 2. Fehler-Avg, Phase 3: Step 20)',
      'Endless Mode: Dynamisches Rating-System statt fester Level-Berechnung',
      'Endless Mode: Fehler-Ratings im Game-Over-Screen angezeigt',
      'Endless Mode: Phase-Indikator waehrend Fasttrack-Spiel',
    ]},
    { version: '0.12.4', date: '2026-05-27', changes: [
      'Endless Mode: Einheitliches UI nach erstem Zug (kein Unterschied zwischen richtig/falsch sichtbar)',
      'Endless Mode: Correct nur bei komplett geloestem Puzzle, sonst immer Reset/Give Up/Eval Buttons',
      'Endless Mode: Eval-Vergleich Start vs. Aktuell beim Klick auf Show Eval',
    ]},
    { version: '0.12.3', date: '2026-05-27', changes: [
      'Endless Mode: Stockfish spielt nach falschem Zug endlos weiter (kein Limit)',
      'Endless Mode: Matt gegen Stockfish zaehlt als alternative Loesung',
      'Endless Mode: Show Eval, Reset (kein Lebensverlust), Give Up Buttons',
      'Endless Mode: Eval-Anzeige aus Weiss-Perspektive',
      'Fix: CSP + WASM MIME-Type fuer Stockfish Web Worker',
    ]},
    { version: '0.12.2', date: '2026-05-27', changes: [
      'Endless Mode: Stockfish-Refutation bei falschem Zug',
      'Stockfish 18 Lite (WASM) als Web Worker integriert',
    ]},
    { version: '0.12.1', date: '2026-05-27', changes: [
      'Endless Mode: Standard Step-Size und Range auf 40 erhoeht',
    ]},
    { version: '0.12.0', date: '2026-05-27', changes: [
      'Endless Puzzle Mode: Progressive Schwierigkeit mit konfigurierbarem Start-Rating, Step-Size und Range',
      'Endless Mode: 3 Leben, bei 0 Game Over mit Highscore-Tracking',
      'Endless Mode: Prefetch fuer schnelles Laden, Session-Timer, Level-Anzeige',
      'Endless Mode: Config wird in localStorage gespeichert, Highscore persistent',
    ]},
    { version: '0.11.3', date: '2026-05-26', changes: [
      'Performance: Puzzle-Loading von 10+ Sekunden auf unter 1 Sekunde optimiert',
      'Backend: ID-Range-Ansatz statt COUNT+SKIP auf 5.3M Rows',
      'Frontend: Naechstes Puzzle wird im Hintergrund vorgeladen',
    ]},
    { version: '0.11.2', date: '2026-05-26', changes: [
      'Fix: Puzzle-Board rendert korrekt (Critical CSS + inlineCritical deaktiviert)',
      'Fix: Puzzle-Zuege funktionieren (Chessground viewOnly-Bug umgangen)',
      'Fix: State-Reihenfolge in Puzzle-Setup korrigiert (dests vor Board-Update)',
    ]},
    { version: '0.11.1', date: '2026-05-26', changes: [
      'Puzzles ohne Anmeldung spielbar (Route + API offen)',
      'Stats und Attempts nur fuer eingeloggte User',
      'Puzzles-Link in Navbar auch fuer nicht eingeloggte User sichtbar',
    ]},
    { version: '0.11.0', date: '2026-05-26', changes: [
      'Puzzle-Feature: Lichess-Puzzles interaktiv loesen mit Chessground-Board',
      'Puzzle-Filter: Rating-Range und Skip-Solved',
      'Puzzle-Statistiken: Accuracy, Streak, History',
      'Admin: CSV-Import fuer Lichess-Puzzle-Datenbank',
      'Dashboard: Puzzle-Stats-Card',
    ]},
    { version: '0.10.0', date: '2026-05-26', changes: [
      'Backend: Automatisches Detail-Crawling (art=9) fuer favorisierte Spieler bei neuer Runde',
      'Crawler: Neuer Endpoint POST /api/crawl/player-details fuer Spieler-Einzelergebnisse',
      'Crawler: Neuer Endpoint GET /api/tournaments/{id}/players/{snr}/results',
      'PlayerResult-Model erweitert: OpponentSnr, OpponentName, OpponentElo, Points',
      'Claude-Settings fuer beide Projekte vereinheitlicht',
    ]},
    { version: '0.9.9', date: '2026-05-21', changes: [
      'Spieler und Freunde werden bei Turnier-Abos automatisch als Favoriten markiert',
      'Matching ueber FIDE-ID und Name',
    ]},
    { version: '0.9.8', date: '2026-05-21', changes: [
      'Fix: Dashboard-Turnier-Links nutzen jetzt korrekte ID (nicht mehr ChessResults-ID)',
      'Crawler akzeptiert sowohl interne DB-ID als auch ChessResults-ID in Turnier-Routen',
    ]},
    { version: '0.9.7', date: '2026-05-21', changes: [
      'Info-Button neben Login/Register zeigt Quickstart-Guide für nicht eingeloggte User',
      'Quickstart aus dem User-Menü entfernt',
    ]},
    { version: '0.9.6', date: '2026-05-21', changes: [
      'Fix: Quickstart-Icons für Monitor und ChessResults-ID korrigiert',
      'Fix: Umlaute im Quickstart-Guide',
    ]},
    { version: '0.9.5', date: '2026-05-21', changes: [
      'Quickstart-Guide im User-Menü erklärt Subscribe, Monitor, Favoriten und ChessResults-ID',
      'Quickstart wird automatisch nach Registrierung angezeigt',
    ]},
    { version: '0.9.4', date: '2026-05-21', changes: [
      'Dashboard: Abonnierte Turniere sind jetzt klickbar und fuehren zur Turnierseite',
    ]},
    { version: '0.9.3', date: '2026-05-20', changes: [
      'Anstehende Turniere werden automatisch abonniert wenn ChessResults-ID hinterlegt',
      'Taegliche automatische Suche nach neuen Turnier-Anmeldungen',
    ]},
    { version: '0.9.2', date: '2026-05-20', changes: [
      'Revert: Spielersuche wieder rein per Vor-/Nachname (ChessResults-ID-Suche entfernt)',
    ]},
    { version: '0.9.1', date: '2026-05-20', changes: [
      'Fix: Spielersuche zeigt bei exaktem Namens-Treffer nur diesen an (statt alle mit gleichem Vornamen)',
    ]},
    { version: '0.9.0', date: '2026-05-20', changes: [
      'Spielersuche im Profil: Vorname/Nachname eingeben und auf ChessResults + FIDE suchen',
      'Suchergebnisse zeigen Name, Titel, Elo, Land und IDs an',
      'Klick auf Ergebnis uebernimmt ChessResults-ID und/oder FIDE-ID automatisch',
      'Bei genau einem Treffer pro Quelle wird die ID automatisch ausgefuellt',
      'Neue Profil-Felder: Vorname und Nachname',
    ]},
    { version: '0.8.7', date: '2026-05-19', changes: [
      'Freunde-Suche durchsucht jetzt auch Chess.com, Lichess, FIDE-ID und ChessResults-ID',
      'Suchergebnisse zeigen vorhandene Schach-Identitaeten an',
    ]},
    { version: '0.8.6', date: '2026-05-19', changes: [
      'Fix: Schachbrett-Rendering komplett ueberarbeitet (JS-basierte Dimensionen statt CSS-Tricks)',
      'Chessground bekommt jetzt explizite Pixel-Groesse via JavaScript',
      'ResizeObserver haelt das Brett bei Fensteraenderungen quadratisch',
    ]},
    { version: '0.8.5', date: '2026-05-19', changes: [
      'Fix: Chessground-Board rendert jetzt korrekt (padding-bottom Trick statt aspect-ratio)',
      'Fix: Board-Wrapper mit zwei DIVs damit Chessground konkrete Dimensionen bekommt',
    ]},
    { version: '0.8.4', date: '2026-05-19', changes: [
      'Fix: Schachbrett rendert jetzt korrekt neben der Zugliste (explizite 400x400px Dimensionen fuer Chessground)',
    ]},
    { version: '0.8.3', date: '2026-05-19', changes: [
      'Fix: Schachbrett-Layout im Repertoire-Detail korrigiert (feste Breite 400px, responsive Breakpoint)',
    ]},
    { version: '0.8.2', date: '2026-05-19', changes: [
      'PGN-Kommentare werden in der Zugliste angezeigt (kursiv, unter dem Zug)',
      'Chessbase-Annotationen ([%csl], [%cal], [%tqu]) werden aus Kommentaren entfernt',
      'PGN-Viewer Dialog zeigt ebenfalls Kommentare an',
    ]},
    { version: '0.8.1', date: '2026-05-19', changes: [
      'Fix: PGN-Parser unterstuetzt Chessbase-Annotationen (RAV-Varianten, Labels, NAGs)',
      'Fix: Header-Erkennung ignoriert jetzt ] in Kommentaren wie {[%tqu ...]}',
    ]},
    { version: '0.8.0', date: '2026-05-19', changes: [
      'Repertoire: Neues Lines/Tree/Edit-Layout mit Inline-Schachbrett',
      'Repertoire: Lines-Ansicht zeigt alle Partien mit Zugliste und Navigation',
      'Repertoire: Tree-Ansicht mit Zugbaum, Haeufigkeiten und Breadcrumb-Navigation',
      'Repertoire: Edit-Ansicht fuer Datei-Upload und -Verwaltung',
      'Repertoire: Keyboard-Navigation (Pfeiltasten) in der Lines-Ansicht',
      'Refactor: PGN-Parser als geteiltes Modul extrahiert',
    ]},
    { version: '0.7.0', date: '2026-05-19', changes: [
      'PGN-Viewer: Interaktives Schachbrett mit Zugnavigation (chess.js + chessground)',
      'PGN-Viewer: Zugliste mit Klick-Navigation und Keyboard-Support (Pfeiltasten, Home/End)',
      'PGN-Viewer: Multi-Game-Support fuer PGN-Dateien mit mehreren Partien',
    ]},
    { version: '0.6.8', date: '2026-05-19', changes: [
      'Admin: Log-Bereich mit Filtern (Pfad, Methode, Status, Benutzer)',
      'Admin: Farbige Status-Codes, Methoden-Badges, IP-Spalte',
      'Admin: Langsame Requests (>1s) hervorgehoben',
    ]},
    { version: '0.6.7', date: '2026-05-19', changes: [
      'Checkliste: Nach jedem Commit wird die aktuelle Version mitgeteilt',
    ]},
    { version: '0.6.6', date: '2026-05-19', changes: [
      'Fix: Copy-Button im Share-Dialog funktioniert jetzt auch ohne HTTPS (Fallback)',
    ]},
    { version: '0.6.5', date: '2026-05-19', changes: [
      'Fix: Typo im Repository-Namen korrigiert (chessreslults → chessresults)',
    ]},
    { version: '0.6.4', date: '2026-05-19', changes: [
      'Security: Input-Validierung und Laengenbegrenzungen auf allen Suchfeldern',
      'Security: Passwort-Policy auf 8 Zeichen + Komplexitaet erhoeht',
      'Security: Crawler-Body-Validierung im Proxy-Controller',
      'Fix: HealthController gibt 503 statt falsches 200 bei IP-Fehler',
      'Fix: Leere Catch-Bloecke durch Logging ersetzt',
      'Performance: Response Compression aktiviert',
    ]},
    { version: '0.6.3', date: '2026-05-19', changes: [
      'Security: API-Key-Authentifizierung fuer Crawler-Endpoints',
      'Security: TournamentMonitorController erfordert jetzt Authentifizierung',
      'Security: .env.dev aus Git-Tracking entfernt',
    ]},
    { version: '0.6.2', date: '2026-05-19', changes: [
      'Dev-Badge im Footer: zeigt "dev" neben der Version im Dev-Build',
      'CI/CD: :dev und :latest Tag-Schema fuer Docker-Images',
    ]},
    { version: '0.6.1', date: '2026-05-19', changes: [
      'CI/CD: GitHub Actions bauen Docker-Images bei Push (latest) und Tag (versioniert)',
      'Prod-Compose nutzt IMAGE_TAG fuer getaggte Versionen',
    ]},
    { version: '0.6.0', date: '2026-05-19', changes: [
      'Admin-System: Benutzerverwaltung mit Admin-Rolle (IsAdmin)',
      'Admin-Seed beim Start ueber ADMIN_USERNAME/ADMIN_PASSWORD Env-Variablen',
      'Admin-Panel mit User-Verwaltung und Request-Log-Ansicht',
      'RequestLogController jetzt nur fuer Admins zugaenglich',
    ]},
    { version: '0.5.2', date: '2026-05-19', changes: [
      'Commit-Checkliste in beiden CLAUDE.md verankert (Version, Changelog, Tests)',
    ]},
    { version: '0.5.1', date: '2026-05-19', changes: [
      '73 neue Unit-Tests fuer beide Projekte (Crawler + RookHub)',
      'Test-Pflicht in CLAUDE.md verankert: jedes Feature/Endpoint/Bugfix braucht Tests',
    ]},
    { version: '0.5.0', date: '2026-05-19', changes: [
      'Oeffentliche Turnierseite /t/:id (ohne Login einsehbar)',
      'Share-Button mit QR-Code und kopierbarem Link',
      'localStorage-Favoriten fuer oeffentliche Ansicht',
    ]},
    { version: '0.4.0', date: '2026-05-18', changes: [
      'Turnier-Datum und Ort von chess-results.com extrahieren',
      'VPN IP-Rotation alle 20 Requests (via Gluetun API)',
      'HTTP-Retry bei transienten Netzwerkfehlern',
    ]},
    { version: '0.3.0', date: '2026-05-18', changes: [
      'Browser-Benachrichtigungen bei neuer Runde (Round Monitor)',
    ]},
    { version: '0.2.0', date: '2026-05-18', changes: [
      'Changelog hinzugefuegt (Klick auf Version im Footer)',
      'Crawler IP-Endpoint (VPN-Verifizierung)',
      'Tournament Round Monitor (1h Auto-Check)',
      'Request Logging Middleware',
      'Keine Default-Werte in Compose-Example-Dateien',
    ]},
    { version: '0.1.0', date: '2026-05-18', changes: [
      'Versionsnummer im Footer (Desktop)',
    ]},
  ]
};
