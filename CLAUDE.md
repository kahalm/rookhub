# RookHub

Zentrales Webportal fuer schachrelevante Funktionen: PGN-Repertoire-Verwaltung, Turnierdaten, Benutzerprofile mit FIDE/ChessResults-Verlinkung, Freundeslisten. Gehoert zusammen mit dem **ChessResults Crawler** (`C:/git/chessresults_crawler`) – bei Aenderungen immer beide Projekte beruecksichtigen.

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

### Kritische Abhaengigkeiten zwischen den Projekten
- `Services/CrawlerProxyService.cs` – HTTP-Client zum Crawler, muss Crawler-Routen kennen
- `Controllers/TournamentProxyController.cs` – Mappt RookHub-Routen 1:1 auf Crawler-Routen
- Crawler-Endpoint-Aenderungen muessen in diesen beiden Dateien nachgezogen werden
- Crawler-Response-Strukturen werden als `JsonElement` durchgereicht (kein festes DTO-Mapping)

## Tech Stack

| Komponente | Technologie | Version |
|-----------|-------------|---------|
| Backend Runtime | .NET | 9.0 |
| Web Framework | ASP.NET Core Web API | 9.0 |
| ORM | EF Core + Pomelo | 9.0.0 (Pomelo) / 9.0.5 (EF Design) |
| Datenbank | MariaDB | 11 |
| Auth | JWT Bearer + BCrypt.Net-Next | 9.0.5 / 4.2.0 |
| API Docs | Swashbuckle (Swagger) | 6.9.0 |
| Frontend | Angular | 19.2 |
| UI Library | Angular Material | 19.2.19 |
| Frontend Webserver | nginx (alpine) | latest |
| Logging | Serilog + Elasticsearch Sink | 9.0.0 / 10.0.0 |
| Log-Speicher | Elasticsearch | 8.17.0 |
| Log-Visualisierung | Kibana | 8.17.0 |
| Tests | xUnit + InMemory DB | - |

**Hinweis**: RookHub nutzt Swashbuckle 6.9.0 (nicht 10.x) wegen Kompatibilitaet mit .NET 9's OpenAPI-Namespace.

## REST API

### Auth (offen, kein JWT noetig)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung (username, email, password) |
| POST | `/api/auth/login` | Login, gibt JWT zurueck |

### Profil (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/profile` | Eigenes Profil |
| PUT | `/api/profile` | Profil bearbeiten |
| GET | `/api/profile/{username}` | Oeffentliches Profil (auch ohne Auth) |
| GET | `/api/profile/player-search?lastName=&firstName=` | Spielersuche (ChessResults + FIDE) |
| POST | `/api/profile/discord/link` | Discord verknüpfen via bot-signiertem Token `{ token }` (400 ungültig/abgelaufen, 409 Discord-ID schon vergeben) |
| DELETE | `/api/profile/discord` | Discord-Verknüpfung trennen |
| GET | `/api/profile/tokens` | Eigene API-Tokens (ohne Raw-Token) |
| POST | `/api/profile/tokens` | Neuen Token anlegen `{ name, expiresInDays?, scope? }` — Raw-Token nur einmalig im Response |
| DELETE | `/api/profile/tokens/{id}` | Token widerrufen |

### Freunde (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/friends` | Freundesliste |
| GET | `/api/friends/requests` | Offene Anfragen |
| POST | `/api/friends/request/{userId}` | Anfrage senden |
| POST | `/api/friends/accept/{friendshipId}` | Annehmen |
| POST | `/api/friends/decline/{friendshipId}` | Ablehnen |
| DELETE | `/api/friends/{friendshipId}` | Entfernen |
| GET | `/api/friends/search?q={query}` | User suchen (min. 2 Zeichen) |

### Repertoires (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/repertoires` | Alle eigenen Repertoires |
| POST | `/api/repertoires` | Neues Repertoire erstellen |
| GET | `/api/repertoires/{id}` | Repertoire mit Dateien |
| PUT | `/api/repertoires/{id}` | Metadaten aendern |
| DELETE | `/api/repertoires/{id}` | Loeschen |
| POST | `/api/repertoires/{id}/files` | PGN hochladen (multipart, max 10 MB) |
| GET | `/api/repertoires/{id}/files/{fileId}` | PGN herunterladen |
| DELETE | `/api/repertoires/{id}/files/{fileId}` | Datei loeschen |
| GET | `/api/repertoires/{id}/pgn` | Alle PGNs kombiniert |

### Extension API (auth, CORS fuer chess.com)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/extension/repertoires?kind=opening` | Leichtgewichtige Liste (id, name, fileCount, kind, totalSizeBytes); `kind` filtert auf `none|opening|middlegame|endgame` |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |

Akzeptiert sowohl JWT (User-Login) als auch ApiToken (`Authorization: Bearer rkh_…`). Bei ApiToken muss `scope=extension` sein (sonst 403). Policy-Scheme im Auth-Stack routet das Bearer-Format automatisch zum passenden Handler.

CORS (`ExtensionPolicy`, nur fuer `ExtensionController`): erlaubt ausschliesslich `https://www.chess.com`, nur `GET`, ohne `AllowCredentials` (Auth strikt ueber Bearer-Header). Die Default-CORS-Policy (Frontend) erlaubt `http://localhost:4200` + `http://localhost:8085`.

### Turnier-Proxy (auth, leitet an Crawler weiter)
| Methode | Endpoint | Crawler-Route |
|---------|----------|---------------|
| GET | `/api/tournaments` | `/api/tournaments` |
| GET | `/api/tournaments/{id}` | `/api/tournaments/{id}` |
| GET | `/api/tournaments/{id}/players?team=&sortBy=` | `/api/tournaments/{id}/players` |
| GET | `/api/tournaments/{id}/teams` | `/api/tournaments/{id}/teams` |
| GET | `/api/tournaments/{id}/pairings?round=` | `/api/tournaments/{id}/pairings` |
| GET | `/api/tournaments/{id}/players/{snr}/results` | `/api/tournaments/{id}/players/{snr}/results` |
| GET | `/api/tournaments/{id}/rounds/check` | `/api/tournaments/{id}/rounds/check` |
| POST | `/api/tournaments/crawl` | `/api/tournaments/crawl` |
| POST | `/api/tournaments/crawl/player-details` | `/api/crawl/player-details` |

**Achtung**: Der Crawler nutzt `/api/crawl` (nicht `/api/tournaments/crawl`). Der Proxy-Endpoint muss ggf. angepasst werden.

### Turnier-Abos (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/subscriptions` | Meine abonnierten Turniere |
| POST | `/api/subscriptions` | Turnier abonnieren |
| DELETE | `/api/subscriptions/{id}` | Abo entfernen |

### Book-Puzzles (offen + Admin)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/book-puzzles/{id}` | AllowAnonymous | Puzzle by ID |
| GET | `/api/book-puzzles/{id}/next` | AllowAnonymous | Nächstes Puzzle im selben Buch (Loop am Ende) |
| GET | `/api/book-puzzles/{id}/random` | AllowAnonymous | Zufälliges Puzzle aus demselben Buch |
| POST | `/api/book-puzzles/{id}/attempt` | Auth | Lösungsversuch erfassen `{ solved, timeSeconds }` (Tagespuzzle) |
| GET | `/api/book-puzzles/{id}/results?since=` | AllowAnonymous | Solver-Liste (je User, inkl. Discord) + Versuchs-/Lösungszähler |
| GET | `/api/book-puzzles/daily/{date}` | AllowAnonymous | Tagespuzzle fuer ein UTC-Datum (`yyyyMMdd` oder `today`); legt on-demand eine persistierte Zuordnung in `DailyPuzzles` an (deterministisch ab da) |
| GET | `/api/book-puzzles/by-line-id?lineId=xxx` | AllowAnonymous | Lookup fuer schach-bot |
| GET | `/api/book-puzzles/books` | AllowAnonymous | Buch-Liste mit Counts |
| POST | `/api/admin/book-puzzles/import` | Admin | Bulk-Import aus JSON |

### Gruppen (Admin + auth)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/groups` | Admin | Alle Gruppen inkl. MemberCount |
| POST | `/api/admin/groups` | Admin | Gruppe anlegen (name, description) |
| PUT | `/api/admin/groups/{id}` | Admin | Gruppe umbenennen / Beschreibung |
| DELETE | `/api/admin/groups/{id}` | Admin | Gruppe + Mitgliedschaften loeschen |
| GET | `/api/admin/groups/{id}/members` | Admin | Mitglieder einer Gruppe |
| POST | `/api/admin/groups/{id}/members/{userId}` | Admin | User zur Gruppe hinzufuegen (idempotent) |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | User aus Gruppe entfernen |
| GET | `/api/admin/groups/{id}/training-goal` | Admin | Trainingsziel-Vorlage der Gruppe (Source "none" wenn keine) |
| PUT | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage setzen/aktualisieren (4 Minuten-/Tage-Werte) |
| DELETE | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage entfernen |
| GET | `/api/my-groups` | Auth | Gruppen-Namen des eingeloggten Users (gruppenabhaengige Anzeige) |

### Endless Puzzle Sync (auth + anon)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/endless/progress` | Auth | Progress + Sessions laden (single call) |
| GET | `/api/endless/history?page=&pageSize=&archived=` | Auth | Paginierte Session-History (archived: bool-Filter) |
| PUT | `/api/endless/progress` | Auth | Config + Highscore + Active Game upsert |
| POST | `/api/endless/archive` | Auth | Sessions archivieren/unarchivieren |
| GET | `/api/endless/progress/anonymous?sessionId=` | Anon+RL | Anonymer Progress |
| PUT | `/api/endless/progress/anonymous` | Anon+RL | Anonymer Progress speichern |
| POST | `/api/endless/sessions` | Auth | Session aufzeichnen |
| POST | `/api/endless/sessions/anonymous` | Anon+RL | Anonyme Session aufzeichnen |
| POST | `/api/endless/sessions/bulk` | Auth | Bulk-Import (localStorage-Migration) |
| POST | `/api/endless/sessions/bulk/anonymous` | Anon+RL | Bulk-Import anonym |
| POST | `/api/endless/claim-session` | Auth | Anonyme Daten auf User uebertragen |

### Kurse (auth, gruppen-/admin-gated)
„Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet. Fortschritt pro Buch (gelöste Puzzles / gesamt), geteilt über beide Modi; der Modus bestimmt nur die Reihenfolge. Alles user-bezogen in der DB. **Sichtbarkeit**: Admins sehen alle Bücher; Nicht-Admins nur Bücher, die einer ihrer Gruppen via `BookGroupAccess` freigegeben sind. Zugriff wird je Buch in jedem Endpoint erzwungen (kein Zugriff → 404).
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses` | Auth | Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle) |
| GET | `/api/courses/access` | Auth | `{ hasAccess }` — Basis für die Menü-Sichtbarkeit (Admin: true wenn Bücher existieren) |
| GET | `/api/courses/{bookId}/next?mode=sequential\|random&after=&exclude=` | Auth | Nächstes ungelöstes Puzzle (sequential: Buchreihenfolge, `after` = überspringen; random: zufällig, `exclude` vermeidet Wiederholung); `completed` wenn alle gelöst |
| POST | `/api/courses/{bookId}/results` | Auth | Lösungsversuch aufzeichnen (idempotent); validiert Puzzle↔Buch |
| GET | `/api/courses/{bookId}/puzzles` | Auth | Alle Puzzles eines (zugänglichen) Buchs am Stück — für Offline-Speichern |
| POST | `/api/courses/{bookId}/reset` | Auth | Fortschritt des Kurses zurücksetzen |

Buch↔Gruppe-Freigabe verwaltet der Admin:
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/books/{id}/groups` | Admin | Gruppen-Ids mit Kurs-Zugriff auf das Buch |
| PUT | `/api/admin/books/{id}/groups` | Admin | Vollständige Gruppen-Freigabe setzen (ersetzt; ungültige Ids ignoriert) |

### Wochenpost (öffentlich lesbar, Admin verwaltet)
Bildet die wöchentlichen schach-bot-Posts auf RookHub ab: ein PGN + Termin (Datum + Uhrzeit). Termin-Vorschlag (letzter + 7 Tage, gleiche Uhrzeit, Standard 19:00) macht das Frontend aus der Liste. PGN-Validierung via `RepertoireService.LooksLikePgn`.
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/weekly-posts` | AllowAnonymous | Liste (ohne PGN), nach Termin absteigend |
| GET | `/api/weekly-posts/{id}` | AllowAnonymous | Detail inkl. PGN |
| GET | `/api/weekly-posts/{id}/puzzles` | AllowAnonymous | Puzzle-Sequenz zum Durchspielen (PGN on-the-fly via `PgnImportService.ParsePgn` geparst) |
| POST | `/api/admin/weekly-posts` | Admin | Upload (multipart: file + scheduledAt + optional title) |
| PUT | `/api/admin/weekly-posts/{id}` | Admin | Termin/Titel ändern |
| DELETE | `/api/admin/weekly-posts/{id}` | Admin | Löschen |

### Client-Diagnostik (offen)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| POST | `/api/client-log` | AllowAnonymous + RL | Client-seitiges Diagnose-Event `{ kind, detail?, url? }` (v. a. Browser-Engine-Crash/Hänger) — wird strukturiert mit Marker „ClientLog" geloggt (→ ES/Kibana), nichts in der DB. Frontend: `ClientLogService` (gedrosselt), Engine-Services melden via `reportEngineEvent`-Hook (in `AppComponent` verdrahtet). |

### Trainingsziele (auth)
Tagesziele je Kategorie (Puzzles/Buch-Kurs/Spielen, in Minuten) + Wochenziel; effektives Ziel = persönlicher Override > zuletzt aktualisierte Gruppen-Vorlage > keins. Tracker aggregiert je UTC-Tag die verbrachte Zeit (Pro-Einzelpuzzle-Clamp 1800 s) und markiert Tage none/partial/full. Logik in `TrainingGoalService`; Admin-Vorlage je Gruppe siehe Gruppen-Tabelle.
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/training-goals` | Auth | Effektives Ziel (`source` personal/group/none, ggf. `groupName`) |
| PUT | `/api/training-goals` | Auth | Persönlichen Override setzen (PuzzleMinutes/BookMinutes/PlayMinutes 0–600, WeeklyDaysTarget 0–7) |
| DELETE | `/api/training-goals` | Auth | Override entfernen → Rückfall auf Gruppen-Vorlage |
| GET | `/api/training-goals/today` | Auth | Heutiger Fortschritt je Kategorie + Tagesstatus + Wochenstand (X/Y Tage) |
| GET | `/api/training-goals/tracker?weeks=27` | Auth | Tagesreihe (nur Tage mit Aktivität) für die Tracker-Heatmap |
| POST | `/api/training-goals/sync-play` | Auth | Externe Spielzeit (Lichess/chess.com) des eigenen Users sofort synchronisieren |

## Datenbank-Schema (eigene DB `rookhub`, nicht geteilt mit Crawler)

| Tabelle | Zweck | Wichtige Felder / Constraints |
|---------|-------|-------------------------------|
| AppUsers | Auth | Username (unique), Email (unique), PasswordHash, CreatedAt |
| UserProfiles | Schach-Identitaet | UserId (1:1 zu AppUser), FideId, ChessResultsId, ChessComUsername, LichessUsername, DisplayName, DiscordId (unique, nullable) + DiscordUsername |
| Friendships | Freundesliste | RequesterId, AddresseeId (unique pair), Status (Pending/Accepted/Declined) |
| Repertoires | PGN-Sammlungen | UserId, Name, Description, IsPublic, CreatedAt, UpdatedAt |
| RepertoireFiles | Einzelne PGNs | RepertoireId, FileName, PgnContent (LONGTEXT), FileSize |
| TournamentSubscriptions | Turnier-Abo | UserId + CrawlerTournamentId (unique pair), TournamentName |
| BookPuzzles | Buch-Puzzles | LineId (unique), BookFileName (indexed), Round, Fen, Moves, Title, Chapter, Comment, Difficulty, BookRating, Tags |
| BookPuzzleAttempts | Buch-/Tagespuzzle-Versuche (eingeloggt) | BookPuzzleId (Restrict) + UserId (Cascade), Solved, TimeSeconds, AttemptedAt; Index (BookPuzzleId, AttemptedAt) + (BookPuzzleId, UserId) |
| DailyPuzzles | Persistierte Tagespuzzle-Zuordnung je UTC-Datum | Date (PK, DATE), BookPuzzleId (Restrict), CreatedAt; einmal pro Tag vom `DailyPuzzleScheduler` (00:00 UTC) gesetzt oder on-demand bei `/daily/{date}` |
| Groups | Benutzergruppen | Name (unique), Description, CreatedAt |
| UserGroups | User<->Gruppe (n:m) | Composite PK (UserId, GroupId), Cascade von AppUser + Group |
| EndlessProgresses | Endless Config+Highscore | UserId (unique, nullable), AnonymousSessionId, StartElo, Themes, FasttrackThreshold1/2, StockfishDepth, Highscore, ActiveGameState (LONGTEXT) |
| EndlessSessions | Abgeschlossene Endless Sessions | UserId (nullable), AnonymousSessionId, Timestamp, TotalSolved, MaxRating, DurationSeconds, ConfigJson (TEXT), MistakeAtRatings |
| CourseProgresses | Per-Kurs-Zustand (Buch) | UserId + BookId (unique pair), LastMode ("sequential"/"random"), CreatedAt, UpdatedAt |
| CoursePuzzleResults | Gelöste Buch-Puzzles im Kurs | UserId + BookPuzzleId (unique pair), BookId (denormalisiert, indexed mit UserId), SolvedAt, TimeSeconds (für Buchstudie-Zeit im Trainingsziele-Tracker) |
| BookGroupAccesses | Welche Gruppe darf welches Buch als Kurs sehen | Composite PK (BookId, GroupId), Cascade von Book + Group, Index GroupId |
| WeeklyPosts | Wochenpost (terminiertes PGN) | Title, FileName, PgnContent (LONGTEXT), FileSize, ScheduledAt (indexed), CreatedAt, UpdatedAt |
| GroupTrainingGoals | Coach-Vorlage Trainingsziel je Gruppe | GroupId (unique, Cascade von Group), PuzzleMinutes, BookMinutes, PlayMinutes, WeeklyDaysTarget, CreatedAt, UpdatedAt |
| UserTrainingGoals | Persönlicher Trainingsziel-Override | UserId (unique, Cascade), PuzzleMinutes, BookMinutes, PlayMinutes, WeeklyDaysTarget, CreatedAt, UpdatedAt |
| PlayTimeDailies | Externe Spielzeit je UTC-Tag/Plattform | UserId + Date + Platform (unique, Cascade), Seconds, UpdatedAt; befüllt vom `PlayTimeSyncService` |
| PlayTimeSyncs | Sync-Cursor externe Spielzeit | UserId + Platform (unique, Cascade), LastGameTimestamp (ms), LastSyncedAt, LastError |
| UserApiTokens | Personal-Access-Tokens für Maschinen-Clients (chess.com-Extension) | UserId (Cascade), Name, TokenHash (SHA-256, UNIQUE), Prefix (12 char), Scope ("extension"), CreatedAt, LastUsedAt, ExpiresAt (nullable); Index (UserId, Name) |

Cascade Deletes: AppUser -> Profile, Repertoires, Subscriptions, EndlessProgresses, EndlessSessions, UserGroups, CourseProgresses, CoursePuzzleResults, UserTrainingGoals, PlayTimeDailies, PlayTimeSyncs; Repertoire -> Files; Group -> UserGroups, BookGroupAccesses, GroupTrainingGoals; Book -> BookPuzzles, CourseProgresses, CoursePuzzleResults, BookGroupAccesses (CoursePuzzleResult.BookPuzzle = Restrict, um doppelte Cascade-Pfade zu vermeiden). Admin-DeleteBook und GroupController.Delete räumen die abhängigen Kurs-/Freigabe-/Ziel-Vorlagen-Daten zusätzlich explizit ab (InMemory-Tests cascaden nicht).
Friendships nutzen Restrict (kein Cascade) wegen zwei FKs zur selben Tabelle.

## Projektstruktur

```
compose.dev.yml             Dev-Stack ohne VPN (MariaDB + Crawler + API + Frontend)
compose.vpn.yml             Prod-Stack mit Gluetun VPN (WireGuard)
init-db.sh                  Erstellt beide DBs + User beim ersten MariaDB-Start
.env.dev.example            Umgebungsvariablen-Template (Development)
.env.vpn.example            Umgebungsvariablen-Template (VPN/Production)
src/
  api/RookHub.Api/
    Controllers/            AuthController, ProfileController, FriendController,
                            RepertoireController, ExtensionController,
                            TournamentProxyController, SubscriptionController
    Services/               AuthService (JWT+BCrypt), ProfileService, FriendService,
                            RepertoireService (CRUD+Upload+CombinedPGN), CrawlerProxyService (HttpClient)
    Models/                 AppUser, UserProfile, Friendship, Repertoire, RepertoireFile, TournamentSubscription
    DTOs/                   AuthDtos, ProfileDtos, FriendDtos, RepertoireDtos, TournamentDtos
    Data/                   AppDbContext, DesignTimeDbContextFactory, Migrations/
    Program.cs              Startup: DB, JWT, CORS, Swagger, Auto-Migration, Health-Endpoint
    Dockerfile              Multi-stage .NET Build
  frontend/
    app/                    Angular 19 CLI Projekt (siehe src/frontend/CLAUDE.md)
    nginx.conf              Proxy /api/ -> api:8080, SPA-Fallback
    Dockerfile              Multi-stage Node Build + nginx
tests/
  RookHub.Api.Tests/        xUnit (18 Tests: Auth, Profile, Friends, Repertoire)
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

```bash
cd tests/RookHub.Api.Tests
dotnet test     # 358 Tests (Auth, Profile, Friends, Repertoire, Subscriptions, Favorites, Monitor, Puzzles, Endless, Books, Groups)
```

### Test-Pattern
- **InMemory DB** pro Testklasse via `UseInMemoryDatabase(Guid.NewGuid().ToString())`
- **IDisposable** fuer DB-Cleanup
- **xUnit `[Fact]`** Attribute
- **Namenskonvention**: `MethodName_Scenario_ExpectedResult`
- **Service-Tests** (FriendService, RepertoireService, AuthService, ProfileService) testen direkt gegen InMemory-DB
- **Controller mit Inline-DB-Logik** (Subscription, Favorites, Monitor) werden direkt als Controller-Instanz getestet
- **BaseApiController.GetUserId()** wird via `ControllerContext` mit `ClaimsPrincipal` + `ClaimTypes.NameIdentifier` gemockt
- **Helper-Methode** `CreateUserAsync()` fuer Test-Daten in jeder Testklasse

### Teststruktur
```
tests/RookHub.Api.Tests/
  UnitTest1.cs                     AuthServiceTests, ProfileServiceTests, FriendServiceTests, RepertoireServiceTests
  SubscriptionServiceTests.cs      SubscriptionController-Tests
  TournamentFavoriteTests.cs       TournamentFavoriteController-Tests
  TournamentMonitorTests.cs        TournamentMonitor DB-Logik
  FriendServiceExtendedTests.cs    Erweiterte FriendService-Tests
  RepertoireServiceExtendedTests.cs Erweiterte RepertoireService-Tests
```

## EF Core Migrations

```bash
cd src/api/RookHub.Api
dotnet ef migrations add <MigrationName>    # Nutzt DesignTimeDbContextFactory
dotnet ef database update                   # Braucht laufende MariaDB
```
Auto-Migration ist in `Program.cs` aktiv – beim Start werden Migrations automatisch angewendet.

## Arbeitsweise

- **Commit early, commit often** – nach jedem abgeschlossenen Feature, Fix oder logischen Schritt committen. Kleine, atomare Commits sind besser als ein grosser Sammel-Commit. So bleibt die History nachvollziehbar und Rollbacks sind einfach.
- **Tags NUR auf Zuruf** – NIEMALS automatisch Git-Tags erstellen. Der User muss vorher testen und explizit nach einem Tag fragen. Tags werden ausschliesslich manuell/auf Anweisung gesetzt.
- **CI/CD**: Docker-Images werden nach Push automatisch gebaut (GitHub Actions). Kein manueller Build noetig.

## Versionierung

- **Aktuelle Version**: `0.84.0` (Trainingsziele-Modus (Trainingsunterstützung für Schüler): User setzen Tagesziele je Kategorie (Puzzles = PuzzleAttempt + EndlessSession + Buch-/Tagespuzzle, Buch/Kurs = neues Feld `CoursePuzzleResult.TimeSeconds`, Spielen = externe Spielzeit) + Wochenziel (volle Tage je ISO-Woche). Coach legt je Gruppe eine Vorlage fest, Schüler überschreiben sie persönlich; effektives Ziel = persönlich > zuletzt aktualisierte Gruppen-Vorlage > keins. Zweiter Aktivitäts-Tracker als Heatmap (Stern = alle Ziele, Teil-Icon = teilweise) neben der Puzzle-Heatmap. `TrainingGoalService` (Tracker-/Today-Aggregation, Pro-Einzelpuzzle-Clamp 1800 s, Tagesstatus none/partial/full), `TrainingGoalController` (`/api/training-goals`: GET/PUT/DELETE, `/today`, `/tracker`, `/sync-play`) + Admin `GET/PUT/DELETE /api/admin/groups/{id}/training-goal`. Spielen-Tracking: `PlayTimeService` (typed HttpClient) holt Lichess exakt (createdAt/lastMoveAt) + chess.com Best-Effort (PGN-Header UTCDate/UTCTime↔EndDate/EndTime) öffentlich ohne Login, `PlayTimeSyncService` (BackgroundService, `PlayTime:IntervalHours`=6) + manueller `/sync-play`-Button. Neue Tabellen GroupTrainingGoals/UserTrainingGoals/PlayTimeDailies/PlayTimeSyncs, Migration `AddTrainingGoals`. i18n in allen 25 Sprachen (en/de/hr ausformuliert, übrige 22 vorerst englischer Fallback bei Key-Parität). 554 BE- + 239 FE-Tests grün. davor: 0.83.0 Google-Play-Vorbereitung & 25 Sprachen: App-Icons 192/512 + maskable + Manifest (installierbare PWA); Konto-Löschung DSGVO (`DELETE /api/profile/account`, anonymisiert Identität+PII, behält Statistik) + öffentliche `/account-deletion`; Datenschutz/Impressum-Seiten (`/privacy`, `/impressum`); Digital Asset Links + TWA-Build-Gerüst (`twa/`, GH-Action) für Google Play; 22 neue Sprachen → 25 (LocaleService + native Labels + RTL ar/fa, alle i18n key-parität); Impressum-Betreiberdaten zentral in `src/environments/operator.ts`. davor: Standard-Puzzle Offline-Pool-Exhausted: separater UI-Pfad mit „Letztes Puzzle nochmal spielen"-Button, sobald der vorab gespeicherte Pool aufgebraucht ist (vs. „nie online geöffnet" = no-cache); neue State-Flag `offlinePoolExhausted` + `lastShownPuzzle`-Tracking, neue i18n-Keys `puzzles.status.offlinePoolExhausted/replayLast` (de/en/hr). 3 neue Specs. davor: 0.82.2 Fix Mobile, Viz-Modus: angetipptes Feld wird vollflächig markiert (chessground-„selected" gelblich + grüner Kreis), chessgrounds eigene Interaktion im Viz-Modus deaktiviert, damit der Tap die Auswahl nicht sofort wieder leert; davor: 0.82.1 Endlos-Config-Screen: Continue/Archive/New-Game-Block oben; davor: 0.82.0 Quick-Stats-Pillen unter dem Brett (Rating/Level/Herzen) — ohne Scrollen sichtbar auf Mobile; davor: 0.81.1 Fix (Mobile, Endlos): Gelöst-Karte spannt jetzt volle Breite — auf Mobile war die Info-Sektion vorher nur min-content breit, weil flex-direction: column ohne explizite width nicht auf 100% streckt; davor: 0.81.0 Tagespuzzle-Datums-Navigation + Dashboard-Link; 0.80.1 Auth-Scheme-Kollision-Fix (Startup-Crash); 0.80.0 Datums-basierte Tagespuzzle-Route; 0.79.1 Extension-Endpoint Scope-Guard + CORS-Härtung; davor: 0.79.0 Extension-Tokens: persönliche API-Tokens (rkh_…-Format, GitHub-PAT-Style) im Profil zum Erstellen/Widerrufen; Tabelle UserApiTokens (SHA-256-Hash, Scope=extension, optionaler Ablauf); Endpoints GET/POST/DELETE /api/profile/tokens; Policy-Scheme im JWT-Pfad routet rkh_… auf den ApiTokenAuthenticationHandler. Vorbereitung für die chess.com-Tampermonkey-Extension. davor: 0.78.0 Repertoire-Kategorie: neues Feld `Repertoire.Kind` (Enum None/Opening/Middlegame/Endgame), Create-Dialog mit Kategorie-Dropdown, farbiger Chip in der Liste; Extension-Endpoint `/api/extension/repertoires?kind=opening` filtert + liefert `totalSizeBytes`; Migration `AddRepertoireKind`; davor: 0.77.0 Gauntlet; davor: Tagespuzzle wird persistiert: neuer Endpoint `GET /api/book-puzzles/daily/{yyyyMMdd|today}` + neue Tabelle `DailyPuzzles` (PK=Date, FK→BookPuzzle); BackgroundService `DailyPuzzleScheduler` legt um 00:00 UTC (+1s) die heutige Zuordnung an, Initial-Catch-up beim API-Start. On-Demand-Fallback wenn Scheduler offline war. `GetRandomAsync(pool="daily")` routet jetzt durch die persistierte Zuordnung — Vergangenheit bleibt stabil, gleiche ID egal wer wann fragt. Migration `AddDailyPuzzleTable`. davor: Tagespuzzle-Solver-Updates per Webhook: API feuert nach jedem Buch-/Tagespuzzle-Versuch HMAC-signierten POST an Schach-Bot — sofortige Discord-Embed-Aktualisierung statt 5-Min-Polling; neuer `SchachBotWebhookService`, fire-and-forget via BackgroundTaskQueue; Konfig `SchachBot__WebhookUrl/WebhookSecret`; Compose-Files reichen `SCHACH_BOT_WEBHOOK_URL/SECRET` durch; davor: Admin: Kibana-Dashboard-Link im Admin-Header (en/de/hr); neuer Endpoint GET /api/admin/config mit kibanaUrl aus Server-Env Kibana__Url; Compose-Files reichen KIBANA_URL durch; davor: Fix (Endlos): playAgain() löscht den beendeten Run jetzt auch in-memory (activeGameState=null + Storage), nicht nur im Storage — sonst zeigte der Config-Screen nach Game-Over + „Nochmal spielen" wieder den Resume-Banner („Resume → Aufgeben → Nochmal → wieder fortsetzbar"-Schleife); davor: Fix: Dashboard/Statistik-Übersicht zeigen das Puzzle-Elo des meistgespielten Levels (GetStatsAsync ohne vizLevel → GetPrimaryLevelAsync = Level mit den meisten Versuchen) statt stur Level 0 (Default 1500); betrifft Nutzer, die überwiegend mit Visualisierung spielen; davor: „Eingeloggt bleiben"-Option beim Login (LoginDto.RememberMe → JWT 30 Tage statt 1 Tag; Checkbox im Login-Formular); Passwort-Anforderung auf nur noch MinLength(4) reduziert (PasswordComplexity-Attribut + Frontend-Pattern entfernt); davor: Modus-Harmonisierung der 3 Puzzle-Modi (Standard/Buch/Endless): Aufgeben spielt überall die Lösung von vorne durch; einheitlicher kurzer, überspringbarer Auto-Advance-Countdown nach dem Lösen (auch Buch); Endless Analyse→Zurück setzt den Run fort statt Übersicht; Endless Retry nach Fehler (kostet kein Leben) + Einstellungen (Thema/Viz) auch während des Spiels; Eval-Button auch im Buch; Mausrutscher-Guard + Panel-Offen-Zustand modusübergreifend gleich; Endless-State-Namen CORRECT/WRONG→SOLVED/FAILED. Strukturell: Review-/Lösungs-/Countdown-/Settings-/Eval-Logik in BasePuzzleSolver gebündelt. EndlessGameEngine bewusst nicht separat extrahiert — Spiel-Logik liegt großteils schon in endless-prefetch.util, der Rest ist eng mit dem Komponenten-State verwoben. 224 FE-Tests grün; davor: Fix (Betrieb): Frontend-Docker-Healthcheck nutzt 127.0.0.1 statt localhost (busybox-wget wählte für „localhost" IPv6 ::1, wo nginx nicht lauscht → Container fälschlich „unhealthy"); betrifft compose.dev.yml + compose.vpn.yml, reiner Healthcheck-Fix ohne App-Änderung; davor: Code-Review-Remediation, weitgehend internes Refactoring ohne bewusste Funktionsänderung: Templates/Styles der 6 großen Komponenten ausgelagert; geteilte Subkomponenten ReviewNav/VizCard/ThemePicker + tournament-table.util; SnackbarService (zentralisiert ~106 MatSnackBar-Aufrufe); fat Controller in Services zerlegt — BookPuzzleService, CourseService, AdminService+BookAdminService; SessionId-Pattern als ValidationConstants, RATING_WINDOW-Dedup, Repertoire-DI; Bugfixes: DeleteBook räumt BookPuzzleAttempts ab, Endless-Anon-Session Unique-Index; ThemePicker-Chips modusübergreifend einheitlich. EndlessGameEngine in die Modus-Harmonisierung verschoben (folgt). 223 FE-/482 BE-Tests grün; davor: Puzzle-Buttons konsistent zum Endlosmodus: „Aufgeben" ab Zug 0 (auch Buch-Puzzle), „Zurücksetzen"/„Mausrutscher" erst nach dem ersten Zug; Einstellungen/Filter (Zahnrad) bleiben beim Puzzle-Wechsel offen (Offen-Zustand gemerkt); davor: Anonyme Buch-/Tagespuzzle-Solves zählen mit: neuer `POST /api/book-puzzles/{id}/attempt/anonymous` (Session-ID, je Session/Puzzle dedupliziert) + `anonymousSolvedCount` in `/results`; `BookPuzzleAttempt.UserId` nullable + `AnonymousSessionId` (Migration `BookPuzzleAttemptAnonymous`); Frontend zeichnet Buch-Solves auch ohne Login auf; Discord zeigt „+N anonym"; davor: Fix: ClientLogController loggt „heartbeat*"-Kinds auf Information statt Warning (sonst warn_spike-Fehlalarm durch den 60s-Bot-Heartbeat via /api/client-log); echte Engine-Events bleiben Warning; davor: Heartbeat/Health: API sendet alle 60s strukturiertes „Heartbeat"-Log nach ES (HeartbeatService, BackgroundService, `Heartbeat:IntervalSeconds`, DB-Selbst-Check → healthy/degraded) → Log-Watcher erkennt toten Dienst an fehlenden Heartbeats; Docker-Healthchecks für api (`/health`, curl im Dockerfile), frontend (wget) und crawler (`/api/health`) in compose.dev.yml + compose.vpn.yml; Crawler + Bot senden analoge Heartbeats (eigene Repos); davor: Kibana-Dashboard: Produkt-Kennzahlen Puzzle/Endless/Kurse/Betrieb + Unique Visits via VisitorId (X-Visitor-Id-Header); Recent-Logs-Spalten timestamp/level/RequestPath/message/username; Log-Event EndlessSessionCompleted; kibana-init als Build-Image (Auto-Deploy); davor: Code-Review-Fixes: Public-Profile (`GET /api/profile/{username}`) gibt reduziertes `PublicProfileDto` zurück (KEINE Klarnamen/ChessResultsId/Discord) — PII-Leak behoben; CourseController.RecordResult zeichnet Solve in eigenem SaveChanges auf (kein stiller Solve-Verlust bei parallelem CourseProgress-Insert) + GetNext-Save abgesichert; ProfileService.LinkDiscord fängt DbUpdateException → 409 (TOCTOU); ClientLog strippt CRLF (Log-Forging); BookPuzzleController `[Authorize]` als Default; ForwardedHeaders (X-Forwarded-For nur von privaten Peers) → Rate-Limiter/IP-Log per echter Client-IP; AdminController.ClearPuzzles Transaktions-Rollback; AutoSubscription `snr` defensiv; EndlessProgressService anonyme Reads deterministisch + Log-Timestamps geclampt; Frontend: AnalysisEngine-Watchdog deckt auch isready→readyok-Lücke (kein „Berechne…"-Deadlock), Endless-recordAttempt offline-queue-fähig, Offline-Fallback nimmt rating-nächstes Puzzle (`takeNearestFromPool`), OfflineQueue Backoff-Retry + UUID-Ids, StockfishService sauberer Crash-Abbruch + gequeuete Suche re-init, SwUpdate `unrecoverable`→Reload, Register-Redirect `navigateByUrl` (Multi-Segment), Navbar `takeUntilDestroyed`+`switchMap`; davor: Stockfish offline: `/assets/stockfish/**` wieder im Service Worker (eigene assetGroup `engine`, installMode prefetch) → Analyse/Eval funktionieren offline; sicher, weil das Glue bei `instantiateStreaming`-Fehler auf `instantiate(arrayBuffer)` zurückfällt + Handshake/Recovery/Watchdog/Telemetrie absichern (der „Berechne…"-Hänger lag am UCI-Sequencing, nicht am SW-Caching); davor: Engine-Diagnostik: Browser-Stockfish-Crashes/Hänger werden erkannt + an die API gemeldet — neuer Endpoint `POST /api/client-log` (AllowAnonymous, rate-limited) loggt strukturiert nach ES/Kibana; erfasst Worker-Crash, init_failed, search_timeout, Analyse-„stall"-Watchdog (kein Info nach `go` → auto-Neustart) + giveup; Frontend ClientLogService (pro Art gedrosselt), Hooks in AppComponent verdrahtet; davor: Fix: Analyse-Hänger „Berechne…" behoben — Stockfish-WASM wird NICHT mehr über den Service-Worker gecacht/serviert (aus ngsw-config entfernt → lädt direkt aus dem Netz; ein aus dem SW-Cache serviertes WASM ließ `instantiateStreaming` scheitern → kein `readyok`); zusätzlich sauberes UCI-Sequencing in AnalysisEngineService.analyze (stop→isready→readyok→position+go); Engine/Analyse braucht damit Verbindung, Offline-Solving unberührt; davor: Stockfish-WASM-Crash-Recovery — Worker-Absturz startet automatisch neu (StockfishService + AnalysisEngineService) statt die Engine bis zum Reload lahmzulegen; Analyse nimmt die Stellung nahtlos wieder auf (Loop-Schutz), Hash auf 16 begrenzt gegen OOM, init-Retry nach Fehlschlag; createWorker als Test-Seam; davor: Offline-Pools (Standard-Puzzle + Endless) werden schon beim App-Start vorab geladen statt erst beim ersten Öffnen des Modus → beide Modi direkt offline startbar (core/offline-prefetch.service.ts, in AppComponent angestoßen); Fenster-Logik geteilt via puzzle-window.util / endless-prefetch.util; davor: Strukturiertes Pro-Puzzle-Logging mit Start-/Lösungszeit über alle Modi (Standard, Tagespuzzle/Buch, Kurs, Endless) für ES/Kibana — Endless meldet die Session-Puzzles mit Zeiten beim Session-Ende (nur Log, nicht persistiert), Kurs-Lösungen senden jetzt die benötigte Zeit; davor: Endless: „Letztes Puzzle analysieren" nach dem Lösen (bleibt sichtbar trotz Auto-Advance) + „Analysieren" für das aktuelle Puzzle beim Aufgeben — öffnet jeweils den Analysemodus, Zurück führt in den Endless-Modus; davor: Service Worker (PWA) → App-Shell + Lazy-Module + i18n offline gecacht, Puzzle/Endless offline startbar; Offline gelöste Puzzles werden lokal vorgemerkt (Offline-Queue) und bei Reconnect automatisch hochgeladen (Standard/Tagespuzzle/Kurs/Endless), Anzahl wartender Lösungen im Profil; Endless prefetcht Run schon beim Config-Öffnen + klarer Offline-ohne-Cache-Hinweis; Bücher offline speichern (Kurs-Liste) + Offline-Buch-Navigation; Offline-Einstellungen im Profil + Cache-Größe/Leeren; Standard-Puzzle offline; Endless mehrere Runs vorab; Tagespuzzle-Solves erfasst + Results-Endpoint für Discord-Anzeige; Fix: Endless „Unfinished run | 0 lives"-Zombie weg; Fix: EF „FirstOrDefault ohne OrderBy" an der Quelle behoben; Buch-Puzzle Standalone: „Nächstes im Buch" + „Zufällig aus Buch"; Statistik „Alle": überlagerte farbkodierte Elo-Kurven + Legende; Buch-Import meldet Importiert / Duplikate / Ungültig getrennt; davor: Log-Rauschen reduziert; Kibana-Dashboard Logins/Tag + Unique Logins; strukturierter UserLogin-Log in der API; Endless offline-Vorabladen eines Runs; Pfeile/Kreise auf allen Puzzle-Brettern + Analyse-Fix; Analyse-Tiefe einstellbar + Zurück-zum-Puzzle; „Letztes Puzzle ansehen" öffnet Analysemodus; Repertoires + Wochenpost vorerst nur Admin; Statistik „Alle" zeigt Kurve je Modus; „Analysieren"-Button bei Puzzles; Puzzle-Aufgeben spielt Lösung durch; Discord-Konto-Verknüpfung; User-Statistikseite; Analysemodus; Frontend mehrsprachig en/de/hr)
- Definiert in `src/frontend/app/src/environments/changelog.ts` (Single Source: `APP_VERSION` + `CHANGELOG`). `environment.ts` (dev) UND `environment.prod.ts` (prod-Build via fileReplacements) importieren beide daraus — so zeigt der Footer in jedem Build dieselbe Version. **Nur `changelog.ts` editieren**, nie die Environment-Dateien.
- Angezeigt im Footer der Desktop-Version (Klick oeffnet Changelog-Overlay)
- **Jeder Fix/jedes Feature MUSS die Version erhoehen**: Patch fuer Fixes (0.0.x), Minor fuer Features (0.x.0)
- **Changelog pflegen**: Jeden Eintrag im `changelog`-Array in `environment.ts` vermerken (Version, Datum, Liste der Aenderungen)
- Version in `environment.ts` UND in diesem Abschnitt aktualisieren
- Changelog ist im Frontend einsehbar durch Klick auf die Versionsnummer im Footer
- **Gilt auch fuer Aenderungen im Crawler-Repo** (`C:/git/chessresults_crawler`): Features/Fixes dort muessen ebenfalls hier Version + Changelog erhoehen und committet werden

### Checkliste vor JEDEM Commit (beide Projekte)
1. [ ] Tests vorhanden fuer die Aenderung?
2. [ ] `APP_VERSION` + `CHANGELOG`-Eintrag in `src/frontend/app/src/environments/changelog.ts` aktualisiert? (gilt automatisch fuer dev + prod-Build)
3. [ ] `Aktuelle Version` in diesem Abschnitt angepasst?
4. [ ] Versionsaenderung committet?
5. [ ] **Nach jedem Commit dem User die aktuelle Version mitteilen** (z.B. "Version: 0.6.6")

**NIEMALS committen ohne diese Checkliste abzuarbeiten.** Auch reine Test- oder Doku-Aenderungen erhoehen die Patch-Version.

## Screenshots

- Screenshots liegen in `C:/git/screenshot/` (z.B. `Screenshot.jpg`)
- Diesen Pfad nutzen um visuelle Pruefungen durchzufuehren

## Wichtige Konventionen

- **Keine Default-Werte in Compose-Example-Dateien** – `compose.yml.example` und `compose.vpn.example` verwenden `${VAR}` ohne `:-default`. Alle Werte muessen explizit in der `.env`-Datei gesetzt werden.
- Crawler-Proxy-Endpoints muessen mit tatsaechlichen Crawler-Routen uebereinstimmen
- Angular nutzt lazy-loaded standalone components (kein NgModule)
- JWT-Claims: `ClaimTypes.NameIdentifier` = UserId, `ClaimTypes.Name` = Username
- PGN-Upload-Limit: 10 MB pro Datei (in `RepertoireService`)
- Alle Controller holen UserId via `User.FindFirstValue(ClaimTypes.NameIdentifier)`
- Friendship-Status ist eine State Machine: Pending -> Accepted/Declined
- Nur der Addressee kann Accept/Decline ausfuehren
