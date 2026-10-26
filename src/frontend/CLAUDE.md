# RookHub Frontend

> **Gestaltung: `../../docs/DESIGN.md` ist verbindlich** (seit 2026-10-11). Neue oder geänderte UI folgt den dortigen
> allgemeinen und Bereichsregeln; jede neue Gestaltungsentscheidung wird dort im passenden Abschnitt und im
> Änderungsprotokoll eingetragen (gleicher Commit). Nicht Geregeltes: den User fragen, dann eintragen.

Angular 19 Frontend fuer das RookHub-Portal. Teil des RookHub-Projekts (`C:/git/rookhub`), haengt vom **ChessResults Crawler** (`C:/git/chessresults_crawler`) ab – Turnierdaten werden ueber die RookHub API vom Crawler bezogen. Bei Aenderungen immer alle drei Schichten bedenken.

## Doku-Wegweiser (Feature-Doku liegt in `../../docs/frontend/`, NICHT hier)

Diese Datei lädt bei jeder Arbeit im Frontend — sie enthält nur Architektur, Regeln und Fallen. Feature-Beschreibungen
stehen je Bereich in `rookhub/docs/frontend/` (Index unten); neue Feature-Doku dorthin, neue Datei + Zeile hier.

- Vollbild-Brett (`shared/fullscreen/`) → `docs/frontend/vollbild-brett.md`
- Externe Engines: zwei Quellen, eine Liste (0.537.0) → `docs/frontend/externe-engines.md`
- Linienzahl der Engine (0.738.0) → `docs/frontend/linienzahl-der-engine.md`
- Maia-Sparring (`features/analysis/maia/`, 0.632.0) → `docs/frontend/maia-sparring.md`
- Formular-Korrektur als geteilte Sitzung (0.573.0) → `docs/frontend/formular-korrektur-als-geteilte-sitzung.md`
- Spielerkarte geteilt + Spielervorbereitung (`shared/player-card/`, `features/prep/`, 0.636.0–0.639.0) → `docs/frontend/spielerkarte-geteilt-spielervorbereitung.md`
- Kurs-Kommentare mehrsprachig (Stufe C, 0.549.0) → `docs/frontend/kurs-kommentare-mehrsprachig.md`
- Routing → `docs/frontend/routing.md`
- Verzeichnisstruktur → `docs/frontend/verzeichnisstruktur.md`
- Langer Kurs-Kommentar im Fenster (0.696.1) → `docs/frontend/langer-kurs-kommentar-im-fenster.md`
- Partie-Rückblick: Bewertungskurve, Genauigkeit, Zug-Klassen (0.512.0, Sonderklassen 0.514.0) → `docs/frontend/partie-rueckblick.md`
- Zweite Analyse: Lc0-Spalte + uneinige Züge (0.683.0) → `docs/frontend/zweite-analyse.md`
- „Warum war das ein Fehler?" (0.534.0) → `docs/frontend/warum-war-das-ein-fehler.md`
- Zug-Klasse als Symbol an der Figur (0.557.0) → `docs/frontend/zug-klasse-als-symbol-an-der-figur.md`
- Ähnliche Meisterpartien: anschauen + Fortschritt (0.567.0) → `docs/frontend/aehnliche-meisterpartien.md`
- Computer-Linien + Pfeil für den besten Zug (0.521.0) → `docs/frontend/computer-linien-pfeil-fuer-den-besten-zug.md`
- Live-Engine + eigene Züge auf der Partieseite (0.525.0) → `docs/frontend/live-engine-eigene-zuege-auf-der-partieseite.md`
- ⋮-Menü für die Stellung (0.527.0) → `docs/frontend/menue-fuer-die-stellung.md`
- Eigene Fehler nachspielen (0.516.0) → `docs/frontend/eigene-fehler-nachspielen.md`
- Zugliste: der aktive Zug scrollt NUR seinen Kasten, nie die Seite (0.514.1) → `docs/frontend/zugliste.md`

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

## Fünf Projekte in einem Workspace

`angular.json` enthaelt **fuenf** Anwendungen, die sich node_modules und den Quellbaum teilen:

| Projekt | Quelle | Bundle | Image | Domain |
|---------|--------|--------|-------|--------|
| `app` | `src/` | `dist/app/browser` | `rookhub-frontend` | rookhub(-dev).oberschmid.homes |
| `turnier` | `src-turnier/` | `dist/turnier/browser` | `rookhub-turnier` | turnier(-dev).oberschmid.homes |
| `kidhub` | `src-kidhub/` | `dist/kidhub/browser` | `rookhub-kidhub` | kidhub(-dev).oberschmid.homes (Kinderseite, spielbar ohne Konto) |
| `leaguehub` | `src-leaguehub/` | `dist/leaguehub/browser` | `rookhub-leaguehub` | leaguehub(-dev).oberschmid.homes (Aufstellungs-Prognosen Tiroler Ligen + Vereins-Datenbank, Admins und die Vereinsgruppe; Teilen-Links `/s/:token` ohne Konto) |
| `clubhub` | `src-clubhub/` | `dist/clubhub/browser` | `rookhub-clubhub` | clubhub(-dev).oberschmid.homes (Kartei der Kinder und Jugendlichen: Kontakte, Gruppen, Anwesenheit, Lernstand — nur Trainer und Leitung, `club.trainer`/`club.manage`) |

- `src-turnier/` enthaelt nur, was die Turnierseite EIGEN hat: Einstiegspunkt, Routen, Navbar und
  die Turnier-Features. Alles Geteilte (Auth, Interceptors, i18n, shared/) kommt per Pfad-Alias
  **`@rh/*` → `src/app/*`** — kein zweiter Bestand, kein Nachziehen von Hand.
- Eigene Dateien der Turnierseite: `src-turnier/`, `public-turnier/` (eigenes Manifest, wird ueber
  `public/` drueberkopiert), `tsconfig.turnier.json`, `ngsw-config.turnier.json`.
- Bauen: `npx ng build turnier --configuration=production` bzw. `npx ng serve turnier`.
- Routen der Turnierseite (Codereview 2026-09-29, F6-001): wie KidHub/LeagueHub auch `forgot-password`,
  `reset-password`, `privacy`, `impressum` und `account-deletion` (Masken über `@rh/*`).
- Jede App (RookHub, Turnierseite, KidHub, LeagueHub, ClubHub) prueft in ihrer `app.routes.spec.ts` mit
  `checkSharedPageLinks` (`src/app/testing/shared-page-links.ts`, UX-003), dass jeder GERENDERTE Link der geteilten
  Anmelde- und Rechtsseiten auf seinen eigenen Weg fuehrt (nicht in '**'/`:slug`). Neue App mit diesen Seiten: dort
  aufrufen; neue geteilte Seite: in `SHARED_AUTH_LEGAL_PAGES` eintragen. Ein Link, der BEWUSST ueber die Anmeldung
  geht (authGuard mit returnUrl, z. B. „Konto löschen …" → `/profile?section=delete`, UX-023), traegt
  `data-login-required` — er muss trotzdem seine eigene Route treffen.
- Gemerkte Ansicht der Turnierseite (F6-002): der lokale Schlüssel der Kalender-Filterleiste ist NUTZERGEBUNDEN
  (`rh.turnier.directoryView.<userId>`), `'rh.turnier.'` ist in `core/local-storage-keys.ts` als `user` eingeordnet (wird beim Abmelden geräumt), und
  beim Einstieg als ein Nutzer (Impersonation) geht kein View-State zum Server.
- KidHub genauso: `src-kidhub/`, `public-kidhub/`, `tsconfig.kidhub.json`, `ngsw-config.kidhub.json`;
  `npx ng build kidhub` / `npx ng test kidhub` (Details im Haupt-CLAUDE.md „Dritte Oberfläche: KidHub").
- LeagueHub genauso: `src-leaguehub/`, `public-leaguehub/`, `tsconfig.leaguehub.json`, `src-leaguehub/leaguehub.scss`
  (eigene Gestaltung, nach `src/styles.scss`); `npx ng build leaguehub` / `npx ng test leaguehub` (Details im
  Haupt-CLAUDE.md, Abschnitt „LeagueHub“ → Oberfläche).
  Die Spielerkarte liegt seit 0.636.0 NICHT mehr dort, sondern geteilt in `src/app/shared/player-card/` (Specs in
  `ng test app`); LeagueHubs Kern (`league-api.service`, `league.models`, `league-format`, `my-games.service`) bleibt in
  `src-leaguehub` und ist von `src/` aus über den Alias **`@lh/*` → `src-leaguehub/app/*`** erreichbar — siehe
  „Spielerkarte geteilt + Spielervorbereitung“.
- ClubHub genauso: `src-clubhub/`, `public-clubhub/` (Symbol + Schriften Zilla Slab/Atkinson Hyperlegible),
  `tsconfig.clubhub.json`, `src-clubhub/clubhub.scss`; `npx ng build clubhub` / `npx ng test clubhub` (Details im
  Haupt-CLAUDE.md, Abschnitt „ClubHub“). Kein Service Worker — die Kartei (Daten von Kindern) soll nie aus einem
  Zwischenspeicher kommen. Formulare ohne FormsModule: native Felder, Zustand in Signalen (wie LeagueHub).
- Testen: **alle Projekte namentlich** — `npx ng test app`, `npx ng test turnier`, `npx ng test kidhub`, `npx ng test leaguehub` UND `npx ng test clubhub`. Der
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
- **Schichten** (Codereview F8-013) – `shared/` importiert nichts aus `features/` (nur `core/`, `shared/`, Pakete); Wache `shared/no-feature-imports.spec.ts` mit abschließender Ausnahmeliste (nur noch `game-replay` → `GameReviewComponent`); `core/` ebenso (einzige Ausnahme: die dynamischen `import()` in `core/login-follow-up.service.ts`). App-eigene Rahmenteile, die Features kennen (RookHub-Kopfleiste, Offline-Vorabruf der Puzzle-Pools), liegen in `shell/`
- **Lazy Loading** – alle Feature-Routes werden per `loadComponent()` geladen
- **Functional Guards** – `authGuard` als `CanActivateFn`
- **Functional Interceptors** – `authInterceptor` als `HttpInterceptorFn`. Die HTTP-Kette ALLER Oberflächen kommt aus
  `provideRhHttpClient(extra)` / `rhInterceptors(extra)` in `core/http-chain.ts`: connectivity, retry, (extra — RookHub:
  `visitorInterceptor`), auth, renderAfterHttp. Oberflächeneigene Interceptoren gehören in `extra`, zwischen retry und
  auth (Codereview F8-014)
- **Rückfragen und Texteingaben nie nativ** (Codereview F8-005/F5-016) – Rückfragen nur über `ConfirmService.ask`
  (`shared/confirm-dialog`), nie `window.confirm`; Texteingaben über `PromptService.ask` (`shared/prompt-dialog`), nie
  `window.prompt`; Verlustaktionen ohne Rückgängig fragen nach. LeagueHub/ClubHub geben die Knopftexte über
  `CONFIRM_LABELS`. Wache: `NativeConfirmGuardTests` (deckt `prompt` mit ab)
- **Auswahl- und Such-Requests** (Codereview F5-004) – Auswahl-Requests über `shared/latest-request.util` (`LatestRequest`:
  nur die jüngste Antwort zählt), Suchfelder als `Subject` + `switchMap` mit `catchError` INNEN (sonst endet der Strom
  beim ersten Fehler)
- **Globale Brett-Tastenkürzel** (Codereview F8-007) – `isBoardHotkey(e, host?)` aus `shared/keyboard.util.ts` ist die
  eine Regel für `window`/`document:keydown`; neue Handler nutzen sie, Komponenten in einem Dialog übergeben ihr
  `ElementRef` als `host`. `PuzzleBoardComponent` nimmt Züge auch per Tastatur (`shared/chess/keyboard-move.util.ts`;
  Figurenbuchstaben über `<html lang>`, nur de/hr/hu zusätzlich zu Englisch, Codereview F2-004)
- **Primärknöpfe im M3-Thema** (Codereview UX-052) – `mat-raised-button color="primary"/"warn"` ist per `styles.scss`
  gefüllt; neue Primärknöpfe `mat-flat-button` oder raised + primary, raised ohne Farbe = M3-„protected“. Auth-Formulare:
  Knopf immer aktiv, Validierung beim Absenden (`markAllAsTouched` + `mat-error`)
- **Neuer Menüpunkt** (Codereview UX-011) – Eintrag in `MenuRegistry` → Eintrag in `MENU_HELP`
  (`features/help/help.component.ts`) und Hilfe-Abschnitt in en/de/hr/hu; Wache
  `MenuRegistryTests.EveryMenuItem_HasHelpSection`
- **Unbekannte Adressen** (Codereview UX-026) – `'**'` (RookHub, LeagueHub) und unbekannte Kurz-URLs zeigen
  `shared/not-found/NotFoundComponent` (guard-frei). Guards, die Angemeldete umleiten, melden das über `blockedNotice()`
  (`core/blocked-notice.ts`) — übersetzt mit `get()`, nicht `instant()` (Kaltstart: Sprachdatei noch unterwegs).
  `PublicSlugComponent` zeigt „nicht gefunden“ nur bei 404, sonst `LoadErrorComponent` mit Wiederholen
- **provideHttpClient / provideRouter** – keine Module-basierte Konfiguration
- **Angular Material** – fuer alle UI-Komponenten (Toolbar, Cards, Lists, Tables, Dialogs, Tabs, etc.)
- **i18n via ngx-translate** – Laufzeit-Lokalisierung (siehe unten)
- **Ungespeicherter Arbeitsstand** – Seiten, die man mit ungespeicherten Aenderungen verlassen koennte, implementieren
  `LeaveConfirm.canLeave()` und haengen `unsavedChangesGuard` (`core/unsaved-changes.guard.ts`) als `canDeactivate`
  an; Muster: Partie-Korrektur (`/games/:id/edit`, Codereview F4-003)
- **Zustandsfarben aus Tokens** (Codereview F8-003) – Fehler, Erfolg, Warnung, Info und Akzent kommen aus den
  semantischen Tokens in `src/_tokens.scss` (`var(--rh-…)`, hell/dunkel), nicht als Hex-Wert. Es sind
  Vordergrund-Tokens (Text/Icon/Rahmen), Flaechen per `color-mix`; den Kontrast je Modus prueft
  `app/core/design-tokens.spec.ts`
- **Rechtsseiten** (`features/legal/`, Betreiber-Entscheidung 2026-09-30, UX-001) – `environments/operator.ts`
  (`OPERATOR`) enthaelt NUR die Kontaktadresse (`rookhub@oberschm.id`); das Impressum zeigt nur den Kontakt, kein
  Diensteanbieter-Block mit Name/Anschrift. Je Oberflaeche steuert `LEGAL_SITE` (`core/legal-site.ts`) Impressum ja/nein,
  Kontaktadresse, `kind`, Ruecklink und `accountHome` (Details im Haupt-CLAUDE.md, KidHub „Kein Impressum"). Daneben
  `AUTH_INTRO` (`features/auth/auth-intro.ts`) als app-eigene Zeile der Anmelde-/Registriermaske (Turnierseite setzt es
  in `turnierConfig`, ClubHub `AUTH_INTRO.login` in `clubhubConfig` — Freischaltung durch den Verein statt
  Kostenlos-Satz; UX-027). Aufbau der Rechtsseiten: Titel als `<h1 mat-card-title>`, Abschnitte `h2`, Datenschutz mit
  Inhaltsverzeichnis per Klick-Sprung (kein Router-Fragment wegen `scrollPositionRestoration: 'top'`; UX-057)
- **Brett-Koordinaten** (UX-057/UX-060) – die Rangziffern-Farben in `styles.scss` sind gegenüber chessground getauscht
  (die Ränge stehen auf der a- bzw. h-Linie), für ALLE Brett-Themen; KidHub-Koordinaten 11 px deckend

## Lokalisierung (ngx-translate)

- **Sprachen**: `en` (Default/Fallback), `de`, `hr`, `hu` — die VOLLSTÄNDIG gepflegten. Sie stehen in
  `FORMAT_LOCALES` (`core/locale.service.ts`), und wer dort eine Sprache einträgt, verpflichtet sich zur
  Vollständigkeit: `i18n-parity.spec.ts` verlangt für genau diese dieselben Keys wie `en`, mit gleichen
  `{{Platzhaltern}}` und ohne leere Werte. Die übrigen 21 `SUPPORTED_LANGS` sind Teilübersetzungen und
  fallen Key für Key auf `en` zurück. Alle Dateien liegen in `public/i18n/<code>.json` (statisch unter
  `/i18n/*.json` ausgeliefert); für `FORMAT_LOCALES` werden zusätzlich die Angular-Locale-Daten
  registriert, an EINER Stelle: `FORMAT_LOCALE_DATA` in `core/locale-data.ts`, die `app.config.ts` von RookHub,
  Turnierseite und KidHub rufen `registerFormatLocaleData()` (LeagueHub/ClubHub: feste `LOCALE_ID` 'de', nur
  `localeDe`). `locale-data.spec.ts` bindet die Schlüssel an `FORMAT_LOCALES`: wer dort eine Sprache einträgt, ohne
  sie hier zu ergänzen, bekommt einen roten Test statt NG0701 „Missing locale data“ in jeder Datums-/Zahlen-Pipe
  (Codereview F8-014).
- **Setup**: `provideTranslateService({ fallbackLang: 'en', loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }) })` in `app.config.ts`. `@ngx-translate/core` + `@ngx-translate/http-loader` **v18** (voll standalone: **kein `TranslateModule` mehr** → `TranslatePipe` importieren; `currentLang`/`fallbackLang` sind **Signals** → `currentLang()`; `defaultLang`/`getDefaultLang()` → `getFallbackLang()`; in Specs statt `TranslateModule.forRoot()` → `provideTranslateService({ fallbackLang: 'en' })` in `providers`).
- **`core/locale.service.ts`**: ermittelt Startsprache (localStorage `rookhub_lang` → Browser → `en`), `use(lang)` persistiert. Wird in `AppComponent`-Konstruktor via `init()` gestartet. Sprachumschalter (Globus-Icon) in der Navbar.
- **Verwendung**: Templates `{{ 'ns.key' | translate }}` bzw. Attribute via Binding (`[attr.title]="'ns.key' | translate"`); dynamische Strings im TS via `TranslateService.instant('ns.key', { param })` mit `{{param}}`-Platzhaltern. Jede Standalone-Component, die übersetzt, importiert `TranslatePipe` (ngx-translate 18 — `TranslateModule` gibt es nicht mehr).
- **Key-Namespaces**: `common`, `nav`, `app`, `auth`, `dashboard`, `profile`, `friends`, `repertoire`, `tournaments`, `puzzles`, `endless`, `book`, `courses`, `weekly`, `trainingGoals`, `admin`, `pgnViewer`. Generische Begriffe (Speichern/Abbrechen/…) unter `common.*` — gleiche Knopftexte (Speichern, Abbrechen, Weiter,
  Zurück, Weitere Aktionen, Kopieren, Weiß/Schwarz am Zug …) IMMER über `common.*`; die Parity-Spec lehnt neue
  wortgleiche Doppel ab. `common.next`/`common.previous` sind in den 21 nicht gepflegten Sprachen noch englische
  Platzhalter (Codereview F8-010).
- **Kein toter Schlüssel** (Codereview F8-011): `src/app/i18n-usage.spec.ts` verlangt, dass jeder `en.json`-Schlüssel im
  Quelltext vorkommt (Literal, `'ns.' + x`, `` `ns.${x}` ``, Namensraum-Literal, `x + '.suffix'`); wer einen Schlüssel
  nicht mehr benutzt, löscht ihn aus allen 25 Dateien. `karma.conf.js` liefert dafür `src*/**/*.ts|html` aus.
- **Nicht übersetzt**: Schach-Notation/FEN/PGN, Eigennamen, „RookHub"/„Stockfish", gecrawlte Daten, HTTP-Methoden.

## Offline / PWA (Service Worker)

- **Service Worker**: `@angular/service-worker` (ngsw), Konfig in `ngsw-config.json`, registriert in `app.config.ts` via `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() })` — **nur im Prod-Build aktiv** (`serviceWorker: "ngsw-config.json"` steht in der `production`-Configuration der `angular.json`). Cacht App-Shell, **alle Lazy-Chunks** (assetGroup `app`, prefetch → Routen wie `/puzzles`, `/endless` öffnen offline) + i18n (prefetch) + Google-Fonts (dataGroup, performance). `/api/*` wird **nicht** vom SW gecacht (immer Netz → App fällt offline auf lokale Caches/Queue zurück). **Stockfish (`/assets/stockfish/**`, ~7 MB `.wasm`) ist als eigene assetGroup `engine` (installMode `prefetch`) im SW** → Engine/Analyse/Eval funktionieren offline. Das `.wasm` wird vom dedizierten Worker per Subresource-Fetch geladen (geht über den SW-Cache); falls `WebAssembly.instantiateStreaming` an einem cache-servierten Response scheitert, fällt das Glue auf `WebAssembly.instantiate(arrayBuffer)` zurück (kein Hänger). Hinweis: Der frühere „Berechne…"-Hänger lag NICHT am SW, sondern am UCI-Sequencing in `AnalysisEngineService.analyze` (stop→isready→readyok→position+go; seit 0.64.2 behoben). Hash ist auf 16 MB begrenzt (OOM-Schutz). nginx: SW-Steuerdateien `no-cache`, CSP `connect-src` enthält die Font-Origins (SW-Caching).
- **`core/local-json-store.ts`** (seit 0.499.1): der Rumpf ALLER geräte-lokalen Speicher — `readJson`/`writeJson` (kaputtes JSON → `null`, Quota/gesperrt → `false`), `readRaw`/`writeRaw`/`hasKey`/`removeKey`/`keysWithPrefix`, dazu `BoundedMapStore` für „höchstens N Einträge, älteste raus" (Verdrängungs-Reihenfolge als Parameter: Datums-Schlüssel lexikografisch, Lösezeiten nach Schreibzeitpunkt). Den Speicher selbst holt man über `localStore()`/`sessionStore()` — schon der ZUGRIFF auf `localStorage` kann werfen (Chrome mit blockierten Cookies). Darauf setzen `book-offline.util.ts`, `repertoire-offline.util.ts`, `calc/calc-local.util.ts`, `solve-elapsed.util.ts`, `daily-elapsed.util.ts` und `last-solved-store.ts` (sessionStorage) auf; in ihnen bleibt nur das Fachliche. **Ein fehlgeschlagener Schreibversuch wird GEMELDET** (Rückgabewert auswerten) — „gespeichert" anzuzeigen, während nichts liegt, ist der Fehler, den die Kalkulations-Regel weiter unten meint. `features/puzzles/endless-storage.service.ts` steht bewusst daneben (eigene Semantik).
- **Jeder Browser-Speicher-Schlüssel ist eingeordnet** (Codereview F1-010): `core/local-storage-keys.ts` (`user` = beim Abmelden räumen, `device` = Gerätevorliebe bleibt, `auth`/`queue`/`session`/`none`); `OfflineService.clearOnLogout` räumt danach. `local-storage-keys.spec.ts` durchsucht die Quelltexte aller Oberflächen nach Literalen mit `rookhub_`, `rh.`, `rh-kids-`, `lh-`, `leaguehub`, `public_fav_` — ein neuer Schlüssel ohne Zeile dort macht sie rot.
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
   vorheriges Abmelden. Gilt ebenso in der Turnierseite (`src-turnier`, Import ueber `@rh/*`). Der Wechsel auf
   ein ANDERES Konto raeumt lokal auf wie `logout()` (ohne `session/end`); ebenso der Ablauf des Tokens
   (`SessionStore.validUser` → `endExpiredSession`, Codereview F1-006)
4a. Speichern der Sitzung (`SessionStore.persist`, hinter der Fassade `AuthService`): ist der localStorage voll, werden die Offline-Caches
   geraeumt und erneut geschrieben; scheitert auch das, sagt eine Snackbar, dass die Anmeldung nur bis zum
   Neuladen haelt (`storageFull`, Kibana-Event `ClientLog storage_full`). Vorher blieb das stumm
5. `AuthService.currentUser$` (BehaviorSubject) fuer reaktive UI-Updates (Navbar etc.)
6. Discord-Link `?dl=<token>` (Codereview F1-002): verknuepft NIE still — `DiscordLinkService.confirmAndLink` fragt
   mit dem Discord-Namen aus dem Token nach (ist das Konto schon mit genau dieser Discord-ID verknuepft: keine
   Rueckfrage; mit einer anderen: Rueckfrage nennt die bestehende, die ersetzt wird). Ohne Anmeldung wird der Link nur
   in `sessionStorage` (dieser Tab) vorgemerkt, nicht mehr in `localStorage`; eine alte localStorage-Vormerkung wird
   nie eingeloest, nur entfernt.

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
npm run lint:ratchet      # ESLint-Ratsche: keine Regel ueber eslint-baseline.json
```

**Linter (ESLint, F8-004):** alle Regeln `'warn'`, aber die CI ist rot, sobald eine Regel mehr Meldungen hat als in
`app/eslint-baseline.json` — neue Stelle fixen, nicht die Baseline heben; nach Abbau `npm run lint:ratchet:update`
(Details: `docs/ci.md`, Abschnitt „ESLint-Ratsche“).

Fuer den vollen Stack: `docker compose -f compose.dev.yml --env-file .env.dev up --build` im rookhub Root.

### Unit-Tests / Headless-Browser (`karma.conf.js`)

`ng test` nutzt `app/karma.conf.js`. In Headless-/Container-Umgebungen ohne system-weites
Chrome (`apt install chromium` braucht root) sucht die Config automatisch die von
**Puppeteer gecachte `chrome-headless-shell`** unter `~/.cache/puppeteer/chrome-headless-shell/`
und setzt sie als `CHROME_BIN` — `ng test` läuft damit ohne sudo und ohne manuelles `CHROME_BIN`.
Default-Browser ist `ChromeHeadlessNoSandbox` (`--no-sandbox --disable-gpu --disable-dev-shm-usage`).

- Ist keine Cache-Shell da: `npx puppeteer browsers install chrome-headless-shell` (lädt nach `~/.cache/puppeteer`, kein root).
- Lokal mit installiertem Chrome: einfach `CHROME_BIN` setzen oder `--browsers=Chrome` übergeben.
- **Kontrast-Specs** messen berechnete Farben über `src/app/testing/contrast.ts` (`textContrast`/`effectiveBackground`;
  `mountWithComponentStyles` wendet gekapselte Komponenten-Stile ohne Zustand an) und `testing/board-coords.ts`
  (Brett-Koordinaten gegen ihr Feld); `body` malt `var(--mat-sys-surface)`, die Textfarbe bleibt bewusst die des
  Browsers (Codereview UX-057).

## Fehlerbehandlung (wann `error: () => {}` legitim ist)

Der Bestand enthält ~80 bewusst stille Fehler-Handler. Die Grenze:

- **Still ist OK** bei Hintergrund-Feeds, deren Ausfall der Nutzer nicht bemerken soll und die sich
  selbst heilen: Poll-Zyklen (Badges, Glocke, CI-Status), Vorab-Laden von Offline-Pools,
  fire-and-forget-Meldungen (Track-Solves, Revenge-Result). Nächster Poll/Retry kommt ohnehin.
- **Still ist NICHT OK**, wenn der Nutzer gerade eine Aktion ausgeführt hat (Speichern, Senden,
  Löschen, Download) oder wenn Daten verloren gehen könnten — dann Snackbar mit konkreter Aussage
  (`saveFailed`-Muster) und, wo sinnvoll, die Änderung als „offen" behalten statt sie zu verwerfen.
- **Ladefehler nie als Leerzustand zeigen** (Codereview F5-017) — `shared/load-error` (`app-load-error`, Standardtext
  `common.loadFailed`) mit Wiederholen-Knopf.
- **Fehlertexte aus HTTP-Fehlern** über `core/api-error.ts` `apiErrorText(err, translate, fallbackKey)` (übersetzt den
  `code` der API über `apiErrors.<code>`, sonst `message`, sonst der Schlüssel) statt `err.error?.message ||
  translate(...)` (Codereview F5-019).
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
