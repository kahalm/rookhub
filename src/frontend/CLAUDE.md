# RookHub Frontend

Angular 19 Frontend fuer das RookHub-Portal. Teil des RookHub-Projekts (`C:/git/rookhub`), haengt vom **ChessResults Crawler** (`C:/git/chessresults_crawler`) ab – Turnierdaten werden ueber die RookHub API vom Crawler bezogen. Bei Aenderungen immer alle drei Schichten bedenken.

## Zusammenspiel

```
Frontend (dieses Projekt)  --/api/-->  RookHub API (.NET)  --proxy-->  Crawler API (.NET)
     :8085 (Docker)                        :5001                           :8080
     :4200 (ng serve)
```

- Aenderungen an RookHub-API-DTOs/Endpoints muessen in den entsprechenden Components/Services nachgezogen werden
- Aenderungen an Crawler-Datenstrukturen fliessen als JSON durch den Proxy und koennen Tournament-Components betreffen
- `/api/*` wird in Docker von nginx auf die RookHub API geproxied (nginx.conf)
- Bei `ng serve` muss ein Proxy oder die API auf einem erreichbaren Port laufen

## Zwei Projekte in einem Workspace

`angular.json` enthaelt **zwei** Anwendungen, die sich node_modules und den Quellbaum teilen:

| Projekt | Quelle | Bundle | Image | Domain |
|---------|--------|--------|-------|--------|
| `app` | `src/` | `dist/app/browser` | `rookhub-frontend` | rookhub(-dev).oberschmid.homes |
| `turnier` | `src-turnier/` | `dist/turnier/browser` | `rookhub-turnier` | turnier(-dev).oberschmid.homes |

- `src-turnier/` enthaelt nur, was die Turnierseite EIGEN hat: Einstiegspunkt, Routen, Navbar und
  die Turnier-Features. Alles Geteilte (Auth, Interceptors, i18n, shared/) kommt per Pfad-Alias
  **`@rh/*` → `src/app/*`** — kein zweiter Bestand, kein Nachziehen von Hand.
- Eigene Dateien der Turnierseite: `src-turnier/`, `public-turnier/` (eigenes Manifest, wird ueber
  `public/` drueberkopiert), `tsconfig.turnier.json`, `ngsw-config.turnier.json`.
- Bauen: `npx ng build turnier --configuration=production` bzw. `npx ng serve turnier`.
- Testen: **beide Projekte namentlich** — `npx ng test app` UND `npx ng test turnier`. Der
  Karma-Builder sammelt Specs je PROJEKT ein; ohne eigenes Test-Target liefen die 17
  Spec-Dateien der Turnierseite gar nicht mit. Und `ng test` OHNE Projektnamen waehlt bei zwei
  Test-Targets nicht mehr verlaesslich `app` (gemessen 81 statt 1911 Tests) — beides lautlos.

## Tech Stack

| Komponente | Version |
|-----------|---------|
| Angular | 22.0 |
| Angular Material | 22.0.4 |
| Angular CDK | 22.0.4 |
| TypeScript | 6.0 |
| RxJS | 7.8 |
| Node (Build) | node:24-alpine (Docker); lokal ≥22.22.3 bzw. ≥24.15 (Angular 22 laesst Node 20 fallen) |
| SCSS | - |

## Architektur-Entscheidungen

- **Standalone Components** – kein NgModule, jede Component deklariert eigene Imports
- **Lazy Loading** – alle Feature-Routes werden per `loadComponent()` geladen
- **Functional Guards** – `authGuard` als `CanActivateFn`
- **Functional Interceptors** – `authInterceptor` als `HttpInterceptorFn`
- **provideHttpClient / provideRouter** – keine Module-basierte Konfiguration
- **Angular Material** – fuer alle UI-Komponenten (Toolbar, Cards, Lists, Tables, Dialogs, Tabs, etc.)
- **i18n via ngx-translate** – Laufzeit-Lokalisierung (siehe unten)

## Lokalisierung (ngx-translate)

- **Sprachen**: `en` (Default/Fallback), `de`, `hr`, `hu` — die VOLLSTÄNDIG gepflegten. Sie stehen in
  `FORMAT_LOCALES` (`core/locale.service.ts`), und wer dort eine Sprache einträgt, verpflichtet sich zur
  Vollständigkeit: `i18n-parity.spec.ts` verlangt für genau diese dieselben Keys wie `en`, mit gleichen
  `{{Platzhaltern}}` und ohne leere Werte. Die übrigen 21 `SUPPORTED_LANGS` sind Teilübersetzungen und
  fallen Key für Key auf `en` zurück. Alle Dateien liegen in `public/i18n/<code>.json` (statisch unter
  `/i18n/*.json` ausgeliefert); für `FORMAT_LOCALES` werden zusätzlich die Angular-Locale-Daten
  registriert (`registerLocaleData` in BEIDEN `app.config.ts`) — ohne das formatieren Datums- und
  Zahlen-Pipes über `en`.
- **Setup**: `provideTranslateService({ fallbackLang: 'en', loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }) })` in `app.config.ts`. `@ngx-translate/core` + `@ngx-translate/http-loader` **v18** (voll standalone: **kein `TranslateModule` mehr** → `TranslatePipe` importieren; `currentLang`/`fallbackLang` sind **Signals** → `currentLang()`; `defaultLang`/`getDefaultLang()` → `getFallbackLang()`; in Specs statt `TranslateModule.forRoot()` → `provideTranslateService({ fallbackLang: 'en' })` in `providers`).
- **`core/locale.service.ts`**: ermittelt Startsprache (localStorage `rookhub_lang` → Browser → `en`), `use(lang)` persistiert. Wird in `AppComponent`-Konstruktor via `init()` gestartet. Sprachumschalter (Globus-Icon) in der Navbar.
- **Verwendung**: Templates `{{ 'ns.key' | translate }}` bzw. Attribute via Binding (`[attr.title]="'ns.key' | translate"`); dynamische Strings im TS via `TranslateService.instant('ns.key', { param })` mit `{{param}}`-Platzhaltern. Jede Standalone-Component, die übersetzt, importiert `TranslatePipe` (ngx-translate 18 — `TranslateModule` gibt es nicht mehr).
- **Key-Namespaces**: `common`, `nav`, `app`, `auth`, `dashboard`, `profile`, `friends`, `repertoire`, `tournaments`, `puzzles`, `endless`, `book`, `courses`, `weekly`, `trainingGoals`, `admin`, `pgnViewer`. Generische Begriffe (Speichern/Abbrechen/…) unter `common.*`.
- **Nicht übersetzt**: Schach-Notation/FEN/PGN, Eigennamen, „RookHub"/„Stockfish", gecrawlte Daten, HTTP-Methoden.

## Vollbild-Brett (`shared/fullscreen/`)

- `fullscreen.util.ts` — Hülle um die Fullscreen-API (`webkit`-Fallback für Safari; iOS-Safari kann kein Element-Vollbild → `fullscreenSupported()` false, Knopf wird nicht gerendert).
- `board-fullscreen-button.component.ts` — der Knopf in der Brett-Ecke. Sitzt INNERHALB des Vollbild-Elements, damit man im Vollbild ohne Tastatur wieder herauskommt; erklärt sich per nativem `title` (einfacher als ein CDK-Overlay, das erst über den `FullscreenOverlayService` ins Vollbild-Element umziehen muss).
- Im **App-Vollbild** (ganze GUI, Host-Klasse `app-fullscreen`) verlässt der Knopf ebenfalls den Fluss und legt sich neben den App-Vollbild-Beenden-Knopf oben rechts (`right: 42px`): die Zeile über dem Brett verschwindet (Host-Höhe 0), das Brett rutscht genau darum nach oben, und beide Vollbild-Ausgänge liegen beieinander. Dialog-Bretter bleiben unberührt — CDK-Overlays hängen außerhalb von `app-root`.
- Eingebaut in alle drei Brett-Komponenten (`PuzzleBoardComponent`, `AnalysisBoardComponent`, `pgn-viewer/ChessBoardComponent`) → jedes Brett der App hat es. Ins Vollbild geht eine ÄUSSERE Hülle (`.board-fs-host`/`.ab-fs-host`/`.cb-fs-host`): deren Größe erzwingt der Browser auf 100 % × 100 % (UA-`!important` schlägt Author-`!important` — dem Vollbild-Element eine eigene Größe zu geben ist zwecklos; Regression 0.322.0: Brett füllte die Breite, lief unten raus). Das Brett wird darin per Flex zentriert als `min(100vw,100vh)`-Quadrat, drumherum schwarze Balken. Der Brett-Wrapper bleibt exakt die Brettfläche — nur so rechnen die absolut positionierten Auflagen (Umwandlungs-Auswahl, Viz-Ring) weiter gegen das Brett.
- Die Brett-Pixelgröße zieht der jeweilige `ResizeObserver` nach; chessground braucht danach `redrawAll()` (Figuren stehen auf Pixel-Transforms).
- `fullscreen-overlay.service.ts` — hängt den CDK-Overlay-Container während des Vollbilds INS Vollbild-Element (app-weit in `AppComponent` instanziiert). Ohne das sind Dialog/Snackbar/Menü/Tooltip im Vollbild unsichtbar; ein modaler Dialog mit `disableClose` (Lösezeit-Nachfrage) hing die App fest. Beim Verlassen — und wenn das Vollbild-Element zerstört wurde — wandert der Container zurück ans `<body>`. **Das funktioniert nur mit KLASSISCHEN Overlays** (0.478.3, `provideFullscreenSafeOverlays()` in `app.config.ts` setzt `usePopover: false`): die CDK öffnet Overlays sonst als Popover in der obersten Browser-Ebene, und ein offenes Popover schließt der Browser beim Umhängen still (Dialog unsichtbar, blockiert aber weiter); in Chromium liegt ein vor dem App-Vollbild geöffnetes Popover zudem unter der Seite. Beides in Chromium und Firefox nachgestellt.
- `isElementFullscreen()` (`fullscreen.util.ts`) unterscheidet Brett- von App-Vollbild — beide setzen `document.fullscreenElement` (App-Vollbild = `<html>`). Wer Verhalten daran knüpft, dass Teile der Oberfläche unsichtbar sind, fragt DIESE Funktion, nicht `!!document.fullscreenElement` (so sprang der Buch-Solver im App-Vollbild trotz Abschlusstext weiter).
- `board-fs-actions.component.ts` (features/puzzles) — kleine Icon-Leiste im schwarzen Balken (Tipp/Zurücksetzen/Mausrutscher/Aufgeben) für alle drei Solver, per `<ng-content>` in die Vollbild-Hülle projiziert und über `data-fs-only` nur dort sichtbar.
- Nicht enthalten: der Stellungs-Editor (`analysis/position-setup.component.ts`) — dort baut die Palette neben dem Brett den Nutzen zunichte.

## Offline / PWA (Service Worker)

- **Service Worker**: `@angular/service-worker` (ngsw), Konfig in `ngsw-config.json`, registriert in `app.config.ts` via `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() })` — **nur im Prod-Build aktiv** (`serviceWorker: "ngsw-config.json"` steht in der `production`-Configuration der `angular.json`). Cacht App-Shell, **alle Lazy-Chunks** (assetGroup `app`, prefetch → Routen wie `/puzzles`, `/endless` öffnen offline) + i18n (prefetch) + Google-Fonts (dataGroup, performance). `/api/*` wird **nicht** vom SW gecacht (immer Netz → App fällt offline auf lokale Caches/Queue zurück). **Stockfish (`/assets/stockfish/**`, ~7 MB `.wasm`) ist als eigene assetGroup `engine` (installMode `prefetch`) im SW** → Engine/Analyse/Eval funktionieren offline. Das `.wasm` wird vom dedizierten Worker per Subresource-Fetch geladen (geht über den SW-Cache); falls `WebAssembly.instantiateStreaming` an einem cache-servierten Response scheitert, fällt das Glue auf `WebAssembly.instantiate(arrayBuffer)` zurück (kein Hänger). Hinweis: Der frühere „Berechne…"-Hänger lag NICHT am SW, sondern am UCI-Sequencing in `AnalysisEngineService.analyze` (stop→isready→readyok→position+go; seit 0.64.2 behoben). Hash ist auf 16 MB begrenzt (OOM-Schutz). nginx: SW-Steuerdateien `no-cache`, CSP `connect-src` enthält die Font-Origins (SW-Caching).
- **`core/local-json-store.ts`** (seit 0.499.1): der Rumpf ALLER geräte-lokalen Speicher — `readJson`/`writeJson` (kaputtes JSON → `null`, Quota/gesperrt → `false`), `readRaw`/`writeRaw`/`hasKey`/`removeKey`/`keysWithPrefix`, dazu `BoundedMapStore` für „höchstens N Einträge, älteste raus" (Verdrängungs-Reihenfolge als Parameter: Datums-Schlüssel lexikografisch, Lösezeiten nach Schreibzeitpunkt). Den Speicher selbst holt man über `localStore()`/`sessionStore()` — schon der ZUGRIFF auf `localStorage` kann werfen (Chrome mit blockierten Cookies). Darauf setzen `book-offline.util.ts`, `repertoire-offline.util.ts`, `calc/calc-local.util.ts`, `solve-elapsed.util.ts`, `daily-elapsed.util.ts` und `last-solved-store.ts` (sessionStorage) auf; in ihnen bleibt nur das Fachliche. **Ein fehlgeschlagener Schreibversuch wird GEMELDET** (Rückgabewert auswerten) — „gespeichert" anzuzeigen, während nichts liegt, ist der Fehler, den die Kalkulations-Regel weiter unten meint. `features/puzzles/endless-storage.service.ts` steht bewusst daneben (eigene Semantik).
- **Offline-Daten-Caches** (`core/offline.service.ts`, localStorage, pro Gerät, im Profil einstellbar): Standard-Puzzle-Pool (`PUZZLE_POOL_KEY`), Endless-Run-Pool (`ENDLESS_POOL_KEY`), ganze Bücher (`BOOK_OFFLINE_PREFIX`, `features/puzzles/book-offline.util.ts`), Kurslisten-Snapshot (`COURSES_CACHE_KEY`; /courses zeigt offline die heruntergeladenen Kurse) und **heruntergeladene Repertoires** (`REPERTOIRE_OFFLINE_PREFIX`, `features/repertoire/repertoire-offline.util.ts`: PGN + SR-Linien-Zustände + effektive Intervalle; Download-Toggle auf der Repertoire-Karte). Pools werden **online beim Modus-Eintritt** vorab geladen, damit ein Offline-Start Daten hat. Der **Repertoire-Trainer** fällt bei nicht erreichbarem Server auf die Offline-Kopie zurück: SR-Bewertungen werden lokal berechnet (`applySrReview`/`applyPromote` in `repertoire-sr.util.ts` — Spiegel der Backend-`ScheduleLevel`-Logik, Intervall-Defaults MÜSSEN synchron bleiben), in die Kopie gespiegelt und via Offline-Queue nachgereicht.
- **Offline-Lösungs-Queue** (`core/offline-queue.service.ts`): schreibende Solve-Requests (Standard-/Tagespuzzle-Attempt, Kurs-Result, Endless-Session), die offline scheitern, werden als rohe `{method,url,body}` in localStorage (`rookhub_offline_queue`) vorgemerkt und bei `window 'online'` + App-Start über den `HttpClient` (inkl. authInterceptor) erneut gesendet — Eintrag erst nach Erfolg entfernt, 4xx verworfen, Netz/5xx bleibt liegen. App-weit instanziiert in `AppComponent`.
- **Zwei Offline-Lagen, und beide müssen tragen** (0.478.4, mit Playwright gegen einen lokalen Prod-Build MIT Service Worker nachgestellt): (A) Gerät offline (`navigator.onLine === false`) und (B) Gerät gilt als online, der Server antwortet aber nicht (Funkloch, VPN, Server weg) — dort liefert der ngsw für jede `/api`-Anfrage eine synthetische **504**, nie Status 0. Wer einen Offline-Rückfall nur an `!navigator.onLine` hängt, deckt (B) NICHT ab; der Rückfall gehört zusätzlich in den `error`-Zweig (so jetzt Endless: `startFromOfflinePool()` auch beim gescheiterten Ketten-Abruf). Der `retryInterceptor` wiederholt bei (A) nicht mehr — die Backoffs (500 ms + 1 s + 2 s) hielten die Offline-Ansichten sonst 7–8 s auf dem Spinner. **Testfalle**: Playwrights `context.setOffline(true)` setzt `navigator.onLine` bei aktivem Service Worker NICHT auf `false` (gemessen) — Lage (A) nur per `addInitScript` nachstellbar. Und der SW registriert sich erst nach `registerWhenStable:30000`; jedes Neuladen davor startet die 30 s neu.
- **Verbindungs-Banner** (`core/connectivity.service.ts`): „Server nicht erreichbar" erscheint erst, wenn das Problem `SHOW_DELAY_MS` (5 s) anhält UND die Gegenprobe (`/api/menu`) wirklich gescheitert ist — eine noch laufende Gegenprobe entscheidet selbst; sichtbar bleibt der Banner mindestens `MIN_VISIBLE_MS` (4 s). Vorher (2,5 s, sofortiges Ausblenden) blinkte er bei einem trägen Server rein und raus.

## Auth-Flow

1. Login/Register -> `AuthService.login()` / `.register()` -> POST an `/api/auth/*`
2. Response enthaelt JWT -> wird in `localStorage` als `rookhub_user` gespeichert
3. `authInterceptor` haengt `Authorization: Bearer <token>` an alle `/api`-Requests. Er loggt NUR aus, wenn der
   Server das Token ablehnt (`WWW-Authenticate: Bearer error="invalid_token"`, siehe `isTokenRejection`) — ein
   Controller-401 wie „aktuelles Passwort falsch" beendet die Sitzung nicht (Regression 2026-09-09)
4. `authGuard` prueft `AuthService.isLoggedIn` -> redirect zu `/login?returnUrl=…` wenn nicht eingeloggt;
   `guestGuard` ist das Gegenstueck auf `/login`/`/register`: Angemeldete landen auf `returnUrl` bzw. `/`
   (`core/return-url.util.ts` sichert das Ziel gegen offene Weiterleitungen, EINE Fassung fuer Maske,
   Registrierung und Guard). `?switch=1` ist die bewusste Tuer zur Maske fuer einen Konto-Wechsel ohne
   vorheriges Abmelden. Gilt ebenso in der Turnierseite (`src-turnier`, Import ueber `@rh/*`)
4a. Speichern der Sitzung (`AuthService.persistSession`): ist der localStorage voll, werden die Offline-Caches
   geraeumt und erneut geschrieben; scheitert auch das, sagt eine Snackbar, dass die Anmeldung nur bis zum
   Neuladen haelt (`storageFull`, Kibana-Event `ClientLog storage_full`). Vorher blieb das stumm
5. `AuthService.currentUser$` (BehaviorSubject) fuer reaktive UI-Updates (Navbar etc.)

## Routing

| Route | Component | Auth |
|-------|-----------|------|
| `/login` | LoginComponent | nein |
| `/register` | RegisterComponent | nein |
| `/dashboard` | DashboardComponent | ja |
| `/profile` | ProfileComponent | ja |
| `/friends` | FriendsComponent | ja |
| `/repertoires` | RepertoireListComponent | `authGuard` (alle eingeloggten Nutzer; API ist pro-Nutzer abgesichert) |
| `/repertoires/:id` | RepertoireDetailComponent | `authGuard` (alle eingeloggten Nutzer) |
| `/tournaments` | TournamentListComponent | ja |
| `/tournaments/:id` | TournamentDetailComponent | ja |
| `/weekly` | WeeklyListComponent | `adminGuard` (vorerst nur Admin; Lese-API bleibt offen) |
| `/analysis/jobs` | AnalysisJobsComponent (Hintergrund-Analyseaufträge: Liste + gespeicherte Linien + Tiefe/Linien anpassen; steht VOR `/analysis`) | `authGuard` |
| `/reconstruct` | ReconstructListComponent („Partie rekonstruieren": Liste anlegen/öffnen/löschen) | `authGuard` + `menuGuard('reconstruct')` |
| `/games/:id` | SharedGameComponent im Modus `own` (`data.mode`): eigene gespeicherte Partie als Seite — Brett, Zugliste, Bewertungskurve, Analysieren, Teilen-Link; seit 0.513.0 statt des PGN-Viewer-Dialogs | `authGuard` + `menuGuard('games')` |
| `/reconstruct/:id` | ReconstructDetailComponent (Arbeitsplatz: Teile links, Brett rechts; Zugfolgen werden lokal mit chess.js mitgespielt, geprüft wird serverseitig) | `authGuard` + `menuGuard('reconstruct')` |
| `/analysis` | AnalysisComponent | nein (öffentlich; Stockfish-MultiPV-Analyse — lokal per WASM, eingeloggt wahlweise über eine externe Engine des eigenen Lichess-Kontos, siehe „Externe Engine" im Haupt-CLAUDE.md) |
| `/install` | InstallComponent | nein (öffentlich; APK-Download + PWA-Install, plattformabhängig via `PwaInstallService`) |
| `/stats` | StatsComponent | ja (Puzzle-Elo-Kurve + Stats; `GET /api/puzzles/elo-history`) |
| `/training-goals` | TrainingGoalsComponent | `authGuard` (Tagesziele setzen, Heute-Fortschritt + Ziele-Heatmap; `/api/training-goals/*`) |
| `/weekly/:weeklyId` | BookPuzzleComponent (Wochenpost-Modus) | `adminGuard` (vorerst nur Admin) |
| `/courses` | CourseListComponent | `courseAccessGuard` (Admin oder Gruppe mit Buch-Freigabe) |
| `/courses/:bookId` | CourseDetailComponent (Detailseite: Metadaten + Kapitel-Verwaltung, Stellungen per Memo einfügen) | `courseAccessGuard` |
| `/courses/:bookId/flashcards` | FlashcardsComponent (Lern-/Druckansicht: AUSGANGSSTELLUNG vorn [Aufgabe], Lösung+Abschlusstext hinten; `?lines=<ids>`/`?chapter=`; MUSS vor der `:mode`-Route stehen) | `courseAccessGuard` |
| `/repertoires/:id/flashcards` | FlashcardsComponent (UMGEKEHRT: Endstellung+[%cal]-Pfeile vorn, Linie+Abschlusstext hinten; `?lines=<lineKeys>`) | `authGuard` |
| `/courses/:bookId/calc` | CalculationComponent (Kalkulations-Modus; MUSS vor der `:mode`-Route stehen) | `coursePlayGuard` |
| `/courses/:bookId/:mode` | BookPuzzleComponent (Kursmodus) | `courseAccessGuard` |
| `/` | redirect -> `/dashboard` | - |
| `**` | redirect -> `/dashboard` | - |

## Verzeichnisstruktur

```
app/src/app/
  core/
    auth.service.ts          JWT-Management, Login/Register/Logout, localStorage
    auth.guard.ts            CanActivateFn, redirect zu /login
    auth.interceptor.ts      HttpInterceptorFn, Bearer-Token an Requests
  features/
    auth/
      login.component.ts     Login-Formular (username + password)
      register.component.ts  Registrierung (username + email + password)
    dashboard/
      dashboard.component.ts Uebersicht: Repertoire-Count, Subscription-Count, Freunde-Count, Abo-Liste
    profile/
      profile.component.ts   Profil bearbeiten (DisplayName, FideId, ChessResults, Chess.com, Lichess)
    friends/
      friends.component.ts   Freundesliste + Requests (Tabs), User-Suche, Request senden/akzeptieren/ablehnen
    repertoire/
      repertoire-list.component.ts          Liste aller Repertoires, Create-Dialog
      repertoire-detail.component.ts        Dateien-Liste, Drag&Drop Upload, Download, Delete
      create-repertoire-dialog.component.ts MatDialog: Name, Description, IsPublic
    tournaments/
      tournament-list.component.ts          Turnierliste vom Crawler, Subscribe-Button
      tournament-detail.component.ts        Tabs: Players (Table), Teams (Table), Pairings (Table + Round-Select)
  shared/
    navbar/
      navbar.component.ts   Material Toolbar, Navigation, User-Menu mit Logout
    loading-spinner/
      loading-spinner.component.ts  Zentrierter MatSpinner
    lines/
      chapter-groups.util.ts  groupByChapter: nach Kapitel gruppieren in Vorkommens-Reihenfolge
      mark-set.ts             MarkSet: Flashcard-Markierungen, optimistisch + Rollback
    chess/
      line-solver.ts          LineSolver + resolveExpectedUci/sameMove/judgeMove: „ist das der
                              erwartete Zug?" — EINE Regel fuer alle Loeser
```

- **`shared/lines/`** (seit 0.499.2): was sich die beiden LINIEN-LISTEN teilen — die Kurs-Durchsicht
  (`features/courses/course-browse.component.ts`) und die Repertoire-Linienliste
  (`features/repertoire/repertoire-lines.component.ts`). `groupByChapter(items, keyOf)` gruppiert in
  der Reihenfolge des ERSTEN Auftretens (= Lesereihenfolge; der Kapitel-SCHLÜSSEL ist generisch,
  weil „ohne Kapitel" im Kurs `null` und im Repertoire `''` heißt), `MarkSet<K>` hält die
  Flashcard-Markierungen und schaltet eine um: sofort in der Liste, Rollback bei Serverfehler. Die
  Klasse verhält sich nach außen wie ein `Set` (`has`/`size`/iterierbar), damit die Vorlagen
  unverändert `marked.has(…)`/`marked.size` benutzen.

- **`shared/chess/line-solver.ts`** (seit 0.499.8): der gemeinsame Kern der Löser — die Antwort auf
  „ist das der erwartete Zug?". Er legt die Semantik fest, und zwar auf **FELDER**: ein erwarteter
  Zug steht als `{ uci }` ODER `{ san }` da, wird über `resolveExpectedUci` auf der AKTUELLEN
  Stellung zu von/nach/Umwandlung aufgelöst (SAN per `chess.move(san)` auf einer KOPIE), und danach
  ist die SAN nur noch Anzeige — `Nbd2` = `Nb1d2` = `b1d2`, `0-0` = `O-O`, Suffixe `+#!?` zählen
  nicht. Verglichen wird mit `sameMove` nach der Präfix-Regel der Puzzles: **fehlt im Nutzerzug die
  Umwandlungsfigur, passt jede; steht eine ANDERE als erwartet, ist es falsch.** `judgeMove` urteilt
  über einen Nutzerzug OHNE ihn anzuwenden (`correct`/`alternative`/`wrong`/`illegal`/`not-your-turn`);
  angewendet wird getrennt. Zwei Fälle sind bewusst entschieden und mit Vektoren festgenagelt:
  **mehrdeutige SAN** (`Nd2`, wenn zwei Springer dorthin können) löst sich zu `null` auf statt zu
  einer Vermutung — für `judgeMove` heißt das, dass dann KEIN Zug `correct` sein kann, und wer das
  unterscheiden muss, fragt `resolveExpectedUci`/`expectedUci()` vorher; **lang-algebraische**
  Schreibweise braucht keine eigene Vorreinigung, weil chess.js sie in seiner (nicht-strikten)
  Vorgabe schon annimmt (an 1.4.0 nachgemessen, samt `0-0` und `e8Q`).
  Der Kern ist **synchron, ohne Timer, ohne Engine, ohne Angular** — Phasen, Verzögerungen, viz,
  Tipps und Eval bleiben bei den Aufrufern. Benutzbar auf zwei Arten: als Klasse `LineSolver`
  (eigenes Brett + Halbzug-Zählstand: `ply`/`expected`/`done`/`userToMove`/`expectedUci()`/`judge()`/
  `playExpected()`/`playFree()`/`opponentReply()`/`undo(n)`/`dests()`/`lastMove()`/`reset()`; `chess`
  ist ausdrücklich LESBAR) oder als reine Funktionen auf einer FREMDEN chess.js-Instanz.
- **Wer ihn benutzt**: `features/worksheets/worksheet-solve.component.ts`
  (`WorksheetTask` ist seither eine dünne Hülle um `LineSolver` — sie urteilt bewusst OHNE die
  Umwandlungsfigur des Dialogs, weil auf dem Blatt die Lösung die Figur setzt),
  `features/puzzles/base-puzzle-solver.ts` (seit 0.499.9: `onMoveMade` holt sein Urteil von
  `judgeMove` auf `this.chess`) und `features/repertoire/repertoire-trainer.component.ts` (seit
  0.499.10, siehe unten). Die letzten beiden benutzen die reinen FUNKTIONEN, nicht die Klasse: beide
  führen ihr Brett schon selbst und WEISEN es neu zu (`reviewGoToCore` im Puzzle-Löser,
  `startCurrentLine`/`showSolution` im Trainer) — ein zweites Brett daneben wäre die Quelle fürs
  Auseinanderlaufen. **Bewusst NICHT im Kern und weiterhin in Base**: moveLog samt `thinkMs`,
  `wrongMoveCount`, Off-Path-Zählung und -Warnung, der `ALT_HOLD_MS`-Timer des Alternativzugs, die
  Timer von `advanceAfterCorrectMove`, die Stockfish-Antwort (`opponentRespond`), `handleGameOver`,
  `mouseslip`, viz/Tipps/Eval. Der Kalkulations-Modus ist **kein Löser** und bleibt außen vor.
- **Repertoire-Trainer am Kern** (0.499.10): `onMove`, `onLearnMove` und `showSolution` urteilen
  über `judgeMove`/`resolveExpectedUci` statt über `normSan`-Textvergleich. Geurteilt wird gegen
  `this.fen` (NICHT `this.chess`): im Wiederhol-Fall nach einem Fehlzug ist `this.fen` bereits auf
  die Ausgangsstellung des Halbzugs zurückgesetzt. `altsAt(cardKey)` bringt die `[%alt]` des
  Repertoire-Graphs in die Form des Kerns und siebt den Hauptzug NICHT mehr aus — die alte Zeile
  `accepted.delete(expectedSan)` steckt im Kern, der `expected` zuerst prüft. Der Lern-Modus gibt
  bewusst eine LEERE Alternativen-Liste mit (er duldet keine). Unangetastet: `pendingWrong`/
  Mausrutscher/Streak, `lineHadWrong` als Urteil über die LINIE, der Eval-Vergleich und die
  Anzeige (`expectedDisplay`, `movesInLine`, `currentMovePrettyLabel`) — dort ist SAN Text für
  Menschen. `normSan` bleibt bestehen (es speist `lineKeyFromSans`, den handgespiegelten Gegenpart
  zu `ChessableTrainedLineService.LineKeyFromSans`) und hat im Trainer keine Verwendung mehr.
  **Gemessen und wichtig für die Erwartung**: `parsePgnText` liefert die Linie als
  chess.js-`Move`-Objekte (`loadPgn` → `history({verbose:true})`), deren `san` IMMER kanonisch ist —
  `Nbd2` im PGN kommt als `Nd2` an, `e2e4` als `e4`, `0-0` als `O-O`. Der Textvergleich war damit
  faktisch schon ein Feldvergleich; die EINE sichtbare Folge des Umbaus ist, dass eine Umwandlung
  ohne genannte Figur jetzt zählt und die Figur der LINIE aufs Brett kommt. Der Rest ist
  Robustheit: ein Tag, an dem die Linie nicht mehr über ein Brett kanonisiert ankommt, kostet
  kein „falsch" mehr. Tests, die das trennen, schreiben die SAN der Linie deshalb absichtlich auf
  eine nicht kanonische Form um — ein Test, der nur ein PGN hineingibt, prüft den PARSER.

## Partie-Rückblick: Bewertungskurve, Genauigkeit, Zug-Klassen (0.512.0, Sonderklassen 0.514.0)

Unter dem Brett der geteilten Partie (`/g/:token`) und der eigenen Partie-Seite (`/games/:id`, seit 0.513.0 statt des Nachspiel-Dialogs; der Dialog `PgnViewerComponent` kann es weiterhin) —
Daten AUSSCHLIESSLICH aus RookHubs eigener Partie-Analyse (`GET /api/games/{id}/evals`,
`…/shared/{token}/evals`, alles in WEISS-Sicht; siehe „Gespeicherte Partien" im Haupt-CLAUDE.md).

- `features/games/game-review.util.ts` — reine Funktionen, Vektoren-Spec mit literalen Zahlen:
  - `winPercent`: `50 + 50 · (2 / (1 + e^(−0,00368208 · cp)) − 1)` (Lichess, lila `WinPercent`);
    Matt = 100/0 (Lichess nimmt ±1000 cp ≈ 97,5 % — hier bewusst der Rand), `mate 0` über die FEN.
  - `moveAccuracy`: `103,1668 · e^(−0,04354 · (vorher − nachher)) − 3,1669`, auf 0..100, kein Verlust = 100
    (Lichess, https://lichess.org/page/accuracy). Aus Sicht des ZIEHENDEN (Schwarz: 100 − Weiß).
  - `volatilityWeights` + `sideAccuracy`: Fenster `clamp(⌊Halbzüge/10⌋, 2, 8)`, die ersten (Breite − 2)
    Züge mit dem ersten Fenster, Gewicht = Standardabweichung (Grundgesamtheit) der Gewinnchance auf 0,5..12;
    Ergebnis = Mittel aus gewichtetem und harmonischem Mittel (lila `AccuracyPercent.gameAccuracy`).
    Lücken fallen aus ihrem Fenster, statt als 0 % zu zählen.
  - `classify`: chess.com-Bänder („How are moves classified?") in Prozentpunkten — best (Engine-Zug oder
    kein Verlust), ≤ 2 excellent, ≤ 5 good, ≤ 10 inaccuracy, ≤ 20 mistake, sonst blunder; Grenze gehört zur
    BESSEREN Klasse. Prozentpunkte statt 0,02 usw., weil Gleitkomma die Grenzwerte sonst verschiebt.
  - `reviewGame(evals, fens, ucis?)`: die Seite am Zug kommt aus der FEN, nie aus der Parität. Bewertbar ist
    ein Zug nur mit gerechneter Stellung davor; „danach" = nächste Stellung (tiefer), sonst der gespielte
    Kandidat. `ucis` (je Halbzug `von+nach+Umwandlung`) schaltet die Sonderklassen ein; ohne sie bleibt es
    bei der Grundklasse.
  - **Sonderklassen (0.514.0, `specialClass`)** — ETIKETTEN über der Grundklasse, Genauigkeit und Kurve
    ändern sich nicht. Bedingungen aus der chess.com-Hilfe „How are moves classified?", die ZAHLEN für
    Brilliant/Great aus WintrCat/freechess (`src/lib/analysis.ts`, `board.ts`; chess.com legt sie nicht offen),
    die für Miss gesetzt (freechess kennt kein Miss). Alles aus Sicht des
    Ziehenden; Bewertungen werden dafür in Centipawns verglichen, Matt in n = ±(1000 − n) Bauern wie
    `GuessScoring.Pawns` (ganzzahlig — „Abstand ≥ 1,5" als Kommazahl-Differenz kippte an der Grenze).
    „Fehler des Gegners" heißt: GRUNDklasse des vorigen Halbzugs mistake/blunder (auch wenn er selbst ein
    Miss wurde). Reihenfolge:
    1. **Miss** ersetzt inaccuracy/mistake/blunder: Gegner hat davor gepatzt, Gewinnchance vorher (= die des
       Bestzugs) ≥ `MISS_BEST_WIN` 70, danach ≤ `MISS_AFTER_WIN` 60 und ≥ `MISS_MIN_AFTER_WIN` 40 — von +5 auf
       −5 bleibt ein grober Fehler, das Etikett darf die Katastrophe nicht verdecken.
    2. **Brilliant** ersetzt best/excellent: `sacrificedPiece` findet ein Opfer, der Zweitbeste liegt unter
       `BRILLIANT_WINNING_ANYWAY_PAWNS` +7 und ist kein Matt für den Ziehenden (fehlt er: kein Ausschluss),
       danach ≥ `BRILLIANT_MIN_AFTER_PAWNS` −1, keine Umwandlung, vorher nicht im Schach.
    3. **Great** ersetzt best (nie zusammen mit Brilliant): zweiter Kandidat da, Abstand ≥
       `GREAT_MIN_GAP_PAWNS` 1,5, der Zweitbeste gewinnt NICHT ohnehin (< +7 und kein Matt — dieselbe Grenze wie
       bei Brilliant; „#2 gegen #4" ist kein einziger guter Zug), Gegner hat davor gepatzt, danach ≥
       `GREAT_MIN_AFTER_WIN` 45 %, und KEIN Opfer (`sacrificedPiece` = null) — ein Zug mit hängender Figur ist
       Brilliants Sache. Erster Zug: kein Great.
    Fehlt eine Zutat (Lücke davor, kein zweiter Kandidat, keine UCI), entfällt die Sonderklasse. Book gibt es
    nicht (kein Eröffnungsbuch im Client). `MOVE_CLASS_COLORS` ist die EINE Farbtabelle (chess.com-nah) für
    Zähler, Abzeichen und Kurven-Punkte.
- `features/games/move-tactics.util.ts` — „hängt eine Figur?": die ANGREIFER sind die LEGALEN Schlagzüge des
  Gegners in der Stellung danach (`legalCapturersOf` über `moves({verbose})` — nach einem Abzugsschach darf
  der Bauer nicht schlagen, ein gefesselter Läufer auch nicht, der König nimmt keine gedeckte Dame; mit
  Pseudo-Angriffen „hing" jede dieser Figuren und der Zug wäre ein falsches Brilliant), die VERTEIDIGER
  Pseudo-Angriffe über chess.js `attackers(square, color)` (chess.js 1.4: ohne Legalität, ohne Röntgen; der
  König zählt mit, Wert ∞) — der Ziehende ist danach nicht am Zug, für ihn gibt es keine legalen Züge.
  `isPieceHanging(vorher, nachher, feld)` nach freechess: (a) vorher stand dort eine gegnerische Figur ≥ Wert
  → nein; (b) Turm schlug eine Leichtfigur und genau EIN Angreifer, eine Leichtfigur → nein; (c) ein
  billigerer Angreifer → ja; (d) mehr Angreifer als Verteidiger → ja, außer (Figur billiger als jeder
  Angreifer UND ein Verteidiger billiger als der billigste Angreifer) oder ein Bauer deckt; (e) sonst nein.
  `sacrificedPiece` = die TEUERSTE eigene Figur (N/B/R/Q, auch eine, die nicht gezogen hat — Legall), die hängt
  und bei der der Gegner im ABTAUSCH mehr gewinnt als das Geschlagene (`exchangeGain`, SEE ohne Röntgen; en
  passant = 1). **Seit 0.518.1 zwei Korrekturen**, beide an der Prod-Partie MYXN3hXqz1X2hm7Cx6V47Q gefunden
  (chess.com: kein Brilliant): (1) der Abtausch statt des vollen Figurenwerts — 22…gxf4 ließ einen von der
  Dame GEDECKTEN Turm vor einem Läufer stehen (Verlust die Qualität, 2 < geschlagener Läufer 3); (2) freechess'
  Schlag-Simulation (`capturable`) ist jetzt drin: kostet das Schlagen den Schläger eine Figur mindestens vom
  Wert des Opfers (Abzugsangriff — 23.Lxd5 öffnet die b-Linie, exd5 verliert die Db8) oder erlaubt es bei
  einem Opfer unter Turmwert Matt in einem, ist es ein Köder. Die frühere Begründung „wir haben die Engine —
  ist der Zug best, ist das Opfer korrekt" war falsch: die Engine sagt „bester Zug", nicht „Opfer".
  Unlesbare FEN → kein Opfer, nie ein Wurf (läuft in
  einem `computed`). Die Erklärung (geopferte Figur, Abstand, Verpasstes) steht als TEXT neben dem Abzeichen,
  nicht als Tooltip — am Handy gibt es kein Hover.
- `shared/pgn-viewer/eval-graph.component.ts` — reines SVG, KEINE `clipPath`/`url(#…)` (mit `<base href>`
  finden Firefox/Safari die Verweise nicht), Flächen als eigene Polygone; feste Figurenfarben statt
  `currentColor` (sonst kehrt sich Weiß/Schwarz im Dunkelmodus um); Punkte als HTML (Kreise in einer
  verzerrten SVG wären Ellipsen) — die Farbe bringt die Marke mit (`EvalGraphMark.color`), die Kurve kennt
  keine Zug-Klassen. Punkte haben brilliant, great, miss, mistake, blunder. Klick → Halbzug-Index wie
  `currentMoveIndex` (−1 = Start).
- `features/games/game-review.component.ts` — lädt, fragt alle 10 s nach, SOLANGE `pending`/`running`
  (nicht bei `none`/`done`/`failed`, nicht nach dem Schließen), zeigt bei `none` nichts; `statusChange` sagt
  der Seite, wann „Partie analysieren" gesperrt (läuft) oder ausgeblendet (fertig) wird, `reload()` nach dem
  Einwurf. Die Seiten halten Knopf-Zustand und Status in SIGNALEN (Angular 22 zeichnet sonst nach der
  HTTP-Antwort nicht neu). Im Dialog macht das Brett der Kurve Platz (`.with-review`, 90-vh-Dialog).
  Die Seiten reichen `game.moves` (chess.js `Move`) als `[moves]` herein — daraus werden die UCIs für die
  Sonderklassen. Das Abzeichen des aktuellen Zugs erklärt Brilliant (geopferte Figur + Feld), Great (Abstand
  zum Zweitbesten; mit Matt im Spiel ohne Zahl) und Miss per Tooltip. „!" gehört Great, Excellent trägt 👍.
  Neben dem Fortschritt steht seit 0.517.0 die Restdauer (`etaMinutes` vom Server, geschrieben mit
  `shared/eta.util.ts` `formatEta` — derselben Funktion wie auf „Partie-Analysen", Text-Key `gameAnalysis.eta`);
  nur solange die Analyse läuft, ohne Tempo gar nicht.

## Eigene Fehler nachspielen (0.516.0)

Der Trainer zur Partie, wie Lichess' „Aus deinen Fehlern lernen": Stellung VOR dem eigenen Fehler,
der gespielte Zug steht daneben, gesucht ist der bessere. Knopf in der Kopfzeile von `/games/:id`
und `/g/:token`, sobald es Aufgaben gibt. **Gespielt wird seit 0.519.0 auf dem BRETT DER SEITE**
(bis dahin ein Dialog mit eigenem, kleinerem Brett — gewünscht 2026-09-24): den Zustand hält
`features/games/mistakes-session.ts` (reine Klasse mit Signalen), die Seite bindet ihr Brett daran
(`boardFen`/`lastMove`/`flipped`/`playable`, Züge über `onTrainingMove`), und unter dem Brett steht statt
der Navigationsleiste die Trainer-Leiste (`mistakes-trainer.component.ts`, Eingabe `session`, Ausgabe
`closed`). Drei Dinge, die dabei nicht kippen dürfen: die Tippzonen neben dem Brett fallen im Training
weg (sie lägen sonst über dem Brett und schluckten jeden Zug), die Pfeiltasten blättern nicht in der
Partie darunter, und Zugliste/Kurve springen je Aufgabe auf die Stellung VOR dem Fehler — nach dem
Beenden steht man dort.
- **Gleichwertige Züge zählen** (0.519.0, `acceptedMoves` in `mistakes.util.ts`): der Bestzug UND jeder
  Kandidat derselben Suche, der höchstens `EQUIVALENT_LIMIT` (= `CLASS_LIMITS.excellent`, 2 Punkte
  Gewinnchance) dahinter liegt — die Grenze, bis zu der der Rückblick einen Zug „Exzellent" nennt. Dafür
  liefert `GameEvalPlyDto.Candidates` seit 0.519.0 alle (bis zu fünf) Kandidaten in Weiß-Sicht.
- **Ein Zug AUSSERHALB der Kandidaten** (0.520.0): liegt schon der SCHWÄCHSTE Kandidat innerhalb der
  Grenze (`unlistedMayBeEquivalent` → `Mistake.checkUnlisted`), kann auch ein nicht gelisteter Zug
  gleichwertig sein — dann rechnet die Browser-Engine nach (`mistake-judge.service.ts`, Phase `checking`,
  Brett gesperrt). Sonst ist er schlechter als der fünfte und sicher daneben, gerechnet wird nichts.
  Verglichen wird Browser gegen Browser (`isEquivalentAfter`): Bestzug der Analyse UND eigener Zug in
  derselben Tiefe (`MistakeJudgeService.DEPTH` = 16) — gegen die Zahl der tieferen Server-Analyse zu
  messen hieße, eine flache mit einer tiefen Suche zu vergleichen. Matt/Remis nach dem Zug stehen ohne
  Engine fest (sie hätte dort keinen Zug). Ein Urteil, das nach einem Aufgabenwechsel eintrifft, verwirft
  die Session (`epoch`); kann die Engine nicht prüfen (`null`), gilt der Zug als daneben und die Leiste
  sagt es. `StockfishResult.score` liefert dafür die Bewertung als Zahl (Weiß-Sicht).

- **Die Auswahl ist rein und getestet** (`features/games/mistakes.util.ts`): `collectMistakes` nimmt
  die Ungenauigkeiten, Fehler und groben Fehler BEIDER Seiten in Partie-Reihenfolge, je mit
  Ausgangsstellung, gespieltem Zug und dem Bestzug der Engine (`GameEvalPly.bestUci`) als SAN.
- **Entschieden wird über die GRUNDklasse** (`ReviewedMove.base`, neu in 0.516.0), nicht über `cls`:
  ein als **Miss** ausgewiesener Zug ist ein Fehler, dem zusätzlich eine Gelegenheit entgangen ist —
  Lichess kennt dieses Etikett gar nicht, und ohne die Unterscheidung fiele genau der Zug aus dem
  Training, der auf den Patzer des Gegners folgte. Dasselbe gilt für Brilliant/Great, die nie zu den
  drei Stufen gehören.
- **Übersprungen wird, was sich nicht abfragen lässt**: kein Bestzug (Lücke in der Analyse), Bestzug
  = gespielter Zug, und ein Bestzug, der in der Stellung gar nicht geht. Eine unspielbare „Lösung"
  vorzuführen wäre schlimmer als eine ausgelassene Aufgabe.
- **Wer wird trainiert**: `SharedGameDetail.ownerSide`; fehlt die Zuordnung (fremde geteilte Partie),
  die Seite mit den meisten Fehlern (`sideWithMoreMistakes`) — EINE Regel, `trainingSide`. Haben beide
  Seiten welche, schaltet der Dialog um — Brett dreht mit, Zähler beginnt von vorn. **Der Knopf zählt NUR
  diese Seite** (0.517.1): er zählte beide zusammen, der Dialog öffnete aber auf der des Besitzers — mit
  einem einzigen Fehler des Gegners stand „(1)" am Knopf, und der Dialog hatte nichts abzufragen (und ohne
  Fehler auf der zweiten Seite auch keinen Umschalter).
- **Die Aufgaben kommen aus dem Rückblick**, nicht aus einem zweiten Abruf: `GameReviewComponent`
  hat die Analyse ohnehin und meldet sie über die Ausgabe `mistakesChange`.
- **Geurteilt wird mit `sameMove`** aus `shared/chess/line-solver` — der gemeinsame Kern. Das Brett
  wandelt ohne Rückfrage in eine Dame um, und die Regel dort lässt eine fehlende Umwandlungsfigur
  gelten; eine Unterverwandlung als Lösung ließe sich dort ohnehin nicht eingeben. Als richtig zählt
  der Zug der Engine, ein anderer ähnlich guter Zug NICHT (dafür ist der Analysieren-Knopf da).
- **Nach einem Fehlversuch muss die FEN neu gebunden werden** (`retry()`): `app-chess-board` führt
  den Nutzerzug selbst aus, und ein Signal mit demselben Wert löst kein `ngOnChanges` aus — deshalb
  steht nach einem falschen Zug dessen Stellung im Signal und „Nochmal" schreibt die Ausgangsstellung
  zurück. Wer erst danebengreift oder die Lösung zeigen lässt, bekommt die Aufgabe nicht als selbst
  gefunden gutgeschrieben.

## Zugliste: der aktive Zug scrollt NUR seinen Kasten, nie die Seite (0.514.1)

`MoveListComponent.scrollToActive` benutzte `scrollIntoView({ block: 'nearest' })` — und das scrollt JEDEN
scrollbaren Vorfahren mit, das Dokument eingeschlossen. Am Handy steht die Zugliste unter dem Brett
(`.moves-section` mit `max-height: 40vh`), der aktive Zug liegt beim Blättern oft unter dem Bildschirmrand, und
jeder Zug schob die ganze Seite samt Brett nach oben (gemeldet 2026-09-23: „das Brett wandert immer weiter nach
oben"). `scrollIntoContainer(el)` (in `move-list.component.ts`) sucht den NÄCHSTEN Vorfahren, der wirklich
scrollt (`overflow-y: auto|scroll` UND Überhang; `html`/`body` zählen nie) und setzt nur dessen `scrollTop` —
`.move-list` selbst, wo es eine feste Höhe hat, sonst `.moves-section`. Gibt es keinen solchen Kasten, passiert
NICHTS: das Brett darf sich beim Navigieren nie bewegen, das ist die Regel. Spec: langer Seiteninhalt, ein
60-px-Kasten, `scrollIntoView` darf nicht aufgerufen werden und `window.scrollY` bleibt 0.

**Partienliste (`features/games/games-list.component.ts`, 0.515.0)**: je Partie kommt `analysis` (Stand der verknüpften
Analyse) mit der Liste. `analysisState` → `none` (Knopf; auch `failed` — der Knopf lädt zum Neuversuch), `running`
(statt des Knopfs `.progress` mit „NN %", derselbe Platz wie ein Icon-Knopf, damit die Zeile nicht springt), `done` (kein
Knopf; `.accuracy` „♔ 87 % · ♚ 72 %" in der Meta-Zeile, `—` für eine Seite ohne bewertbaren Zug). Nachgefragt wird
alle `ANALYSIS_POLL_MS` (10 s) NUR, solange irgendeine Partie läuft (`schedulePoll` nach jedem Laden und nach dem
Einwurf) — sonst ruht die Liste. Die Genauigkeit rechnet der SERVER (`GameAccuracy`), die Liste rechnet nichts.
`GameAnalysisService.list(true)` (Seite „Partie-Analysen") nimmt die Partie-Analysen mit; die Punktepartie-Seite ruft
`list()` ohne Parameter.

**Tippzonen am Brettrand** (`.board-tap` in shared-game, pgn-viewer, shared-line, analysis): EINE globale Regel in
`styles.scss` — `user-select: none` + `touch-action: manipulation` (0.514.3). Ohne sie markierte ein schnelles
Doppeltippen Text, und Safari zoomte. Bewusst nur auf den Zonen, nicht seitenweit: Namen und Züge bleiben kopierbar.

## API-Aufrufe (alle relativ, nginx proxied zu API)

| Component | Endpoints |
|-----------|-----------|
| AuthService | POST `/api/auth/register`, POST `/api/auth/login` |
| DashboardComponent | GET `/api/repertoires`, GET `/api/subscriptions`, GET `/api/friends` |
| ProfileComponent | GET `/api/profile`, PUT `/api/profile` |
| FriendsComponent | GET `/api/friends`, GET `/api/friends/requests`, POST `/api/friends/request/{id}`, POST `/api/friends/accept/{id}`, POST `/api/friends/decline/{id}`, DELETE `/api/friends/{id}`, GET `/api/friends/search?q=` |
| RepertoireListComponent | GET `/api/repertoires`, POST `/api/repertoires`, DELETE `/api/repertoires/{id}` |
| RepertoireDetailComponent | GET `/api/repertoires/{id}`, POST `/api/repertoires/{id}/files` (multipart), GET `/api/repertoires/{id}/files/{fileId}` (blob), DELETE `/api/repertoires/{id}/files/{fileId}` |
| TournamentListComponent | GET `/api/tournaments`, POST `/api/subscriptions` |
| TournamentDetailComponent | GET `/api/tournaments/{id}`, GET `/api/tournaments/{id}/players`, GET `/api/tournaments/{id}/teams`, GET `/api/tournaments/{id}/pairings?round=` |

## Development

```bash
cd app
npm install
npx ng serve              # http://localhost:4200 (braucht API auf :5001)
npx ng build              # Production Build -> dist/app/browser/
npx ng build --watch      # Watch-Mode
npx ng test --watch=false # Unit-Tests (Karma/Jasmine), einmalig headless
```

Fuer den vollen Stack: `docker compose -f compose.dev.yml --env-file .env.dev up --build` im rookhub Root.

### Unit-Tests / Headless-Browser (`karma.conf.js`)

`ng test` nutzt `app/karma.conf.js`. In Headless-/Container-Umgebungen ohne system-weites
Chrome (`apt install chromium` braucht root) sucht die Config automatisch die von
**Puppeteer gecachte `chrome-headless-shell`** unter `~/.cache/puppeteer/chrome-headless-shell/`
und setzt sie als `CHROME_BIN` — `ng test` läuft damit ohne sudo und ohne manuelles `CHROME_BIN`.
Default-Browser ist `ChromeHeadlessNoSandbox` (`--no-sandbox --disable-gpu --disable-dev-shm-usage`).

- Ist keine Cache-Shell da: `npx puppeteer browsers install chrome-headless-shell` (lädt nach `~/.cache/puppeteer`, kein root).
- Lokal mit installiertem Chrome: einfach `CHROME_BIN` setzen oder `--browsers=Chrome` übergeben.

## Fehlerbehandlung (wann `error: () => {}` legitim ist)

Der Bestand enthält ~80 bewusst stille Fehler-Handler. Die Grenze:

- **Still ist OK** bei Hintergrund-Feeds, deren Ausfall der Nutzer nicht bemerken soll und die sich
  selbst heilen: Poll-Zyklen (Badges, Glocke, CI-Status), Vorab-Laden von Offline-Pools,
  fire-and-forget-Meldungen (Track-Solves, Revenge-Result). Nächster Poll/Retry kommt ohnehin.
- **Still ist NICHT OK**, wenn der Nutzer gerade eine Aktion ausgeführt hat (Speichern, Senden,
  Löschen, Download) oder wenn Daten verloren gehen könnten — dann Snackbar mit konkreter Aussage
  (`saveFailed`-Muster) und, wo sinnvoll, die Änderung als „offen" behalten statt sie zu verwerfen.
- Offline-Schreibwege melden Fehlschläge IMMER (writeCalcLocal*/saveBookOffline/Offline-Queue geben
  null/false zurück und die Aufrufer zeigen es an) — ein vorgetäuschtes „gespeichert" ist der
  teuerste Fehler dieser Kategorie.

## Kein horizontaler Seiten-Scroll (Hochformat)

Breite Inhalte — Tabellen, Balken-/Säulenstreifen, Heatmaps, Diagramme — brauchen **ein eigenes
`overflow-x: auto`** auf ihrem Container. Läuft stattdessen die SEITE über, ist nicht nur das
Scrollen unschön: **CDK-Overlays positionieren sich gegen das verbreiterte Dokument**, und die
Untermenüs des Hamburger-Menüs landen außerhalb des Sichtfelds (unten rechts, nur beim Rauszoomen
sichtbar). Genau so gemeldet für `/stats` (2026-09-05): `.bands` (Rating-Säulen, 200er-Schritte
⇒ 14–18 Säulen à min. 28 px) hatte kein `overflow-x` und dehnte die Seite um rund zwei Bildschirme.

Faustregeln:
- Streifen aus `@for`-Kindern: `overflow-x: auto` auf den Streifen, `flex: 1 0 <Mindestbreite>` auf
  die Kinder (füllt bei Platz, schrumpft nie unter die Beschriftung).
- Tabellen: `min-width` auf die Tabelle, `overflow-x: auto` auf den Wrapper (Muster
  `.recent-scroll`/`.recent-table` in `stats.component`).
- Testbar: Host auf 390 px setzen, viele Elemente rendern, `container.scrollWidth <=
  container.clientWidth` prüfen (siehe `stats.component.spec.ts`) — der Test fällt gegen die alte
  Fassung um und ist damit mehr als Dekoration.
- **Falle beim Kommentieren**: Die `styles: [...]`-Blöcke sind Template-Literale — ein Backtick im
  CSS-Kommentar beendet den String und der Build stirbt mit Sass-/TS-Fehlern weit entfernt.


### Service Worker: was bei einer neuen Version wirklich geladen wird

Gemessen am Turnier-Image (2026-09-06): der ngsw laedt bei jeder neuen Version die
`prefetch`-Gruppen komplett neu, BEVOR er umschaltet — und erst die naechste Navigation zeigt
die neue Fassung. Deshalb fuehlt sich „die neue Version ist da" traeger an, als die Seitengroesse
vermuten laesst.

| Gruppe | Dateien | Groesse |
|---|---|---|
| app (Bundles) | 35 | ~1,5 MB |
| i18n | 25 | ~1,5 MB |
| fonts | 11 | ~368 KB |

**Die Bundles und die Schriften tragen einen Inhalts-Hash im Namen** — unveraenderte Dateien
kommen aus dem HTTP-Cache, real neu geladen wird nur, was sich geaendert hat. **Die Sprachdateien
NICHT**: `/i18n/de.json` heisst immer gleich, der ngsw muss also alle 25 bei jeder Version neu
holen — 1,5 MB, von denen ein Nutzer genau eine braucht. Die Gruppe steht deshalb auf
`installMode: lazy` (`updateMode` bleibt `prefetch`): geholt wird die Sprache, die der Nutzer
tatsaechlich oeffnet, und die bleibt danach auch offline verfuegbar. Der Preis: eine Sprache, die
auf diesem Geraet noch NIE benutzt wurde, laesst sich offline nicht umstellen.

## Build-Konfiguration

- Budget: 1.5MB warning / 2MB error (initial bundle; angehoben, da der Single-Source-Changelog stetig wächst)
- Output: `dist/app/browser/` (wird in Docker nach nginx kopiert)
- SCSS als Style-Preprocessor
- Keine Server-Side Rendering / SSR
