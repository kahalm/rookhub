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
| GET | `/api/extension/repertoires` | Leichtgewichtige Liste |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |

CORS erlaubt: `https://www.chess.com`, `chrome-extension://*`, `http://localhost:4200`

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

## Datenbank-Schema (eigene DB `rookhub`, nicht geteilt mit Crawler)

| Tabelle | Zweck | Wichtige Felder / Constraints |
|---------|-------|-------------------------------|
| AppUsers | Auth | Username (unique), Email (unique), PasswordHash, CreatedAt |
| UserProfiles | Schach-Identitaet | UserId (1:1 zu AppUser), FideId, ChessResultsId, ChessComUsername, LichessUsername, DisplayName |
| Friendships | Freundesliste | RequesterId, AddresseeId (unique pair), Status (Pending/Accepted/Declined) |
| Repertoires | PGN-Sammlungen | UserId, Name, Description, IsPublic, CreatedAt, UpdatedAt |
| RepertoireFiles | Einzelne PGNs | RepertoireId, FileName, PgnContent (LONGTEXT), FileSize |
| TournamentSubscriptions | Turnier-Abo | UserId + CrawlerTournamentId (unique pair), TournamentName |
| BookPuzzles | Buch-Puzzles | LineId (unique), BookFileName (indexed), Round, Fen, Moves, Title, Chapter, Comment, Difficulty, BookRating, Tags |
| Groups | Benutzergruppen | Name (unique), Description, CreatedAt |
| UserGroups | User<->Gruppe (n:m) | Composite PK (UserId, GroupId), Cascade von AppUser + Group |
| EndlessProgresses | Endless Config+Highscore | UserId (unique, nullable), AnonymousSessionId, StartElo, Step, Themes, Fasttrack, Highscore, ActiveGameState (LONGTEXT) |
| EndlessSessions | Abgeschlossene Endless Sessions | UserId (nullable), AnonymousSessionId, Timestamp, TotalSolved, MaxRating, DurationSeconds, ConfigJson (TEXT), MistakeAtRatings |

Cascade Deletes: AppUser -> Profile, Repertoires, Subscriptions, EndlessProgresses, EndlessSessions, UserGroups; Repertoire -> Files; Group -> UserGroups.
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
dotnet test     # 350 Tests (Auth, Profile, Friends, Repertoire, Subscriptions, Favorites, Monitor, Puzzles, Endless, Books, Groups)
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

- **Aktuelle Version**: `0.34.0`
- Definiert in `src/frontend/app/src/environments/environment.ts`
- Angezeigt im Footer der Desktop-Version (Klick oeffnet Changelog-Overlay)
- **Jeder Fix/jedes Feature MUSS die Version erhoehen**: Patch fuer Fixes (0.0.x), Minor fuer Features (0.x.0)
- **Changelog pflegen**: Jeden Eintrag im `changelog`-Array in `environment.ts` vermerken (Version, Datum, Liste der Aenderungen)
- Version in `environment.ts` UND in diesem Abschnitt aktualisieren
- Changelog ist im Frontend einsehbar durch Klick auf die Versionsnummer im Footer
- **Gilt auch fuer Aenderungen im Crawler-Repo** (`C:/git/chessresults_crawler`): Features/Fixes dort muessen ebenfalls hier Version + Changelog erhoehen und committet werden

### Checkliste vor JEDEM Commit (beide Projekte)
1. [ ] Tests vorhanden fuer die Aenderung?
2. [ ] `version` und `changelog`-Array in `src/frontend/app/src/environments/environment.ts` aktualisiert?
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
