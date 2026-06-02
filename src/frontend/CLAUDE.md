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

## Tech Stack

| Komponente | Version |
|-----------|---------|
| Angular | 19.2 |
| Angular Material | 19.2.19 |
| Angular CDK | 19.2.19 |
| TypeScript | 5.7 |
| RxJS | 7.8 |
| Node (Build) | 24 (Docker), lokal: 24.14 |
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

- **Sprachen**: `en` (Default/Fallback), `de`, `hr`. Übersetzungen liegen in `public/i18n/{en,de,hr}.json` (statisch unter `/i18n/*.json` ausgeliefert).
- **Setup**: `provideTranslateService({ fallbackLang: 'en', loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }) })` in `app.config.ts`. `@ngx-translate/core` + `@ngx-translate/http-loader` v17.
- **`core/locale.service.ts`**: ermittelt Startsprache (localStorage `rookhub_lang` → Browser → `en`), `use(lang)` persistiert. Wird in `AppComponent`-Konstruktor via `init()` gestartet. Sprachumschalter (Globus-Icon) in der Navbar.
- **Verwendung**: Templates `{{ 'ns.key' | translate }}` bzw. Attribute via Binding (`[attr.title]="'ns.key' | translate"`); dynamische Strings im TS via `TranslateService.instant('ns.key', { param })` mit `{{param}}`-Platzhaltern. Jede Standalone-Component, die übersetzt, importiert `TranslateModule`.
- **Key-Namespaces**: `common`, `nav`, `app`, `auth`, `dashboard`, `profile`, `friends`, `repertoire`, `tournaments`, `puzzles`, `endless`, `book`, `courses`, `weekly`, `admin`, `pgnViewer`. Generische Begriffe (Speichern/Abbrechen/…) unter `common.*`.
- **Nicht übersetzt**: Schach-Notation/FEN/PGN, Eigennamen, „RookHub"/„Stockfish", gecrawlte Daten, HTTP-Methoden.

## Offline / PWA (Service Worker)

- **Service Worker**: `@angular/service-worker` (ngsw), Konfig in `ngsw-config.json`, registriert in `app.config.ts` via `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() })` — **nur im Prod-Build aktiv** (`serviceWorker: "ngsw-config.json"` steht in der `production`-Configuration der `angular.json`). Cacht App-Shell, **alle Lazy-Chunks** (assetGroup `app`, prefetch → Routen wie `/puzzles`, `/endless` öffnen offline) + i18n (prefetch) + Google-Fonts (dataGroup, performance). `/api/*` wird **nicht** vom SW gecacht (immer Netz → App fällt offline auf lokale Caches/Queue zurück). **Stockfish (`/assets/stockfish/**`, ~7 MB `.wasm`) ist als eigene assetGroup `engine` (installMode `prefetch`) im SW** → Engine/Analyse/Eval funktionieren offline. Das `.wasm` wird vom dedizierten Worker per Subresource-Fetch geladen (geht über den SW-Cache); falls `WebAssembly.instantiateStreaming` an einem cache-servierten Response scheitert, fällt das Glue auf `WebAssembly.instantiate(arrayBuffer)` zurück (kein Hänger). Hinweis: Der frühere „Berechne…"-Hänger lag NICHT am SW, sondern am UCI-Sequencing in `AnalysisEngineService.analyze` (stop→isready→readyok→position+go; seit 0.64.2 behoben). Hash ist auf 16 MB begrenzt (OOM-Schutz). nginx: SW-Steuerdateien `no-cache`, CSP `connect-src` enthält die Font-Origins (SW-Caching).
- **Offline-Daten-Caches** (`core/offline.service.ts`, localStorage, pro Gerät, im Profil einstellbar): Standard-Puzzle-Pool (`PUZZLE_POOL_KEY`), Endless-Run-Pool (`ENDLESS_POOL_KEY`), ganze Bücher (`BOOK_OFFLINE_PREFIX`, `features/puzzles/book-offline.util.ts`). Pools werden **online beim Modus-Eintritt** vorab geladen, damit ein Offline-Start Daten hat.
- **Offline-Lösungs-Queue** (`core/offline-queue.service.ts`): schreibende Solve-Requests (Standard-/Tagespuzzle-Attempt, Kurs-Result, Endless-Session), die offline scheitern, werden als rohe `{method,url,body}` in localStorage (`rookhub_offline_queue`) vorgemerkt und bei `window 'online'` + App-Start über den `HttpClient` (inkl. authInterceptor) erneut gesendet — Eintrag erst nach Erfolg entfernt, 4xx verworfen, Netz/5xx bleibt liegen. App-weit instanziiert in `AppComponent`.

## Auth-Flow

1. Login/Register -> `AuthService.login()` / `.register()` -> POST an `/api/auth/*`
2. Response enthaelt JWT -> wird in `localStorage` als `rookhub_user` gespeichert
3. `authInterceptor` haengt `Authorization: Bearer <token>` an alle Requests
4. `authGuard` prueft `AuthService.isLoggedIn` -> redirect zu `/login` wenn nicht eingeloggt
5. `AuthService.currentUser$` (BehaviorSubject) fuer reaktive UI-Updates (Navbar etc.)

## Routing

| Route | Component | Auth |
|-------|-----------|------|
| `/login` | LoginComponent | nein |
| `/register` | RegisterComponent | nein |
| `/dashboard` | DashboardComponent | ja |
| `/profile` | ProfileComponent | ja |
| `/friends` | FriendsComponent | ja |
| `/repertoires` | RepertoireListComponent | `adminGuard` (vorerst nur Admin) |
| `/repertoires/:id` | RepertoireDetailComponent | `adminGuard` (vorerst nur Admin) |
| `/tournaments` | TournamentListComponent | ja |
| `/tournaments/:id` | TournamentDetailComponent | ja |
| `/weekly` | WeeklyListComponent | `adminGuard` (vorerst nur Admin; Lese-API bleibt offen) |
| `/analysis` | AnalysisComponent | nein (öffentlich; lokale Stockfish-MultiPV-Analyse) |
| `/stats` | StatsComponent | ja (Puzzle-Elo-Kurve + Stats; `GET /api/puzzles/elo-history`) |
| `/weekly/:weeklyId` | BookPuzzleComponent (Wochenpost-Modus) | `adminGuard` (vorerst nur Admin) |
| `/courses` | CourseListComponent | `courseAccessGuard` (Admin oder Gruppe mit Buch-Freigabe) |
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
```

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
```

Fuer den vollen Stack: `docker compose -f compose.dev.yml --env-file .env.dev up --build` im rookhub Root.

## Build-Konfiguration

- Budget: 750kB warning / 1.5MB error (initial bundle)
- Output: `dist/app/browser/` (wird in Docker nach nginx kopiert)
- SCSS als Style-Preprocessor
- Keine Server-Side Rendering / SSR
