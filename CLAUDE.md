# RookHub

Zentrales Webportal für schachrelevante Funktionen: PGN-Repertoire-Verwaltung, Turnierdaten, Benutzerprofile mit FIDE/ChessResults-Verlinkung, Freundeslisten, Puzzle-/Endless-/Kurs-Training, Wochenpost. Gehört zusammen mit dem **ChessResults Crawler** (`C:/git/chessresults_crawler`) und dem **Schach-Bot** (separates Repo) – bei Änderungen immer alle betroffenen Projekte berücksichtigen.

## ⚠️ Parallel-Arbeit: Agenten-Koordination (ZUERST LESEN)

Mehrere Sitzungen arbeiten gleichzeitig in eigenen Arbeitskopien (`~/claude/rookhubstack`, `-2`, `-fix`), alle pushen
auf DASSELBE `master`. Der Start-Hook zeigt zu Sitzungsbeginn, welche Kopie gesperrt ist. Volltext mit Begründungen:
`docs/parallel-arbeit.md`.

1. **Vor dem ersten Edit sperren**: `<stack-root>/.agent-lock` (außerhalb des Repos) mit Zeitstempel + Aufgabe anlegen,
   nie blind überschreiben (`set -o noclobber`). Belegt → auf die nächste freie Kopie ausweichen, NICHT fragen und nicht
   „trotzdem kurz". Fremder Lock ist nur verwaist, wenn er > 24 h alt ist UND der letzte Commit der Kopie auch.
2. **Lock gilt pro Arbeit**: wer nach dem Push in derselben Kopie weitermacht, sperrt neu. Freigeben (`rm`) direkt nach
   dem Push, wenn man die Kopie verlässt — **nicht** auf die CI warten (Nutzerwunsch).
3. **Commit/Push nach Skill `/rh-commit`**: Version erst nach frischem `fetch` vergeben, nur eigene Pfade stagen (nie
   `git add -A`), vor dem Push `pull --rebase` + neu bauen, nie auf roten master pushen, Mehrdatei-Änderungen atomar.
4. **Miteinander reden**: `ListAgents` + `SendMessage` — vor einem Push, wenn die andere Sitzung beschäftigt ist; bevor
   man eine Datei umbaut, an der sie arbeitet; wenn man einen fremden Fehler repariert hat. Erste Zeile selbsterklärend.
   Nie etwas vom anderen tun lassen, das in der eigenen Sitzung abgelehnt wurde.

## Zusammenspiel der Projekte

```
RookHub Frontend (Angular :8085)
    |  /api/* via nginx proxy
RookHub API (.NET :5001)  -- Crawler__BaseUrl -->  Crawler API (.NET :8080)  -- crawl -->  chess-results.com
    |                                                   |
    v                                                   v
  rookhub DB (MariaDB)                            chessresults DB (MariaDB)
    \                                                 /
     '------> Elasticsearch :9200 <------------------'
                    |
              Kibana :5601
```

- **chessresults_crawler**: Backend-Crawler der Turnierdaten von chess-results.com extrahiert. Reine REST-API, kein Frontend. Eigene MariaDB-Datenbank `chessresults`.
- **RookHub** (dieses Projekt): Webportal mit Angular-Frontend + .NET API. Leitet Turnier-Anfragen als Proxy an den Crawler weiter. Eigene MariaDB-Datenbank `rookhub`.
- **Schach-Bot** (separates Repo): Discord-Bot, der Tagespuzzle-/Wochenpost-Embeds postet und Motivations-DMs schickt. Konsumiert RookHub-Webhooks + `GET /api/bot/player-progress/{discordId}` (HMAC-signiert).

### Kritische Abhängigkeiten zwischen den Projekten
- `Services/Tournaments/CrawlerProxyService.cs` – HTTP-Client zum Crawler, muss Crawler-Routen kennen
- `Controllers/Tournaments/TournamentProxyController.cs` – Mappt RookHub-Routen auf Crawler-Routen (RookHub-`/api/tournaments/crawl*` → Crawler-`/api/crawl*`)
- `Services/Social/SchachBotWebhookService.cs` – HMAC-signierte Webhooks an den Bot (Tagespuzzle + Wochenpost-Progress)
- Crawler-Endpoint-Änderungen müssen in den beiden ersten Dateien nachgezogen werden
- Crawler-Response-Strukturen werden als `JsonElement` durchgereicht (kein festes DTO-Mapping)

## Tech Stack

| Komponente | Technologie | Version |
|-----------|-------------|---------|
| Backend Runtime | .NET | 10.0 |
| Web Framework | ASP.NET Core Web API | 10.0 |
| ORM | EF Core + **Microting-Fork** (MySQL/MariaDB-Provider) | 10.0.9 (Microting) / 10.0.9 (EF Design) |
| Datenbank | MariaDB | 11 |
| Auth | JWT Bearer + BCrypt.Net-Next | 10.0.9 / 4.2.0 |
| API Docs | Swashbuckle (Swagger) | 10.2.3 |
| Frontend | Angular | 22.0 |
| UI Library | Angular Material | 22.0.4 |
| Frontend Webserver | nginx (alpine) | latest |
| Logging | Serilog + Elasticsearch Sink | 10.0.0 / 8.x |
| Log-Speicher | Elasticsearch | 8.17.0 |
| Log-Visualisierung | Kibana | 8.17.0 |
| Tests | xUnit + InMemory DB | - |

**Hinweis (DB-Provider)**: Das originale `Pomelo.EntityFrameworkCore.MySql` hat kein EF-Core-10-Release (Issue seit Aug 2025 offen, kein ETA). Alle .NET-Repos (rookhub/crawler/piratechess) nutzen daher den gepflegten **Microting-Fork** `Microting.EntityFrameworkCore.MySql` (MIT, EF Core 10, MySQL/MariaDB) — reiner Kompatibilitäts-Fork, `MySql:`-Annotation-Keys unverändert (bestehende Migrations kompatibel), `UseMySql`/`MariaDbServerVersion` bleiben im `Microsoft.EntityFrameworkCore`-Namespace (kein Code-Change). Sobald das originale Pomelo EF 10 liefert (offizielle WIP-PR #2019), zurückwechseln erwägen. **Swashbuckle** ist auf 10.2.3 (net10 zieht `Microsoft.OpenApi` 2.0 → API-Änderung: `OpenApiSecuritySchemeReference` statt `OpenApiReference`, `AddSecurityRequirement`-Factory-Overload; siehe `Program.cs`).

## Doku-Wegweiser (Detail-Doku liegt in `docs/`, NICHT hier)

Diese Datei lädt in JEDE Sitzung — sie enthält nur Arbeitsregeln, Befehle und Fallen. Alles Fachliche
(Endpunkte, Datenmodell, Feature-Beschreibungen) steht in `docs/` und wird gelesen, wenn man im Bereich arbeitet.

- **Neue oder geänderte Feature-Doku** → in die passende Datei unter `docs/api/` (neuer Bereich = neue Datei + Zeile
  in der Liste unten). **Nie** Endpunkt-Tabellen oder Feature-Prosa in die CLAUDE.md schreiben.
- Verweise im Code wie „siehe CLAUDE.md, Abschnitt X“ meinen die gleichnamige Überschrift in einer dieser Dateien:
  `grep -rl "^# X" docs/` findet sie.
- Datenbank-Schema: `docs/datenbank-schema.md` · Oberflächen (RookHub/Turnier/KidHub, Filterleiste): `docs/oberflaechen.md`
- Konventionen im Volltext (mit Begründung und Fallen): `docs/konventionen.md` · CI/CD im Detail: `docs/ci.md`
- Frontend-Regeln: `src/frontend/CLAUDE.md` (lädt automatisch, sobald man dort arbeitet); Frontend-Feature-Doku: `docs/frontend/` · Gestaltung: `docs/DESIGN.md` (verbindlich)

### Endpunkte und Features je Bereich (`docs/api/`)

- Auth (offen, kein JWT nötig) → `docs/api/auth.md`
- Profil (auth) → `docs/api/profil.md`
- Freunde (auth) → `docs/api/freunde.md`
- Puzzle-Challenges (auth) — „schick dieses Puzzle an Freunde" → `docs/api/puzzle-challenges.md`
- Revenge-Benachrichtigungen (auth) — Ziel-User über Revanche informieren → `docs/api/revenge-benachrichtigungen.md`
- Benachrichtigungen / Glocke (auth) — generischer In-App-Strom → `docs/api/benachrichtigungen-glocke.md`
- Direktnachrichten Admin↔User (auth) → `docs/api/direktnachrichten-adminuser.md`
- Repertoires (auth) → `docs/api/repertoires.md`
- Lochfinder + „Häufigste zuerst" (Lichess-Explorer, 0.502.0) → `docs/api/lochfinder-haeufigste-zuerst.md`
- Extension API (auth, CORS für chess.com) → `docs/api/extension-api.md`
- Gespeicherte Partien (auth + öffentlicher Teilen-Link) → `docs/api/gespeicherte-partien.md`
- Turnier-Proxy (leitet an Crawler weiter; lesen und Turnier holen ohne Anmeldung) → `docs/api/turnier-proxy.md`
- Chessable-Integration (auth, leitet an piratechess-API weiter) → `docs/api/chessable-integration.md`
- Turnierverlauf (auth) → `docs/api/turnierverlauf.md`
- Turnier-Abos + Favoriten + Monitor (auth) → `docs/api/turnier-abos-favoriten-monitor.md`
- Turnierverzeichnis / Turnierkalender (lesen ohne Anmeldung, schreiben mit Konto) → `docs/api/turnierverzeichnis-turnierkalender.md`
- Anzeige-Zustand einer Seite je NUTZER (auth) → `docs/api/anzeige-zustand-einer-seite-je-nutzer.md`
- Book-Puzzles (offen + Admin) → `docs/api/book-puzzles.md`
- KidHub — Kinderseite (offen, 0.554.0) → `docs/api/kidhub.md`
- ClubHub — Kartei der Kinder und Jugendlichen des Vereins (0.613.0) → `docs/api/clubhub.md`
- LeagueHub — Tiroler Ligen, Aufstellungs-Prognosen (Admin + öffentliche Teilen-Links, 0.569.0) → `docs/api/leaguehub-tiroler-ligen-aufstellungs-prognosen.md`
- LeagueHub — Vereine als Mandanten (0.698.0) → `docs/api/leaguehub-vereine-als-mandanten.md`
- LeagueHub — Aufstellungen je Runde + erste Züge (0.721.0) → `docs/api/leaguehub-aufstellungen-je-runde-erste-zuege.md`
- LeagueHub — Vereins-Datenbank (0.573.0, Übersicht/Teilen-Link/Megabase 0.574.0) → `docs/api/leaguehub-vereins-datenbank.md`
- Spielervorbereitung (Prep) — jeder Spieler des Partiebestands (0.631.0–0.639.0) → `docs/api/spielervorbereitung.md`
- Gruppen (Admin + auth) → `docs/api/gruppen.md`
- Menü-Sichtbarkeit (Admin konfiguriert, je Nutzer aufgelöst) → `docs/api/menue-sichtbarkeit.md`
- Endless Puzzle Sync (auth + anon) → `docs/api/endless-puzzle-sync.md`
- Kurse (auth, gruppen-/admin-gated) → `docs/api/kurse.md`
- Kurs-Detailseite + Inhaltspflege (auth) → `docs/api/kurs-detailseite-inhaltspflege.md`
- Aufgabenblätter (auth) — Stellungen sammeln, ordnen, drucken → `docs/api/aufgabenblaetter.md`
- Kalkulations-Modus (auth) — Stellungen ohne Lösung → `docs/api/kalkulations-modus.md`
- Wochenpost (öffentlich lesbar, durchspielbar mit Login, Admin verwaltet) → `docs/api/wochenpost.md`
- Bot-Stats (Bot-intern, HMAC-signiert) → `docs/api/bot-stats.md`
- Endspiel-Datenbank (Tablebase, offen, 0.729.0) → `docs/api/endspiel-datenbank.md`
- Externe Engine (auth) — eigener Broker ODER Lichess-External-Engine-Protokoll als CLIENT → `docs/api/externe-engine.md`
- Eigener Engine-Broker (0.537.0) — der Provider spricht direkt mit RookHub → `docs/api/eigener-engine-broker.md`
- Hintergrund-Analyseaufträge (auth) — „diese Stellung rechnen, sobald die Hintergrund-Engine frei ist" → `docs/api/hintergrund-analyseauftraege.md`
- Analyse-Verlauf + Sterne (0.603.0, auth) → `docs/api/analyse-verlauf-sterne.md`
- Maia-Sparring im Analysebrett (0.632.0) → `docs/api/maia-sparring-im-analysebrett.md`
- Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?" → `docs/api/zuege-vergleichen.md`
- Knotenziel + „nur auf Anforderung"-Engines (2026-10-05, Phase 1 der Lc0-Zweitprüfung, UNGETAGGT) → `docs/api/knotenziel-nur-auf-anforderung-engines.md`
- Endstellung in Partie-Analysen (0.689.0) → `docs/api/endstellung-in-partie-analysen.md`
- „Tiefe Analyse" einer Stellung (0.686.0) — `DeepAnalysisService`, `POST /api/deep-analysis` → `docs/api/tiefe-analyse-einer-stellung.md`
- Vereinspartien zusätzlich auf Lc0 (0.685.0) — `ClubSecondEngineScheduler` → `docs/api/vereinspartien-zusaetzlich-auf-lc0.md`
- Meisterpartien im Hintergrund analysieren (2026-09-28) — `MasterAnalysisScheduler` → `docs/api/meisterpartien-im-hintergrund-analysieren.md`
- Punktepartie (`/guess`) — eine Meisterpartie Zug fuer Zug erraten → `docs/api/punktepartie.md`
- Rohbestand (`LibraryGames`) — der Vorrat, aus dem die Punktepartie ausgewaehlt wird → `docs/api/rohbestand.md`
- Partie anfordern (`/api/library-games`) — der Rohbestand als Nachschlagewerk → `docs/api/partie-anfordern.md`
- „Frag die Kommentare" — semantische Suche im Rohbestand (0.536.0) → `docs/api/frag-die-kommentare.md`
- Stellungsfilter (`/api/guess-tree`) — der Eroeffnungsbaum der Punktepartie → `docs/api/stellungsfilter.md`
- Anmerkungen in mehreren Sprachen (`CommentSets`) → `docs/api/anmerkungen-in-mehreren-sprachen.md`
- Partieformular einlesen (auth, 0.529.0) → `docs/api/partieformular-einlesen.md`
- Partie rekonstruieren (auth) → `docs/api/partie-rekonstruieren.md`
- Client-Diagnostik (offen) → `docs/api/client-diagnostik.md`
- Bestenlisten (auth) → `docs/api/bestenlisten.md`
- Trainingsziele (auth) → `docs/api/trainingsziele.md`

## Projektstruktur

```
compose.dev.yml             Dev-Stack ohne VPN (MariaDB + Crawler + API + Frontend)
compose.vpn.yml             Prod-Stack mit Gluetun VPN (WireGuard)
init-db.sh                  Erstellt beide DBs + User beim ersten MariaDB-Start
.env.dev.example            Umgebungsvariablen-Template (Development)
.env.vpn.example            Umgebungsvariablen-Template (VPN/Production)
twa/                        Android-TWA-Build-Gerüst (Bubblewrap, GH-Action — prod + dev-Variante)
engine-provider/            Docker-Setup für den RECHNER DES NUTZERS: verbindet lokales Stockfish
                            direkt mit RookHub (eigener Broker) oder über den Lichess-Broker mit dem
                            Analysebrett (läuft NICHT im Stack)
src/
  api/RookHub.Api/
    Controllers/            <Bereich>/ wie Services/; direkt darin BaseApiController + Infrastruktur (ClientLog, Ci, BotHeartbeat)
    Services/               <Bereich>/ je Fachbereich (siehe „Arbeitsweise“ → Dienste nach Fachbereich),
                            direkt darin nur gemeinsame Bausteine (PGN/FEN, PeriodicWorker, Rate-Limiter, LLM-Clients)
    Models/                 EF-Entities (1:1 zum Schema oben)
    DTOs/                   <Bereich>/ Request/Response-Typen je Endpoint-Familie
    Data/                   AppDbContext, DesignTimeDbContextFactory, Migrations/
    Program.cs              Startup: DB, JWT+ApiToken Policy-Scheme, CORS, Swagger,
                            Auto-Migration, Health-Endpoint, BackgroundServices,
                            ForwardedHeaders (private Peers only)
    Dockerfile              Multi-stage .NET Build
  frontend/
    app/                    Angular-Workspace mit ZWEI Projekten (siehe src/frontend/CLAUDE.md):
                            src/ = RookHub, src-turnier/ = Turnierseite (Alias @rh/* teilt den Code)
    nginx.conf              Proxy /api/ → api:8080, OSM-Kachel-Cache, OG-Weiche, Scanner-404, SPA-Fallback
                            (dieselbe Datei in BEIDEN Images)
    Dockerfile              Multi-stage Node Build + nginx; `--build-arg APP_PROJECT=turnier`
                            baut daraus das Turnier-Image
tests/
  RookHub.Api.Tests/        xUnit, eine Testklasse je Controller/Service
                            (Helpers: CapturingLogger, TestLogger, NoOpTaskQueue,
                             DiscordTokenTestHelper)
```

## Lokales Development

### Kompletter Stack via Docker
```bash
# Development (ohne VPN):
docker compose -f compose.dev.yml --env-file .env.dev up --build

# Production (mit Gluetun VPN):
docker compose -f compose.vpn.yml --env-file .env.vpn up --build
```

| Port | Dienst | URL |
|------|--------|-----|
| 8085 | Frontend (nginx) | http://localhost:8085 |
| 5001 | RookHub API | http://localhost:5001/swagger |
| 8080 | Crawler API | http://localhost:8080/swagger/ui/index.html |
| 3306 | MariaDB | Host: localhost, DBs: `chessresults` + `rookhub` |
| 9200 | Elasticsearch | http://localhost:9200 |
| 5601 | Kibana | http://localhost:5601 |

### Angular standalone (ohne Docker)
```bash
cd src/frontend/app
npm install
npx ng serve    # http://localhost:4200, braucht API auf :5001
```

### API standalone (ohne Docker, braucht MariaDB auf :3306)
```bash
cd src/api/RookHub.Api
dotnet run
```

### Tests

**Pflicht**: Jedes neue Feature, jeder neue Endpoint und jeder Bugfix MUSS mit mindestens einem Test abgedeckt werden. Kein PR/Commit ohne passenden Test.

> **`dotnet` ist installiert, aber NICHT im PATH** — liegt unter `/home/kahalm/.dotnet/dotnet`.
> Vor `dotnet`-Befehlen daher: `export PATH="$HOME/.dotnet:$PATH"` (ggf. `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`).
> **Achtung Test-Lücke:** Tests laufen gegen die EF **InMemory-DB** (LINQ-to-Objects) und stellen die
> **MySQL/Pomelo-SQL-Übersetzung NICHT nach**. Übersetzungsfehler (z. B. `EF.Functions.Like` in
> handgebauten Expression-Trees, raw SQL, provider-spezifische Funktionen) fallen erst gegen echtes
> MariaDB auf — solche Änderungen zusätzlich auf Dev verifizieren.

```bash
export PATH="$HOME/.dotnet:$PATH"
cd tests/RookHub.Api.Tests
dotnet test
```

**Tests gezielt** (schnelle Rückmeldung — erst den Bereich, die ganze Suite nur vor dem Push größerer Änderungen):
```bash
dotnet test --filter "FullyQualifiedName~GameAccuracy"              # eine Klasse / ein Namensteil
dotnet test --filter "FullyQualifiedName~RookHub.Api.Tests.Courses"  # ein ganzer Bereich (Ordner)
cd src/frontend/app && npx ng test app --watch=false --include='src/app/features/games/**/*.spec.ts'
```

### Test-Pattern
- **InMemory DB** pro Testklasse via `UseInMemoryDatabase(Guid.NewGuid().ToString())`
- **IDisposable** für DB-Cleanup
- **xUnit `[Fact]`** Attribute
- **Namenskonvention**: `MethodName_Scenario_ExpectedResult`
- **Service-Tests** testen direkt gegen InMemory-DB
- **Controller-Tests** instanziieren den Controller direkt; `BaseApiController.GetUserId()` wird via `ControllerContext` mit `ClaimsPrincipal` + `ClaimTypes.NameIdentifier` gemockt
- **Helper-Methode** `CreateUserAsync()` pro Testklasse für Test-Daten
- **InMemory cascaded nicht** — Admin-Delete-Pfade räumen abhängige Daten explizit ab; Tests entsprechend prüfen
- **Integrationstests: Aufbau je KLASSE, nicht je Test** (seit 0.496.1) — xUnit legt je Testmethode eine neue Instanz der Testklasse an. Stand `MariaDbSchema.CreateAsync` + `MigrateAsync` + `new ApiFactory` direkt in deren `InitializeAsync`, lief das je TEST: gemessen 21 vollständige Durchläufe aller 139 Migrationen und 13 Anwendungsstarts für 22 Tests, von denen 18 zwischen 14 ms und 945 ms dauern. Der Aufbau gehört deshalb in eine `MariaDbClassFixture` (`IClassFixture<…>`); je Test ruft die Klasse nur `fixture.ResetAsync()` (leert jede Tabelle, Fremdschlüssel währenddessen aus) und holt sich einen frischen DI-Scope. **Die Isolation bleibt** — jeder Test beginnt auf einer leeren Datenbank —, aber die AUTO_INCREMENT-Zähler laufen über die Tests hinweg weiter: ein Test, der sich auf „die erste Id ist 1" verlässt, fällt damit sofort auf. Eine neue Testklasse braucht eine eigene Fixture-Unterklasse (`IClassFixture<T>` unterscheidet nach TYP); wer die Anwendung hochfährt, gehört zusätzlich in `ApiFactoryCollection`. Lokal gemessen: 9:45 → 1:54

## EF Core Migrations

```bash
cd src/api/RookHub.Api
dotnet ef migrations add <MigrationName>    # Nutzt DesignTimeDbContextFactory
dotnet ef database update                   # Braucht laufende MariaDB
```
Auto-Migration ist in `Program.cs` aktiv – beim Start werden Migrations automatisch angewendet.

## UI-Sweep (Screenshots aller Seiten, `tools/ui-sweep`)

Rendert jede Seite aller fünf Oberflächen in Chromium, prüft sie automatisch (Seite zu breit, Elemente ragen raus, Text
abgeschnitten, Übersetzungsschlüssel statt Text, Ladekreis steht, Konsolen-/API-Fehler) und vergleicht mit dem vorigen
Lauf (Pixel-Diff). `cd tools/ui-sweep && ./sweep.sh` (Dev, Handy + breit, dunkel, ab- und angemeldet), `--area kurse,repertoire`
für einzelne Bereiche (`--areas` listet sie), `--local` für den lokalen Build gegen die Dev-API, `--help`. Ergebnis in
`runs/<Zeitpunkt>/index.html` (nicht im Repo). Anleitung und Fallen: `tools/ui-sweep/README.md`. Zwei Dinge, die dabei
nicht kippen dürfen: (1) Die API drosselt je Adresse (Prod 100/min; Dev seit 11.10.2026 ×5 = 500/min über
`RateLimiting__PermitScale` in der Dev-compose) — das Werkzeug bremst sich selbst (`--rate`, Vorgabe Dev 400/min + 4 Browser, Prod 70/min + 2; `--fast` = Seite einmal laden, alle Breiten darauf; Volllauf Dev ~11 min, fast ~7 min),
schaltet die Offline-Vorräte der App ab und pausiert nach einem 429 eine Minute; ein Bild mit gebremsten Anfragen trägt
`toolDelayMs` und zeigt evtl. einen Ladezustand, der NICHT von der App kommt. (2) Testdaten legt `seed.mjs` für `claude-dev`
an, `seed-user.mjs` für das normale Konto; Rollen `--auth anon,user,admin` (Standard) sowie `empty`/`trainer` (alles mit „ui-sweep" im Namen, wird wiedergefunden). Neue Route → in `routes.mjs` eintragen und einem Bereich zuordnen. Design-Durchsicht gegen `docs/DESIGN.md`: Skill `/ui-design-check`.

## Offene Aufgaben

Nicht direkt angegangene Bugs, geparkte Features, Refactoring-Ideen und periodische Aufgaben (Code Review, Security Review etc.) werden in **`rookhub/TODO.md`** geführt. Neue Punkte dort eintragen, nicht separat als Markdown-Datei anlegen.

## Arbeitsweise

- **PFLICHT: `git pull` vor jedem Edit** — sobald du anfängst, Dateien auf der Platte zu ändern, MUSS unmittelbar davor ein `git pull` (bzw. `git pull --rebase`) laufen. Beide Stack-Kopien + diese Windows-Workstation arbeiten parallel am selben Remote; ein Edit auf einem N Versionen alten Stand führt unweigerlich zu Merge-Konflikten und verlorener Arbeit (passiert vor v0.95.2 mit 10 verpassten Commits). Lesen/Recherchieren ohne Pull ist OK; sobald du `Edit`/`Write` greifst → vorher pullen.
- **Commit early, commit often** – nach jedem abgeschlossenen Feature, Fix oder logischen Schritt committen. Kleine, atomare Commits sind besser als ein großer Sammel-Commit.
- **Tags NUR auf Zuruf** – NIEMALS automatisch Git-Tags erstellen. Der User muss vorher testen und explizit nach einem Tag fragen.
- **IMMER erst `git fetch`/`pull` vor jedem Tag** – ein Tag zeigt auf einen konkreten Commit; wegen der zwei Stack-Kopien am selben Remote ist der lokale HEAD oft veraltet. Vor dem Taggen `git fetch` und den AKTUELLEN `origin/master`-HEAD taggen (dessen `APP_VERSION` aus `changelog.ts` = Tag-Name), sonst zeigt der Tag auf einen alten Stand OHNE die zwischenzeitlich von der anderen Kopie gepushten Features → das `:latest`-Prod-Image ist dann unvollständig (passiert 2026-07-06: v0.266.0 getaggt, während master schon auf 0.270.0 mit dem Chapter-Feature stand).
- **CI/CD**: Docker-Images baut GitHub Actions nach jedem Push (kein manueller Build). Jobs sind **pfadgefiltert**
  (`.github/filters.yml`, neuer Job braucht einen Filter); nur `vX.Y.Z`-Tags auf einem master-Commit bauen `:latest`;
  `gh workflow run docker.yml` baut alles von Hand (schiebt `:dev`). Regeln dazu hängen an `CiWorkflowTests` —
  Details, Vorbau/Umhängen und Begründungen: `docs/ci.md`.
- **Große Dateien beim Anfassen verkleinern** – berührt eine Aufgabe eine Datei mit über 1 000 Zeilen, ZUERST einen in
  sich geschlossenen Teil herausziehen (Angular: Unterbereich als Kind-Komponente, Zustand/Logik in einen Dienst; C#:
  ein abgegrenzter Schritt als eigene Klasse) — eigener Commit, kein Verhalten geändert, Tests grün; erst danach die
  eigentliche Änderung. Kein eigener Zerlege-Durchgang, nur dort, wo ohnehin gearbeitet wird. Aufrufer über das
  LSP-Werkzeug (`findReferences`) suchen, nicht per grep.
- **Tests spiegeln die Bereiche**: `tests/RookHub.Api.Tests/<Bereich>/`, Namespace `RookHub.Api.Tests.<Bereich>`; geteilte
  Hilfen (`TestServices`, `TestLogger`, Fakes …) und Infrastruktur-Tests liegen direkt im Testordner.
- **Dienste, Controller, DTOs und Tests liegen nach Fachbereich in Unterordnern** (`Services/<Bereich>/`,
  `Controllers/<Bereich>/`, `DTOs/<Bereich>/`, `tests/…/<Bereich>/`; Namespace jeweils `….<Bereich>`, z. B. `RookHub.Api.Services.<Bereich>`,
  Nutzer binden ihn per `using` ein — kein `global using`). Bereiche: Accounts, Chessable, ChessBase, Club, Courses,
  EngineBroker, Engines, Games, Kids, League, Library, Og, Prep, Puzzles, Repertoires, Scoresheet, Social, Tactics,
  Tournaments, TournamentDirectory. **Gemeinsame Bausteine bleiben direkt in `Services/`** (PGN/FEN-Werkzeuge,
  `PeriodicWorker`, Rate-Limiter, `Paging`, LLM-Clients …): der Eltern-Namespace ist aus jedem Bereich ohne `using`
  sichtbar. Abgesichert durch `ArchitectureGuardTests` (Ort, Namespace = Ordner,
  keine Dienst→Controller-/Model→Dienst-Abhängigkeit, DTO→Dienst nur per Erlaubt-Liste). Neue Dienste in den Ordner ihres Bereichs; ein neuer Bereich = eigener Commit, nur `git mv` + Namespace +
  `using`s, vorher der anderen Sitzung ankündigen.
- **NIEMALS automatisch deployen** — weder auf Dev noch auf Prod. Der User startet Deploys immer selbst explizit.

## Versionierung

- **Aktuelle Version**: siehe `APP_VERSION` in `src/frontend/app/src/environments/changelog.ts` (Single Source; Footer zeigt sie). Vollständiger Verlauf ausschließlich dort — NICHT in CLAUDE.md duplizieren.
- `environment.ts` (dev) UND `environment.prod.ts` (prod-Build via fileReplacements) importieren beide aus `changelog.ts` — Footer zeigt in jedem Build dieselbe Version. **Nur `changelog.ts` editieren**, nie die Environment-Dateien
- Angezeigt im Footer der Desktop-Version (Klick öffnet Changelog-Overlay)
- **Jeder Fix/jedes Feature MUSS die Version erhöhen**: Patch für Fixes (0.0.x), Minor für Features (0.x.0)
- **Changelog pflegen**: Jeden Eintrag im `CHANGELOG`-Array in `changelog.ts` vermerken (Version, Datum, Liste der Änderungen). **Jeder Änderungstext gehört ZWEISPRACHIG hin** — pro Eintrag `changes: { en, de }[]` (Englisch = Default/Fallback, Deutsch). Der Footer zeigt die Variante der aktiven UI-Sprache (`changeText()` in `app.component`; `hr` fällt auf `en` zurück). Neue Einträge also IMMER mit `en` UND `de` anlegen, nicht nur eine Sprache
- **Gilt auch für Änderungen im Crawler-Repo** (`C:/git/chessresults_crawler`): Features/Fixes dort müssen ebenfalls hier Version + Changelog erhöhen und committet werden
- **Parallel-Arbeit**: Wegen der zwei Stack-Kopien (siehe Lock-Block oben) können Versionssprünge nicht-monoton wirken — beim Commit immer den **aktuellen** `APP_VERSION`-Wert aus `changelog.ts` als Basis nehmen, nicht den Commit-Subject-Wert

### Checkliste vor JEDEM Commit (beide Projekte)
1. [ ] Tests vorhanden für die Änderung?
2. [ ] `APP_VERSION` + `CHANGELOG`-Eintrag in `src/frontend/app/src/environments/changelog.ts` aktualisiert? (gilt automatisch für dev + prod-Build)
3. [ ] `Aktuelle Version` in diesem Abschnitt angepasst?
4. [ ] Versionsänderung committet?
5. [ ] **Nach jedem Commit dem User die aktuelle Version mitteilen** (z.B. "Version: 0.95.2")

**NIEMALS committen ohne diese Checkliste abzuarbeiten.** Auch reine Test- oder Doku-Änderungen erhöhen die Patch-Version.

## Screenshots

- Screenshots liegen in `C:/git/screenshot/` (z.B. `Screenshot.jpg`)
- Diesen Pfad nutzen um visuelle Prüfungen durchzuführen

## Wichtige Konventionen (Kurzfassung — Volltext mit Begründung: `docs/konventionen.md`)

- **Gestaltung folgt `docs/DESIGN.md` — PFLICHT** (seit 2026-10-11): neue/geänderte UI aller fünf Oberflächen hält sich an
  die allgemeinen Regeln (Abschnitte 2–7) und die Bereichsregeln (Abschnitt 8); vorher lesen. Jede neue
  Gestaltungsentscheidung kommt im selben Commit in den passenden Abschnitt UND ins Änderungsprotokoll. Bestand
  weicht ab → „Regel ab jetzt“ (Abschnitt 10). Nicht geregelt → User fragen, dann eintragen. Prüfen: Skill `/ui-design-check`.

Vor Arbeit an einem dieser Punkte den Volltext lesen.

- 401 ist nicht gleich Rauswurf
- Eine selbst geoeffnete Transaktion MUSS in der Execution-Strategy laufen
- Kurs ⇄ Repertoire liegt in `Services/Courses/CourseRepertoireConversionService.cs`
- Handgespiegelte Listen bekommen auf BEIDEN Seiten einen Test mit LITERALEN Werten
- Versuchs-Werte kommen aus `Services/Puzzles/AttemptRecording.cs`
- PGN wird mit `Services/PgnWriter.cs` geschrieben
- DER PGN-Baumparser ist `Services/PgnMoveTree.cs`
- Statistik-Kennzahlen kommen aus `Services/Puzzles/AttemptStats.cs`
- Import-/Aufbereitungs-Pipeline versionieren
- Kalkulations-Modus ist KEIN Solver
- Vollbild-Brett gehört in die BRETT-Komponenten
- UI-Dichte-Regel (seit UI-Welle 2/3, v0.334.0)
- Puzzle-Modi konsistent halten
- Buch-/Kurs-FENs sind nicht immer legal
- Platzhalter-Geheimnisse gelten nirgends als Schlüssel
- Erwartete Fehler als Domänen-Ausnahme, nicht als BCL-catch im Controller
- Periodische Hintergrunddienste erben von `PeriodicWorker`
- Fluent-Konfiguration neuer Entitäten gehört in `Data/Configurations/`
- Literal-Routen vor Parameter-Routen
- Scanner-Pfade bekommen 404, nicht die Startseite

**Regeln, die automatisch laden**, sobald eine passende Datei gelesen/bearbeitet wird (`.claude/rules/`, Pfade im Kopf
der Datei): `migrations.md` (EF-Migrationen, leere `Up()`), `changelog.md` (Anführungszeichen, Versionsvergabe),
`i18n.md`, `compose.md`, `book-source.md` (`SourcePgn`), `llm-text-clients.md` (Schlüssel, Spark, Tipp-Queue),
`turnierverzeichnis.md` (Ortsnamen, Quellen). Neue bereichsspezifische Fallen gehören dorthin, nicht hierher.
