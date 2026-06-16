# RookHub

Zentrales Webportal für schachrelevante Funktionen: PGN-Repertoire-Verwaltung, Turnierdaten, Benutzerprofile mit FIDE/ChessResults-Verlinkung, Freundeslisten, Puzzle-/Endless-/Kurs-Training, Wochenpost. Gehört zusammen mit dem **ChessResults Crawler** (`C:/git/chessresults_crawler`) und dem **Schach-Bot** (separates Repo) – bei Änderungen immer alle betroffenen Projekte berücksichtigen.

## ⚠️ Parallel-Arbeit: Agenten-Koordination (ZUERST LESEN)

Es gibt **zwei gleichwertige, funktionierende Arbeitskopien** des gesamten Stacks:

| Kopie | Pfad |
|-------|------|
| 1 (primär) | `/home/kahalm/claude/rookhubstack` |
| 2 | `/home/kahalm/claude/rookhubstack-2` |

**Damit sich zwei gleichzeitig laufende Agenten nicht ins Gehege kommen, gilt ein Lock-Protokoll. Jede Instanz führt das BEVOR sie zu arbeiten beginnt aus:**

1. **Lock prüfen/claimen** — Lock-Datei ist `<stack-root>/.agent-lock` (liegt im Stack-Root, **außerhalb** aller Git-Repos → wird nie committet).
   - Existiert `rookhubstack/.agent-lock` **nicht** → diese Kopie ist frei: Lock anlegen (Inhalt: Zeitstempel + kurze Aufgabenbeschreibung) und **hier** in `rookhubstack` arbeiten.
   - Existiert `rookhubstack/.agent-lock` schon → Kopie 1 ist belegt: **direkt nach `rookhubstack-2` wechseln**, dort dasselbe prüfen und `rookhubstack-2/.agent-lock` anlegen, und dort arbeiten.
   - Sind **beide** gelockt → nicht parallel weiterarbeiten; nachfragen (vermutlich Stale-Lock).
2. **Stale-Locks**: Ein Lock älter als ~24 h darf als verwaist betrachtet und überschrieben werden (Zeitstempel im Lock prüfen).
3. **Beim Abschluss** den **eigenen** Lock wieder entfernen (`rm <stack-root>/.agent-lock`).

Die beiden Kopien werden NICHT automatisch synchronisiert — jede committet/pusht für sich. Nach Merges ggf. per `git pull` abgleichen.

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
- `Services/CrawlerProxyService.cs` – HTTP-Client zum Crawler, muss Crawler-Routen kennen
- `Controllers/TournamentProxyController.cs` – Mappt RookHub-Routen auf Crawler-Routen (RookHub-`/api/tournaments/crawl*` → Crawler-`/api/crawl*`)
- `Services/SchachBotWebhookService.cs` – HMAC-signierte Webhooks an den Bot (Tagespuzzle + Wochenpost-Progress)
- Crawler-Endpoint-Änderungen müssen in den beiden ersten Dateien nachgezogen werden
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

**Hinweis**: RookHub nutzt Swashbuckle 6.9.0 (nicht 10.x) wegen Kompatibilität mit .NET 9's OpenAPI-Namespace.

## REST API

### Auth (offen, kein JWT nötig)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung `{ username, email?, password }` — E-Mail optional (`null` erlaubt, Unique-Index toleriert NULL-Duplikate) |
| POST | `/api/auth/login` | Login, gibt JWT zurück (`rememberMe` → 30 Tage statt 1 Tag) |
| POST | `/api/auth/forgot-password` | „Passwort vergessen" `{ email }` — schickt (falls die Adresse zu einem aktiven Konto gehört) einen einmaligen Reset-Link (TTL 1 h) per Mail. Antwortet IMMER 200 (keine User-Enumeration). Versand via `PasswordResetService` + `IEmailSender` (SMTP/MailKit); ohne `Email:SmtpHost` wird die Mail nur geloggt. Link-Basis = `App:BaseUrl` |
| POST | `/api/auth/reset-password` | Neues Passwort setzen `{ token, newPassword }` — 204 bei Erfolg, 400 bei ungültigem/abgelaufenem/verbrauchtem Token. Token ist einmalig (`UsedAt`) |

### Profil (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/profile` | Eigenes Profil |
| PUT | `/api/profile` | Profil bearbeiten |
| DELETE | `/api/profile/account` | Konto löschen (DSGVO: anonymisiert Identität+PII, behält Statistik) |
| GET | `/api/profile/{username}` | Öffentliches Profil (reduziertes `PublicProfileDto` ohne Klarnamen/ChessResultsId/Discord) |
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
| GET | `/api/friends/{userId}/stats` | Puzzle-Statistik eines Freundes (Vergleich „Du vs. Freund": Elo/Gelöst/Versuche/Genauigkeit/Serien + Themen-Aufschlüsselung). Nur zwischen akzeptierten Freunden (sonst 403); reused `PuzzleService.GetStatsAsync`/`GetBreakdownAsync` |
| GET | `/api/friends/{userId}/revenge` | „Revenge a Friend": Standard-Puzzles, an denen der Freund gescheitert ist und die er nie gelöst hat (`PuzzleService.GetUnsolvedFailuresAsync(targetId, viewerId)`, sortiert nach jüngstem Fehlversuch). Pro Puzzle `solvedByViewer` (hat der Aufrufer es schon gelöst → erledigte Revanche). Nur zwischen akzeptierten Freunden (sonst 403) |

### Puzzle-Challenges (auth) — „schick dieses Puzzle an Freunde"
Nach dem Lösen kann ein User ein konkretes Puzzle an **einen oder mehrere** Freunde schicken (Multi-Select im Solver-Menü, alle Modi außer Wochenpost). Die Challenge ist **polymorph**: `Source` (`Standard` = `Puzzles`-Tabelle, Standard/Endless; `Book` = `BookPuzzles`-Tabelle, Buch/Kurs/Tagespuzzle). Der Empfänger löst sie über den quellen-passenden Deep-Link (`/puzzles/:id?challengeId=…` bzw. `/puzzles/book/:id?challengeId=…`, meldet das Ergebnis nach dem Versuch via Resolve zurück), der Status (Pending→Solved/Failed) erscheint beim Absender. Logik in `ChallengeService` (nutzt `FriendService.AreFriendsAsync`); Existenz wird je Quelle geprüft (kein FK). Frontend: wiederverwendbare `ChallengeFriendsComponent`.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/challenges` | Batch-Challenge anlegen `{ toUserIds[], puzzleId, source }` — antwortet `{ sent, skipped[] }` (übersprungene Empfänger mit Grund `self`/`not_friends`/`duplicate`); 404 nur wenn das Puzzle in der zur `source` passenden Tabelle fehlt |
| GET | `/api/challenges/incoming` | Offene eingehende Challenges (Posteingang) inkl. Absender + Puzzle-Rating |
| GET | `/api/challenges/outgoing` | Gesendete Challenges inkl. Ergebnis-Status + Lösezeit |
| GET | `/api/challenges/incoming/count` | Anzahl offener eingehender Challenges (Navbar-Badge) |
| POST | `/api/challenges/{id}/resolve` | Ergebnis melden `{ solved, timeSpentSeconds }` — nur der Empfänger (403), 409 wenn schon aufgelöst |

### Revenge-Benachrichtigungen (auth) — Ziel-User über Revanche informieren
Geht ein Freund (Avenger) eines gescheiterten Puzzles eines Users (Target) im Revenge-Modus an, wird der Target informiert (gelöst ODER gescheitert). Frontend: `/puzzles/:id?revengeUserId=…` meldet das Ergebnis nach dem Versuch (fire-and-forget). `RevengeNotificationService` legt nur an, wenn die beiden befreundet sind UND der Target an dem Puzzle tatsächlich gescheitert ist.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/revenge/result` | Revanche-Ergebnis melden `{ targetUserId, puzzleId, solved }` — legt Benachrichtigung an (still ignoriert, wenn keine Freunde / Target nie gescheitert) |
| GET | `/api/revenge/notifications` | Eigene Revanche-Benachrichtigungen (neueste zuerst) |
| GET | `/api/revenge/notifications/count` | Anzahl ungelesener (Navbar-Badge, kombiniert mit Challenges) |
| POST | `/api/revenge/notifications/seen` | Alle als gelesen markieren |

### Benachrichtigungen / Glocke (auth) — generischer In-App-Strom
Eine zentrale Navbar-Glocke mit „!"-Indikator. `Notifications`-Tabelle (`UserId`, `Type`, `DataJson` = i18n-Parameter, `Link`, `SeenAt?`), Text wird im Frontend über `notifications.type.<type>` lokalisiert. `NotificationService.CreateAsync` wird per fire-and-forget von den Domänen-Services aufgerufen. Trigger-Typen: `chessable_import_completed`/`_failed` (ChessableImportService), `friend_request_received`/`friend_request_accepted` (FriendService), `challenge_received`/`challenge_resolved` (ChallengeService), `revenge_performed` (RevengeNotificationService, Dual-Write). Frontend: `InAppNotificationService` + Glocke in der Navbar (löste den Freunde-Badge ab); 60-s-Poll für den Zähler; Browser-`NotificationService` (Web-Notification-API) bleibt separat für späteres Push. Mail/Push sind Phase 2/3.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/notifications?take=20` | Letzte Benachrichtigungen (neueste zuerst) |
| GET | `/api/notifications/history?page=&pageSize=` | Vollständige History (paginiert, neueste zuerst) + Gesamtzahl — für die `/notifications`-Seite |
| GET | `/api/notifications/count` | Anzahl ungelesener (Glocken-Badge) |
| POST | `/api/notifications/seen` | Alle als gelesen markieren (beim Öffnen der Glocke) |

### Direktnachrichten Admin↔User (auth)
Beide Seiten können eine Konversation **starten**: der Admin schreibt einem User, ODER der User kontaktiert von sich aus das Admin-Team. Danach beliebig oft hin und her (durchgehende Konversation). Ein „Thread" = alle `AdminMessages` mit derselben `UserId` (Nicht-Admin-Teilnehmer); Metadaten/Zuweisung in `MessageThreads` (1 Zeile je User). Jede neue Nachricht legt eine In-App-Benachrichtigung bei der Gegenseite an: Admin→User `admin_message_received` (Link `/messages`), User→Admin `user_message_received` an **alle** Admins (Link `/admin`). **Claim/Übernahme**: ein Admin kann einen Thread übernehmen (`ClaimedByAdminId`) — alle Admins sehen, wer welchen bearbeitet; eine Admin-Antwort auf einen offenen Thread übernimmt ihn automatisch. Read-Receipts getrennt je Seite (`SeenByUserAt`/`SeenByAdminAt`). Logik in `AdminMessageService`; User-Seite `/api/messages`, Admin-Seite `/api/admin/messages`. Frontend: User-Seite `/messages` (Navbar-Mail-Icon, immer sichtbar, mit Badge), Admin-Tab „Nachrichten" (Thread-Liste mit Claim-Status + Übernehmen/Freigeben).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/messages` | Auth | Eigener Thread (chronologisch); leer, solange niemand schrieb |
| GET | `/api/messages/unread-count` | Auth | Ungelesene Admin-Nachrichten (Navbar-Badge) |
| POST | `/api/messages/reply` | Auth | User schreibt dem Admin-Team `{ body }` — startet die Konversation selbst oder antwortet (400 nur bei leerem Text) |
| POST | `/api/messages/seen` | Auth | Eigene Admin-Nachrichten als gelesen markieren |
| GET | `/api/admin/messages/threads` | Admin | Alle Konversationen (je User: letzte Nachricht, ungelesene User-Antworten, Claim-Status `ClaimedByAdminId`/`-Name`) |
| GET | `/api/admin/messages/unread-count` | Admin | Ungelesene User-Antworten über alle Threads (Tab-Badge) |
| GET | `/api/admin/messages/threads/{userId}` | Admin | Vollständiger Thread mit einem User |
| POST | `/api/admin/messages/threads/{userId}` | Admin | Schickt/antwortet dem User `{ body }` (legt Thread an + übernimmt offenen Thread automatisch; 404 wenn User fehlt) |
| POST | `/api/admin/messages/threads/{userId}/seen` | Admin | User-Antworten des Threads als gelesen markieren |
| POST | `/api/admin/messages/threads/{userId}/claim` | Admin | Thread übernehmen (Zuweisung an den aufrufenden Admin) |
| POST | `/api/admin/messages/threads/{userId}/release` | Admin | Thread wieder freigeben |

### Repertoires (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/repertoires` | Alle eigenen Repertoires |
| POST | `/api/repertoires` | Neues Repertoire (`kind`: none/opening/middlegame/endgame) |
| GET | `/api/repertoires/{id}` | Repertoire mit Dateien |
| PUT | `/api/repertoires/{id}` | Metadaten ändern |
| DELETE | `/api/repertoires/{id}` | Löschen |
| POST | `/api/repertoires/{id}/files` | PGN hochladen (multipart, max 10 MB) |
| GET | `/api/repertoires/{id}/files/{fileId}` | PGN herunterladen |
| DELETE | `/api/repertoires/{id}/files/{fileId}` | Datei löschen |
| GET | `/api/repertoires/{id}/pgn` | Alle PGNs kombiniert |

### Extension API (auth, CORS für chess.com)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/extension/repertoires?kind=opening` | Leichtgewichtige Liste (id, name, fileCount, kind, totalSizeBytes); `kind` filtert auf `none|opening|middlegame|endgame`. Nur Repertoires mit `UseForExtension=true` (Default true, im Bearbeiten-Dialog abwählbar); gilt ebenso für das Positions-Set der Abweichungsanalyse (`RepertoireAnalyzeService`) |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |

Akzeptiert sowohl JWT (User-Login) als auch ApiToken (`Authorization: Bearer rkh_…`). Bei ApiToken muss `scope=extension` sein (sonst 403). Policy-Scheme im Auth-Stack routet das Bearer-Format automatisch zum passenden Handler.

CORS (`ExtensionPolicy`, nur für `ExtensionController`): erlaubt ausschließlich `https://www.chess.com`, nur `GET`, ohne `AllowCredentials` (Auth strikt über Bearer-Header). Die Default-CORS-Policy (Frontend) erlaubt `http://localhost:4200` + `http://localhost:8085`.

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
| POST | `/api/tournaments/crawl` | `/api/crawl` |
| POST | `/api/tournaments/crawl/player-details` | `/api/crawl/player-details` |

### Chessable-Integration (auth, leitet an piratechess-API weiter)
RookHub speichert nur den per-User Chessable-Bearer (AES-verschlüsselt via `EncryptionService` → `ChessableCredentials.EncryptedBearer`). Alle Chessable-HTTP-Calls (curl-impersonate gegen Cloudflare) liegen im piratechess-Stack; `ChessableProxyService` reicht den Bearer pro Request an `POST /api/chessable/direct/*` durch und authentifiziert sich mit dem `X-Service-Key`-Header (`Chessable:ServiceKey` ↔ piratechess `Service:ApiKey`). Netzwerk: externes Docker-Netz `chessable-bridge` (von piratechess_docker bereitgestellt). **Admin-Download „im Namen eines Users"**: `ChessableImport.BearerUserId` (nullable) entkoppelt Bearer-Quelle von Besitzer — der Service lädt den Bearer von `BearerUserId ?? UserId`. Admin-Import setzt `UserId`=Admin (Repertoire + Notification beim Admin), `BearerUserId`=Ziel-User; piratechess ist stateless, der gespeicherte Bearer des Ziel-Users genügt.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/chessable/credentials` | Status + maskierter Bearer (`{ hasCredentials, maskedBearer }`) |
| POST | `/api/chessable/credentials` | Bearer setzen/überschreiben `{ bearer }` |
| DELETE | `/api/chessable/credentials` | Bearer löschen |
| POST | `/api/chessable/test` | Bearer-Validität + Kursanzahl (`{ uid, courseCount }`) |
| GET | `/api/chessable/courses` | Liste der Kurse des Users (`[{ bid, name }]`) |
| GET | `/api/chessable/admin/imports` | **Admin**: alle Importe ALLER User (Verlauf, max. 200, neueste zuerst) inkl. `username`/`createdAt`/`completedAt` + globaler Queue-Position |
| GET | `/api/chessable/admin/active` | **Admin**: nur aktive (laufende/pausierte) Importe aller User — fürs Dashboard-Widget |
| GET | `/api/chessable/admin/credentialed-users` | **Admin**: User mit hinterlegtem Bearer (Auswahl für „Kurse von Usern holen") |
| GET | `/api/chessable/admin/users/{userId}/courses?refresh=` | **Admin**: Kursliste eines Users (mit dessen Bearer; Import-Status gegen die eigenen Admin-Importe markiert) |
| POST | `/api/chessable/admin/users/{userId}/import/{bid}` | **Admin**: lädt Kurs `{bid}` eines Users ins EIGENE Admin-Konto als Repertoire (`{ name? }`). Import-Besitzer = Admin (`UserId`), Bearer vom Ziel-User (`BearerUserId`). 404 unbek. User, 400 wenn Ziel-User keinen Bearer hat |

### Turnier-Abos + Favoriten + Monitor (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET/POST/DELETE | `/api/subscriptions[/{id}]` | Abonnierte Turniere verwalten |
| GET/POST/DELETE | `/api/tournament-favorites[/{id}]` | Favoriten verwalten |
| GET/POST | `/api/tournament-monitor[/{id}]` | Per-Turnier-User-Einstellungen + Runden-Monitor (Round-Watch, Auto-Subscribe) |

### Book-Puzzles (offen + Admin)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/book-puzzles/{id}` | AllowAnonymous | Puzzle by ID |
| GET | `/api/book-puzzles/{id}/next` | AllowAnonymous | Nächstes Puzzle im selben Buch (Loop am Ende) |
| GET | `/api/book-puzzles/{id}/random` | AllowAnonymous | Zufälliges Puzzle aus demselben Buch |
| POST | `/api/book-puzzles/{id}/attempt` | Auth | Lösungsversuch erfassen `{ solved, timeSeconds }` (Tagespuzzle) |
| POST | `/api/book-puzzles/{id}/attempt/anonymous` | Anon | Anonymer Versuch (Session-ID, je Session/Puzzle dedupliziert) |
| GET | `/api/book-puzzles/{id}/results?since=` | AllowAnonymous | Solver-Liste (je User, inkl. Discord) + Versuchs-/Lösungszähler + `anonymousSolvedCount`. Löser-Status: nur wer im **ersten** Versuch löste, gilt als Löser |
| GET | `/api/book-puzzles/daily/leaderboard?month=yyyy-MM` | AllowAnonymous | Monats-Wertung des Tagespuzzles (für den Bot): je User Punkte (10 je Erstversuch-Lösung + Tages-Rang-Bonus 5/3/1), `solved`, `golds`; absteigend nach Punkten. Default = laufender UTC-Monat. Literal-Route **vor** `daily/{date}` |
| GET | `/api/book-puzzles/daily/hall-of-fame?top=5` | AllowAnonymous | All-time-Bestenlisten: meiste gelöste Dailies, meiste 🥇 (Tage als schnellster Erstversuch-Löser), schnellste je gelöste Lösung. `top` 1–25 |
| GET | `/api/book-puzzles/daily/{date}` | AllowAnonymous | Tagespuzzle für UTC-Datum (`yyyyMMdd` oder `today`); legt on-demand eine persistierte Zuordnung in `DailyPuzzles` an (deterministisch ab da) |
| GET | `/api/book-puzzles/by-line-id?lineId=xxx` | AllowAnonymous | Lookup für schach-bot |
| GET | `/api/book-puzzles/books` | AllowAnonymous | Buch-Liste mit Counts |
| POST | `/api/admin/book-puzzles/import` | Admin | Bulk-Import aus JSON |
| POST | `/api/admin/book-puzzles/daily/{date}/regenerate` | Admin | Tagespuzzle eines UTC-Datums neu generieren: Datum/Link bleibt, bisheriges Puzzle wird `Retired=true` gesetzt (nie wieder in Daily/Random/Blind), neues aus dem forDaily-Pool zugeordnet |

### Gruppen (Admin + auth)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/groups` | Admin | Alle Gruppen inkl. MemberCount |
| POST | `/api/admin/groups` | Admin | Gruppe anlegen (name, description) |
| PUT | `/api/admin/groups/{id}` | Admin | Gruppe umbenennen / Beschreibung |
| DELETE | `/api/admin/groups/{id}` | Admin | Gruppe + Mitgliedschaften löschen |
| GET | `/api/admin/groups/{id}/members` | Admin | Mitglieder einer Gruppe |
| POST | `/api/admin/groups/{id}/members/{userId}` | Admin | User zur Gruppe hinzufügen (idempotent) |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | User aus Gruppe entfernen |
| GET | `/api/admin/groups/{id}/training-goal` | Admin | Trainingsziel-Vorlage der Gruppe (Source "none" wenn keine) |
| PUT | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage setzen/aktualisieren (PuzzleMinutes/BookMinutes 0–600, PlayGames 0–200 Partien/Woche, WeeklyDaysTarget 0–7) |
| DELETE | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage entfernen |
| GET | `/api/my-groups` | Auth | Gruppen-Namen des eingeloggten Users (gruppenabhängige Anzeige) |

### Menü-Sichtbarkeit (Admin konfiguriert, je Nutzer aufgelöst)
Admin legt pro Menüeintrag eine Sichtbarkeitsstufe fest: `All` (jeder, auch anonym) / `Registered` (eingeloggt) / `Groups` (Mitglieder bestimmter Gruppen, Admins immer) / `Admin`. Defaults in `Services/MenuRegistry.cs` (bilden das bisherige Verhalten ab); nur Overrides landen in der DB. `MenuVisibilityService` löst die effektive Sichtbarkeit auf. Frontend: `MenuService` (Navbar-Snapshot + frischer Guard-Check) + `menuGuard('<key>')` sperrt auch den direkten URL-Aufruf. „courses" bleibt zusätzlich content-gegated (courseAccessGuard).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/menu` | AllowAnonymous | Sichtbare Menü-Keys für den (ggf. anonymen) Aufrufer |
| GET | `/api/admin/menu` | Admin | Vollständige Konfiguration (Defaults + Overrides) |
| PUT | `/api/admin/menu` | Admin | Konfiguration setzen (Liste `{ key, level, groupIds }`; unbekannte Keys ignoriert) |

### Endless Puzzle Sync (auth + anon)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/endless/progress` | Auth | Progress + Sessions laden (single call) |
| GET | `/api/endless/history?page=&pageSize=&archived=` | Auth | Paginierte Session-History (archived: bool-Filter) |
| GET | `/api/endless/sessions/{id}` | Auth | Lauf-Detail inkl. einzelner Puzzle-Versuche (History-Detailansicht) |
| PUT | `/api/endless/progress` | Auth | Config + Highscore + Active Game upsert |
| POST | `/api/endless/archive` | Auth | Sessions archivieren/unarchivieren |
| GET | `/api/endless/progress/anonymous?sessionId=` | Anon+RL | Anonymer Progress |
| PUT | `/api/endless/progress/anonymous` | Anon+RL | Anonymer Progress speichern |
| POST | `/api/endless/sessions` | Auth | Session aufzeichnen |
| POST | `/api/endless/sessions/anonymous` | Anon+RL | Anonyme Session aufzeichnen |
| POST | `/api/endless/sessions/bulk` | Auth | Bulk-Import (localStorage-Migration) |
| POST | `/api/endless/sessions/bulk/anonymous` | Anon+RL | Bulk-Import anonym |
| POST | `/api/endless/claim-session` | Auth | Anonyme Daten auf User übertragen |

### Kurse (auth, gruppen-/admin-gated)
„Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet. Fortschritt pro Buch (gelöste Puzzles / gesamt), geteilt über beide Modi; der Modus bestimmt nur die Reihenfolge. Alles user-bezogen in der DB. **Sichtbarkeit**: Admins sehen alle Bücher; Nicht-Admins nur Bücher, die einer ihrer Gruppen via `BookGroupAccess` freigegeben sind. Zugriff wird je Buch in jedem Endpoint erzwungen (kein Zugriff → 404).

Der `mode`-Parameter bei `/next` akzeptiert `sequential` (Buchreihenfolge, `after` = überspringen) oder `random` (zufällig, `exclude` vermeidet Wiederholung); `completed` wenn alle gelöst.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses` | Auth | Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle) |
| GET | `/api/courses/access` | Auth | `{ hasAccess }` — Basis für die Menü-Sichtbarkeit (Admin: true wenn Bücher existieren) |
| GET | `/api/courses/{bookId}/chapters` | Auth | Kapitel des Buchs in Lesereihenfolge inkl. Fortschritt je Kapitel (`index`/`name`/`puzzleCount`/`solvedCount`/`progressPercent`); `name=null` = Sammel-„ohne Kapitel" |
| GET | `/api/courses/{bookId}/next?mode=&after=&exclude=&chapterIndex=` | Auth | Nächstes ungelöstes Puzzle (siehe `mode` oben); mit `chapterIndex` auf das Kapitel beschränkt (Pool + Fortschritt) |
| POST | `/api/courses/{bookId}/results` | Auth | Lösungsversuch aufzeichnen (idempotent); validiert Puzzle↔Buch |
| GET | `/api/courses/{bookId}/puzzles` | Auth | Alle Puzzles eines (zugänglichen) Buchs am Stück — für Offline-Speichern |
| POST | `/api/courses/{bookId}/reset` | Auth | Fortschritt des Kurses zurücksetzen |

Buch↔Gruppe-Freigabe verwaltet der Admin:
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/books/{id}/groups` | Admin | Gruppen-Ids mit Kurs-Zugriff auf das Buch |
| PUT | `/api/admin/books/{id}/groups` | Admin | Vollständige Gruppen-Freigabe setzen (ersetzt; ungültige Ids ignoriert) |

### Wochenpost (öffentlich lesbar, durchspielbar mit Login, Admin verwaltet)
Bildet die wöchentlichen schach-bot-Posts auf RookHub ab: ein PGN + Termin (Datum + Uhrzeit). PGN-Validierung via `RepertoireService.LooksLikePgn`. Puzzles werden on-the-fly aus dem PGN geparst (`PgnImportService.ParsePgn`) — Progress ist index-basiert.

**Per-User-Fortschritt**: idempotenter erster Versuch je `(WeeklyPostId, UserId, PuzzleIndex)`. „Erledigt" = **alle Puzzles gespielt** (gelöst egal). Aufgeben und Reset nach mindestens einem Zug zählen als ✗. Nach jedem **neuen** Versuch fire-and-forget Webhook (`SchachBotWebhookService.NotifyWeeklyAsync`, HMAC-signiert) an den Bot → Discord-Embed mit Live-Bestenliste.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/weekly-posts` | AllowAnonymous | Liste (ohne PGN), nach Termin absteigend |
| GET | `/api/weekly-posts/progress` | Authorize | Batch-Fortschritt für die Übersicht (`List<WeeklyPostProgressDto>`, nur Posts mit Versuchen) — literal-Route MUSS vor `{id}` stehen |
| GET | `/api/weekly-posts/{id}` | AllowAnonymous | Detail inkl. PGN |
| GET | `/api/weekly-posts/{id}/puzzles` | AllowAnonymous | Puzzle-Sequenz zum Durchspielen |
| POST | `/api/weekly-posts/{id}/attempt` | Authorize | Versuch erfassen `{ puzzleIndex, solved, timeSeconds }` (idempotent je Index) |
| GET | `/api/weekly-posts/{id}/progress` | Authorize | Eigener Fortschritt `{ total, playedCount, solvedCount, totalSeconds, playedIndices[], completed }` |
| GET | `/api/weekly-posts/{id}/results` | AllowAnonymous | Bestenliste (alle Spieler mit ≥1 Versuch): `playedCount`, `solvedCount`, `totalSeconds`, `completed`; Sortierung erledigt→gelöst→Name |
| POST | `/api/admin/weekly-posts` | Admin | Upload (multipart: file + scheduledAt + optional title) |
| PUT | `/api/admin/weekly-posts/{id}` | Admin | Termin/Titel ändern |
| DELETE | `/api/admin/weekly-posts/{id}` | Admin | Löschen |

### Bot-Stats (Bot-intern, HMAC-signiert)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/bot/player-progress/{discordId}` | AllowAnonymous + HMAC | Heutiger Trainingsziel-Fortschritt + Puzzle-Stats + jüngster Wochenpost-Status für eine verknüpfte Discord-ID. Signaturheader `X-Bot-Signature: sha256=…` mit `SchachBot:StatsSecret` (== Bot-`ROOKHUB_STATS_SECRET`); 401 bei falscher Signatur, 404 bei nicht verknüpfter Discord-ID |

### Client-Diagnostik (offen)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| POST | `/api/client-log` | AllowAnonymous + RL | Client-seitiges Diagnose-Event `{ kind, detail?, url? }` (v. a. Browser-Engine-Crash/Hänger) — wird strukturiert mit Marker „ClientLog" geloggt (→ ES/Kibana), nichts in der DB. `heartbeat*`-Kinds auf Information, sonst Warning. Frontend: `ClientLogService` (gedrosselt), Engine-Services melden via `reportEngineEvent`-Hook |

### Bestenlisten (auth)
Ranglisten über drei Kategorien je Periode (`daily`/`weekly`/`monthly`/`alltime`, UTC-Grenzen; Woche = ISO/Montag). Nur eingeloggte Nutzer (Menü-Key `leaderboards`, Stufe `Registered`); anonyme Versuche (`UserId == null`) zählen nicht. Logik in `LeaderboardService` (rein lesend, keine neue Tabelle). Kategorien: **Puzzles** = einzigartige gelöste Standard-Puzzles (distinct `PuzzleAttempts.PuzzleId` mit `Solved`, im Fenster), **EndlessRuns** = abgeschlossene `EndlessSessions` (je Lauf), **CourseLines** = gelöste Kurs-Linien (`CoursePuzzleResults`, idempotent = einzigartig). Sortierung Count desc → Name asc; `top` 1–500 (Default 100). Frontend: `/leaderboards` (Perioden-Umschalter + 3 Karten).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/leaderboards?period=&top=` | Auth | Alle drei Bestenlisten für die Periode (`{ period, puzzles[], endlessRuns[], courseLines[] }`, je Eintrag `{ name, discordId?, discordUsername?, count }`) |

### Trainingsziele (auth)
Tagesziele Puzzles/Buch-Kurs (in Minuten) + wöchentliches Spielen-Ziel (Anzahl Rapid-/Classical-Partien pro ISO-Woche) + Wochenziel (volle Tage); effektives Ziel = persönlicher Override > zuletzt aktualisierte Gruppen-Vorlage > keins. Tracker aggregiert je UTC-Tag die verbrachte Zeit (Pro-Einzelpuzzle-Clamp 1800 s) für Puzzles/Buch + die Partienzahl für Spielen und markiert Tage none/partial/full (**Tagesstatus nur aus Puzzles + Buch** — Spielen ist ein Wochenziel). Kategorien-Quellen: Puzzles = PuzzleAttempt + EndlessSession + BookPuzzleAttempt + **CourseAttempt aus Büchern der Art Puzzle**; Buch/Kurs = **CourseAttempt aus Büchern der Art Study** (`Book.Kind` steuert das Routing; **jeder** Kurs-Versuch zählt, nicht nur die Erstlösung). Logik in `TrainingGoalService`; Admin-Vorlage je Gruppe siehe Gruppen-Tabelle.

Spielen-Tracking: `PlayTimeService` (typed HttpClient) holt Lichess exakt (createdAt/lastMoveAt) + chess.com Best-Effort (PGN-Header UTCDate/UTCTime↔EndDate/EndTime) öffentlich ohne Login; `PlayTimeSyncService` (BackgroundService, `PlayTime:IntervalHours`=6) + manueller `/sync-play`-Button. Gezählt: Lichess `speed` rapid+classical, chess.com `time_class` rapid (keine eigene classical-Live-Klasse); Bullet/Blitz/Korrespondenz zählen nicht.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/training-goals` | Auth | Effektives Ziel (`source` personal/group/none, ggf. `groupName`) |
| PUT | `/api/training-goals` | Auth | Persönlichen Override setzen (PuzzleMinutes/BookMinutes 0–600, PlayGames 0–200 Partien/Woche, WeeklyDaysTarget 0–7) |
| DELETE | `/api/training-goals` | Auth | Override entfernen → Rückfall auf Gruppen-Vorlage |
| GET | `/api/training-goals/today` | Auth | Heutiger Fortschritt Puzzles/Buch (Tag) + Spielen-Partien (Woche) + Tagesstatus + Wochenstand (X/Y Tage) |
| GET | `/api/training-goals/tracker?weeks=27` | Auth | Tagesreihe (nur Tage mit Aktivität) für die Tracker-Heatmap; je Tag auch PlayGames (informativ) |
| POST | `/api/training-goals/sync-play` | Auth | Gespielte Rapid-/Classical-Partien (Lichess/chess.com) des eigenen Users sofort synchronisieren |

## Datenbank-Schema (eigene DB `rookhub`, nicht geteilt mit Crawler)

| Tabelle | Zweck | Wichtige Felder / Constraints |
|---------|-------|-------------------------------|
| AppUsers | Auth | Username (unique), Email (unique, **nullable**), PasswordHash, CreatedAt |
| UserProfiles | Schach-Identität | UserId (1:1 zu AppUser), FideId, ChessResultsId, ChessComUsername, LichessUsername, DisplayName, DiscordId (unique, nullable) + DiscordUsername |
| Friendships | Freundesliste | RequesterId, AddresseeId (unique pair), Status (Pending/Accepted/Declined) |
| PuzzleChallenges | Puzzle an Freund(e) schicken | FromUserId, ToUserId (beide Restrict-FK auf AppUser), **Source (Enum Standard/Book)** + PuzzleId (polymorph, **kein FK** — je nach Source `Puzzles.Id` oder `BookPuzzles.Id`), Status (Pending/Solved/Failed), CreatedAt, ResolvedAt?, TimeSpentSeconds?; Index (ToUserId, Status) + (FromUserId) + (Source, PuzzleId) |
| RevengeNotifications | Revanche an gescheitertem Puzzle | AvengerUserId, TargetUserId, PuzzleId (alle Restrict), Solved, CreatedAt, SeenAt?; Index (TargetUserId, SeenAt) |
| Repertoires | PGN-Sammlungen | UserId, Name, Description, Kind (Enum None/Opening/Middlegame/Endgame), IsPublic, CreatedAt, UpdatedAt |
| RepertoireFiles | Einzelne PGNs | RepertoireId, FileName, PgnContent (LONGTEXT), FileSize |
| TournamentSubscriptions | Turnier-Abo | UserId + CrawlerTournamentId (unique pair), TournamentName |
| TournamentFavorites | Markierte Turniere | UserId + CrawlerTournamentId |
| TournamentUserSettings | Per-Turnier-User-Einstellungen | UserId + TournamentId, Highlights/Notes/Pinning |
| TournamentMonitors | Runden-Monitor | TournamentId, RoundsCount, LastSeenRound, AutoSubscribed; `RoundMonitorService` checkt periodisch |
| Puzzles + PuzzleAttempts | Standard-Puzzle-Pool + Versuche | klassische Lichess-Puzzles + Pro-User-Versuche (UserId Cascade) |
| Tags + PuzzleTags | Normalisierte Puzzle-Themen für schnellen Themen-Filter | Tag.Name (unique); PuzzleTag composite PK (PuzzleId, TagId) + denormalisiertes Rating, Index **(TagId, Rating)** → indexgestützter Themen-Filter statt LIKE-Scan. Import pflegt automatisch; **einmaliger Backfill bestehender Puzzles via `POST /api/admin/puzzles/backfill-tags`** (Hintergrund-Job). Bis Backfill: Fallback auf LIKE |
| BookPuzzles | Buch-Puzzles | LineId (unique), BookFileName (indexed), Round, Fen, Moves, Title, Chapter, Comment, Difficulty, BookRating, Tags, **Retired (indexed; ausgemustert → nicht mehr in Daily/Random/Blind-Pools)** |
| BookPuzzleAttempts | Buch-/Tagespuzzle-Versuche | BookPuzzleId (Restrict) + UserId (Cascade, nullable für Anon) + AnonymousSessionId, Solved, TimeSeconds, AttemptedAt; Index (BookPuzzleId, AttemptedAt) + (BookPuzzleId, UserId) |
| Books | Buch-Metadaten | FileName (unique), Title, Author, **Kind** (Enum Puzzle/Study, Default Puzzle; steuert das Trainingsziel-Routing der Kurszeit) |
| DailyPuzzles | Persistierte Tagespuzzle-Zuordnung je UTC-Datum | Date (PK, DATE), BookPuzzleId (Restrict), CreatedAt; vom `DailyPuzzleScheduler` (00:00 UTC) gesetzt oder on-demand bei `/daily/{date}`; Admin-Regenerate ändert nur `BookPuzzleId` (Datum bleibt) |
| Groups | Benutzergruppen | Name (unique), Description, CreatedAt |
| UserGroups | User<->Gruppe (n:m) | Composite PK (UserId, GroupId), Cascade von AppUser + Group |
| EndlessProgresses | Endless Config+Highscore | UserId (unique, nullable), AnonymousSessionId, StartElo, Themes, FasttrackThreshold1/2, StockfishDepth, Highscore, ActiveGameState (LONGTEXT) |
| EndlessSessions | Abgeschlossene Endless Sessions | UserId (nullable), AnonymousSessionId, Timestamp, TotalSolved, MaxRating, DurationSeconds, ConfigJson (TEXT), MistakeAtRatings |
| CourseProgresses | Per-Kurs-Zustand (Buch) | UserId + BookId (unique pair), LastMode ("sequential"/"random"), CreatedAt, UpdatedAt |
| CoursePuzzleResults | Gelöste Buch-Puzzles im Kurs (idempotente „gelöst"-Menge für Fortschritt) | UserId + BookPuzzleId (unique pair), BookId (denormalisiert, indexed mit UserId), SolvedAt, TimeSeconds (nur Erstlösung; **nicht mehr Aggregations-Quelle**) |
| CourseAttempts | Append-only Zeit-Log JEDES Kurs-Versuchs (gelöst/fehlgeschlagen/Wiederholung) für die akkumulierte Kurs-/Studienzeit im Trainingsziele-Tracker | UserId (Cascade) + BookId (denormalisiert für Kind-Join, Cascade) + BookPuzzleId (Restrict), Solved, TimeSeconds, AttemptedAt; Index (UserId, AttemptedAt) |
| BookGroupAccesses | Welche Gruppe darf welches Buch als Kurs sehen | Composite PK (BookId, GroupId), Cascade von Book + Group, Index GroupId |
| WeeklyPosts | Wochenpost (terminiertes PGN) | Title, FileName, PgnContent (LONGTEXT), FileSize, ScheduledAt (indexed), CreatedAt, UpdatedAt |
| WeeklyPostAttempts | Per-User-Fortschritt Wochenpost | WeeklyPostId + UserId + PuzzleIndex (unique triple), Solved, TimeSeconds, AttemptedAt; beide FKs Cascade |
| GroupTrainingGoals | Coach-Vorlage Trainingsziel je Gruppe | GroupId (unique, Cascade von Group), PuzzleMinutes, BookMinutes, PlayGames (Partien/Woche), WeeklyDaysTarget, CreatedAt, UpdatedAt |
| UserTrainingGoals | Persönlicher Trainingsziel-Override | UserId (unique, Cascade), PuzzleMinutes, BookMinutes, PlayGames (Partien/Woche), WeeklyDaysTarget, CreatedAt, UpdatedAt |
| PlayTimeDailies | Gespielte Rapid-/Classical-Partien je UTC-Tag/Plattform | UserId + Date + Platform (unique, Cascade), Games (Anzahl Partien), UpdatedAt; befüllt vom `PlayTimeSyncService` |
| PlayTimeSyncs | Sync-Cursor externe Spielzeit | UserId + Platform (unique, Cascade), LastGameTimestamp (ms), LastSyncedAt, LastError |
| UserApiTokens | Personal-Access-Tokens für Maschinen-Clients (chess.com-Extension) | UserId (Cascade), Name, TokenHash (SHA-256, UNIQUE), Prefix (12 char), Scope ("extension"), CreatedAt, LastUsedAt, ExpiresAt (nullable); Index (UserId, Name) |
| PasswordResetTokens | „Passwort vergessen"-Einmal-Token | UserId (Cascade), TokenHash (SHA-256-Hex, UNIQUE), CreatedAt, ExpiresAt, UsedAt (nullable); Roh-Token nur per Mail, nie gespeichert. Beim Anfordern werden ältere offene Tokens des Users entwertet |
| MenuItemSettings | Admin-Override der Menü-Sichtbarkeit | ItemKey (PK, string), Level (Enum All/Registered/Groups/Admin); fehlt eine Zeile → Default aus `MenuRegistry` |
| MenuItemGroupAccesses | Welche Gruppe sieht einen gruppen-gegateten Menüeintrag | Composite PK (ItemKey, GroupId), Cascade von MenuItemSetting + Group, Index GroupId |
| ChessableCredentials | Per-User Chessable-Bearer (1:1) | UserId (unique, Cascade), EncryptedBearer (TEXT, AES via `EncryptionService`), CreatedAt, UpdatedAt; Plaintext nie persistiert. Wird vom `ChessableProxyService` an piratechess durchgereicht |
| AdminMessages | Admin↔User-Direktnachrichten (Thread je User) | UserId (Cascade, = Thread-Schlüssel/Nicht-Admin-Teilnehmer), SenderId (Audit), FromAdmin (bool, Richtung), Body (max 4000), CreatedAt, SeenByUserAt?, SeenByAdminAt?; Index (UserId, CreatedAt) + (FromAdmin, SeenByAdminAt) |
| MessageThreads | Metadaten/Zuweisung einer Konversation (1 Zeile je User) | UserId (PK + FK AppUser Cascade), ClaimedByAdminId? (welcher Admin übernommen hat, **ohne FK** → vermeidet doppelte Cascade-Pfade; Name wird beim Abruf aufgelöst), ClaimedAt?; entsteht mit der ersten Nachricht |

Cascade Deletes: AppUser → Profile, Repertoires, Subscriptions, EndlessProgresses, EndlessSessions, UserGroups, CourseProgresses, CoursePuzzleResults, CourseAttempts, UserTrainingGoals, PlayTimeDailies, PlayTimeSyncs, WeeklyPostAttempts; Repertoire → Files; Group → UserGroups, BookGroupAccesses, GroupTrainingGoals; Book → BookPuzzles, CourseProgresses, CoursePuzzleResults, CourseAttempts, BookGroupAccesses (CoursePuzzleResult.BookPuzzle + CourseAttempt.BookPuzzle = Restrict, um doppelte Cascade-Pfade zu vermeiden); WeeklyPost → WeeklyPostAttempts; AppUser → AdminMessages + MessageThreads (über UserId, der Nicht-Admin-Teilnehmer; MessageThread.ClaimedByAdminId hat bewusst keinen FK). Admin-DeleteBook und GroupController.Delete räumen die abhängigen Kurs-/Freigabe-/Ziel-Vorlagen-Daten zusätzlich explizit ab (InMemory-Tests cascaden nicht).
Friendships nutzen Restrict (kein Cascade) wegen zwei FKs zur selben Tabelle.

## Projektstruktur

```
compose.dev.yml             Dev-Stack ohne VPN (MariaDB + Crawler + API + Frontend)
compose.vpn.yml             Prod-Stack mit Gluetun VPN (WireGuard)
init-db.sh                  Erstellt beide DBs + User beim ersten MariaDB-Start
.env.dev.example            Umgebungsvariablen-Template (Development)
.env.vpn.example            Umgebungsvariablen-Template (VPN/Production)
twa/                        Android-TWA-Build-Gerüst (Bubblewrap, GH-Action — prod + dev-Variante)
src/
  api/RookHub.Api/
    Controllers/            Auth, Profile, Friend, Repertoire, Extension, TournamentProxy,
                            TournamentFavorite, TournamentMonitor, Subscription, BookPuzzle,
                            Course, Endless, Group, WeeklyPost, TrainingGoal, ClientLog,
                            Puzzle, Admin, Me, BotStats, BaseApiController
    Services/               Auth, Profile, Friend, Repertoire, CrawlerProxy, PlayerSearch,
                            BookPuzzle, Course, Puzzle, EndlessProgress, TrainingGoal,
                            PlayTime, PlayTimeSync, WeeklyPost, BotStats,
                            ApiToken+ApiTokenAuthenticationHandler, DiscordLink, PgnImport,
                            SchachBotWebhook, BackgroundTaskQueue, Admin, BookAdmin,
                            AdminSeeder, AutoSubscription, RoundMonitor,
                            DailyPuzzleScheduler, Heartbeat
    Models/                 EF-Entities (1:1 zum Schema oben)
    DTOs/                   Request/Response-Typen je Endpoint-Familie
    Data/                   AppDbContext, DesignTimeDbContextFactory, Migrations/
    Program.cs              Startup: DB, JWT+ApiToken Policy-Scheme, CORS, Swagger,
                            Auto-Migration, Health-Endpoint, BackgroundServices,
                            ForwardedHeaders (private Peers only)
    Dockerfile              Multi-stage .NET Build
  frontend/
    app/                    Angular 19 CLI-Projekt (siehe src/frontend/CLAUDE.md)
    nginx.conf              Proxy /api/ → api:8080, SPA-Fallback
    Dockerfile              Multi-stage Node Build + nginx
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

### Test-Pattern
- **InMemory DB** pro Testklasse via `UseInMemoryDatabase(Guid.NewGuid().ToString())`
- **IDisposable** für DB-Cleanup
- **xUnit `[Fact]`** Attribute
- **Namenskonvention**: `MethodName_Scenario_ExpectedResult`
- **Service-Tests** testen direkt gegen InMemory-DB
- **Controller-Tests** instanziieren den Controller direkt; `BaseApiController.GetUserId()` wird via `ControllerContext` mit `ClaimsPrincipal` + `ClaimTypes.NameIdentifier` gemockt
- **Helper-Methode** `CreateUserAsync()` pro Testklasse für Test-Daten
- **InMemory cascaded nicht** — Admin-Delete-Pfade räumen abhängige Daten explizit ab; Tests entsprechend prüfen

## EF Core Migrations

```bash
cd src/api/RookHub.Api
dotnet ef migrations add <MigrationName>    # Nutzt DesignTimeDbContextFactory
dotnet ef database update                   # Braucht laufende MariaDB
```
Auto-Migration ist in `Program.cs` aktiv – beim Start werden Migrations automatisch angewendet.

## Offene Aufgaben

Nicht direkt angegangene Bugs, geparkte Features, Refactoring-Ideen und periodische Aufgaben (Code Review, Security Review etc.) werden in **`rookhub/TODO.md`** geführt. Neue Punkte dort eintragen, nicht separat als Markdown-Datei anlegen.

## Arbeitsweise

- **PFLICHT: `git pull` vor jedem Edit** — sobald du anfängst, Dateien auf der Platte zu ändern, MUSS unmittelbar davor ein `git pull` (bzw. `git pull --rebase`) laufen. Beide Stack-Kopien + diese Windows-Workstation arbeiten parallel am selben Remote; ein Edit auf einem N Versionen alten Stand führt unweigerlich zu Merge-Konflikten und verlorener Arbeit (passiert vor v0.95.2 mit 10 verpassten Commits). Lesen/Recherchieren ohne Pull ist OK; sobald du `Edit`/`Write` greifst → vorher pullen.
- **Commit early, commit often** – nach jedem abgeschlossenen Feature, Fix oder logischen Schritt committen. Kleine, atomare Commits sind besser als ein großer Sammel-Commit.
- **Tags NUR auf Zuruf** – NIEMALS automatisch Git-Tags erstellen. Der User muss vorher testen und explizit nach einem Tag fragen.
- **CI/CD**: Docker-Images werden nach Push automatisch gebaut (GitHub Actions). Kein manueller Build nötig.
- **NIEMALS automatisch deployen** — weder auf Dev noch auf Prod. Der User startet Deploys immer selbst explizit.

## Versionierung

- **Aktuelle Version**: `0.146.0` — Details + Historie ausschließlich in `src/frontend/app/src/environments/changelog.ts` (Single Source: `APP_VERSION` + `CHANGELOG`). Hinweis: 0.145.0 (Admin-Kurs-Download im Namen eines Users) **bewusst NICHT im CHANGELOG** (internes Admin-Tool, auf Wunsch nicht öffentlich vermerkt); 0.144.0 = „Originale Lösung zeigen" (parallel gepusht); 0.146.0 = Bestenlisten
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

## Wichtige Konventionen

- **Puzzle-Modi konsistent halten** – Standard (`puzzle.component`), Endless (`endless-puzzle.component`) und Book/Course/Weekly/Daily (`book-puzzle.component` – ist selbst schon Mehr-Modus-Template) sollen optisch + funktional so ähnlich wie möglich bleiben. Wenn ein Modus eine UI-/UX-Erweiterung bekommt (z. B. „Tags ausklappbar", „Eval-Button", „Viz-Pfeil"), **immer kurz nachfragen**, ob das nicht auch in den anderen zwei Modi sinnvoll wäre. Gemeinsame Bausteine in dedizierte Komponenten (`PuzzleTagsComponent`, `VizCardComponent`, `ReviewNavComponent`, `ThemePickerComponent`) auslagern statt 3-fach kopieren; die Solver-Mechanik liegt in `BasePuzzleSolver`.
- **Keine Default-Werte in Compose-Example-Dateien** – `compose.yml.example` und `compose.vpn.example` verwenden `${VAR}` ohne `:-default`. Alle Werte müssen explizit in der `.env`-Datei gesetzt werden.
- **i18n-Validierung**: Nach jeder Änderung an `src/frontend/app/src/assets/i18n/*.json` alle 25 Sprachdateien mit `JSON.parse` validieren — Trailing-Comma-Fehler bricht ngx-translate komplett, UI zeigt dann nur noch Schlüssel statt Texte
- **Literal-Routen vor Parameter-Routen**: z.B. `GET /api/weekly-posts/progress` MUSS vor `GET /api/weekly-posts/{id}` deklariert sein, sonst matcht der Router „progress" als ID
- Crawler-Proxy-Endpoints müssen mit tatsächlichen Crawler-Routen übereinstimmen
- Angular nutzt lazy-loaded standalone components (kein NgModule)
- JWT-Claims: `ClaimTypes.NameIdentifier` = UserId, `ClaimTypes.Name` = Username
- PGN-Upload-Limit: 10 MB pro Datei (in `RepertoireService`)
- Alle Controller holen UserId via `User.FindFirstValue(ClaimTypes.NameIdentifier)`
- Friendship-Status ist eine State Machine: Pending → Accepted/Declined; nur der Addressee kann Accept/Decline ausführen
- Stockfish-WASM **NICHT** über Service-Worker cachen außer in eigener assetGroup `engine` (installMode prefetch) — der Glue muss bei `instantiateStreaming`-Fehler auf `instantiate(arrayBuffer)` zurückfallen, sonst hängt die Analyse
- HMAC-Webhooks zum Bot: gleiches Secret-Pattern (`SchachBot:WebhookSecret` für Tagespuzzle/Wochenpost, `SchachBot:StatsSecret` für Bot-Stats-Pull) — `ComputeHmacHex` aus `SchachBotWebhookService` wiederverwenden
