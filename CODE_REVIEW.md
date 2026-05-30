# Code-Review — RookHub & ChessResults Crawler

**Datum:** 2026-05-30
**Stand:** RookHub `2a1e5de` (master, v0.23.2) · Crawler `1b331b0` (main) — beide Working Trees sauber
**Umfang:** ~20k LOC (4.800 Backend RookHub, 9.400 Angular, 5.500 Crawler), Review des Gesamtbestands (kein Diff)
**Methodik:** 6 Fachbereiche parallel reviewt; die folgenschwersten Findings am Code gegengeprüft (mit ✅ *verifiziert* markiert)

---

## Gesamteinschätzung

Solide, durchdachte Codebasis mit erkennbar gepflegter Sicherheits-/Qualitätskultur.

**Stark:** vollständige JWT-Validierung mit Key-Längenprüfung · BCrypt · timing-sicherer API-Key-Vergleich · keine Secrets im Code · sauberes Cascade-Design · korrektes DbContext-Scoping in allen Background-Services · kein `innerHTML`/`bypassSecurityTrust` im Frontend · Open-Redirect-Schutz beim Login · durchgängig non-root Container. Viele in der Git-History dokumentierte Fixes (VPN-Threadsafety, ID-Routing, Pairing-Transaktionen) greifen verifiziert.

**Schwerpunkte der Findings:** Fehlerpfade in Job-/Sync-Lebenszyklen · einige Nebenläufigkeits-Races · Infra-Härtung (Deployment-Exposition) · CI-Gating.

---

## Priorisierte Top-Findings

> **Deployment-Kontext (vom Betreiber bestätigt):** Der Stack läuft hinter NAT im Heimnetz; von außen sind **keine** Ports weitergeleitet. Externe Erreichbarkeit wird ausschließlich über den Reverse-Proxy gesteuert. Dadurch sinkt die Schwere mehrerer „Exposition"-Findings deutlich — sie betreffen nur das LAN, nicht das Internet. Severities unten entsprechend angepasst.

| # | Sev | Finding | Ort |
|---|-----|---------|-----|
| 1 | LOW | ES (:9200) + Kibana (:5601) ohne Auth auf `0.0.0.0` → nur aus dem **Heimnetz** erreichbar (Ports nicht weitergeleitet). Residualrisiko nur, falls ES/Kibana je in den Reverse-Proxy aufgenommen werden | `compose.dev.yml:107-134`, `compose.vpn.yml:118-143` |
| 2 | CRIT | Docker-Images werden bei Push **ohne vorgeschaltete Tests** gebaut & gepusht (kein `needs:`-Gate) ✅ | beide `.github/workflows/docker.yml` |
| 3 | CRIT | Default-Passwörter in Crawler-Standalone-Compose (`:-rootpassword`), verletzt eigene „keine Defaults"-Regel | `chessresults_crawler/docker-compose.yml:6-9` |
| 4 | HIGH | Crawler-`API_KEY` im Standalone-Compose ungesetzt → Middleware **fail-open**, ganze API offen | `docker-compose.yml:84-87` + `ApiKeyMiddleware.cs:26-31` |
| 5 | HIGH | `StockfishService` ist `root`-Singleton (1 Worker), wird aber von 3 Komponenten im `ngOnDestroy` `terminate()`t → Worker-Konflikt | `stockfish.service.ts:8` + 3 Komponenten |
| 6 | HIGH | `CrawlJob` bleibt bei Enqueue-Fehler/Crash dauerhaft `Queued` → Duplikat-Check blockiert das Turnier **für immer** | `CrawlController.cs:56-73` |
| 7 | HIGH | `GetRandomAsync`: ID-Range über **ungefilterte** Tabelle → bei restriktiven Filtern deterministisch immer dasselbe Puzzle ✅ | `PuzzleService.cs:49-64, 330` |
| 8 | HIGH | BookPuzzle-Ladefehler → `state='LOADING'` bleibt → **endloser Spinner**, kein Retry | `book-puzzle.component.ts:284-288` |
| 9 | MED | `FriendController` `return Forbid(ex.Message)` interpretiert Message als Auth-Scheme → wirft 500 statt 403 ✅ | `FriendController.cs:57,70,83` |
| 10 | MED | Friendship A→B / B→A: Unique-Index nur auf geordnetes Paar → TOCTOU-Race erlaubt gespiegelte Doppel-Beziehung ✅ | `AppDbContext.cs:53` + `FriendService.cs:56` |

---

## Bereich 1 — Security (Backends)

- **[HIGH → Intent prüfen] `[AllowAnonymous]` auf 6 Turnier-Proxy-GETs** ✅ (`TournamentProxyController.cs:34-95`). `[Authorize]` der Klasse wird überschrieben; `GetById/Players/Teams/TeamDetail/Pairings/PlayerResults` sind anonym. Das **public-tournament**-Feature im Frontend legt nahe, dass dies *gewollt* ist. Falls ja: dokumentieren + eigenes (niedrigeres) Rate-Limit für die anonymen Proxy-Routen — aktuell greift nur das globale 100/min, ein anonymer Nutzer kann teure Crawler-Proxy-Calls auslösen. Inkonsistenz: `GetAll` (Liste) verlangt Auth, Einzel-Reads nicht.
- **[MED] BCrypt Work Factor nicht gesetzt** (Default 11, heute ≥12 empfohlen) — `AuthService.cs:37`, `AdminSeeder.cs:25,34`. Außerdem `HashPassword` statt `EnhancedHashPassword` → BCrypt schneidet bei 72 Byte ab, was `MaxLength(1024)` im DTO irreführend macht.
- **[MED] Login-Timing → User-Enumeration** (`AuthService.cs:55`): bei unbekanntem User wird BCrypt per Short-Circuit übersprungen. Fehlermeldung korrekt generisch. Fix: Dummy-Verify gegen Konstanten-Hash.
- **[MED] JWT `ClockSkew` nicht gesetzt** → Default 5 min Toleranz über `exp` hinaus (`Program.cs:63`). Explizit auf 1 min/Zero setzen.
- **[MED] Default-Admin `change_me` im Template** + Seeder setzt Passwort bei **jedem Start** zurück (`AdminSeeder.cs:31-36`) → UI-Passwortänderung wird beim nächsten Deploy überschrieben.
- **[LOW] `/api/health/ip` anonym** leakt VPN-Exit-IP, leichter SSRF-Probe-Vektor; `OpenPaths`-Match per `StartsWith` zu großzügig (`HealthController.cs:21`, `ApiKeyMiddleware.cs:9`).
- ✅ Sauber: keine Secrets in appsettings/Code · globaler Exception-Handler ohne Stacktrace-Leak · API-Key timing-safe (`FixedTimeEquals`) · JWT vollständig validiert (HMAC-SHA256) · Admin-Routen `[Authorize(Roles="Admin")]` mit Self-Delete/Demote-Schutz · Swagger nur in Development.

## Bereich 2 — RookHub Backend-Logik

- **[HIGH] Upload-Größenlimit greift bei nicht-seekbaren Streams erst nach `ReadToEndAsync`** (`RepertoireService.cs:135-148`) → Heap-DoS via Chunked-Upload; Controller-`file.Length`-Check schützt nur an einer Stelle. Zusätzlich doppelte Stream-Ownership (`StreamReader` ohne `leaveOpen`).
- **[HIGH] `RecordAttemptAsync` ohne Idempotenz/Limit** (`PuzzleService.cs:81`) → Stats/Streaks fälschbar, `excludeSolved` aushebelbar, unbegrenztes Tabellenwachstum.
- **[HIGH] `RoundMonitorService` — ein `SaveChanges` nach der ganzen Schleife** (`RoundMonitorService.cs:137`): bei Exception gehen alle Iterations-Updates verloren; Crawl-Trigger hängt am Crawler-`hasNewRound`-Flag statt an `LastKnownRounds` → potenziell wiederholtes Crawlen jede 30 s.
- **[MED] `EndlessProgressService.ClaimSessionAsync` ohne Transaktion** + Doppelklick-Race kann zwei `EndlessProgress` mit gleicher `UserId` anlegen → unbehandelte `DbUpdateException` → 500 (`EndlessProgressService.cs:177`).
- **[MED] `AutoSubscriptionService`**: pauschales Detach **aller** Added-Entries bei einer Kollision verwirft auch valide Subscriptions (`AutoSubscriptionService.cs:155`).
- **[MED] `BackgroundTaskQueue`: `DropOldest` + `WriteAsync`-Fallback ist toter, widersprüchlicher Code** — Tasks werden bei Last lautlos verworfen, Warn-Log greift nie (`BackgroundTaskQueue.cs:19-30`). Gleiches Muster im Crawler mit `DropWrite`.
- **[MED] Inkonsistente Statuscodes**: Create-Endpoints liefern teils `200` statt `201` (Subscription/Favorite/Endless); `RepertoireController` macht es richtig.
- **[LOW] Reads ohne `AsNoTracking`** durchgängig; `FriendService.GetFriends/SearchUsers` materialisiert volle Entities statt zu projizieren.
- ✅ Sauber: kein `async void`/`.Result` · korrektes Cancellation-Handling · DbContext-Scoping in Singletons korrekt · IDOR-frei (Ownership-Filter überall) · `CrawlerProxyService` mit typed HttpClient · `ImportFromCsvAsync` vorbildlich (Batching + `ChangeTracker.Clear`).

## Bereich 3 — Crawler-Logik

- **[HIGH] Player-Detail-Crawl ohne Job-Status-Tracking** + geschluckte Per-Spieler-Fehler (`CrawlerService.cs:224`, `CrawlController.cs:101`) → Teilfehler unsichtbar, Job gilt als erfolgreich.
- **[HIGH] `CrawlerService` transient + scoped `AppDbContext`** vermischt HTTP-Fetching und DB-Schreiben; reine Such-Endpoints ziehen unnötig einen DbContext mit. Konzeptionell trennen.
- **[MED] Redirect/SNode hängt am `AllowAutoRedirect`-Default**, kein `PooledConnectionLifetime` → nach VPN-Rotation bleiben gepoolte Connections auf alter Route (`Program.cs:53`, `CrawlerService.cs:525`). Expliziten `SocketsHttpHandler` setzen.
- **[MED] VPN-Rotation hält Semaphore ~13 s und ignoriert `ct`** (`CrawlerService.cs:655`) — durch Single-Worker entschärft, aber Shutdown nicht sauber abbrechbar.
- **[MED] `ParseIndividualPairingsAsync` mit harten Spaltenindizes** (`cells[3/6/9]`, `HtmlParserService.cs:152`) → bei Layout-Änderung **stille Fehlzuordnung** statt Exception. Restliche Parser nutzen robustes Header-Matching.
- **[MED] Player-Details-Upsert ohne Transaktion** (`CrawlerService.cs:177`) → inkonsistente Teil-Ergebnisse bei Fehler.
- **[MED] Numerische Route-ID: DB-Id vs ChessResultsId mehrdeutig** (`TournamentsController.cs:146`) — bei kleinen Zahlen still falsches Turnier möglich.
- ✅ Sauber: Rate-Limiter mit try/finally + Timeout · SSRF-Host-Checks nach Redirect · Pairing-Re-Crawl in Transaktion · ID-Whitelisting per Regex · CultureInfo beim Score-Parsing · null-sichere AngleSharp-Navigation.

## Bereich 4 — Datenschicht / EF Core

- **[HIGH] Friendship** (siehe #10) + fehlender Self-Friend-Ausschluss (`RequesterId == AddresseeId`).
- **[HIGH] `TournamentMonitor.CrawlerTournamentDbId`** ist der *volatile* Crawler-PK, wird direkt in Crawler-URLs eingesetzt (`RoundMonitorService.cs:69`). Nach Löschen/Neuanlage im Crawler zeigt er ins Leere/falsch. Besser über stabile chess-results-ID auflösen + 404-Handling.
- **[MED] Cross-DB-Referenzen** (`CrawlerTournamentId` in 4 Tabellen) ohne Reconciliation → verwaiste Subscriptions/Favorites veralten still. Bewusste Architektur, aber Cleanup-Job/Doku fehlt.
- **[MED] `EndlessProgress`: Unique nur auf `UserId`**, nicht auf `AnonymousSessionId` (NULL-tolerant) → pro anonymer Session Upsert-Duplikate möglich.
- **[LOW] Keine Concurrency-Token (RowVersion)** bei Upsert-Pfaden (Endless-Highscore via Parallel-Tabs → Last-Write-Wins).
- ✅ Sauber: ModelSnapshots stimmen exakt mit letzten Migrations überein (kein Drift) · Cascade/Restrict/SetNull durchdacht · alle FK + fachlichen Unique-Constraints vorhanden · UTC durchgängig · RequestLogs-Drop in beiden Projekten verlustfrei.

## Bereich 5 — Angular-Frontend

- **[HIGH] JWT im `localStorage`** (`auth.service.ts:48`) — XSS-exfiltrierbar; Crawler-Daten werden zwar nur per `{{ }}` (escaped) gerendert, aber das Token ist die wertvollste Beute. HttpOnly-Cookie + strenge CSP erwägen.
- **[HIGH] Kein Token-Refresh** (`auth.service.ts:82`) → bei aktivem Polling (Monitor 30 s / Crawl-Job 2 s) plötzlicher Redirect auf `/login` bei Ablauf.
- **[HIGH] Endless-Sync vs. Claim-Race** (`endless-puzzle.component.ts:851`) — Migration und `claimAnonymousPuzzleSession` schreiben unkoordiniert parallel zum Server; Login-Statuswechsel zwischen Konstruktor und Antwort nicht abgesichert.
- **[MED] Teure Template-Getter ohne OnPush** in `public-tournament.component.ts:495` (Sort/Filter bei jedem CD-Tick) — `TournamentDetailComponent` löst es bereits per gecachten Feldern; Public-Variante nicht nachgezogen.
- **[MED] PGN-Parser zählt Move-Tokens per Zweit-Heuristik** parallel zur chess.js-History (`pgn-parser.ts:67`) → Kommentar-Fehlzuordnung bei Rochade/Promotion/Null-Move (keine Tests dafür).
- **[MED] Doppeltes `chess.undo()` ohne Rückgabeprüfung** in 3 Komponenten → Brett/State-Divergenz im Race.
- **[MED] Durchgängig manuelle Subscriptions ohne `takeUntilDestroyed`** (nur `DashboardComponent` macht es vorbildlich); nested subscribe in `recordAttempt` ist Anti-Pattern (→ `switchMap`).
- ✅ Sauber: Guards inkl. Open-Redirect-Sanitization · `retryInterceptor` idempotent · defensives JWT-Parsing · `PuzzleBoardComponent` räumt `ResizeObserver`/Chessground korrekt auf · Timer-Cleanup konsequent.

## Bereich 6 — Infrastruktur / CI

Zusätzlich zu #1–3 oben:
- **[CRIT/dev-vpn] api ohne `gluetun: service_healthy`-Gate** trotz `Crawler__BaseUrl=gluetun:8080` (`compose.dev.vpn.yml:123`).
- **[HIGH] Keine Healthchecks für api/frontend/crawler** in dev/vpn → `depends_on: service_started` wartet nicht auf echte Bereitschaft (E2E-Stack hat es bereits richtig).
- **[HIGH] Gluetun Kill-Switch nicht explizit** + Restart-Leak/Ausfall-Risiko bei `network_mode: service:gluetun`.
- **[HIGH] Base-Images nicht gepinnt** (`qmcgaw/gluetun` ohne Tag = latest, `:9.0`, `nginx:alpine`, `node:24-alpine`).
- **[HIGH] Crawler ohne `.dockerignore`** → `bin/`/`obj/` landen im Build-Context.
- **[HIGH] Frontend-Port-Drift**: Beispiel-Compose mappen `:80`, nginx lauscht auf `8080` → kopiertes Beispiel ergibt totes Frontend.
- **[HIGH] `.env.example` (Crawler) mit echten Default-Passwörtern** statt Platzhaltern.
- **[MED] Gluetun Healthcheck prüft `:9999`** (Control-Server-Default ist 8000) → `service_healthy`-Gate evtl. unzuverlässig; MariaDB-Port öffentlich in vpn-Variante.
- **[MED] nginx ohne Rate Limiting** auf `/api/`-Proxy; CSP ohne `frame-ancestors`.
- **[MED → BEHOBEN v0.23.3] `kibana-init` Timing-Bug → leeres Kibana** (`init-kibana.sh`, `compose.dev.yml:147-159`). Der One-Shot (`restart: "no"`) hängt nur an `kibana: service_healthy`, nicht daran, dass API/Crawler bereits geloggt haben. Beim ersten `up` läuft er, bevor die Indizes `rookhub-logs-*`/`crawler-logs-*` existieren; `create_data_view` ohne `allowNoIndex:true` → Kibana 8 antwortet HTTP 400 ("no matching indices") → vom Skript geschluckt → nie wieder gelaufen. Folge: Logs landen in ES, aber Kibana bleibt ohne Data Views/Dashboard. **Live verifiziert** (ES hatte 434 rookhub- + 9 crawler-Logs, Kibana hatte 0 Data Views/Dashboards). Fix: `allowNoIndex:true` in `init-kibana.sh` ergänzt → Erstellung timing-unabhängig. Offen optional: Retry-Schleife statt reinem One-Shot.

> **Korrektur zum ersten Infra-Befund:** Die frühere Aussage „`kibana-init` existiert nur in dev/dev-vpn, nicht in vpn" ist **falsch** — der Service ist in allen drei Varianten vorhanden (`compose.dev.yml:147`, `compose.dev.vpn.yml:190`, `compose.vpn.yml:157`). Das eigentliche Problem war nicht das Fehlen des Services, sondern der oben beschriebene Timing-/`allowNoIndex`-Bug.

- ✅ Sauber: RookHub-Secrets mit `:?`-Fail-Fast · non-root überall · gute nginx-Security-Header + CSP · 10 MB/15 M PGN-Limit konsistent · korrektes `network_mode: service:gluetun` · `GITHUB_TOKEN` statt PAT · saubere semver-Tag-Strategie.

---

## Empfohlene Reihenfolge

1. **Sofort:** Crawler-`API_KEY` verpflichtend / Middleware fail-closed; Default-Passwörter aus Crawler-Compose/`.env.example` entfernen. *(ES/Kibana-Exposition entschärft durch Heimnetz/NAT — siehe Deployment-Kontext; nur abdichten, falls je hinter den Reverse-Proxy gehängt.)*
2. **CI-Gate:** Docker-Push an grüne Tests koppeln (`needs:`/`workflow_run`).
3. **Funktionsbugs mit Nutzerwirkung:** CrawlJob-Stuck-`Queued` (#6), `GetRandomAsync`-Determinismus (#7), BookPuzzle-Spinner (#8), `Forbid(message)`→500 (#9), Stockfish-Worker-Lifecycle (#5).
4. **Datenintegrität:** Friendship-Normalisierung + Self-Friend-Check, EndlessProgress-Unique auf `AnonymousSessionId`, TournamentMonitor stale-Handling.
5. **Härtung/Qualität:** ClockSkew, BCrypt-Workfactor, Healthchecks, Image-Pinning, `.dockerignore`, `takeUntilDestroyed`.

---

## Detail: ES/Kibana-Zugang (Finding #1) — Hintergrund

Port-Bindungs-Stufen:

| Schreibweise | Erreichbar von |
|---|---|
| `"5601:5601"` (aktuell) | **0.0.0.0** — jeder im Netz/Internet mit Host-IP |
| `"127.0.0.1:5601:5601"` | nur vom **Host** (localhost) / per SSH-Tunnel |
| kein `ports:` | nur **containerintern** (Docker-Netz) |

Die App liest ES über den internen Namen `elasticsearch:9200`, **nicht** über den Host-Port → das Entfernen/Einschränken des Host-Ports bricht die Logging-Pipeline nicht.

**Server-Optionen (Zugang bleibt erhalten):**
- **A) SSH-Tunnel (empfohlen, 0 Aufwand):** Kibana intern lassen, `ssh -L 5601:localhost:5601 server`, lokal `http://localhost:5601`.
- **B) `xpack.security.enabled=true`** → echtes Login, Kibana darf offen bleiben.
- **C) Reverse-Proxy (nginx) mit Basic-Auth** vor Kibana.

Konkrete Änderungen:
- `compose.dev.yml:113` → `- "127.0.0.1:${ES_PORT:-9200}:9200"`
- `compose.dev.yml:134` → `- "127.0.0.1:${KIBANA_PORT:-5601}:5601"`
- `compose.vpn.yml:143` (Kibana) → entfernen (Variante A) oder `- "127.0.0.1:${KIBANA_PORT:-5601}:5601"`
