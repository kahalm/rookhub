# TODO

Dinge die nicht direkt angegangen werden, aber nicht vergessen werden sollen.

_Legende: `[ ]` offen · `[~]` Hauptteil erledigt, Rest bewusst geparkt (Begründung steht dabei) ·
`[x]` erledigt, bleibt als Beleg stehen. Erledigte Einzelfunde ohne weiteren Wert stehen unten
im Archiv. Zuletzt gesichtet: **2026-08-26**._

## Periodisch
- [ ] Code Review — letzter: **2026-08-25** → Review über `rookhub` (~1800 Quelldateien). 15 Funde, alle verifiziert (kein Fehlalarm), **14 behoben in v0.376.2**: WQL-Wildcard `*` statt `%` im Waisen-Aufräumer (`reap_orphans.ps1` traf NIE etwas und meldete 15-minütlich „ohne Befund“), PID-Recycling bei der Elternprüfung, README-Stopp-Prozedur killte jeden `python.exe`, `init()`-Fehler ohne `fatalError$` (Karte log dauerhaft „Berechne…“), `destroy()` ließ das Init-Promise ungelöst hängen, Selbstvergleich zweier WASM-Kerne, doppeltes `startCompare()`, fehlender Telemetrie-Hook, `compareCrashed` überlebte den Stellungswechsel. **Offen: 1 Fund** (EngineSlot-Umbau, siehe „Bewusste Entscheidung“). +4 Regressionstests (gegengeprüft: fallen ohne die Fixes um). **Einschränkung: faktisch Delta auf die jüngste Arbeit** — im .NET-Teil (1283 Dateien) kam kein einziger Fund heraus, für einen Vollscan unplausibel; API separat nachholen. (vorher 2026-08-10 Delta v0.340.0..HEAD: Kalk-Lösungs-Leck behoben v0.356.0, 4 calc-Funde offen; 2026-08-07 stack-weit, 51 Fixes v0.340.0)
- [ ] Übersetzungen prüfen (en/de/hr vollständig + korrekt) — letzter: **2026-08-25** → Registercheck der neuen `nav.support`-Strings gegen den Rest der jeweiligen Datei (Anredeform + Wortart der Aktionslabels); 2 von 25 nachgeschärft (v0.376.1): el war formeller Imperativ statt Verbalsubstantiv, hr unnötig lang für einen Menüeintrag. **Gemessener Stand 2026-08-26: `en` = 2281 Keys, `de` und `hr` = 0 Lücken, die 22 Weltsprachen je 1368 fehlend + 32 veraltet** — also rund **60 % jeder dieser Dateien unübersetzt**, nicht die früher notierten ~174 Keys. Wer die App auf Spanisch/Japanisch/Russisch stellt, sieht mehrheitlich Englisch (Fallback greift, siehe i18n-Weltsprachen unten). (vorher 2026-07-12: en/de/hr je 1867 Keys, hr 2 Lücken → v0.291.34; 2026-06-13: hr 73 Lücken)
- [ ] Security Review — letzter: **2026-08-10** (im Delta-Review mitgelaufen: Auth-/Injection-/Contract-Blickwinkel). Einziger echter Fund = das Kalk-Lösungs-Leck (behoben). Keine CRIT/HIGH offen. (vorher 2026-07-18 6-Wege-Fan-out)
- [ ] Logs prüfen (Kibana: Errors/Warnings/Anomalien) — letzter: **2026-08-09** → ES :9200, 7-Tage-Fenster.
  **rookhub Prod: 0 Errors**; 82 Warns, davon 49× `storage_persist_denied` (Browser-Routine → auf
  Information gestuft, v0.355.4) + 18× connectivity_restored (Standby-Tabs kahalm/mhoehfeld) + 9×
  Stockfish-Timeout/Crash (Client-Geräte, beobachten). Crawler: 46 Verbindungs-Retry-Warns
  (Re-Queue greift), 3 „Crawl failed" (Ids 1324829, „10"). piratechess: 6 Warns. **Fund: beide
  Disk-HIGH-Alerts (02.+09.08.) waren Fehlalarme des log-watchers** — nacktes „XFS" matchte den
  wöchentlichen xfs_scrub_all-Timer → Heuristik gefixt (log-watcher v0.19.1) + Regressions-Wache.
  (vorher 2026-07-12: 5 Errors OG-Render)
- [ ] Frameworks + Abhängigkeiten aktualisieren — letzter: **2026-08-09** → In-Range ueberall:
  Angular 22.0→22.1, .NET-Pakete 10.0.10 (rookhub+crawler+piratechess), Anthropic 12.40,
  AngleSharp 1.7.1, Playwright 1.62 (alle Suiten gruen; Frontend-Lock frisch aufgeloest).
  **Geparkte MAJORS** (je eigene Runde): SkiaSharp 2.88→4.x + Svg.Skia 1→5 (OG-Renderer haengt
  dran — nur mit Bild-Vergleichstest!), Elastic.Serilog.Sinks 8→9 (Sink-Config-Bruch pruefen),
  TypeScript 7 + zone.js 0.16 (erst wenn Angular sie traegt), xunit.runner 3 / Test.Sdk 18 /
  coverlet 10 (Testinfra, gebuendelt). Python: >=-Ranges, erneuern sich beim Image-Build.

## Runbooks

### Service-Worker-Notfall (kaputter SW-Stand flächig bei Usern)
Kontext: Vorfall 2026-07-15 (Endlos-Reload-Schleife nach UNRECOVERABLE_STATE, gefixt v0.309.1/0.309.2).
Eskalationsstufen, wenn trotz Selbstheilung viele Clients einen kaputten SW-/Cache-Stand ausliefern
(Symptome in Kibana: Häufung der ClientLog-Arten `sw_unrecoverable` / `sw_install_failed`):

1. **Einzelner User**: Chrome am Gerät → rookhub.oberschmid.homes → Schloss → Website-Einstellungen →
   „Daten löschen" (TWA/PWA/Browser teilen sich denselben SW), danach neu einloggen. Alternativ
   App force-stoppen/Gerät neu starten — degradierte ngsw-Zustände (EXISTING_CLIENTS_ONLY/SAFE_MODE)
   werden NICHT persistiert, eine frische SW-Instanz startet nominal.
2. **Weicher Kill-Switch (alle User)**: `ngsw.json` serverseitig mit 404 beantworten — der ngsw löscht
   dann selbst alle Caches und deregistriert sich (eingebautes Verhalten). In `src/frontend/nginx.conf`
   temporär einfügen: `location = /ngsw.json { return 404; }` (Container: Datei in
   /etc/nginx/conf.d/default.conf editieren + `nginx -s reload` — überlebt keinen Redeploy, für den
   Notfall genau richtig). Wieder entfernen, sobald ein sauberer Build deployed ist.
3. **Harter Kill-Switch (alle User)**: den bereits im Image liegenden `safety-worker.js` unter der URL
   des Workers ausliefern: `location = /ngsw-worker.js { try_files /safety-worker.js =404; }` —
   deregistriert den Worker + löscht die Caches bei JEDEM Client, der die Seite lädt. Muss so lange
   aktiv bleiben, bis auch seltene Rückkehrer ihn abgeholt haben (Tage, nicht Minuten).

## Geparkt

- [ ] **Anon öffentlicher Kurs: Offline-Cache-Verlustfenster** (Delta-Review 2026-08-09, unverifiziert
  belegt per Sonde, LOW): die Cache-Flush-Strategie schreibt nur nach Seite 1 und am Kettenende
  (`book-puzzle.component.ts`, anon `loadCourseNext`). Bricht die Kette ab (Kurswechsel mitten drin)
  ODER scheitert der End-Flush an der Quota, gehen empfangene Seiten still verloren — schlimmer: ein
  gekappter Cache wird beim Wiederbesuch als vollständig serviert (`courseTotal`=Cache-Größe), es
  wird NIE nachgeladen. Betrifft nur das anonyme Offline-Browsing öffentlicher Kurse. Fix:
  End-Flush-Fehlschlag melden/Cache invalidieren + beim Wiederbesuch Vollständigkeit prüfen.

- [ ] **Kalkulations-Modus: calc→calc-Navigation ist eine latente Falle** (Review 2026-08-09,
  verifiziert): bookId/?chapter=/?pos= werden nur aus `route.snapshot` in ngOnInit gelesen, keine
  paramMap-Subscription. Navigiert man je von einer calc-URL DIREKT zu einer anderen (heute gibt es
  keinen solchen Link — course-card/-detail/public-slug starten alle von anderen Routen), wird die
  Komponente wiederverwendet und zeigt still das ALTE Buch unter neuer URL. Vor dem ersten
  calc→calc-Link (z. B. „verknüpfter Kurs") paramMap abonnieren und das Buch neu laden.
- [ ] **Kalkulations-Modus: bewusst nicht gebaute Anschlüsse** (v0.319.0, 2026-07-28). Der Modus (`features/courses/calc/`, `/api/calculations`) ist inhaltlich fertig; folgende Anschlüsse fehlen absichtlich und sind je nach Bedarf nachzuziehen:
  - **Trainingsziele**: die im Kalkulations-Modus verbrachte Zeit fließt NICHT in den Tracker (es gibt keinen `CourseAttempt`, weil es kein „Versuch/gelöst" gibt). Falls gewünscht: eigene Zeit-Erfassung (Stoppuhr je Stellung → `CourseAttempt` mit `Solved=false` oder eine eigene Kategorie) — dabei entscheiden, wie „Zeit am Baum" gegen bloßes Offenhalten der Seite abgegrenzt wird.
  - **Offline**: keine Service-Worker-/Queue-Anbindung (Bäume brauchen den Server). Analog zu `book-offline.util.ts` möglich: Stellungen + eigene Bäume lokal cachen und über die `OfflineQueueService`-Queue nachreichen.
  - **Kapitel-Route**: es gibt nur `/courses/:bookId/calc` (die Kapitel dienen als Sprungliste); eine `chapter/:chapterIndex/calc`-Variante wie beim Solver existiert nicht.
  - **Teilen/Export**: ein Baum ist rein privat — kein PGN-Export (der Baum ist als Variantenbaum 1:1 PGN-fähig) und keine Freigabe an Freunde/Trainer, wäre aber eine naheliegende Erweiterung.
- [~] **Detailseite für Kurs UND Repertoire mit Kapitel-Verwaltung + Einzel-Kapitel-Reset** (Feature, User-Wunsch 2026-07-24).
  **KURS-TEIL ERLEDIGT v0.320.0 (2026-07-28)**: Detailseite `/courses/:bookId` (`CourseDetailComponent`) mit Metadaten,
  Fortschritt, Kapitelliste, **Kapitel anlegen + Stellungen per Memo-Feld einfügen** (`FenListParser`),
  Kapitel umbenennen/löschen, Einzel-Linie löschen und **Einzel-Kapitel-Reset des eigenen Fortschritts**.
  Backend `Services/CourseAuthoringService.cs`. **Abweichung von der Skizze unten:** der Reset läuft über den
  Kapitel-NAMEN (`POST /api/courses/{bookId}/chapters/reset` mit `{ chapter }`), nicht über den Index — der Index
  verschiebt sich, sobald Kapitel hinzukommen. Analysebäume (Kalkulations-Modus) bleiben beim Reset erhalten.
  **OFFEN bleibt der REPERTOIRE-TEIL** (Kapitel-/Datei-weiser SR-Reset + Detailseite ausbauen).
  Ursprüngliche Skizze: Aktuell gibt es nur den **buchweiten** Reset (`POST /api/courses/{bookId}/reset` rückt `CourseProgress.ResetAt` vor + leert `CoursePuzzleResults`); ein Kapitel einzeln zurückzusetzen geht nur per Hand in der DB (2026-07-24 für kahalm/Buch 16/„7. Promotion" gemacht: DELETE aus `CoursePuzzleResults` + `CourseAttempts` + `CourseInfoViews` für die BookPuzzle-Ids des Kapitels). Gewünscht: eine echte **Detailseite** je Kurs (und analog je Repertoire), die Kapitel/Linien auflistet mit Fortschritt und einem **„Kapitel zurücksetzen"-Knopf pro Kapitel**.
  - Backend: neuer Endpoint `POST /api/courses/{bookId}/chapters/{chapterIndex}/reset` (Kapitel-Index über `ChapterOrder.GetOrderedChapterNamesAsync` auflösen → Kapitelname → BookPuzzle-Ids). Reset = die drei User-Tabellen für diese Ids leeren (`CoursePuzzleResults`, `CourseAttempts`, `CourseInfoViews`). `ResetAt` NICHT anfassen (ist buchweit). Zugriff/Guard wie die übrigen Kurs-Endpoints (kein Zugriff → 404). Analog fürs Repertoire (SR-Linien-Zustände je Kapitel/Datei zurücksetzen).
  - Frontend: Detail-Route `/courses/{bookId}` (bzw. `/repertoires/{id}` ausbauen) mit Kapitelliste (nutzt schon `GET /chapters` inkl. `InfoCount` seit v0.316.2) + Reset-Knopf je Kapitel (Bestätigungsdialog) + optional Gesamt-Reset. Hinweis: der Attempt-Reset entfernt auch die Trainingsziel-Zeit dieses Kapitels (bewusst, wie beim Hand-Reset).

- [x] **Info-/Muster-Linien mit ILLEGALER Diagramm-FEN durchklickbar machen (wie Chessable)** — **ERLEDIGT v0.317.0 (2026-07-24).** Permissiver SAN→UCI-Parser `Services/PermissiveSan.cs` (reine Figuren-Geometrie + Disambiguierung, keine Legalität) via `PgnParser.TryExtractUciMainlinePermissive` (nur für illegale FENs) füllt `Moves` beim Import; Frontend `illegal-board.util.ts` `replayIllegalFen` (dummer Koordinaten-Applier ohne chess.js) + `renderStaticInfo(index)` → bestehende INFO-◀/▶-Navigation blättert durch. `ImportPipeline.CurrentVersion` 16→17 → nach Deploy **„Aktualisieren"/Reprocess** nötig (lokal aus SourcePgn). Über alle 87 illegal-FEN-Linien in Buch 16 verifiziert (81 durchklickbar, 6 bleiben statisch = nur `1. --`-Fließtext). Bleiben IsInfoOnly (kein Quiz). +10 Tests. NICHT deployed (api + frontend).

- [ ] **Benachrichtigungen Phase 2/3: E-Mail + Web-Push** (Feature). Phase 1 (zentrale In-App-Glocke + Trigger) ist live. Offen: dieselben `Notification`-Events zusätzlich per **E-Mail** (reuse `IEmailSender`/SMTP wie beim Passwort-Reset; Opt-out/Frequenz je Nutzer bedenken) und per **Web-Push** (Browser-`NotificationService` + Push-Subscription/VAPID; SW-`push`-Handler) ausspielen. Pro Notification-Typ konfigurierbar (welche Kanäle), Nutzer-Präferenzen im Profil. Siehe `NotificationService.CreateAsync` als Fan-out-Punkt. (2026-07-05 aus dem offene-Punkte-Tracking hierher verlagert.)

- [x] **Themen-Schnellauswahl / Preset-Chips für Puzzle-Themen** — **erledigt für Endless 2026-06-23 (v0.183.0):** kuratierte Preset-Chips über dem Themenfeld (`puzzle-theme-presets.ts` + `applyThemePreset`/`isThemePresetActive`), Klick setzt `config.themes`-Bündel (greift dank ODER out-of-the-box), aktiver Preset hervorgehoben, „schwächste Themen" wird beim Anwenden deaktiviert; i18n en/de/hr `endless.themePreset.*`. 6 Chips: Matt in 1 / Mattjagd 1–2 / Grundtaktik / Kombination & Opfer / Mustermatts / Endspiele. Specs: `puzzle-theme-presets.spec.ts` + 2 Component-Tests. **OFFEN:** Standard-Solver (`puzzle.component`) bietet die Chips noch nicht an (dort separat prüfen, ob die Themenauswahl ODER nutzt). Ursprüngliche Idee:
  Statt Themen einzeln zusammenzusuchen: ein Klick auf einen kuratierten Preset-Chip setzt `config.themes` auf ein passendes Bündel. **Endless filtert Themen bereits ODER** (`themesAny`, seit v0.99.1 `14b80a8`) → die Bündel greifen out-of-the-box („fork pin" = fork ODER pin). Für den **Standard-Solver** (`puzzle.component`) ggf. ebenfalls anbieten — dort prüfen, ob die Themenauswahl auch ODER nutzt, sonst analog umstellen.
  - Umsetzung: Chip-Leiste über/neben der Themenliste (Endless-Config + evtl. `puzzle-settings-dialog`); Klick = `setSelectedThemes(bundle)`. i18n-Labels de/en/hr (`endless.themePreset.*`).
  - Vorgeschlagene Presets (Theme-Keys + Pool-Größen, dev-DB Stand 2026-06-23, PuzzleTags voll backfilled):
    - **Blitz-Matt** = `mateIn1` (698k) — reines Matt in 1, schnellster Speedrun *(Ein-Theme)*
    - **Ein-Zug-Mix** = `oneMove` (700k) — alle Ein-Zug-Lösungen (Matt + Materialgewinn) *(Ein-Theme)*
    - **Mattjagd 1–2** = `mateIn1`,`mateIn2` (≈1,37M)
    - **Grundtaktik** = `fork`,`pin`,`skewer` (≈1,17M)
    - **Material schnappen** = `hangingPiece`,`trappedPiece`,`capturingDefender` (≈290k)
    - **Kombination & Opfer** = `sacrifice`,`deflection`,`attraction`,`clearance`,`interference` (≈950k)
    - **Abzug & Doppelschach** = `discoveredAttack`,`discoveredCheck`,`doubleCheck` (≈414k)
    - **Mustermatts** = `backRankMate`,`smotheredMate`,`arabianMate`,`anastasiaMate`,`bodenMate`,`operaMate`,`hookMate`,`epauletteMate`,`dovetailMate` (≈300k)
    - **Königsangriff** = `kingsideAttack`,`exposedKing`,`attackingF2F7` (≈655k)
    - **Endspiel** = `rookEndgame`,`pawnEndgame`,`queenEndgame`,`knightEndgame`,`bishopEndgame` (≈667k; gezielter als das breite `endgame`=2,67M)
  - Empfohlene Default-Chips: Blitz-Matt, Mattjagd 1–2, Grundtaktik, Kombination & Opfer, Mustermatts, Endspiel.

- [ ] Google Play / TWA fertigstellen (Branches 0.78.1–0.78.5 bereits in master 0.83.0):
  - [ ] Impressum/Betreiberdaten in `src/frontend/app/src/environments/operator.ts` eintragen (Name, Anschrift, UID, Kontakt-E-Mail)
  - [ ] Google-Play-Developer-Account prüfen/anlegen (25 $; neue Accounts: 12 Tester / 14 Tage Closed-Test vor Production)
  - [ ] Upload-Keystore erzeugen (`keytool -genkeypair … -alias rookhub`) + Play App Signing aktivieren
  - [ ] CI-Secrets setzen: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD`
  - [ ] AAB bauen: GH-Action „Build Android TWA" (manuell) oder `bubblewrap build`
  - [ ] Play-Listing: Beschreibung, Icon 512, Feature-Graphic 1024×500, ≥2 Screenshots
  - [ ] Datenschutz-URL in Play Console: `https://rookhub.oberschmid.homes/privacy`
  - [ ] Data-Safety-Formular ausfüllen (gemäß Datenschutzerklärung)

- [~] **RBAC-Ausbau: echtes Rollen-/Berechtigungssystem** (Feature, mittel–groß; Skizze 2026-06-19) — **Phasen 1–4 ERLEDIGT (v0.292.0–0.292.3, 2026-07-12).** 1) Role/UserRole/RolePermission + Migration + Permissions-Konstanten + RoleSeeder (v0.292.0) · 2) HasPermission-Attribut + PermissionPolicyProvider/-Handler, alle `[Authorize(Roles=Admin)]`→Bereichs-Permissions (v0.292.1) · 3) JWT-perm-Claims + Frontend `has()`/`permissionGuard` (v0.292.2) · 4) Admin-Tab „Rollen" (Rollen-CRUD + Permission-Auswahl + Nutzer-Zuweisung), `RolesManage`/`RoleAdminService`/`RolesAdminController` (v0.292.3). **OFFEN (bewusste User-Entscheidung, zurückgestellt):** `AppUser.IsAdmin`-Spalte NICHT entfernt — bleibt Sync-Quelle der admin-Rollenmitgliedschaft (via toggle-admin + RoleSeeder). Ein späteres Ablösen wäre eine eigene Migration (admin-Zuweisung nur noch über die Rollen-UI). Skizze unten zur Referenz.
  Status vor Umsetzung: nur binär `AppUser.IsAdmin` (→ JWT `ClaimTypes.Role="Admin"`, durchgesetzt via `[Authorize(Roles="Admin")]` + `User.IsInRole("Admin")`) plus Gruppen (`UserGroup`/`MenuVisibilityLevel`/`BookGroupAccess`) NUR fürs Content-Scoping (Menü/Kurse). Kein RBAC, keine granularen Permissions, keine Auth-Policies, keine Resource-Level-ACL, `UserApiToken.Scope` fest "extension".
  **Zielmodell:** `User ─n:m─ Role ─n:m─ Permission`. Permissions = feste **Code-Konstanten** (`static class Permissions`, an Features gekoppelt, NICHT als DB-Tabelle frei editierbar). Rollen = DB-Daten, frei bestückbar (`Admin`=Superuser/alle Permissions, `Trainer`, `Moderator`, `Member`). Backend prüft **Permissions, nicht Rollen** → Endpoints bleiben stabil bei Rollen-Umdefinition. Gruppen bleiben separat (was sieht jemand) von Rollen (was darf jemand).
  - Neue Entities + Migration: `Role` (Id, Key, Name, IsSystem), `UserRole` (n:m), `RolePermission` (RoleId, Permission-Key). `AppUser.IsAdmin` bleibt zunächst, wird als `Admin`-Rolle geseedet → später Spalte entfernen.
  - Enforcement: `PermissionRequirement` + `AuthorizationHandler` (Admin-Rolle ODER `permission`-Claim erfüllt) + `[HasPermission("course.import")]`-Attribut über dynamischen `IAuthorizationPolicyProvider` (Policy-Name `perm:<key>`, kein Vorab-Registrieren).
  - JWT (`AuthService.cs:~136`): statt nur `IsAdmin` alle Rollen-Keys + aufgelöste Permissions als Claims schreiben. Trade-off: stale bis Re-Login → bei Rollenänderung Token invalidieren (oder per-Request aus DB mit MemoryCache je UserId auflösen).
  - Frontend: `AuthService.permissions: Set<string>` + `has(p)` (`isAdmin || permissions.has(p)`); generischer `permissionGuard(p)` ersetzt/ergänzt `adminGuard`; `@if (auth.isAdmin)` schrittweise → `@if (auth.has('...'))`.
  - `MenuVisibilityLevel.Admin` später auf eine Permission mappen; Rest (All/Registered/Groups) unverändert. Optional `UserApiToken.Scope` auf mehrere Scopes erweitern.
  - **Phasen:** 1) Entities+Migration+Seed (`Admin`/`Member`, `IsAdmin`→Rolle), Verhalten unverändert · 2) PermissionHandler+`HasPermission`, bestehende `[Authorize(Roles="Admin")]` 1:1 auf Permissions umstellen (Admin erfüllt weiter alles) · 3) JWT + Frontend `has()`/`permissionGuard` · 4) neue Rollen (Trainer/Moderator) + Admin-UI zum Zuweisen + `IsAdmin`-Spalte entfernen.

## Refactoring / Qualität
- [x] **FE-Test-Abdeckung (erledigt 2026-07-12, v0.292.14–0.292.18):** ALLE Quelldateien haben ein Spec — Logik-Units/Guards/Services + auth + alle 42 Feature-Komponenten (Creation-Smoke: AOT-Template-Compile + DI). 1185 Tests grün. Tiefere Verhaltenstests für Brett-/Engine-/Dialog-Komponenten bewusst ausgelassen (Worker+WASM-Flakiness).
- [x] **BE-Test-Abdeckung geprüft (2026-07-12, v0.292.20):** API über Controller-treibt-Service-Konvention gut abgedeckt (1471 Tests); nur ~8 echte Lücken, die hoch/mittel-wertigen geschlossen (ApiTokenAuthenticationHandler/MeController/CatalogController/EndlessController-Guards/NotificationController-Push/TournamentMonitorController/ChessableImportResumeService). ReprocessLauncher (fire-and-forget-Hülle) bewusst ausgelassen.
_Sortiert: sinnvoll/einfach → aufwändig/marginal. Stand der Sichtung: 2026-06-13 (gegen Code geprüft)._

- [x] CI: Docker-Push an grüne Tests koppeln (`needs:`-Gate) — bereits behoben (war nach dem Audit gefixt, aber nicht abgehakt). RookHub: `docker.yml` hat `tests`-Job (`uses: ./.github/workflows/test.yml`, `workflow_call`), `build-api`/`build-frontend` mit `needs: tests` (Commit e26f44a, 0.114.1). Crawler: `test`-Job + `build-crawler: needs: test` (Commit 9b8804c). Verifiziert 2026-06-14: kein ungated Push-Pfad mehr, beide committed + in sync.
- [x] Crawler-Standalone-Compose: Default-Passwörter entfernt (0.114.2) — `docker-compose.yml` nutzt jetzt `${...:?}` (required, fail-closed) für Root-/DB-Passwort inkl. Connection-String; `.env.example` hat Platzhalter statt echter Passwörter
- [x] Crawler: `CancellationToken` durchgezogen (0.114.3) — `SearchPlayersAsync` UND `SearchPlayerTournamentsAsync` (beiden fehlte er) reichen ct jetzt an Fetch/RateLimit/PostAsync/ReadAsStringAsync; PlayerSearchController bindet `RequestAborted`. 2 Tests (cancelled token → wirft)
- [ ] gluetun-Control-Server API-Key: **Code-Seite seit 2026-08-09 BEIDSEITIG fertig** (piratechess sendete den Key schon, Crawler jetzt via optionalem `Gluetun:ApiKey`). OFFEN ist NUR die Deploy-Aktivierung: `HTTP_CONTROL_SERVER_AUTH`/Rollen-Config in den gluetun-Containern + Keys in den .envs (User-Schritt, kein Code). Urspruenglich: gluetun-Control-Server (IP-Rotation) auf API-Key-Auth härten statt `auth = "none"` (HIGH; Aufwand M, nur intern erreichbar) — `gluetun-auth/config.toml` im rookhub-schach-dev-Stack gibt `GET /v1/publicip/ip` + `GET|PUT /v1/vpn/status` unauthentifiziert frei (nur intern via FIREWALL_INPUT_PORTS=8000 im Bridge-Netz). Härtung: `auth = "apikey"` + `apikey = "<secret>"`, Secret in beide `.env` (`rookhub-schach`/`-dev`), dann `X-API-Key`-Header senden in **piratechess-api** (`VpnRotationService`, `Gluetun__ApiKey`-Env) UND **chessresults_crawler** (`CrawlerService.RotateVpnAsync`/`TryGetPublicIpAsync`); beide Images neu bauen + deployen. Betrifft prod + dev. Liegt im Deploy-Stack (piratechess_docker), nicht im Repo. — **Status:** piratechess-Seite (X-API-Key) erledigt (b398963, DEV deployed); OFFEN = chessresults_crawler-Seite + die eigentliche Aktivierung (auth="apikey"+Secret+koordinierter Restart, s. „## Audit-Funde / piratechess_docker").
- [x] Tournament-Detail-Komponente aufgeteilt (0.114.4) — HTTP-Calls → `TournamentDetailService`, reine Favoriten-Logik → `tournament-favorites.util.ts` (+Spec). Komponente 545→513 Z., Verantwortung getrennt. Polling-Logik bewusst in der Komponente belassen (UI-State-nah). Nebenbei kaputten Navbar-Spec repariert (289 FE-Tests grün)
- [x] JWT `ClockSkew` explizit auf 1 min setzen — **erledigt v0.184.1** (`Program.cs`, `TimeSpan.FromMinutes(1)`, war Default 5 min).
- [x] Retry-Interceptor erweitern — **erledigt v0.184.6**: Exponential-Backoff (0,5/1/2 s) + bis zu 3 Versuche statt 1 (Versuchszähler im `X-Retry`-Header), nur GET/HEAD bei 502/503/0. +2 Specs.
- [~] Endless-Puzzle-Komponente: State-Management in dedizierten Service auslagern (`endless-puzzle.component.ts`). **Teilfortschritt 2026-06-23 (v0.181.2):** Fasttrack-Schwellen-State (avg/auto/steps + compute/applyOverrides/reset) in eigene, rein unit-getestete Klasse `endless-fasttrack-state.ts` ausgelagert (5 Specs); Komponente delegiert via Getter/Setter → Template unverändert. Hinweis: die Komponente delegiert bereits viel (`EndlessStorageService` Persistenz, `endless-prefetch.util` Ketten-/Fasttrack-Mathematik, `board-theme.util`, `BasePuzzleSolver`, `LongSolveService`). **OFFEN/bewusst NICHT angefasst:** der eigentliche Run-State (lives/level/solved/maxRating/Session-Aggregation) hängt über zig direkte `[(ngModel)]`/Interpolationen am 32-KB-Template → ein Umzug in einen Store bräuchte breite Template-Änderungen + interaktive Endless-Verifikation (mittleres Regressionsrisiko); opportunistisch in weiteren Schritten.
- [x] `takeUntilDestroyed` durchgängig einsetzen — **2026-06-23 geprüft (v0.181.1): kein echter Leak im Code.** Sweep über alle `.subscribe(`-Stellen ergab: die ~228 Aufrufe sind ganz überwiegend self-completing HTTP-Calls (kein Schutz nötig). JEDER langlebige Stream (interval/timer, route.paramMap, Service-Subjects) ist bereits abgesichert — `dashboard`/`navbar`/`admin`(queryParam) via `takeUntilDestroyed`, `chessable`/`admin`(Download-Polling)/`book-puzzle`(dailySub) via manuelles `unsubscribe()` (dort funktional nötig, nicht nur Destroy → bewusst NICHT auf takeUntilDestroyed umgestellt). `fromEvent` gibt es nicht (alle Tastatur-Listener via `@HostListener`, auto-cleaned). Einzige nackte langlebige Subs saßen in `app.component` (Root) auf `router.events` + `swUpdate.versionUpdates`/`unrecoverable` → auf `takeUntilDestroyed` umgestellt (v0.181.1; Root leakt zwar praktisch nie, aber damit ist die letzte ungeschützte Stelle weg). Restliche manuelle `ngOnDestroy`-Cleanups sind korrekt und bleiben.
- [ ] Puzzle-Board auf den gemeinsamen `PromotionPickerComponent` (`shared/promotion-picker/`, seit 0.152.0 vom Analysebrett genutzt) migrieren — `puzzle-board.component.ts` hat noch seine eigene Inline-Umwandlungs-Overlay (Normal- + Viz-Pfad) mit identischer Guard-/Positionslogik. Zusammenführen vermeidet Doppelpflege; Risiko = Viz-Pfad (eigene Farb-/FEN-Erkennung) + frisch gefixter Ghost-Tap-Guard, daher bewusst getrennt belassen bis zum nächsten Anfassen

### Bewusste Entscheidung — kein Bug (nur falls gewünscht umbauen)
- [x] Crawler-`API_KEY` fail-open → **erledigt 2026-06-25 (Crawler `4ca4feb`, rookhub v0.184.36):** `ApiKeyMiddleware` ist bei leerem Key in **Production fail-closed** (503), Dev-Fallback (offen) + Liveness/Swagger bleiben. +2 Tests.
- [ ] Token-Refresh im Frontend — `auth.interceptor.ts` macht bei 401 harten `logout()` (fail-closed, sicher). Refresh-Flow wäre reines Komfort-Feature bei aktivem Polling (Monitor 30 s / Crawl-Job 2 s)
- [ ] **Vergleichsmodus als `EngineSlot` statt zweier paralleler Feldsätze** (Codereview 2026-08-25, einziger nicht umgesetzter Fund). `analysis.component.ts` führt sieben Compare-Felder (`compareLines`/`compareDepth`/`compareNps`/`compareFallback`/`compareCrashed`/`compareEngine`/`compareEngineId`) plus drei Subscriptions parallel zu ihren Haupt-Engine-Zwillingen, abzugleichen über sechs Aufrufstellen (`refresh`, `restartSearches`, `onDepthChange`, `onLinesChange`, `applyEngineSelection`, `ngOnInit`). **Vier der behobenen Bugs in v0.376.2 waren genau diese Drift** (fehlendes `compareCrashed`-Reset, fehlender Telemetrie-Hook, doppelter `startCompare`, Selbstvergleich). Ein Slot-Objekt `{ engine, id, lines, depth, nps, fallback, crashed, subs }` in einem Array aus zwei Slots machte daraus je EINEN Codepfad. **Kein Bug, sondern Umbau am Template-Vertrag** (alle `compare*`-Bindings im Template wandern mit) — bewusst nicht zusammen mit den Fixes gemacht, damit die Fehlerbehebung nicht in einem Refactoring untergeht. Regressionsnetz dafür liegt bereits: `describe('AnalysisComponent compare mode invariants')`.

### Bei der Sichtung 2026-06-13 als bereits erledigt verifiziert (entfernt)
- AdminSeeder setzt PW nur beim ersten Start (`AdminSeeder.cs:35`, `AnyAsync(...) return`)
- BCrypt Work Factor ist bereits 12 (`AuthService.cs:21`, auch AdminSeeder)
- Crawler `HtmlParserService` ist durch Tests abgedeckt (`HtmlParserServiceTests.cs`, ~448 Z.)
- Crawler `RoundDetectionService` cacht bereits 60 s (`:50`)

## Security-Review 2026-07-18 (6-Wege-Fan-out über alle Repos)
Read-only-Review je Repo (rookhub-API/-Frontend, crawler, piratechess, schach-bot+log-watcher, repcheck). **Keine CRIT/HIGH offen.** Sortiert nach Priorität; erledigte Punkte abhaken.

### MED — angehen
- [x] **Kontolöschung lässt Secrets/PII + öffentliche Links stehen (GDPR)** — ERLEDIGT v0.315.0: `DeleteAccountAsync` (`Services/ProfileService.cs`) entfernt jetzt zusätzlich `ChessableCredentials`, `PasswordResetTokens`, `SavedGames` (/g/), `SharedLines` (/l/), `RememberedPositions` und leert `ManualActivities.Note`. Solve-Statistik + `EndlessProgresses` (Highscore/Stats) bleiben bewusst anonym erhalten. +BE-Test (Secrets/Share-Inhalte weg, Notiz null).
- [x] **Offline-Schreib-Queue nicht user-scoped → Cross-User-Fehlbuchung** — ERLEDIGT v0.315.0: `PendingRequest.userId` (Stempel aus `rookhub_user` beim Enqueue); `flush`/`sendNext` sendet nur eigene (oder anonyme, userId null) Einträge, fremde bleiben liegen bis IHR User wieder eingeloggt ist. Queue wird bei logout NICHT geleert (user-gestempelt → sicher; versehentliches Logout verliert keine offenen Lösungen). +6 FE-Tests.
- [x] **Offline-Content-Caches nicht user-scoped, nicht bei Logout geleert** — ERLEDIGT v0.315.0: `AuthService.logout()` ruft `OfflineService.clearAll()` (Repertoire-PGNs, Bücher, Kursliste, Tagespuzzle, idmap, Pools). Trade-off: Re-Download nach explizitem Logout. Rest (`rookhub_course_local_solved_*` = anonyme Kurs-Solve-Ids, `rookhub_daily_elapsed`, Endless-History = server-synced) bewusst belassen (geringe Sensitivität). +FE-Test.
- [~] **piratechess: gecachter Kurs ohne Bearer-Ownership-Check** — **ENTSCHIEDEN 2026-08-09: bewusst NICHT umgesetzt**, siehe Begründung beim Zwilling im Abschnitt „2. Pass (vertieft, Chessable/Import/Auth)“ — die uid-Bindung des Kurs-Caches kollidiert mit dem per Tests fixierten Admin-Feature „Kurs im Namen eines Users laden“. Stand hier faelschlich weiter unter „angehen“. Urspruenglicher Befund: (Defense-in-Depth) — `ChessableDirectController` Cache-Hit-Pfade (`:134-214,289-313,325`) liefern vollen Kurs-PGN + `CachedBids` listet alle Ids ohne Bearer. Nur durch Service-Key+Netz-Isolation+rookhub-Check geschützt; leakt der Service-Key, leaken alle gecachten Kurse. Bewusster Trade-off — optional Ownership auch bei Cache-Hit erzwingen.

### LOW — Härtung
- [x] **Crawler SSRF via Redirect** — ERLEDIGT (Crawler `3a6798c`, rookhub v0.315.1): `AllowAutoRedirect=false` (Program.cs) + `SendFollowingRedirectsAsync` folgt Redirects manuell und prüft jeden Hop (https + chess-results.com) VOR dem Absenden (Cap 10, relative Location aufgelöst). GET- UND POST-Pfade (inkl. POST-Antwort der Spielersuche) umgestellt; post-hoc-Check entfällt. +6 Redirect-Tests (207 grün).

### INFO / accepted-by-design (bewusst)
- gluetun-Calls (crawler+piratechess) senden kein X-API-Key (Port unexponiert; Aktivierung liegt im Deploy-Stack — piratechess-Code sendet den Key bereits). `/api/health/ip` (crawler) jetzt key-gated ✓. Body→ES-Logging entfernt ✓. curl-Arg-Injection (piratechess) via ArgumentList+`IsValidBid` geschlossen ✓. RBAC: `UsersManage`/`RolesManage`/`ChessableAdmin` sind faktisch admin-nah (Machtkonzentration). `EncryptionService` behält Legacy-AES-CBC-Decrypt (neue Writes AES-GCM). RepCheck-host_permissions `https://*/*` breit, aber Proxy origin-locked + `sender.id`-geprüft + kein `externally_connectable` → nicht von Seiten aus nutzbar. Frontend-CSP solide (`script-src 'self' 'wasm-unsafe-eval'`); optional `frame-ancestors 'none'`/`base-uri 'self'`/`form-action 'self'` ergänzen. `SanitizeLikeInput` escaped kein `\` (nur Korrektheit, kein SQLi). `RememberedPositions.SourceUrl` ohne Scheme-Check gespeichert (kein SSRF; nur relevant, falls je als href gerendert).

## UI-Review 2026-07-26 (Überladung) — ABGESCHLOSSEN (Welle 1 v0.317.3, Welle 2 v0.318.0+v0.334.0, Welle 3 v0.335.0)
Praktisches Oberflächen-Review (Seiten headless gerendert **und vermessen**, Prod anonym + Dev eingeloggt,
1440×900 und 390×844; Screenshots waren einmalig im Session-Scratchpad). Befund: „überladen" trifft nicht
überall zu — `/analysis` ist gut sortiert, die Kurs-Karte nutzt bereits Primär-Aktion + Overflow-Menü. Die
Dichte konzentriert sich auf **Solver, Endless-Start und Trainingsziele** und folgt drei Mustern:
(1) dieselbe Aussage mehrfach, (2) alles gleichzeitig statt gestuft, (3) kein Seiten-Container.

**Welle 1 = erledigt in v0.317.3** (Handy-Overflow ausgeloggt, 3 rohe i18n-Keys, Karten-Abstände +
abgeschnittenes Label auf den Trainingszielen, Endless-Kurvenmarken, doppelte Zug-Anzeige, Null-Statistik,
leere Karte auf /analysis).

**Welle 2 — ERLEDIGT** (Reiter/Endless/Kapitelzeile v0.318.0; Aktionsleiste + Hilfetexte v0.334.0):
- [x] **Eine Aktionsleiste statt vier Karten im Solver** → **erledigt v0.334.0**: neue
  `PuzzleActionBarComponent` (Rating-Pille + Tags-Toggle + kontextuelle Knöpfe + Teilen + ⋮-Menü mit
  Letztes-ansehen/-lieben, Endlos, Einstellungen) ersetzt in allen 3 Modi Rating-Card, Last-Actions,
  Endlos-Knopf und Bottom-Actions; `puzzle-rating-card` gelöscht, Zahnrad aus der Status-Card entfernt.
  Tipp/Bewertung/Aufgeben bleiben bewusst an Ort (Brett-Leiste bzw. Your-Turn-Zeile im Status) — sie
  sind zustandsgebunden, die statischen Karten drumherum waren die eigentliche Überladung.
- [x] **Endless-Start stufen** → **erledigt v0.318.0**: Engine-Tiefe, beide Schwellen und die
  Kurvenvorschau liegen unter einem zugeklappten „Feineinstellungen"; sichtbar bleiben Start, Start-Rating,
  Themen-Vorlagen und Themen. Zugeklappt 844 px statt 1233 px. (Offen geblieben: die Stockfish-Tiefe ganz in
  die Puzzle-Einstellungen zu verschieben — sie gilt geräteweit, nicht pro Lauf.)
- [x] **Trainingsziele in Tabs** → **erledigt v0.318.0**: vier Reiter `Ziele | Verlauf | Erfassen |
  Chessable` (Vorlagen zu „Erfassen" gezogen, Tracker/Tageshistory nach „Verlauf" — sonst wäre Reiter 1
  wieder 1000 px lang geworden). Aktiver Reiter in der URL (`?tab=history`), Inhalt lazy je Reiter.
  Gemessen (Handy, leeres Konto): 1569 → 844 px, 5 → 2 Karten, 15 → 8 interaktive Elemente.
- [x] **Kurs-Kapitelzeile entdichten** → **erledigt v0.318.0**: ein Play-Knopf je Kapitel (startet im
  zuletzt genutzten Kursmodus aus `CourseProgress.LastMode`), Alternativen (sequenziell/zufällig/durchsehen)
  im ⋮-Menü der Zeile. **Noch nicht mit echten Daten gegengesehen** — das Testkonto auf Dev hat keine
  freigegebenen Kurse; nur Unit-Tests + Prod-Build.
- [x] **Hilfetexte → Tooltip/Hilfe-Icon** → **erledigt v0.334.0**: neue `HelpHintComponent`
  (shared/help-hint, ?-Icon + Klick-Tooltip, globale `.hh-tooltip`-Klasse); Trainingsziele-Ziel-Karte
  (dailyHint+playHint) und Offline-Aktivitäten-Intro umgestellt. Endless-Hilfe war schon ein Overlay,
  Rest der App hatte nach v0.318.0 keine Absatz-Stapel mehr (per Regex über alle Templates geprüft).
  Bei künftigen Karten: HelpHintComponent statt `<p class="muted">`-Absätzen verwenden.

**Welle 3 — ERLEDIGT (v0.335.0):**
- [x] **Globaler Seiten-Container** → **erledigt v0.335.0**: War organisch schon fast da (alle Seiten
  haben inzwischen eigene `max-width`-Container, das Brett skaliert seit den Vollbild-Arbeiten via
  `min(60vw, 820px, 100vh-180px)`). Rest vereinheitlicht: globale CSS-Variable `--page-max-width`
  (1240px, `styles.scss`); die drei zuvor randlosen Brett-Seiten (Standard/Buch/Endless, vorher
  `min(1400px, 96vw)` ≈ randlos bei 1440px) binden daran. Admin bleibt bewusst 1400px (Tabellen).
- [x] **Dashboard-Default kuratieren** → **erledigt v0.335.0**: `DEFAULT_VISIBLE` auf den
  Trainings-Kern reduziert (puzzles, weekly, pinnedCourses, courses, trainingGoals — 4 Kacheln +
  bedingte Pinned-Kachel); repertoires + leaderboards jetzt Standard-aus. Gespeicherte
  Personalisierungen bleiben unangetastet (Load-Pfad respektiert `rookhub_dashboard_layout_v2`).
- [x] **Designregel festschreiben** → **erledigt v0.335.0**: „UI-Dichte-Regel" in `CLAUDE.md`
  (Wichtige Konventionen, vor „Puzzle-Modi konsistent halten"): 1 primäre Aktion, ≤3 sekundäre,
  Rest ⋮; HelpHint statt Absatz-Stapeln; Container an `--page-max-width`; neue Dashboard-Kacheln
  nicht in `DEFAULT_VISIBLE`.

## Code-Review 2026-08-07 (Stack-weit, alle 6 Repos — GEFIXT in v0.340.0)

Multi-Agent-Review über rookhub (api+frontend), chessresults_crawler, piratechess_docker,
schach-bot, log-watcher, repcheck + Infra/CI. 71 Roh-Findings, 20 adversarisch widerlegt, 51 übrig
— **alle verifizierten Punkte sind behoben und gepusht** (rookhub v0.340.0, repcheck v1.38.1, die
übrigen Repos ohne eigenes Versionsschema). Vollständiger Bericht mit Fehlerszenarien, Belegen und
den widerlegten Punkten: `rookhubstack/CODE_REVIEW_2026-08-07.md`.

**Die zwei schwersten Funde waren stille Korrektheitsfehler, beide an Prod-Daten belegt:**
(1) `[FEN]`-Header wurde von beiden Server-PGN-Walkern ignoriert → ~7.700 Repertoire-Linien (ganze
Chessable-Kurse) fielen aus dem Positions-Index; (2) der Linien-Hash driftete zwischen Server (rohe
PGN-Tokens) und Frontend (chess.js-kanonisch) → „auf Chessable trainiert" rückte bei
Umwandlungs-Linien den SR-Fortschritt nicht vor. Dazu als schwerster Security-Fund: das
`rkh_`-Extension-Token war außerhalb des ExtensionControllers ein Voll-Account-Token
(E-Mail ändern → „Passwort vergessen" → Kontoübernahme); der Scope wird jetzt zentral erzwungen.

**Lehre fürs nächste Mal:** die Gegenleser haben in **vier von sechs** Bereichen Fehler gefunden,
die der jeweilige Fix selbst eingeführt hatte (Watchdog setzte laufende Browser-Importe zurück;
piratechess-Force-Refresh löschte den einzigen Linien-Speicher VOR dem Abruf; log-watcher bildete
die Alarm-Signatur nach dem PII-Schwärzen → zweiter Angreifer im Cooldown des ersten;
Compose-Beispiel hätte mit leerem `ENCRYPTION_KEY` still mit `SHA256("")` „verschlüsselt").
Ein Fix-Durchgang ohne unabhängiges Gegenlesen wäre netto schlechter gewesen als kein Durchgang.

### Bewusst geparkt (zu großer Umbau für einen Sammel-Durchgang)
- [ ] **`BookPuzzleComponent` (~1600 Zeilen) in Modus-Strategien zerlegen.** Bedient Kurs, Buch,
  Daily, Wochenpost und geteiltes Einzelpuzzle in einer Klasse; Modus-Fallunterscheidungen ziehen
  sich durch Laden, Speichern, Navigation, Review und Teilen. Laut Changelog wiederkehrende
  Regressionsquelle. Schnitt: je Modus ein kleiner Service mit `load/next/report/share`, Komponente
  auf Darstellung + Delegation reduzieren. (Verifiziert als PLAUSIBLE/low — Aufwand ≫ Einzelnutzen,
  lohnt nur zusammen mit dem geparkten Punkt „Cross-Solver-Duplikation".)
- [ ] **Refresh-Token-Rotation statt langlebiger JWTs.** `rememberMe` gibt 30 Tage (Konfig bis 90),
  es gibt keinen serverseitigen Widerruf außer der SecurityStamp-Prüfung bei Passwortänderung. Ein
  abgegriffenes Token bleibt sehr lange gültig. Ziel: kurzlebiger Access-Token (1–24 h) + rotierender
  Refresh-Token, mindestens aber eine Session-Liste zum Widerrufen.
- [ ] **Key-Rotation für die verschlüsselten Chessable-Credentials.** `EncryptionService` leitet aus
  einem einzigen `Encryption:Key` ab, kennt keine Key-Version, und der Alt-Pfad `DecryptLegacyCbc`
  bleibt dauerhaft **AES-CBC ohne MAC** entschlüsselbar. Eine Rotation würde heute alle Credentials
  still entwerten (`TryDecrypt` → null). Nötig: Key-Version am Datensatz + Re-Encrypt-Migration,
  danach den Legacy-Pfad entfernen. (Seit v0.340.0 bricht wenigstens ein LEERER Key den Start ab,
  statt mit `SHA256("")` schein-zu-verschlüsseln.)
- [ ] **Zeitzonen statt harter UTC-Tagesgrenzen.** `TrainingGoalService`, `BookPuzzleService`,
  `LeaderboardService`, `PlayTimeService` rechnen durchgängig mit `DateTime.UtcNow.Date`; im
  gesamten API-Code null Treffer für `TimeZoneInfo`. Bei österreichischer Nutzerbasis rollen Streak,
  Tagesziel und Tagespuzzle im Sommer um 02:00 Ortszeit — zwischen Mitternacht und zwei zählt alles
  auf den Vortag. Immerhin konsistent falsch (`DateTime.Now` kommt nirgends vor). Braucht eine
  Nutzer-Zeitzone im Profil + eine zentrale „Tagesgrenze"-Hilfe, sonst driften die Bereiche
  auseinander.
- [ ] **Integrationstests gegen echtes MariaDB + Migrations-Smoke-Test in der CI.** Alle 1753
  Backend-Tests laufen gegen EF InMemory (LINQ-to-Objects); Übersetzungsfehler, Collation-/
  Case-Sensitivity, Unique-Index-Verhalten bei NULL und fehlerhafte Migrationen fallen erst gegen
  echtes MariaDB auf — die eigene Doku warnt davor, ohne Gegenmaßnahme. Nötig: `db-integration`-Stage
  mit Testcontainers-MariaDB (leere DB → alle Migrationen → Snapshot-Vergleich) + eine kleine Suite
  für die riskanten Queries. Vom Test-Agenten bewusst übersprungen, weil es Workflow-Dateien und ein
  Docker-fähiges CI braucht.

### Nachprüfung des Fix-Durchgangs (2026-08-07, eigener Verifikations-Lauf)
- [ ] **Chessable-Kursinhalt kann nicht aktualisiert werden — Entscheidung nötig.** piratechess hat
  jetzt ein `ForceRefresh`-Flag (umgeht den Rohdaten-Cache, ersetzt erst nach Erfolg per Upsert),
  aber **rookhub setzt es nirgends**. Damit bedient jeder Kurs-Abruf weiter den Stand des
  Erst-Imports: ein vom Autor aktualisierter Chessable-Kurs kommt nie an. Es blind für jeden
  „Aktualisieren"-Lauf zu setzen wäre falsch — der Reprocess will bewusst die ALTEN Rohdaten mit der
  NEUEN Pipeline neu parsen (kein Chessable-Traffic), und ein pauschales Erzwingen zöge alle Kurse
  erneut über die VPN-IPs (dokumentiertes Ban-Risiko). Zu entscheiden: eine eigene, seltene Aktion
  („Von Chessable neu laden", pro Kurs, gedrosselt) statt einer Kopplung an den Reprocess.

### Kleinere Restpunkte aus dem Gegenlesen (nicht blockierend)
- [ ] **`PuzzleService.GetRandomAsync` (themesAny): Verteilung geändert.** Der neue Random-Seek zieht
  gleichverteilt über *Rating*, nicht über *Puzzles* — bei offener Rating-Spanne ist das Fenster der
  gesamte Pool, dünn besetzte Ränder werden übergewichtet und wiederholen sich. Praktisch entschärft,
  weil das Frontend immer `r.min/r.max` schickt; betroffen ist nur der rohe API-Aufruf. Entweder den
  Trade-off im Kommentar benennen oder das offene Fenster begrenzen.
- [ ] **Append-Sperre ist prozesslokal.** `AppendLiveAsync` serialisiert je (User, bid) über ein
  In-Memory-Semaphor; bei mehreren API-Instanzen bräuchte es ein DB-seitiges Schloss bzw. einen
  Unique-Index. Zusätzlich laufen `ImportPgnDirectAsync`/`ImportAsRepertoireAsync` NICHT unter dieser
  Sperre — Mixed-Path bleibt ein (kleines) Duplikat-Risiko für das `chessable-{bid}`-Repertoire.
- [ ] **log-watcher: serverseitige Filter-Aggregation für `warn_spike_ignore`** bewusst übersprungen
  (die Rausch-Muster werden weiterhin client-seitig aus den Top-Termen gerechnet → stille Degradation
  möglich, wenn ein Muster aus den Top-N fällt).

## Code-Review 2026-07-26 (rookhub Frontend, `src/frontend`)
Review des Angular-Frontends (207 TS-Dateien / ~35.400 Zeilen ohne Specs): Lifecycle/RxJS-Teardown, Robustheit gegen Server-/Storage-Daten, Security/XSS, Performance + Bundle, Offline-/localStorage-Schicht, i18n/A11y, Struktur. **Stand: nur dokumentiert, nichts davon gefixt** (Basis v0.317.2, 1285 FE-Tests grün, Prod-Build sauber).

**Verifiziert = kein Handlungsbedarf:** kein XSS-Vektor (`[innerHTML]` nur auf eigenen i18n-Strings + chess.js-generiertem SAN; einziger `bypassSecurityTrust` = eingebettetes Discord-SVG-Literal); Teardown durchweg sauber (Timer/Listener/Subs in `ngOnDestroy`, `takeUntilDestroyed` bei den Poll-Timern, Board-Pointer-Listener + `ResizeObserver` + `ground.destroy()`); JWT nur an `/api`, 401→Logout, Retry-Interceptor nur idempotent; Routen vollständig geguarded (`:slug`-Catch-all bewusst als vorletzte Route); i18n ohne harte Strings (Scan über alle Templates: nur Eigennamen); Typsicherheit gut (9× `as any` in 35k Zeilen); SR-Offline-Spiegel (`repertoire-sr.util`) deckt sich exakt mit Backend `DefaultLevels`/`ScheduleLevel`/`HoursOf`.


## Code-Review 2026-07-25 (rookhub Backend, `src/api` komplett)
Umfangreiches Review des gesamten Backends (238 Dateien / ~30.600 Zeilen ohne Migrations): Auth/Authorization, offene Endpoints, EF-/Datenschicht, Hintergrund-Services, Chessable-Import-Pipeline, Performance, Struktur. Gesamtbild solide (kein Raw-SQL, LIKE-Sanitizing, konsequentes Race-Handling an Unique-Indizes, 1552 Tests grün).

**Gefixt v0.317.1** (Funde 1–6, api-Image, kein Reprocess):
- [x] **Buch-/Kursinhalte anonym vollständig auslesbar** — `?bookId=` überschrieb bei `/api/book-puzzles/random` den Pool-Filter ohne jede Prüfung, `/books` listete alle Bücher, `{id}/next` lief ein Buch von jedem Einstieg durch → Gruppen-/Freigabe-Gating war Kosmetik. Neue gemeinsame Regel `Services/BookAccess.cs` (anonym nur `IsPublic`/Pool-Flags; eingeloggt + eigene/geteilte/Gruppe; Einzel-Puzzle per Id bleibt bewusst offen).
- [x] **`users.manage` = de-facto Admin** — Impersonation eines Admin-Kontos bzw. Admin-Toggle ohne echte Admin-Rolle → beides jetzt admin-only (403).
- [x] **`UseRateLimiter` vor `UseAuthentication`** → `user-flag`-Policy fiel immer auf IP zurück (Pro-User-Drossel griff nie).
- [x] **Unbegrenzte Rekursion** im Catch-Zweig von `ShareCourseAsync`/`RepertoireService.ShareAsync` (jede `DbUpdateException` → Selbstaufruf; nicht-Duplikat = StackOverflow/Prozess-Abbruch) → auf `IsUniqueViolation` eingegrenzt + genau 1 Retry.
- [x] **Ingest-Puffer-DoS** — 128 MB/Session, aber unbegrenzt viele client-benannte Sessions → Deckel pro User (3) + prozessweit (512 MB) + SessionId-Validierung.
- [x] **500 bei `POST /api/puzzles/random-batch`** mit verdrehtem/extremem Rating-Fenster (`Random.Shared.Next(min, max+1)`) → DTO-`[Range]` + Service-Guard.

**Offen (bewertet, nicht gefixt):**
- [ ] **`ComputeStatsAsync` im Hot-Path (MITTEL, Performance)**: lädt bei JEDEM `/next` und `/results` alle Puzzles des Buchs + alle `CourseAttempts` des Users in den Speicher; `GetNextAsync` (sequenziell) zusätzlich alle Pool-Schlüssel. Wächst mit Buchgröße × Historie.
- [ ] **Kleinere Performance-Fallen (LOW)**: `GamesController.List` zieht über `CountPlies(g.Pgn)` in der Projektion die LONGTEXT-PGNs aller (bis 500) Partien, obwohl das DTO „ohne PGN" ist → Zähler persistieren; `GetImportedOidsAsync` lädt alle Repertoire-PGNs + Regex im Request-Pfad eines Extension-Polls; `AppendLiveAsync` dedupliziert per `existing.Contains(moves)` über das ganze PGN (quadratisch) und setzt `FileSize` in Zeichen statt Bytes; `BookPuzzleService.ImportAsync` lädt alle `LineId`s der DB in ein HashSet.
- [ ] **Struktur (LOW)**: God-Services (`CourseService` 1059, `ChessableImportService` 1011, `TrainingGoalService` 892 Zeilen); optionale Konstruktor-Parameter „für Tests", die sich Abhängigkeiten selbst bauen (`new NotificationService(db)` in `CourseService`/`RepertoireService`) statt DI/Fakes; Import-Zustände als Magic Strings (`"running"`/`"queued"`/`"claimed"`/… über 5 Services + 2 Controller) statt Enum + Value-Converter; `CourseService.CanAccessAsync` macht 4–5 Round-Trips pro Endpoint-Aufruf (als eine `Any`-Query mit ODER-Zweigen formulierbar — Vorlage: `BookAccess.ReadableBy`).
- [ ] **Optional**: falls der `/kurs`-Katalog des Bots durch Fund 1 zu klein wird — entweder die betroffenen Bücher im Admin-Bereich auf einen Pool/`öffentlich` setzen, oder dem Bot einen authentifizierten Pfad geben (er hat bereits `SchachBot:StatsSecret`).

## Code-Review 2026-06-30 (rookhub API, 5-Dimensionen-Fan-out)
Umfangreiches API-Review (Auth/Security, EF/Datenschicht, Controller, Geschäftslogik, Hintergrund-Services). **Gefixt + gepusht (v0.202.0–0.203.5):**
- [x] Rate-Limiter `auth`/`anonymous-puzzle`/`anonymous-tournament` pro-IP partitioniert (war je ein globaler Bucket → Login-DoS); Default-CORS ohne `AllowCredentials`; JWT-„Bleib eingeloggt" 365→90 T (v0.202.2)
- [x] Wochenpost-Übersicht N+1 + PGN-Parse je Post → `WeeklyPost.PuzzleCount` gecacht (v0.203.1)
- [x] Daily-Leaderboard Gold-Tie → Competition-Ranking; AdminMessages „letzte Nachricht" via `Max(Id)` statt `CreatedAt` (v0.203.2)
- [x] AutoSubscription: frischer Scope/DbContext pro User + Freundes-/Profil-Set 1× je User (v0.203.3)
- [x] SavedGame-Dedup hart per Unique-Index `(UserId,Source,ExternalId)` (v0.203.4)
- [x] Challenge-Batch `[MaxLength(50)]`; BookPuzzle-Routen `{id:int}`; RepertoireTraining-Review Race-Catch; TournamentMonitor `TryGetInt32` (v0.203.5)

**Bewusst NICHT umgebaut (gegen reale Prod-Größe geprüft 2026-06-30 — 49 User):** Die „unbounded read"-Funde sind bei dieser Größenordnung Nicht-Probleme und ein SQL-Umbau brächte echtes Pomelo-Übersetzungsrisiko ohne Nutzen:
- [ ] Daily Hall-of-Fame lädt „alle" Daily-Attempts in den Speicher → real **50 Zeilen** (14 Dailies). Revisit erst bei ~100× Nutzerwachstum.
- [ ] TrainingGoal `daily-series` lädt ganze User-Historie → real max **1355 Zeilen** (ein User). Unkritisch.
- [ ] Leaderboard `alltime` lädt distinct (User,Puzzle)-Paare → real **~1900 Zeilen**; zudem **bewusst provider-sicher** designt (Kommentar: vermeidet `COUNT(DISTINCT)` im GroupBy). Nicht anfassen.
- [ ] ChessableImport ohne `RowVersion`-Concurrency-Token: die realen Stall-Incidents sind bereits anderweitig adressiert (atomarer `ExecuteUpdate`-Claim v0.184.25 + Watchdog v0.192.0 + Download-Lane-Gate v0.195.4). RowVersion-Retrofit auf dem Import-Hot-Path = hohes Regressionsrisiko bei marginalem Zusatznutzen → zurückgestellt.
- [ ] PuzzleChallenge-Pending-Dedup als DB-Constraint: MySQL kann keine partiellen Indizes; ein voller Unique-Index würde legitime Re-Challenges nach Auflösung blocken. Race nur bei Doppelklick → geringer Nutzen, ausgelassen.
- [x] Anonymer Buch-Versuch ohne Unique-Constraint → **erledigt v0.203.8** (Unique `(BookPuzzleId, AnonymousSessionId)` + Race-Catch).

### 2. Pass (vertieft, Chessable/Import/Auth) 2026-06-30
- [x] **Cached-Content-Bypass, 2. Tür** (Reprocess-Re-Fetch ohne Eigentums-Check) → **gefixt v0.203.9** (`EnqueueReimportAsync` → `OwnerHasCourseAsync`).
- [~] **piratechess Defense-in-Depth (MEDIUM)** — **2026-08-09 BEWUSST nicht umgesetzt:** die uid-Bindung des Kurs-Caches kollidiert mit dem per Tests fixierten Admin-Feature „Kurs im Namen eines Users laden" (Bearer-Quelle ≠ Besitzer). Bleibt Defense-in-Depth-Wunsch hinter Service-Key + Netz-Isolation. Urspruenglich:: `POST direct/course` liefert gecachte Kurse OHNE Bearer-Eigentumsprüfung — heute nur durch Service-Key + Netz-Isolation + den rookhub-Check abgesichert. Geleakter Service-Key / Rogue-Container im `chessable-bridge`-Netz könnte jeden gecachten Kurs lesen + via `GET courses/cached` alle bids enumerieren. Echte Prüfung kostet einen Chessable-Call pro Cache-Treffer (hebt Cache-Vorteil auf) → bewusst zurückgestellt; Kontrolle = langer/rotierter Service-Key + gesperrte Bridge-Netz-Mitgliedschaft. (Cache-POISONING NICHT möglich — `SetAsync` nur mit selbst-gefetchten, `IsComplete`-validierten Daten.)

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

**Round 2 zusätzlich gefixt (v0.155.14–0.155.18):** endless-history View-Model statt JSON.parse pro CD (0.155.14); RAF-/Timer-Cleanup chess-board+api-tokens (0.155.15); friends nested-subscribe via switchMap entflochten (0.155.16); Typing `Repertoire.kind`→Enum + endless-storage `<{id}>` (0.155.17); Custom-Overlays Escape/Focus-Trap/role=dialog + version-link tastaturbedienbar (0.155.18).

**Verifiziert = kein Handlungsbedarf:** Singleton-Engine-Lifecycle (`AnalysisEngineService` `providedIn:'root'` + `AnalysisComponent.ngOnDestroy → engine.destroy()`) ist **korrekt by design**: `analyze()` ruft `init()`, das den Worker nach `destroy()` neu erzeugt; `app.component` setzt `reportEngineEvent` auf der Singleton-Instanz (überlebt destroy). `destroy()` beim Verlassen von /analysis gibt die ~7 MB WASM frei → erwünscht. Component-scopen würde die Telemetrie-Verdrahtung zerreißen → NICHT ändern.

**Weiter geparkt (Aufwand/Regressionsrisiko, brauchen Laufzeit-Verifikation):**
- [~] **`OnPush` ausrollen** → **fortgesetzt v0.184.20/0.184.22/0.184.30**: präsentationale Komponenten loading-spinner, puzzle-tags, theme-picker, review-nav, promotion-picker (R3/R5) + **puzzle-your-turn, puzzle-status-card, puzzle-rating-card, viz-card** (v0.184.30 — alle nur primitive Inputs, Eltern rebinden je CD, keine In-place-Mutation; +Spec). OFFEN: die wertvollen, aber risikoreichen Solver/Analyse/Turnier-Tabellen (Timer via `NgZone.runOutsideAngular`) — bewusst nicht angefasst.
- [~] **God-Components entzerren / Service-Layer** → **weitgehend erledigt v0.184.16–0.184.22**: Services extrahiert für repertoire-list/tournament-list/dashboard (R3) + **friends/friend-stats/friend-revenge/challenge-friends (FriendsService), public-tournament (PublicTournamentService), profile+games-list (ProfileService), repertoire-detail/-edit (RepertoireService erweitert)** (R5, +Specs). `changePassword`→AuthService. OFFEN: api-tokens (eigener Dialog-Flow), puzzle/endless-history (komplexer State); God-Components `endless-puzzle` (1359 LOC) + `admin.component` (732) noch nicht zerlegt.
- [~] **Cross-Solver-Duplikation in `BasePuzzleSolver` hochziehen** → **teilerledigt v0.184.31**: `formatTime` (5× byte-identisch in puzzle/book/endless + status-/your-turn-Card) → gemeinsame `puzzle-format.util.ts` (`formatPuzzleTime`), Basis-`formatTime` delegiert. Einzel-Stoppuhr-Timer (`elapsedSeconds`/`stopwatch`/`startTimer`/`stopTimer`, byte-identisch in puzzle+book) in die Basis hochgezogen; Endless erbt `elapsedSeconds`+`formatTime`, behält bewusst seine Doppel-Stoppuhren (Session+Puzzle). +2 Specs, alle Solver-Specs grün. OFFEN (bewusst nicht angefasst): eval-Toggle + Keyboard-Handler + Theme-Setter divergieren genug zwischen den Modi (mode-spezifische Tasten/Theme-Persistenz), dass ein Hochziehen mehr Override-Mechanik als Ersparnis brächte.
- [x] **Test-Lücke** → **erledigt**: v0.184.11 Specs für `menu.service`/`preferences.service`/`chessable.service`/`admin.service` + `profile.component`; v0.184.19 `admin.component`-Spec (Direkt-Instanziierung im Injection-Context, Tab-URL/loadAllUsers/Recompute/Guard). Damit ist der Audit-Gap geschlossen.
- [~] **Klickbare `<div>`/`<span>`/`<mat-icon>`** ohne Tastatur → **fortgesetzt v0.184.13 + v0.184.20 + v0.184.32**: theme-chips/endless-history-Karte (0.184.13); puzzle-tags-Toggle, repertoire-tree (crumb/child-item), repertoire-lines (line-item) (0.184.20); **tournament-favoriten** (v0.184.32): die Favoriten-Sterne (`<mat-icon class="fav-icon">` Spieler+Team) UND die klickbare Mobil-`player-card` in tournament-detail+public-tournament sind jetzt tastaturbedienbar (`role=button`/`tabindex=0`/`keydown.enter`+`space`/`aria-label`+`aria-pressed`/`:focus-visible`; i18n `tournaments.favorites.toggleAria`). Anders als die alte Notiz behauptete, GAB es klickbare Nicht-Button-Stellen. OFFEN (separater Fund): die `team-link`-Spans (Team-Spieler anzeigen) — kleinere Sekundär-Aktion, später.
- [~] Kleinkram-Rest: api-tokens-Subscribes **erledigt v0.184.12** (filter/switchMap/catchError). OFFEN: `AppNotification.type:string` als Union (bewusst offen — Server-getriebenes Feld, Über-Constraint-Risiko).

## Audit-Funde 2026-06-16 (Code-Review aller Repos)
Read-only-Review über rookhub (API+Frontend), chessresults_crawler, schach-bot, piratechess_docker. **5 Top-Funde direkt gefixt** (in v0.149.2 / piratechess): #1 Revenge-`solved` serverseitig hergeleitet+Dedupe, #3 Job-Feld-Data-Race (Gate/Complete/Snapshot), #4 Per-Bid-Lock gegen Doppel-Fetch, #5 Admin-Deep-Link via queryParamMap-Abo, #8 `GetThreadsAsync` auf GROUP-BY/bounded umgebaut. Rest hier geparkt (priorisiert; vieles intern/VPN-geschützt → Risiko realistisch einordnen):

### rookhub API
- [x] HIGH `EncryptionService`: **erledigt** — rookhub (0.176.2) + piratechess (Commit 38cc375) auf AES-GCM + `SHA256(key)` + `TryDecrypt` + Längen-Guard; Alt-CBC bleibt rückwärtskompatibel lesbar (kein Migration). Call-Sites nutzen `TryDecrypt` → kein 500 mehr bei Rotation. Beide Repos: Tests grün. (piratechess noch NICHT getaggt/deployed.)
- [x] HIGH `AdminMessageService.EnsureThreadAsync`: PK-Race bei gleichzeitiger Erst-Nachricht → behoben (0.152.5): EnsureThreadAsync legt die Thread-Zeile jetzt in EINEM eigenen SaveChanges an und fängt `DbUpdateException` (PK-Konflikt) ab → eigene Add-Entry detachen + existierende Zeile nachladen. Idempotenz-Test ergänzt (3× EnsureThread → 1 Thread-Zeile + Claim bleibt). Hinweis: der echte Concurrency-Pfad ist mit InMemory nicht deterministisch nachstellbar → gegen MariaDB verifizieren.
- [x] HIGH ChessableImport: kein atomarer Claim beim Job-Picking → **erledigt v0.184.25**: `RunNextAsync` übernimmt den fair nächsten Job per atomarem `ExecuteUpdateAsync` (Phase „queued"→„claimed", gefiltert auf unveränderte Phase) auf relationalen Providern; nur der Worker mit 1 getroffener Zeile bearbeitet ihn, verlorene Claims probieren den nächsten Kandidaten. InMemory-Re-Check-Fallback (kein `ExecuteUpdate`-Support). +2 Tests.
- [x] MED Challenge-`ResolveAsync`: `solved` serverseitig hergeleitet — **erledigt v0.184.5**: asymmetrisch — „nicht gelöst" wird übernommen, „gelöst" nur wenn ein bestätigter gelöster Versuch (`PuzzleAttempts`/`BookPuzzleAttempts`) seit Erstellen der Challenge existiert (`HasConfirmedSolveAsync`, analog Revenge). `timeSpentSeconds` bleibt geklemmt geglaubt (kosmetisch). +2 Tests.
- [x] MED N+1 im Challenge-Batch → behoben (0.152.7): `FriendService.GetAcceptedFriendIdsAsync` (eine Abfrage statt N× `AreFriendsAsync`) + Duplikat-Check für alle Kandidaten in EINER Abfrage; Benachrichtigung via `CreateManyAsync` (ein Save). Vorher teilerledigt (0.152.3): `NotificationService.CreateManyAsync` für die Admin-Schleife. (+1 Test: nur erstellte Empfänger werden benachrichtigt; 16 ChallengeControllerTests grün.)
- [~] MED `FriendService.SearchUsersAsync`: **Such-Teil erledigt v0.184.26** — Identitäts-/Konto-Felder (Username/chess.com/Lichess/FIDE/ChessResults) auf Präfix (`StartsWith`, indexfreundlich, Username-Unique-Index greift) statt `LIKE %q%`; nur DisplayName bleibt Teilstring (Mitte zählt dort). Länge (50) + Take (20) zusätzlich service-seitig hart gekappt + leere/Wildcard-Query → leer. +3 Tests. OFFEN: Auth-Rate-Limiter IP- statt account-basiert (Credential-Stuffing über viele IPs).
- [x] LOW (0.152.6): `GetUserCoursesAdmin` prüft jetzt User-Existenz → 404 statt irreführender 400; `Mask` zeigt nur noch die letzten 4 Zeichen (Anfang nicht mehr preisgegeben). `RunDetached` existiert nicht mehr (Import-Service = `RunNextAsync`/`RunAsync`) → Fund obsolet. (+2 Controller-Tests, Mask-Test angepasst.)

### rookhub Frontend
- [x] HIGH Test-Lücke: `InAppNotificationService`, `notification-text.ts`, `messages.component`, `notifications.component` ohne Spec → behoben (0.152.4): 4 neue Specs, 22 Tests (Service: Count/markSeen-Clamp/markAllSeen/reset/Query-Params; notification-text: Key-Wahl inkl. _solved/_failed + Chessable-Suffix + Icon-Map; beide Components direkt instanziiert: loadMore-Pagination/open-markSeen+navigate bzw. load+markUserSeen/send-trim/Fehlerpfade).
- [x] MED `/messages` Refresh-on-focus (0.154.1): `MessagesComponent` lädt den Thread bei `window:focus` neu (still, kein Spinner, nicht während Senden) → neue Admin-Antwort + Read-State sofort aktuell. +2 Specs.
- [x] MED Tab-Index: **erledigt** — (0.154.2) `messagesTabIndex=6` Magic Number ersetzt durch `admin-tabs.ts` (`ADMIN_TAB_KEYS` + `adminTabIndex()`, Deep-Link auf BELIEBIGEN Tab-Key generalisiert, Guard-Test hält die Reihenfolge mit dem HTML konsistent); (v0.184.19) `onTabChange` schreibt den Tab als `?tab=<key>` zurück in die URL (`queryParamsHandling:'merge'`, `replaceUrl`), Reload/Back behält den Tab. Spec deckt Write-back + Out-of-range ab. (Bei 2026-06-24-Sichtung als bereits erledigt verifiziert.)
- [x] MED Label-Methoden im Template (`translate.instant` je CD-Zyklus während Polling) → **erledigt**: (v0.184.8) Dashboard `chessableActive` + Admin-Importliste `adminImports` cachen ihre Labels je Poll; (v0.184.28) `chessable.component` `activeImports`-Dict cacht jetzt ebenfalls — `ActiveImport = ChessableImport & { queueLabelText }`, EINE Helper-Methode `setActiveImport` berechnet das Label bei jedem Update (load/start/applyUpdate/pollActive), Template bindet `imp.queueLabelText` statt `queueLabel(imp)`. +2 Specs.
- [x] MED Badge-Flackern: **erledigt v0.184.7** — `refreshCount` ignoriert innerhalb eines 5-s-Schutzfensters nach einer optimistischen `markSeen`/`markAllSeen`-Verkleinerung einen HÖHEREN Serverwert (verhindert das Zurückspringen durch einen gleichzeitig gestarteten, veralteten Refresh). +1 Spec.
- [x] LOW `dlImport`-Polling + Admin-Kleinkram → **erledigt**: (v0.184.8) `dlImport`-Polling stoppt nur bei Endzuständen, `loadAllUsers`-Error-Hinweis, `acceptDisclaimer`-Doppelsubmit-Guard; `availableUsers()` ist seit v0.184.19 memoisiert (Feld + `recomputeAvailableUsers`, keine Allokation je CD); (v0.184.29) `bypassSecurityTrustUrl`-Bookmarklet mit Origin-Guard + Sicherheits-Kommentar abgesichert (Code rein app-konstruiert, kein User-Input), Mitglieder-Dropdown-„500er-Limit" bewusst belassen (kleiner Nutzerkreis) + warnt jetzt bei `totalCount > 500` statt still abzuschneiden. +2 Specs.

### piratechess_docker
- [x] HIGH „Chessable"-HttpClient nie in `Program.cs` registriert — **erledigt 2026-06-23 (piratechess Commit `6de7fa7` in der Kopie `rookhubstack`, committet, NICHT gepusht/getaggt/deployed):** `builder.Services.AddChessableHttpClient(builder.Configuration)` in `Program.cs` ergänzt → `CreateClient("Chessable")` läuft jetzt über den gluetun-Proxy (:8888) statt Default-Client ohne Proxy. Fixt `WaitForProxyReadyAsync` (Readiness-Probe nach Rotation) UND `VpnController`-IP-Status-Fallback (meldete sonst Host-IP). +Regressionstest `ChessableHttpClientRegistrationTests` (148 Tests grün).
- [x] HIGH `ServiceKeyAuth` zeitkonstant → **erledigt 2026-06-24 (piratechess 5719d0e):** `CryptographicOperations.FixedTimeEquals` + `header.Count==1`-Guard. +1 Test.
- [~] MED Rotations-Zähler / Job-Store / RunFetch / cached → **2026-06-24:** Job-Store-Leak **behoben** (CourseFetchJobStore.Prune: TTL 30 min terminal / 6 h hart / Cap 500, lazy beim Create; piratechess Commit Job-Store). `RunFetchAsync`/`ct` war bereits durchgezogen → zusätzlich curl-Prozess-Kill bei Cancel ergänzt. **Verworfen (kein Defekt):** geteilter Rotations-Zähler ist für EINE Exit-IP korrekt; `cached`-AnyAsync würde den Truncated-Cache-Schutz brechen (Dekompression ist load-bearing, siehe Code-Kommentar).
- [x] LOW `.Wait()`/`int.Parse`/Unique-Index/RawResponses → **2026-06-24:** SignalR-Progress fire-and-forget statt `.Wait()` (sync-over-async); `int.Parse(claim)`→`GetRequiredUserId()` (401 statt 500); Login-JWT in Roh-Antwort redigiert + Retention existiert bereits (`RawResponseRetentionService`, 14 Tage). **Gegenstandslos:** `CachedCourse`/`GeneratedPgn` sind tote Tabellen (kein Upsert-Code mehr); die Live-Caches `CachedRawCourse`/`CachedRawLine` haben bereits Unique-Indizes.

### chessresults_crawler
- [x] HIGH Voll-HTML-Body (bis 500 KB) auf `Information` → **erledigt (0.115.1)** — Dublette des Funds unter „## Audit-Funde 2026-06-13 / chessresults_crawler / Body-Logging" (nur noch Größe/Status geloggt).
- [~] HIGH VPN-Rotation läuft IM gehaltenen Semaphor → blockiert alle Parallel-Crawls bis ~8 s (Timeout-Risiko); 429/5xx von chess-results.com lösen kein Backoff aus (harter Job-Fail) → `Retry-After`/Polly. (= derselbe Fund wie „HIGH VPN-Rotation läuft im Request-Lock" im 2026-06-13-Abschnitt — hier konsolidiert.) — **ROTATION-TEIL ERLEDIGT** (2026-07-05, crawler `d64b4fc`, rookhub v0.260.1, gepusht): der ~5s-Public-IP-Poll läuft jetzt detached außerhalb des Rate-Limiters (`RestartVpnTunnelAsync` + `LogNewPublicIpDetached`), nur der eigentliche Tunnel-Neustart bleibt im Lock (Tunnel ist dabei ohnehin unten). Knobs `Crawler:RotateAfterRequests`/`VpnRestartPauseMs`/`MinDelayMs` konfigurierbar. **OFFEN: der 429/5xx-`Retry-After`/Polly-Backoff-Teil** — chess-results.com-4xx/5xx macht weiterhin nur EINEN Fixed-Delay-Retry (kein Honorieren von `Retry-After`, kein Exp-Backoff, retryt auch nicht-retrybare Codes wie 404/403). Fetch-Retry-Pfad in `FetchWithRedirectAsync`/`FetchHtmlAsync` + POST-Suchpfade.
- [~] MED **teilerledigt 2026-06-24 (Crawler-Commits c518e74/cf6b5a9/7522f3a/bc59f31, NICHT gepusht):** `ExtractHiddenField` von Regex auf **AngleSharp** umgestellt (name→id-Fallback, +6 Paritäts-/Robustheits-Tests); defensives **Response-Größenlimit** beim Lesen (`Crawler:MaxResponseBytes`, Default 32 MB, streamender `ReadBodyBoundedAsync` bricht statt OOM ab, +2 Tests); **Player/Team-Upsert in DB-Transaktion** geklammert (gegated über `Database.IsRelational()` → InMemory bleibt grün, +1 Test). OFFEN: Encoding-Annahme (windows-1252-Umlaute → Datenkorruption) — bewusst NICHT angefasst (zu riskant ohne Live-Daten); normalisiertes Team-Matching.
- [~] LOW Crawler-Kleinkram → **teilerledigt (Crawler f5071aa/052007b, rookhub v0.184.21):** `/api/health/ip` ist jetzt API-Key-pflichtig (war unauth + Outbound-Trigger); Phantom-Runden werden gegen `TotalRounds` geclampt (`ParseAvailableRoundsAsync(html, maxRound)`). OFFEN: `ApiKeyMiddleware` Fail-Fast in Prod bei leerem Key (bewusster Dev-Fallback); Retry-Pfad-Duplikation in `FetchWithRedirect/FetchHtml` (kein Bug — Retry-Exception soll propagieren; reiner Code-Smell, deferred).

### schach-bot
- [~] HIGH Webhook Replay-/Timestamp-Schutz + `daily-regenerate`-Idempotenz → **erledigt 2026-06-24 (schach-bot e523896, v2.70.0):** `_verify_signature` zieht optionalen `X-Webhook-Timestamp`-Header in die HMAC (`HMAC("<ts>.<body>")`, ±300s-Fenster); fehlt der Header, greift der alte body-only-Pfad (rückwärtskompatibel). `daily-regenerate` ist idempotent (no-op wenn aktuelle puzzleId == regenerierte) + puzzleId validiert. OFFEN: **rookhub-Seite `SchachBotWebhookService` muss Timestamp signieren+senden** (sonst kein scharfer Replay-Schutz); Port `0.0.0.0:9000` nicht veröffentlichen = Compose/Deploy-Aufgabe.
- [x] HIGH `asyncio.create_task`-Schwarm (Reinforcement-DMs) → **erledigt 2026-06-24 (schach-bot 1e9eabc, v2.71.0):** `core.reinforcement.spawn_dm()` hält Task-Referenzen in einem Set (GC-Schutz) + drosselt via `asyncio.Semaphore` (`_MAX_CONCURRENT_DMS`=3); daily_results + weeklypost nutzen ihn. (Slacker-DMs im Motivations-Loop werden bereits sequenziell awaited, kein Schwarm.)
- [x] MED KI-Chat-Caps → **erledigt 2026-06-24 (schach-bot aa3ebce, v2.72.0):** Tages-Token-Cap pro Nicht-whitelisted DM-User (`CHAT_DAILY_TOKEN_CAP`, chat.json `usage`); `analyze_move` `fen`-Override nur als Folgestellung des Puzzles (`_is_followup_position`); `_rate_hits` gebounded (Prune+FIFO `_RATE_LIMIT_MAXSIZE`=5000); Motivations-/Reinforcement-`_via_claude` mit `asyncio.wait_for(30s)`.
- [~] LOW Webhook-/SFTPGo-Härtung → **größtenteils erledigt 2026-06-24:** SFTPGo-Passwort als separate Spoiler-Nachricht statt im Link-Block (fb10044, v2.72.1); Webhook `client_max_size` (256 KiB) (e523896, v2.70.0). OFFEN: Help-Definitionen aus `bot.py` auslagern (zyklische Kopplung mit `chat_tools`).

## Audit-Funde 2026-06-13 (Code- + Security-Review aller Repos)
Read-only-Audit über rookhub (API+Frontend), chessresults_crawler, schach-bot, piratechess_docker, repcheck. Zwei sichere Fixes direkt erledigt (s. u.), Rest geparkt — priorisiert. Adressraum-Hinweis: vieles davon ist intern/VPN-geschützt; Risiko realistisch einordnen.

### chessresults_crawler
- [x] **Body-Logging nach ES** — `LogCrawlRequest` loggte bei jedem erfolgreichen Fetch bis 500 KB Roh-HTML (Spieler-PII + ES-Bloat). In 0.115.1 entfernt (nur noch Größe). (`CrawlerService.cs:700`)
- [x] **HIGH `/api/health/ip` offen + triggert Outbound** → **erledigt 2026-06-24 (Crawler f5071aa, rookhub v0.184.21):** aus `IsOpenPath` entfernt → API-Key-pflichtig; nur noch `/api/health` (Liveness) offen.
- [x] **HIGH VPN-Rotation läuft im Request-Lock** → **ERLEDIGT (Crawler v0.260.1), am 2026-08-26 im Code gegengeprüft:** `RateLimitAsync` hält den Semaphor nur noch für den eigentlichen Tunnel-Neustart (da darf ohnehin kein Request raus); die rein informative Public-IP-Ermittlung (5×1 s Polling) läuft detached außerhalb des Locks. Stand hier noch als offener HIGH. Offen bleibt nur der 429/Retry-After-Backoff — geführt beim `[~]`-Zwilling im Crawler-Abschnitt.
- [x] MED verwaiste `Queued`-Jobs ohne Recovery — **erledigt (0.176.3, Crawler 66722a4):** `CrawlJobRecovery.RecoverStaleJobsAsync` setzt beim Start alle Queued/Running-Jobs auf Failed (in `Program.cs` nach `Migrate()`) → kein dauerhaft blockierter ActiveKey mehr. Tests `CrawlJobRecoveryTests`.
- [x] MED finaler Status-Save mit bereits gecanceltem Token → **erledigt (0.176.4, Crawler fed3c65):** finaler `SaveChangesAsync(CancellationToken.None)` (Z. 134) → Status wird auch bei Cancellation persistiert. (Hinweis: der mittlere Save bei `:42/70/114` läuft weiter mit `ct` — gewollt, nur der FINALE Status-Save muss garantiert durchgehen.)
- [x] MED Team-Upsert via `ToDictionaryAsync(t => t.Name)` → **erledigt (0.176.4, Crawler fed3c65):** `CrawlerService.BuildTeamNameMap` (tolerant, kleinste Snr gewinnt) statt ToDictionary; Tests `CrawlerServiceTeamMapTests`.
- [~] LOW Retry-Pfad in `FetchWithRedirect`/`FetchHtml` — **2026-06-24 bewusst nicht geändert:** kein Bug (die Exception des einen Retry SOLL propagieren); reine Copy-paste-Duplikation, Refactor des Kern-Fetch-Pfads lohnt das Regressionsrisiko nicht. (siehe 2026-06-16-Abschnitt, Crawler-Kleinkram)

### piratechess_docker
- [x] **HIGH curl-Arg-Injektion via `bid`** → behoben (piratechess b398963): Umstieg auf `ProcessStartInfo.ArgumentList` (jeder Wert ein escapetes argv-Token, content-agnostisch → schützt bid/uid/oid/bearer/url). `BuildGetArgs/BuildPostArgs` → `List<string>`, 3 Sicherheitstests. DEV deployed.
- [~] **HIGH gluetun `auth = "none"`** — *nur Verweis, nicht doppelt abarbeiten:* Master-Eintrag ist „gluetun-Control-Server API-Key“ im Abschnitt Refactoring/Qualität, dort steht der aktuelle Stand (Code beidseitig fertig, offen ist die Deploy-Seite). (**= Dublette** von „gluetun-Control-Server … auf API-Key-Auth härten" im Refactoring-Abschnitt oben — dort der Master-Eintrag inkl. Crawler-Seite) — Code fertig (piratechess b398963, DEV deployed): GluetunControl-HttpClient sendet `X-API-Key`, WENN `Gluetun:ApiKey` gesetzt (rückwärtskompatibel: ohne Key kein Header). **OFFEN = Aktivierung (koordinierter Restart):** in `/opt/stacks/rookhub-schach{,-dev}/gluetun-auth.toml` `auth="apikey"` + `apikey=<secret>`, `GLUETUN_APIKEY` in beide `.env` → `Gluetun__ApiKey`-Env, dann **gluetun + piratechess-api ZUSAMMEN** neu starten (sonst Mismatch → Rotation bricht). Repo-`gluetun-auth.toml`-Template steht schon auf `apikey` (Platzhalter). Betrifft prod + dev.
- [x] MED `GET /api/vpn/status` ohne Auth → **erledigt 2026-06-24 (piratechess 89a78ac):** `[ServiceKeyAuth]` auf den Status-Endpoint (POST /rotate bleibt JWT). +2 Tests.
- [x] MED Login-Response roh persistiert → **erledigt 2026-06-24:** `RedactForStorage` redigiert das `jwt`-Feld der Login-Antwort vor dem Speichern; Retention (14 Tage) existiert bereits via `RawResponseRetentionService`.
- [x] MED ServiceKey-Vergleich zeitkonstant → **erledigt 2026-06-24 (piratechess 5719d0e):** `FixedTimeEquals` + Count-Guard (Duplikat des HIGH-Funds oben).
- [x] LOW DB-Port `3308:3306` auch in Prod auf Host gemappt; Prod-Compose fehlen `Service__ApiKey`/`Gluetun__*`/`Elasticsearch__*` ggü. dev (Config-Drift → /direct/* in Prod fail-closed 503). → **erledigt 2026-06-24 (piratechess `a5a7864`, committet, NICHT gepusht/getaggt/deployed):** `docker-compose.prod.yml` an dev angeglichen (`Service__ApiKey`/`Gluetun__ControlUrl`+`__ApiKey`/`Vpn__RotateAfterRequests`/`Elasticsearch__Url`+`__IndexFormat` + `FIREWALL_INPUT_PORTS=8888,8000` + gluetun-auth.toml-Mount + api am `chessable-bridge`-Netz); DB-Port in Prod nur noch `expose: 3306` statt `3308:3306` auf den Host. `.env.example` um `GLUETUN_CONTROL_URL`/`GLUETUN_APIKEY`/`VPN_ROTATE_AFTER_REQUESTS` ergänzt; Prod-Template bare `${VAR}` (Konvention). Beide Compose-Dateien per `docker compose config` validiert, 161 Tests grün. `auth="apikey"`-Aktivierung bewusst NICHT vorgenommen (koordinierter Restart, s. Z. 161).

### rookhub API
- [x] **HIGH BotStats-HMAC ohne Timestamp/Nonce** → **erledigt 2026-06-25 (rookhub v0.184.35 `4d03570` + schach-bot v2.73.0 `f6ea6ba`):** `X-Bot-Timestamp`-Header, HMAC über `"<ts>.<discordId>"`, ±300 s. rookhub akzeptiert weiter die alte body-only-Signatur (rückwärtskompatibel → rookhub ≥ v0.184.35 vor/mit Bot deployen). +3 rookhub-Tests, +1 Bot-Test. NICHT gepusht/deployed.
- [x] MED JWT-Invalidierung bei Passwort-Reset/-Change + Account-Löschung → **erledigt**: Account-Löschung (0.176.1, `DeletedAt`-Check); **Reset/Change erledigt v0.184.9**: `AppUser.SecurityStamp` (Migration `AddUserSecurityStamp`) rotiert bei ChangePassword+ResetPassword, GenerateJwt schreibt `sstamp`-Claim, `OnTokenValidated` prüft via `AuthUserValidation.IsTokenValidAsync` (Cache nun aktiv+Stamp). Grandfathering: Token ohne Claim / User ohne Stamp → kein Massen-Logout; Login backfillt fehlende Stamps lazy. (API-Tokens bewusst NICHT betroffen — eigenständig verwaltet.) +8 Tests.
- [x] MED AES-CBC ohne Auth-Tag + schwache Key-Ableitung — **erledigt (0.176.2, verifiziert 2026-06-23):** `EncryptionService` schreibt v2 = AES-GCM (authentifiziert) mit `SHA256(key)` (32 Byte, kein Null-Padding); `TryDecrypt` liefert null statt 500 bei Key-Rotation. Alt-CBC bleibt nur noch lesend rückwärtskompatibel (keine Datenmigration nötig). Duplikat des bereits abgehakten HIGH-Funds unter „## Audit-Funde 2026-06-16 / rookhub API".
- [x] MED Reset-Link inkl. Roh-Token bei deaktiviertem SMTP im Klartext geloggt → **erledigt v0.184.2**: `SmtpEmailSender` loggt den Body (inkl. Link) nur noch in `Development`; sonst nur Empfänger+Subject als `LogError`-Fehlkonfiguration (kein Klartext-Link → ES). +2 Tests.
- [x] LOW Anon-Sessions per erratener `sessionId` claim-/überschreibbar (IDOR) → **erledigt v0.184.27**: `ValidationConstants.SessionIdPattern` Mindestlänge 1→32 (UUID-Form 32–36 Zeichen). Erratbare Kurz-Werte (z. B. „1"/„abc") werden jetzt von allen anon-Schreibpfaden (BookPuzzle/Endless/Puzzle-Attempt + Claim + VisitorId) abgewiesen → keine fremden anonymen Puzzle-/Endless-Stats mehr claim-/überschreibbar. Rückwärtskompatibel: Clients vergeben die Id ohnehin per `crypto.randomUUID()` (36 Zeichen). +1 Controller-Test (Kurz-Id → 400) + VisitorId-Theory-Fall; 5 Tests mit kurzen Literal-Ids auf 32-Hex angehoben.
- [x] LOW Impersonation-`imp`-Claim + ApiToken-`LastUsedAt` → **erledigt**: (v0.184.4) `imp`-Claim wird jetzt ausgewertet — `BaseApiController.IsImpersonating()` sperrt DeleteAccount/ChangePassword/Token-Create (403) im Impersonations-Kontext; (v0.184.3) `ValidateAsync` schreibt `LastUsedAt` nur noch gedrosselt (höchstens alle 5 min statt je Request). +4 Tests.

### rookhub Frontend
- [x] MED i18n-Verstoß behoben (0.117.1) — die tatsächlich gerenderten hartcodierten Strings lagen im **`puzzle-settings-dialog`** (`vizLevelOptions`-Beschreibungen + `difficultyInfoOptions`-Beschreibungen), nicht in `base-puzzle-solver`. Neue Keys `puzzles.viz.level{0..4}Name/Desc` + `puzzles.difficulty.*Desc` (en/de/hr), Template via `| translate`. Die in der Notiz genannten `base-puzzle-solver`-Getter + `book-puzzle`-Override + toter `VizCardComponent`-Import waren **toter Code** (nirgends gerendert) → entfernt. +Spec.
- [x] LOW Frontend-Kleinkram komplett erledigt: `rel="noopener noreferrer"` ergänzt (`tournament-detail`/`public-tournament` + `chessable` von `noopener` → `noopener noreferrer`) (0.117.1); `clipboard.writeText` mit Guard + `.catch()` (`api-tokens.component.ts`), `stopImpersonation()` parst vor dem Commit + loggt bei beschädigtem Backup sauber aus (`auth.service.ts`, +2 Specs), Crawler-Job-/Monitor-Responses typisiert (`CrawlJob`/`TournamentMonitorStatus` in `core/models.ts` statt `Observable<any>`) (0.117.2).

### schach-bot (Python) — sehr sauber, keine ≥MED-Funde
- [~] LOW `isinstance(puzzle_id, int)` akzeptiert bool + `_id_cache` ohne Maxsize → **erledigt 2026-06-24 (schach-bot e523896/fb10044, v2.70.0/v2.72.1):** webhook nutzt `type(x) is int`; `_id_cache` FIFO-gebounded (`_ID_CACHE_MAXSIZE`=10000). (DM-Chat-RateLimit bleibt prozesslokal — als Missbrauchsschutz ausreichend, resettet bei Neustart.)

### repcheck (Browser-Extension, Kopie 1) — nicht in Kopie 2
- [~] **HIGH `host_permissions` überbreit + Background-Fetch-Proxy** → **Proxy-Härtung erledigt 2026-06-25 (repcheck v1.17.0 `81bc012`):** `background.js` prüft `sender.id == runtime.id` UND erlaubt nur Requests an die Origin der gespeicherten RookHub-URL (`chrome.storage.local rookhubConfig`) — kein offener Fetch-Proxy mehr; `saveRookhubConfig` wartet den Storage-Set ab (race-frei beim Erst-Connect). OFFEN: `host_permissions` selbst bleibt breit, weil die RookHub-Instanz user-konfigurierbar ist (beliebige Origin) — die Laufzeit-Allowlist setzt die eigentliche Schranke; `http` weiterhin erlaubt (Self-Hosting ohne TLS).
- [~] MED Chessable-Bearer-JWT unverschlüsselt in `chrome.storage.local` → **Versions-Drift erledigt 2026-06-25 (v1.17.0):** `content.js __rdc_loaded.version` 1.12.0 → 1.17.0, manifest + Userscript synchron. OFFEN: Token-Verschlüsselung-at-rest + TTL (`chessable-token.js`); `http`-URLs erlauben Token im Klartext.

### Aus den Live-Logs (24h Prod) zusätzlich aufgefallen
- [~] ASP.NET **DataProtection-Keys** → **rookhub-Persistenz erledigt v0.184.14**: `PersistKeysToFileSystem` auf konfigurierbaren Pfad (`DataProtection:KeyPath`, Default `/keys`), Verzeichnis wird angelegt, In-Memory-Fallback statt Crash bei nicht beschreibbarem Pfad, `SetApplicationName("RookHub")`. OFFEN: Verschlüsselung-at-rest (`ProtectKeysWith*` — auf Linux ohne Zertifikat nicht trivial) + piratechess-Seite.
- [ ] **VPN-Rotation instabil** (live bestätigt: 27 Warns/24h „rotation failed (non-critical)" / „incomplete → forcing VPN restart") — verstärkt die Crawler/piratechess-Rotation-Funde oben; lohnt echte Ursachenanalyse (gluetun-Control-Timing).

### i18n-Weltsprachen (22 Stück)
- [ ] Massen-Übersetzung/Bereinigung der 22 erweiterten Sprachen — **gemessen 2026-08-26: je 1368 von 2281 Keys fehlend (~60 %) + 32 veraltet**, nicht die früher notierten ~174. `en` ist seit dem alten Eintrag um 414 Keys gewachsen, `de`/`hr` sind mitgezogen (0 Lücken), die 22 nicht. Praktische Folge: Diese Oberflächen sind mehrheitlich Englisch — der `fallbackLang: en` verdeckt das nur. Braucht eine Pipeline-/Tooling-Entscheidung (MT mit Review vs. manuell vs. Sprachen mit Restlücke bewusst aus dem Umschalter nehmen). en/de/hr sind die gepflegten Sprachen.

## Features
- [x] Start-ELO schneller einpendeln (0.123.0) — betraf den **Standard-/Random-Puzzle-Modus** (persönliche Puzzle-Elo), NICHT Endless. Umgesetzt im Backend `PuzzleService.ProvisionalKFactor`: K-Faktor **×4** (in beide Richtungen — K skaliert Gewinn wie Verlust) bis **≥5 gelöst UND ≥5 gescheitert** (je vizLevel), **×2** bis 10/10, danach normaler K (20). Ersetzt das alte `attemptCount<30?40:20`. Tests in `PuzzleServiceTests`.
- [ ] Trainersystem mit eigenen Gruppen einführen — Konzept noch offen. Idee: Trainer-Rolle, die eigene Gruppen anlegen/verwalten und Mitglieder zuweisen kann (heute nur Admin via `/api/admin/groups`), inkl. Trainingsziel-Vorlagen + ggf. Kurs-Freigaben für die eigenen Gruppen. Aufbauen auf bestehender Gruppen-/`GroupTrainingGoals`-/`BookGroupAccess`-Infrastruktur; offene Fragen: Rollenmodell (neue Rolle vs. Flag), Sichtbarkeits-/Berechtigungsgrenzen Trainer ↔ Mitglieder, Einladungsfluss.
- [ ] Push-Benachrichtigungen (PWA) — z.B. „Dein Tagespuzzle wartet"
- [~] Benachrichtigung bei neuen Turnierblättchen → **In-App erledigt v0.184.15**: `RoundMonitorService.NotifyNewRoundAsync` informiert bei erkannter neuer Runde alle Abonnenten via Glocke (`NotificationType.TournamentNewRound`, Link zur Detailseite; i18n en/de/hr). OFFEN: E-Mail-Kanal (Phase 2, dockt an `IEmailSender` an).
- [x] Kapitel-Spoiler dauerhaft entschärfen → **erledigt v0.184.10**: `PgnImportService.ImportFileAsync` strippt für `Kind=Puzzle` via `StripChapterSpoiler` den Titel nach „Chapter N:"/„Kapitel N:"/„Poglavlje N:" (→ nur „Chapter N"); Study-Bücher behalten ihre Kapitelnamen. `ImportPipeline.CurrentVersion` 1→2 → Bestands-Puzzle-Bücher per „Aktualisieren"-Knopf entschärfbar (deckt auch das manuelle `1001_deadly_checkmates.sql` ab; Chessable-Import läuft ebenfalls durch `ImportFileAsync`). +10 Tests.
- [ ] Puzzle-Streaks / Achievements
- [ ] Admin-Dashboard: User-Übersicht + Aktionen
- [x] Schach-Bot auf Elasticsearch umbauen (Logging/Events) → umgesetzt im Bot-Repo v2.60.0/2.60.1 (`core/es_client.py`, ESHandler in `log_setup.py`, Events `reaction`+`stat_inc`); Index `schach-bot-logs-*` ist live in Prod. Weitere Event-Typen (Daily-Post, DMs, Webhooks, Commands, Buttons) bei Bedarf später ergänzen.

## Kalkulations-Serie (Noel) — eigener Bereich, terminierte Ausgaben (geplant, Design bestätigt 2026-08-19)
Eigener Bereich à la Wochenpost, PRIVAT. Positionen = Calc-Positionen wiederverwendet (Trainings-UI/
Grading/Bäume/Punkte geschenkt); der Bereich ist die Termin-/Verteiler-/Video-/Tester-Schicht.
Noel = Calc-Buch 403, Wochen = Datums-Kapitel (je 6 Stellungen), heute öffentlich (`/noel`).

**Modell (neue Tabellen):**
- `CalcEdition` (BookId, Chapter [= Wochen-Kapitelname], VideoUrl?, PublishAt, TesterPreviewAt?, Title?,
  CreatedAt, UpdatedAt; UNIQUE (BookId, Chapter)). KEIN Kaskaden-FK auf Book-Löschung erwägen (wie WeeklyPost).
- `CalcSeriesMember` (BookId, UserId, IsTester bool, CreatedAt; UNIQUE (BookId, UserId)) = privater Verteiler + Tester-Häkchen.
- `CalcEditionView` (CalcEditionId, UserId, ViewedAt; UNIQUE (EditionId, UserId)) = „gesehen".

**Gating (in CalculationService.GetBookAsync/GetPublicBookAsync + CalcPosition-Zugriff):** eine Woche
(Kapitel) ist sichtbar, wenn eine CalcEdition existiert UND now≥PublishAt (Mitglied) bzw. now≥TesterPreviewAt
(Tester); sonst Entwurf/versteckt (nur Owner/Admin sieht alles). Kapitel OHNE Edition = altes Verhalten
(Übergang). Zugriff privat: statt Book.IsPublic gilt CalcSeriesMember (Owner/Admin immer).

**Phasen:**
1. ✅ CalcEdition + CRUD (Video + PublishAt/TesterPreviewAt) + DATUMS-Gating (Woche versteckt bis PublishAt,
   für alle; Owner/Admin sieht Entwürfe). Backend v0.361.0, Frontend v0.362.0 (Kurs-Detailseite: Verwalter
   plant/bearbeitet je Kapitel eine Ausgabe über den Kapitel-Menüpunkt; Betrachter sieht „Video ansehen"-Link
   bzw. Freigabe-Datum). Endpoints `GET/PUT /api/calc-editions/{bookId}`, `GET .../manage`, `DELETE .../{id}`.
   Noch kein privater Verteiler (Gating hängt weiter an Datum, nicht an Mitgliedschaft).
2. ✅ Backend (v0.363.0): Tabelle `CalcSeriesMembers` (BookId, UserId, IsTester; UNIQUE) + Mitglied-CRUD
   (`GET/PUT /api/calc-editions/{bookId}/members`, `DELETE .../{userId}`, per Benutzername). Zugriff privat:
   Mitgliedschaft ist zusätzlicher Pfad in `CourseAccess.CanAccessAsync` → sobald das Buch nicht mehr IsPublic
   ist, sehen nur noch Mitglieder (+ Owner/Admin/Share/Gruppe). `isTester` ins Gating verdrahtet (Tester sehen
   Wochen ab TesterPreviewAt).
   ✅ Phase 2b (v0.364.0): Verwaltungs-UI — „⋮"-Menü der Kurs-Detailseite → „Serien-Verteiler"-Dialog
   (Mitglieder per Benutzername hinzufügen, Tester-Häkchen je Mitglied, entfernen). `/noel` von öffentlich
   auf privat umstellen bleibt eine reine Daten-Aktion (Book.IsPublic=false setzen, sobald der Verteiler
   steht) — kein Code.
3. Gesehen-Tracking (CalcEditionView) + Benachrichtigung (In-App/Mail an Liste bzw. Tester zum Termin).
   ✅ Phase 3a Backend (v0.365.0): Tabelle `CalcEditionViews` (CalcEditionId, UserId, ViewedAt; UNIQUE) +
   Migration; Auto-Erfassung in `CalculationService.GetPositionAsync` (nur Verteiler-Mitglieder, einmalig je
   Ausgabe+Nutzer; Owner/Admin/öffentliche Betrachter zählen nicht) + Übersicht `GET /api/calc-editions/{bookId}/views`.
   ✅ Phase 3b Backend (v0.367.0): `CalcSeriesAnnounceScheduler` (HostedService, 5 min) + `CalcSeriesAnnounceService`
   kündigen freigegebene Wochen IN-APP (`calc_series_edition_released`) an: Tester zum `TesterPreviewAt`, alle übrigen
   zur `PublishAt` (Tester nicht doppelt). Idempotenz-Marker `CalcEdition.TesterAnnouncedAt`/`PublishAnnouncedAt`.
   BEWUSST kein Mail-Kanal (kein Mail-Opt-out-Modell → wäre unerbetenes Bulk-Mailing; andockbar sobald Opt-out da).
   ✅ Phase 3c Frontend (v0.366.0): „Gesehen"-Anzeige (N/M) im Verteiler-Dialog.
   → Kalkulations-Serie (Noel) damit funktional KOMPLETT (Phase 1–3). Offen nur optional: Mail-Kanal (braucht Opt-out).

Tester-Rückmeldung: nur mündlich (kein Melden-Knopf). Kommentare pro Stellung = bestehendes BookPuzzle.Comment.
Admin-UI: eigener Bereich (Route/Tab) analog Wochenpost-Verwaltung; Viewer: Serien-Seite mit Ausgaben-Liste
(Video + Status), freigegebene Ausgabe öffnet den bestehenden Calc-Trainer aufs Wochen-Kapitel.

## Archiv — erledigte Einzelfunde

_Standen frueher unter „Periodisch“, sind aber einmalige Fehlerbehebungen aus der 0.97er-Zeit_
_und keine wiederkehrenden Aufgaben. Bleiben als Beleg stehen, damit sie nicht erneut_
_untersucht werden._

- [x] Bauernumwandlung (Pawn Promotion) auf Mobile — behoben (vom User bestätigt 2026-06-23).
- [x] Engine-Hang bei Puzzle→Analyse-Wechsel → behoben in 0.97.5 (engine.destroy() statt stop())
- [x] BookPuzzle: Ladefehler → endloser Spinner → behoben in 0.97.6 (loadError-Flag + Retry-Button)
- [x] FriendController: return Forbid(ex.Message) → 500 → war bereits behoben in 0.40.9
- [x] Friendship TOCTOU-Race → war bereits behoben (PairLow/PairHigh computed columns + Self-Friend-Check)
- [x] CrawlJob bleibt bei Enqueue-Fehler dauerhaft Queued → behoben in Crawler (Job auf Failed setzen)
- [x] StockfishService in ngOnDestroy terminate() → war bereits behoben (kein terminate()-Aufruf mehr)
- [x] RecordAttemptAsync ohne Idempotenz/Limit → behoben in 0.97.8 (30s-Idempotenz + Elo-Guard)
- [x] RoundMonitorService: ein SaveChanges nach ganzer Schleife → behoben in 0.97.9 (pro Iteration)
