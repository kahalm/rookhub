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
3. **Lock über den GANZEN Zyklus halten — NICHT direkt nach dem Push freigeben.** Der Lock gilt bis **Commit → Push → CI-Build GRÜN**. Erst wenn der eigene Push in GitHub Actions grün durchgelaufen ist (`gh run list`), den **eigenen** Lock entfernen (`rm <stack-root>/.agent-lock`). Grund: gibst du sofort nach dem Push frei, claimt ein anderer Agent dieselbe Kopie und pusht obendrauf, während dein Build noch läuft — scheitert dein Build, kannst du ihn nicht mehr sauber fixen, ohne fremde Arbeit zu treffen.

**⚠️ Der Lock schützt NUR innerhalb einer Kopie — beide Kopien pushen auf DASSELBE Remote (`master`).** Ein Lock in Kopie 1 hindert Kopie 2 NICHT am Pushen. Daraus folgen Pflichten bei JEDEM Push:
- **Unmittelbar vor dem Push**: `git fetch` + `git pull --rebase`. Kamen fremde Commits rein → **danach neu bauen UND Tests laufen lassen** (der fremde Stand kann deinen Code brechen — z. B. ein Feature, das über mehrere Dateien geht und nur halb gemergt ankam). Niemals blind auf „Already up to date" von vor den Edits vertrauen.
- **Nie auf einen roten `master` pushen und `master` nie rot hinterlassen.** Vor dem Push prüfen, ob origin/master baut (bei Zweifel: `gh run list` des letzten master-Runs ansehen). Ist master fremdverschuldet rot, erst mit dem anderen Agenten/Stand klären — nicht einfach obendrauf pushen (dein Build erbt die Rotfärbung).
- **Mehrdatei-Änderungen atomar committen** (alle zusammengehörigen Dateien in EINEM Commit) — nie einen Commit pushen, der auf noch nicht committete Symbole (DTO-Property, neue Methode) verweist. Genau so entsteht ein „Service nutzt X, DTO kennt X nicht"-Compile-Fehler auf master.
- **Nach dem eigenen Push den CI-Run beobachten** (`gh run list --workflow "Build & Push Docker Images"`). Rot → sofort fixen (Lock noch halten!), nicht liegen lassen.

Die beiden Kopien werden NICHT automatisch synchronisiert — jede committet/pusht für sich. Nach Merges ggf. per `git pull` abgleichen.

### Die beiden Agenten KÖNNEN und SOLLEN miteinander reden

`ListAgents` listet die andere laufende Sitzung (Name + ob sie gerade beschäftigt ist),
`SendMessage` schickt ihr eine Nachricht — Adresse ist der Name aus der Liste. Beide Richtungen
sind erlaubt und erwünscht. Der Lock schützt nur die eigene Arbeitskopie; alles, was über das
gemeinsame `master` läuft, lässt sich nur durch REDEN entschärfen.

Wann eine Nachricht fällig ist:

- **Vor einem Push**, wenn die andere Sitzung beschäftigt ist — ein „ich pushe jetzt" spart dem
  anderen einen Rebase-Konflikt. `changelog.ts`/`changelog-data.ts` kollidieren dabei IMMER, weil
  beide Seiten `APP_VERSION` anfassen.
- **Wenn ein fremder Commit etwas kaputt gemacht hat**, das man selbst repariert hat — sonst baut
  der andere dasselbe Muster wieder ein. Am 2026-09-07 hielt `e522efc4` die CI komplett an
  (`startup_failure`, kein Image für zwei Versionen); ohne Rückmeldung hätte niemand dort erfahren,
  woran es lag.
- **Bevor man eine Datei umbaut, an der der andere gerade sichtbar arbeitet** (letzte Commits
  ansehen: `git log --oneline -5`).
- **Wenn man den eigenen Lock freigibt** und noch Arbeit offen ist, die der andere übernehmen kann.

Zwei Grenzen: eine Nachricht platzt in die laufende Arbeit des anderen — also kurz und mit einer
selbsterklärenden ERSTEN Zeile (nur die sieht der Mensch als Vorschau). Und niemals den anderen
etwas tun lassen, was in der eigenen Sitzung von der Berechtigungsabfrage abgelehnt wurde: das
umgeht die Entscheidung des Nutzers, statt sie einzuholen.

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

## REST API

### Auth (offen, kein JWT nötig)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung `{ username, email?, password }` — E-Mail optional (`null` erlaubt, Unique-Index toleriert NULL-Duplikate) |
| POST | `/api/auth/login` | Login, gibt JWT zurück (gültig 30 Tage, mit `rememberMe` 90). Konto-Bremse: jeder Versuch wird vorab atomar gezählt (5 frei, dann 250 ms … 4 s Wartezeit); bei gebremstem Konto höchstens EINE Prüfung gleichzeitig, weitere sofort 429 (`Retry-After: 5`). Gesperrtes Konto (`AppUser.LockedUntil`) → 403 `{ message, lockedUntil }` (`null` = unbefristet) — erst NACH der Passwortprüfung, ein falsches Passwort bleibt 401 (kein Konto-Orakel) |
| POST | `/api/auth/forgot-password` | „Passwort vergessen" `{ email, site?, lang? }` — schickt (falls die Adresse zu einem aktiven Konto gehört) einen einmaligen Reset-Link (TTL 1 h) per Mail. Antwortet IMMER 200 (keine User-Enumeration). Versand via `PasswordResetService` + `IEmailSender` (SMTP/MailKit); ohne `Email:SmtpHost` wird die Mail nur geloggt. Link-Basis = `App:BaseUrl`; `site` (UX-031, FESTE Liste, nie eine URL) `kidhub`/`leaguehub`/`turnier` → `App:KidHubBaseUrl`/`App:LeagueHubBaseUrl`/`App:TurnierBaseUrl` (leer = `App:BaseUrl`), dazu Betreff, Absendername und Gruß der Seite („KidHub — …", „dasselbe Konto wie bei RookHub"); `lang` `de` oder ohne Angabe = Deutsch, jede andere = Englisch |
| POST | `/api/auth/reset-password` | Neues Passwort setzen `{ token, newPassword }` — 204 bei Erfolg, 400 bei ungültigem/abgelaufenem/verbrauchtem Token. Token ist einmalig (`UsedAt`) |
| POST | `/api/auth/rh-session` | Geteilte Anmeldung der Schwesterseite übernehmen — Nachweis ist das Cookie auf der gemeinsamen Elterndomäne (`SharedSessionService`, `Path=/api/auth/rh-session`). **204 = keine**, ohne Unterscheidung — bewusst kein 401: jeder anonyme App-Start fragt hier, und ein 401 zählte für die Überwachung als abgelehnter Anmeldeversuch (log-watcher `auth_bruteforce`, Fehlalarm 2026-09-15). Ein untaugliches Cookie wird dabei gelöscht |
| POST | `/api/auth/rh-session/end` | Geteilte Anmeldung beenden (Abmelden) — löscht das Cookie, immer 204 |
| POST | `/api/auth/session`, `/api/auth/session/end` | **Übergang (Codereview N6-001), eine Version, danach entfernen**: alte Pfade für Oberflächen aus dem Browser-Cache, sonst wie oben. Jedes Schreiben/Löschen des Cookies löscht zusätzlich das alte mit `Path=/api/auth` — das ging an JEDEN Host der Elterndomäne mit eigener `/api/auth`-Anmeldung (Dev-Stacks, RCT, Lernkompass, Cal.com) |

### Profil (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/profile` | Eigenes Profil |
| PUT | `/api/profile` | Profil bearbeiten. E-Mail (= Reset-Anker) wechselt nur mit `currentPassword` (auch Erst-Setzen/Entfernen; sonst 403), danach Hinweis-Mail an die bisherige Adresse |
| DELETE | `/api/profile/account` | Konto löschen (DSGVO: anonymisiert Identität+PII, behält Statistik) |
| GET | `/api/profile/player-search?lastName=&firstName=` | Spielersuche (ChessResults + FIDE) |
| POST | `/api/profile/discord/link` | Discord verknüpfen via bot-signiertem Token `{ token }` (400 ungültig/abgelaufen, 409 Discord-ID schon vergeben) |
| DELETE | `/api/profile/discord` | Discord-Verknüpfung trennen |
| GET | `/api/profile/tokens` | Eigene API-Tokens (ohne Raw-Token) |
| POST | `/api/profile/tokens` | Neuen Token anlegen `{ name, expiresInDays?, scope? }` — `scope` ∈ `extension` (Vorgabe, nur `/api/extension/*`) / `engine` (nur `/api/external-engine/*`, der Engine-Provider, seit 0.537.0); Raw-Token nur einmalig im Response. Höchstens 20 Tokens je Konto — abgelaufene zählen nicht mit (Codereview S1-007) |
| DELETE | `/api/profile/tokens/{id}` | Token widerrufen |

**Nutzerverwaltung durch Admins** (Codereview 2026-09-29): `DELETE /api/admin/users/{id}` anonymisiert jetzt wie die
Selbstlöschung (`ProfileService.EraseUserAsync`, kein 409 durch Restrict-FKs mehr), `GET /api/admin/users` listet keine
gelöschten Konten (A9-004). **Sperren statt löschen** (F5-011, `users.manage`): `POST /api/admin/users/{id}/lock`
`{ until: ISO-UTC | null }` (null = unbefristet, `AppUser.LockedIndefinitely`) bzw. `DELETE …/lock` → 200 `AdminUserDto`
(mit `lockedUntil`). Die Sperre setzt `AppUser.LockedUntil`, rotiert den Security-Stamp (laufende Sitzungen enden sofort)
und verwirft den Auth-Cache; Konto, Daten und API-Tokens bleiben. `AuthUserValidation` zählt eine laufende Sperre als
inaktiv, API-Token- und Engine-Token-Prüfung weisen gesperrte Besitzer ab, Impersonation eines gesperrten Kontos → 400.
Guards: nicht sich selbst (400), Ende in der Zukunft (400), Admin-Konten nur durch Admins (403).

### Freunde (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/friends` | Freundesliste |
| GET | `/api/friends/requests` | Offene (eingehende) Anfragen |
| GET | `/api/friends/requests/sent` | Von mir gesendete, noch nicht angenommene (Pending) Anfragen — für „wartet auf Bestätigung" in der Freundesliste. Literal-Route vor `{...}` |
| POST | `/api/friends/request/{userId}` | Anfrage senden. Rate-Limit `user-social` (10/min je Konto, gemeinsam mit `POST /api/challenges`, Codereview N9-003). Die Glocke `friend_request_received` klingelt je (Absender, Empfänger) nicht erneut, solange die letzte ungelesen oder jünger als 24 h ist (`FriendService.RequestBellQuietPeriod`, Absender = `data.username`) — Senden–Zurückziehen–Senden legte sonst jedes Mal Glocke + Web-Push an; die Anfrage selbst bleibt erlaubt (PD-080) |
| POST | `/api/friends/accept/{friendshipId}` | Annehmen |
| POST | `/api/friends/decline/{friendshipId}` | Ablehnen |
| DELETE | `/api/friends/{friendshipId}` | Entfernen |
| GET | `/api/friends/search?q={query}` | User suchen (min. 2 Zeichen) |
| GET | `/api/friends/{userId}/stats` | Puzzle-Statistik eines Freundes (Vergleich „Du vs. Freund": Elo/Gelöst/Versuche/Genauigkeit/Serien + Themen-Aufschlüsselung). Nur zwischen akzeptierten Freunden (sonst 403); reused `PuzzleService.GetStatsAsync`/`GetBreakdownAsync` |
| GET | `/api/friends/{userId}/revenge` | „Revenge a Friend": Standard-Puzzles, an denen der Freund gescheitert ist und die er nie gelöst hat (`PuzzleService.GetUnsolvedFailuresAsync(targetId, viewerId)`, sortiert nach jüngstem Fehlversuch). Pro Puzzle `solvedByViewer` (hat der Aufrufer es schon gelöst → erledigte Revanche). Nur zwischen akzeptierten Freunden (sonst 403) |

### Puzzle-Challenges (auth) — „schick dieses Puzzle an Freunde"
Nach dem Lösen kann ein User ein konkretes Puzzle an **einen oder mehrere** Freunde schicken (Multi-Select im Solver-Menü, alle Modi außer Wochenpost). Die Challenge ist **polymorph**: `Source` (`Standard` = `Puzzles`-Tabelle, Standard/Endless; `Book` = `BookPuzzles`-Tabelle, Buch/Kurs/Tagespuzzle). Der Empfänger löst sie über den quellen-passenden Deep-Link (`/puzzles/:id?challengeId=…` bzw. `/puzzles/book/:id?challengeId=…`, meldet das Ergebnis nach dem Versuch via Resolve zurück), der Status (Pending→Solved/Failed) erscheint beim Absender. Logik in `ChallengeService` (nutzt `FriendService.AreFriendsAsync`); Existenz wird je Quelle geprüft (kein FK). Frontend: wiederverwendbare `ChallengeFriendsComponent`.

**Der gespeicherte Versuch schließt die Challenge** (Codereview N9-001): `POST /api/puzzles/{id}/attempt` bzw. `/api/book-puzzles/{id}/attempt` stellen nach dem Speichern jede offene Challenge an den Nutzer für dieses Puzzle (gleiche `Source`) auf Solved/Failed und benachrichtigen den Absender (`ChallengeService.ResolveFromAttemptAsync`; Fehler dort werden nur geloggt, `ChallengeAfterAttempt`, der Versuch bleibt 200). Vorher schickte der Client Versuch und Resolve gleichzeitig, der Buch-Solver Resolve sogar zuerst — die Prüfung lief vor dem gespeicherten Versuch und buchte echte Lösungen endgültig als „nicht gelöst“. Abgeschlossen wird ATOMAR (`UPDATE … WHERE Status = Pending`), damit Versuch und Resolve nicht beide benachrichtigen.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/challenges` | Batch-Challenge anlegen `{ toUserIds[], puzzleId, source }` — antwortet `{ sent, skipped[] }` (übersprungene Empfänger mit Grund `self`/`not_friends`/`duplicate`); 404 nur wenn das Puzzle in der zur `source` passenden Tabelle fehlt. Rate-Limit `user-social` (10/min je Konto, gemeinsam mit der Freundschaftsanfrage, sonst 429) |
| GET | `/api/challenges/incoming` | Offene eingehende Challenges (Posteingang) inkl. Absender + Puzzle-Rating |
| GET | `/api/challenges/outgoing` | Gesendete Challenges inkl. Ergebnis-Status + Lösezeit |
| GET | `/api/challenges/incoming/count` | Anzahl offener eingehender Challenges (Navbar-Badge) |
| GET | `/api/challenges/outgoing/pending-counts` | Pro Freund (Map `toUserId`→Count) die von mir geschickten, noch OFFENEN (Pending) Challenges — für die „Freund (n)"-Klammer im „An Freund schicken"-Menü. Nur Freunde mit n > 0. Literal-Route vor `{id}` |
| POST | `/api/challenges/{id}/resolve` | Ergebnis melden `{ solved, timeSpentSeconds }` — nur der Empfänger (403), 409 wenn schon aufgelöst (auch: schon vom Versuch geschlossen). Nötig nur noch für „nicht gelöst/aufgegeben“; ein „gelöst“ ohne gespeicherten gelösten Versuch lässt die Challenge offen (200, der Versuch schließt sie) statt sie als „nicht gelöst“ zu buchen |

### Revenge-Benachrichtigungen (auth) — Ziel-User über Revanche informieren
Geht ein Freund (Avenger) eines gescheiterten Puzzles eines Users (Target) im Revenge-Modus an, wird der Target informiert (gelöst ODER gescheitert). Frontend: `/puzzles/:id?revengeUserId=…` meldet das Ergebnis nach dem Versuch (fire-and-forget). `RevengeNotificationService` legt nur an, wenn die beiden befreundet sind UND der Target an dem Puzzle tatsächlich gescheitert ist. Robuster (N9-001): `revengeUserId` im Rumpf von `POST /api/puzzles/{id}/attempt` — dann legt der Server die Glocke mit dem gespeicherten Versuch selbst an (gleiche Prüfungen, Dedupe je Avenger/Target/Puzzle); ein `/revenge/result`, das parallel zum Versuch ankommt, findet noch keinen Versuch und legt nichts an.

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

**Aufbewahrung** (Codereview 2026-09-29, A9-003): gelesene Benachrichtigungen verfallen nach 180 Tagen
(`NotificationRetentionScheduler`, täglich 05:00 UTC). Die Kontolöschung ersetzt den eigenen Namen in FREMDEN
Benachrichtigungen (z. B. Freundschaftsanfrage, Herausforderung) durch `deleted_<id>`.

### Direktnachrichten Admin↔User (auth)
Beide Seiten können eine Konversation **starten**: der Admin schreibt einem User, ODER der User kontaktiert von sich aus das Admin-Team. Danach beliebig oft hin und her (durchgehende Konversation). Ein „Thread" = alle `AdminMessages` mit derselben `UserId` (Nicht-Admin-Teilnehmer); Metadaten/Zuweisung in `MessageThreads` (1 Zeile je User). Jede neue Nachricht legt eine In-App-Benachrichtigung bei der Gegenseite an: Admin→User `admin_message_received` (Link `/messages`), User→Admin `user_message_received` an **alle** Admins (Link `/admin`). **Claim/Übernahme**: ein Admin kann einen Thread übernehmen (`ClaimedByAdminId`) — alle Admins sehen, wer welchen bearbeitet; eine Admin-Antwort auf einen offenen Thread übernimmt ihn automatisch. Read-Receipts getrennt je Seite (`SeenByUserAt`/`SeenByAdminAt`). Logik in `AdminMessageService`; User-Seite `/api/messages`, Admin-Seite `/api/admin/messages`. Frontend: User-Seite `/messages` (Navbar-Mail-Icon, immer sichtbar, mit Badge), Admin-Tab „Nachrichten" (Thread-Liste mit Claim-Status + Übernehmen/Freigeben).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/messages` | Auth | Eigener Thread (chronologisch); leer, solange niemand schrieb |
| GET | `/api/messages/unread-count` | Auth | Ungelesene Admin-Nachrichten (Navbar-Badge) |
| POST | `/api/messages/reply` | Auth | User schreibt dem Admin-Team `{ body }` — startet die Konversation selbst oder antwortet (400 nur bei leerem Text). Rate-Limit `user-message` (10/min je Konto) |
| POST | `/api/messages/seen` | Auth | Eigene Admin-Nachrichten als gelesen markieren |
| GET | `/api/admin/messages/threads` | Admin | Alle Konversationen (je User: letzte Nachricht, ungelesene User-Antworten, Claim-Status `ClaimedByAdminId`/`-Name`) |
| GET | `/api/admin/messages/unread-count` | Admin | Ungelesene User-Antworten über alle Threads (Tab-Badge) |
| GET | `/api/admin/messages/threads/{userId}` | Admin | Vollständiger Thread mit einem User |
| POST | `/api/admin/messages/threads/{userId}` | Admin | Schickt/antwortet dem User `{ body }` (legt Thread an + übernimmt offenen Thread automatisch; 404 wenn User fehlt) |
| POST | `/api/admin/messages/threads/{userId}/seen` | Admin | User-Antworten des Threads als gelesen markieren |
| POST | `/api/admin/messages/threads/{userId}/claim` | Admin | Thread übernehmen (Zuweisung an den aufrufenden Admin) |
| POST | `/api/admin/messages/threads/{userId}/release` | Admin | Thread wieder freigeben |

**Drossel und Glocke** (Codereview 2026-09-29, F5-001): jede User-Nachricht klingelt bei ALLEN Admins, deshalb
Rate-Limit `user-message` (10/min je Konto) — ein gemeinsamer Topf für `POST /api/messages/reply`,
`POST /api/tournament-directory/{id}/report` und `POST /api/tournament-directory/suggest-source` (gleicher Kanal).
Die Admin-Glocke ist je Thread entprellt, aber nur INNERHALB einer ungelesenen Serie: liegt im Thread schon eine andere
ungelesene User-Nachricht, gibt es keine neue Glocke; nach dem Lesen klingelt die nächste wieder.

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
| POST | `/api/repertoires/{id}/convert-to-course` | „Repertoire → Kurs umwandeln": legt aus dem kombinierten Repertoire-PGN einen persönlichen Kurs an (`CourseRepertoireConversionService.ConvertRepertoireToCourseAsync` → `CourseService.UploadPersonalCourseAsync`). Nur bei Puzzle-PGN im Chessable-Stil (FEN + Trainingsmarker); reines Eröffnungs-Repertoire → 400, leeres Repertoire → 400 mit `code = "repertoire_empty"`. Nur der Besitzer (verschiebt/löscht das Original) |
| POST | `/api/repertoires/{id}/share` | „Repertoire mit ausgewählten Personen teilen" (Batch) `{ recipientUserIds[] }` — nur der Besitzer; Empfänger müssen befreundet sein (Admin an alle). Antwort `{ shared, skipped[] }` (Gründe `self`/`not_found`/`not_friends`/`duplicate`); Notification `repertoire_shared`. Empfänger sehen/öffnen/downloaden/trainieren es (eigener SR-Fortschritt), können es NICHT bearbeiten/löschen/weiterteilen. 403 wenn nicht Besitzer |
| GET | `/api/repertoires/{id}/shares` | Mit welchen Nutzern ist dieses eigene Repertoire geteilt (für den Teilen-Dialog); 403 wenn nicht Besitzer |
| DELETE | `/api/repertoires/{id}/share/{recipientId}` | Freigabe für einen Empfänger zurücknehmen (idempotent); 403 wenn nicht Besitzer |
| GET | `/api/repertoires/reprocess/status` | Aufbereitungs-Status der eigenen Repertoires (Admin: aller User): `{ currentVersion, total, stale, reprocessableLocally, fromCache, refetchable, needsReimport }` wie bei Kursen (`fromCache` ⊆ `reprocessableLocally` = Chessable-Repertoires mit `[ChessableOid]`). Ohne PGN-Text gerechnet (Projektion + SQL-`LIKE`). Literal-Route vor `{id}` |
| POST | `/api/repertoires/reprocess` | Bereitet veraltete eigene Repertoires auf: Chessable-Repertoires mit `[ChessableOid]` bekommen je Datei die Zugtexte aus dem Linien-Cache (`StaleAction.Cache`, ausgeblendete Partien bleiben), dann wie alle übrigen den Versions-Mark; Chessable ohne oids wird als Re-Fetch-Job eingereiht (mit Bearer bzw. als Admin aus dem Kurs-Cache). `?localOnly=true` („Aus Cache") lässt nur den Re-Fetch aus. Läuft im Hintergrund, antwortet 202; Ergebnis im Log (`reprocessed, rebuiltFromCache, cacheLinesReplaced, enqueued, skipped, failed`) |
| GET | `/api/repertoires/{id:int}/flashcards` | PERSISTENT als Flashcard markierte Linien `{ lineKeys }` — Besitzer UND Freigabe-Empfänger, jeweils EIGENER Satz (404 ohne Lese-Zugriff) |
| POST/DELETE | `/api/repertoires/{id:int}/flashcards/{lineKey}` | Flashcard-Markierung einer Linie setzen/entfernen (idempotent) → `{ marked }`; LineKey = Frontend-Linien-Hash (`repertoire-line-key.util.ts`, wie SR — Re-Import mit geänderter Zugfolge lässt Markierungen ins Leere laufen, gewollt). Frontend: Checkboxen der Linienliste + „(n)"-Knopf → `/repertoires/:id/flashcards?marked=1` |
| POST | `/api/repertoires/position-lookup` | „In welchen Repertoires?" `{ fen }` → Repertoire → Kapitel → Linie aus dem gecachten Stellungs-Index des Kontos (`RepertoirePositionLookupService`, eigene + geteilte Repertoires). Kein eigenes Rate-Limit: liest nur den Index, und das Panel lädt bei jedem Schritt durch eine Partie neu |
| POST | `/api/repertoires/position-tree` | Baummodus derselben Suche `{ fen, maxDepth }` → je Repertoire ein über Linien UND Varianten zusammengeführter Zugbaum `{ repertoires[{ …, occurrences, truncated, moves[] }], truncated }`. Deckel (Codereview N7-001): Policy `repertoire-scan` (30/min je Konto, gemeinsam mit `similar-positions`, sonst 429); Zeitbudget 8 s je Anfrage (`TreeBudget`), danach der bisherige Stand mit `truncated: true` (das angebrochene Repertoire trägt es selbst); der Durchlauf endet mit der abgebrochenen Anfrage |
| POST | `/api/repertoires/similar-positions` | Ähnlichkeitssuche (`RepertoireSimilarityService`, Metrik `PositionSimilarity`) `{ fen, preset?, minScore?, limit?, includeMirrored?, sameSideToMove?, repertoireIds?, move?, onlyWithMove? }` → `{ matches[], preset, minScore, limit, compared, move, onlyWithMove, truncated }`. Deckel wie `position-tree` (Policy `repertoire-scan`, Zeitbudget 8 s `Budget`, dann `truncated: true` mit den Treffern des schon verglichenen Teils). Der Brett-Walk (`RepertoireLineSource.WalkPositions`) hört nach `MaxPositionsPerLine` (400) je Linie auf, statt die Linie ohne Meldung zu Ende zu spielen |
| GET | `/api/explorer/position?fen=&source=&database=&ratings=&speeds=` | **Eröffnungs-Explorer des Analysebretts** (0.504.0, `ExplorerController`, nur angemeldet): Zugstatistik EINER Stellung `{ status, retryAfterSeconds?, source, database, total, white, draws, black, opening?, eco?, moves[{ uci, san, games, white, draws, black, averageRating?, opening?, eco? }] }`; `status` ∈ `ok`/`tokenMissing`/`tokenInvalid`/`rateLimited`/`failed` (immer 200), keine FEN / unbekannte Auswahl → 400. `ratings`/`speeds` als Komma-Liste. Dieselbe Datenstrecke wie der Lochfinder (`RepertoireExplorerService.PositionAsync`) |
| GET | `/api/explorer/games?fen=&source=&database=&ratings=&speeds=` | Eine Handvoll Partien (≤ 5), die die Stellung erreicht haben (0.505.0): Meister = die bestbewerteten, Lichess = bestbewertete + jüngste, ohne Doppelte `{ status, retryAfterSeconds?, games[{ id, white, whiteRating?, black, blackRating?, winner?, date?, speed?, url? }] }`. Der Client fragt die Stellung NACH einem Zug = die Partien mit diesem Zug. Nur Arbeitsspeicher (1 h), kein DB-Speicher. `url` fehlt bei LOKALEN Meisterpartien (Lumbra — die Kennung gibt es auf lichess.org nicht) |
| GET | `/api/explorer/sources` | Wie `/api/repertoires/explorer/sources` (Quellen für den Explorer) |
| GET | `/api/repertoires/explorer/sources` | Welche Explorer-Quellen es gibt `{ online, local, localRatings[], localSpeeds[] }` — `local` nur mit `LichessExplorer:LocalUrl` (0.503.0) |
| POST | `/api/repertoires/{id:int}/explorer-analysis` | **Lochfinder + Linien-Häufigkeiten** (0.502.0) `{ color?, chapterColors, database, ratings[], speeds[], thresholdPercent, includeHoles, includeLineFrequencies }` (+ `source`: `online`/`local`, 0.503.0) → `{ complete, positionsAnalyzed, positionsPending, rateLimited, retryAfterSeconds?, tokenMissing, tokenInvalid, fetchFailed, holes[], lineFrequencies? }`. Antwortet nach ~20 s Abfragezeit mit dem bisherigen Stand (`complete: false`) — der Client fragt erneut, das Abgefragte liegt im Speicher. Lesend: Besitzer ODER Freigabe-Empfänger (sonst 404); ungültige Auswahl → 400. Siehe „Lochfinder" unten |

### Lochfinder + „Häufigste zuerst" (Lichess-Explorer, 0.502.0)

Zwei Fragen, eine Rechnung (`Services/RepertoireReach.cs`, Modell aus Opening Fenix, GPLv3):
**Wie oft landet man in welcher Repertoire-Stellung?** Die Wurzel hat Wahrscheinlichkeit 1; ist der
NUTZER am Zug, teilt sie sich gleichmäßig auf seine Repertoire-Züge auf, ist der GEGNER am Zug, nach
den Explorer-Häufigkeiten. Knoten sind Stellungen, Zugumstellungen summieren sich. Daraus fallen
(1) die **Löcher** — ein Gegnerzug mit Anteil ≥ Schwelle, dessen Zielstellung NIRGENDS im Repertoire
steht (auch nicht über eine andere Zugfolge), sortiert nach `P(Stellung) × Anteil` — und (2) die
**Linien-Häufigkeit** für den Trainer-Modus „Häufigste zuerst" (P der tiefsten bekannten Stellung
der Hauptvariante, Schlüssel = Endstellung).

Regeln, die dabei nicht kippen dürfen:
* **Nur Gegner-Stellungen MIT Repertoire-Antwort werden abgefragt.** Das Ende einer Linie ist kein
  Loch — sonst wäre jede Chessable-Linie (endet mit dem eigenen Zug) ein Nest aus Löchern.
* **Stellungs-Schlüssel = die ersten DREI FEN-Felder** (ohne en passant), dieselbe Regel wie
  `RepertoirePositionLookupService.NormalizeKey` ↔ `normalizeFen` in `position-filter.util.ts`: die
  Linien-Häufigkeiten ordnet der CLIENT über diesen Schlüssel zu, und chess.js setzt das ep-Feld
  anders als Gera.Chess.
* **Die Farbe je Kapitel rechnet der Client** (`chapterColorsOf` in `repertoire-color.util.ts`,
  dieselbe Funktion wie im Trainer) und schickt sie mit. Eine zweite Heuristik am Server würde
  auseinanderlaufen — und die eigenen Festlegungen liegen ohnehin nur im Browser.
* **Abbruchgrenzen**: `MinReach` (0,02 %) — seltenere Stellungen werden nicht mehr abgefragt;
  `MinGames` (10) — darunter keine Löcher und keine Weitergabe. Repertoire-Gegnerzüge, die der
  Explorer gar nicht kennt, bekommen „eine halbe Partie", damit ihre Linien noch geordnet werden.
* **Zeitbudget statt Hintergrundauftrag** (`RepertoireExplorerService.Budget`, 20 s): der Endpunkt
  antwortet mit dem bisherigen Stand, der Client (`RepertoireExplorerService.run`) fragt Runde um
  Runde nach und hört auf, wenn alles da ist, ein Token fehlt/abgelehnt wird, der Explorer wiederholt
  nicht antwortet oder eine Runde OHNE Drossel keinen Fortschritt bringt.
* **EINE Leitung zum Explorer** (`LichessExplorerGate`, Singleton): Anfragen nacheinander UND aus
  einem eigenen Kontingent — 15 sofort, danach eine je 4 s (`LichessExplorer__Burst`,
  `LichessExplorer__RefillMs`). Gegen den echten Explorer gemessen (mit Token): dicht hintereinander
  429 nach 21 Anfragen, mit 0,5 s Abstand nach 27, mit 1 s Abstand nach 33 — das passt zu einem Eimer
  von gut 20, der mit ~0,3/s nachläuft. Ein fester Abstand hilft deshalb nicht; auf Dauer sind es
  rund 15 Stellungen je Minute. Nach einem 429 eine Minute Ruhe für ALLE Läufe (die Antwort nennt
  `retryAfterSeconds`), der Eimer ist danach LEER. Die erste Abfrage einer Runde geht immer raus —
  sonst könnte eine Runde ohne Fortschritt enden, und der Client hielte den Lauf für festgefahren.
* **Token**: seit 2025 verlangt der Explorer eine Anmeldung (ohne: 401). Zuerst gilt
  `LichessExplorer:Token` (Compose `LICHESS_EXPLORER_TOKEN`, beliebiger Token ohne Scope), sonst der
  Engine-Token, den der Nutzer im Profil hinterlegt hat.
* **Speicher** `LichessExplorerCacheEntries`: je Auswahl (Datenbank + Elo + Tempo) und Stellung,
  90 Tage, für ALLE Nutzer — die ersten Züge sind in jedem Repertoire dieselben.

**Explorer auf dem Analysebrett** (0.504.0, `features/analysis/opening-explorer.component.ts`): Karte
unter der Zugliste, nur angemeldet. Fragt `GET /api/explorer/position` erst 250 ms nach dem letzten
Stellungswechsel (Durchklicken einer Partie mit den Pfeiltasten kostet online sonst Kontingent),
merkt sich Antworten je Stellung + Auswahl im Speicher der Seite, verwirft eine späte Antwort für
eine schon verlassene Stellung und fragt zugeklappt gar nicht (`rookhub_analysis_explorer_open`).
Die Auswahl ist DIESELBE wie im Lochfinder (`rookhub_explorer_settings`). **Vorgabe seit 0.504.1:
lokal + Meister** (`DEFAULT_EXPLORER_SETTINGS`); ohne lokalen Explorer gilt online, und dieser
Rückfall wird NICHT gespeichert (sonst bliebe ein Gerät nach einem Besuch auf einem Server ohne
lokalen Explorer für immer auf online). Dafür tragen die
Explorer-Daten seit 0.504.0 auch Weiß/Remis/Schwarz und das Durchschnitts-Elo je Zug
(`ExplorerMoveStat`, JSON `w`/`d`/`b`/`r`); ein Speicher-Eintrag von vorher (`HasResults == false`)
genügt dem Lochfinder weiter, der Explorer holt ihn neu und überschreibt ihn.

**Beliebtheit im Repertoire-Baum** (0.508.0): hinter jedem Zug steht, wie oft er in der Stellung
davor gespielt wird — für eigene UND gegnerische Züge dieselbe Frage, aus `GET /api/explorer/position`
der aktuellen Baumstellung (`RepertoireDetailComponent.loadTreePopularity`, Auswahl wie im
Lochfinder, je Stellung + Auswahl im Speicher der Seite). Führt der Explorer einen Repertoire-Zug
nicht, steht „—" (kaum gespielt). **Bewusst NICHT die Häufigkeit „wie oft erreicht man die Stellung"**
(0.506.0/0.507.0, wieder entfernt): bei eigenen Zügen stand dort immer der Wert davor — bei einem
Weiß-Repertoire mit nur 1.e4 also 100 % —, und das las sich wie eine Aussage über den Zug.

Frontend: vierter Modus der Repertoire-Detailseite (`repertoire-holes.component.ts`, Lupe;
`?mode=holes`), Brett zeigt die Stellung NACH dem fehlenden Zug. Trainer: Trend-Knopf in der Leiste
(`freqOrder`, localStorage `rookhub_rep_train_freq_order`), die Explorer-Auswahl teilt er sich mit dem
Lochfinder (`rookhub_explorer_settings`). Offline ist der Knopf ausgeblendet.

**Zweite Quelle: der LOKALE Explorer** (0.503.0, `LocalExplorerClient`): `lila-openingexplorer` als
Container im Stack (`rookhub-explorer:9002`, von der Parallel-Sitzung aufgebaut), dieselben
Endpunkte und Antworten wie explorer.lichess.ovh (`BuildUrl`/`Parse` sind geteilt). Eingeschaltet
über `LichessExplorer:LocalUrl` (Compose `LICHESS_EXPLORER_LOCAL_URL`); leer = die Quelle gibt es
nicht, und eine Anfrage mit `source: "local"` ist ein 400. Lokal gilt: kein Token, keine Leitung,
KEIN Datenbank-Speicher (die Daten wachsen dort monatlich), nur eine Stunde im Arbeitsspeicher; die
Stellungen einer Tiefenschicht gehen gleichzeitig raus (`RepertoireReach.EvaluateAsync` mit
`prefetchLayer`, `LocalParallelism` = 8). **Ein Ausreißer ist kein Ausfall** (0.503.1): während eines
Imports kompaktiert der Explorer auf der HDD (gemessen Median 88 ms, p99 5,9 s, Spitze 11 s — nachts
22–06 Uhr, dann importiert die Stack-Sitzung). Deshalb: 30 s Timeout je Abfrage, eine gescheiterte
oder zu langsame Stellung bleibt OFFEN (je Aufruf nur EIN Versuch, die nächste Runde fragt erneut),
`fetchFailed` erst, wenn in einem Aufruf GAR KEINE Antwort kam, und eine Schicht wird am Budget
abgeschnitten (mindestens `LocalLayerFloor` = 5 s), damit die Runde vor dem 60-s-Schnitt des
Reverse-Proxys antwortet. Gemessen am Test-Repertoire: 26 Stellungen Meister kalt in
8 s, Lichess 2 s — online waren es 44 s. **Datenstand lokal**: Lichess-Partien erst ab Elo-Schnitt
1600 und ohne (Ultra-)Bullet (darunter kommen korrekt 0 Partien — die Oberfläche blendet diese
Stufen aus, `fitToLocal`), Meister = Lumbra-GigaBase (Brettpartien ab 2200). Die Wahl der Quelle
teilen sich Lochfinder und Trainer (`rookhub_explorer_settings.source`); ist sie „lokal", der Server
hat aber keinen, fällt sie still auf online zurück (`effectiveSettings`).

### Extension API (auth, CORS für chess.com)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/extension/repertoires?kind=opening` | Leichtgewichtige Liste (id, name, fileCount, kind, totalSizeBytes); `kind` filtert auf `none|opening|middlegame|endgame`. Nur Repertoires mit `UseForExtension=true` (Default true, im Bearbeiten-Dialog abwählbar); gilt ebenso für das Positions-Set der Abweichungsanalyse (`RepertoireAnalyzeService`) |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |
| POST | `/api/extension/analyze-game` | Abweichungsanalyse `{ moves[] (≤ 600), kind?, refresh? }` gegen das gecachte Positions-Set des Kontos (`RepertoireAnalyzeService`) → `{ deviation, gaps, inRepertoire, fenBeforeDeviation, repertoireFileCount, illegalMoveAt, repertoireTruncated }`. Deckel (Codereview N8-005): 30/min je Konto (Policy `extension-analyze`, sonst 429); `refresh` baut höchstens einmal je Konto und Minute neu, sonst gilt der Cache; je Konto ein Neuaufbau zur Zeit; ein Set liest höchstens 16 MB PGN und hält höchstens 500 000 Stellungen — darüber Teil-Set mit `repertoireTruncated: true`; eigener Cache mit Größengrenze (2 Mio. Stellungen) |
| POST | `/api/extension/training-activity` | Meldet ein Häppchen AKTIVER Chessable-Trainingszeit `{ secondsActive (1–3600), movesTrained?, linesTrained?, courseId?, courseName?, courseKind? }` — Modus-Labels („Practice Moves“) als Kursname werden verworfen und via Kurs-ID aus der gecachten Kursliste geheilt (von RepCheck auf chessable.com gemessen). Append-only → `ChessableActivities`; fließt in die Kategorie „Chessable" des Trainingsziele-Trackers. Zeitstempel serverseitig |
| POST | `/api/extension/remember-line` | Merkt eine auf chessable.com angezeigte Stellung `{ fen, courseId?, courseName?, sourceUrl? }` → `RememberedPositions` (append-only, Verwendungszweck offen). **Kursname**: die Extension liefert ihn (über den erfassten Chessable-Bearer aus der Chessable-API) mit; fehlt er, löst der Server ihn aus dem gespeicherten Bearer des Users auf — cache-first aus `ChessableCredential.CachedCoursesJson`, sonst best-effort Live-Abruf (`ChessableProxyService.GetCoursesAsync`). `GET /remembered-lines` trägt bei Alt-Einträgen ohne Namen den Cache-Namen nach |
| POST | `/api/extension/chessable/line-trained` | „Linie auf Chessable trainiert" `{ bid, oid }` — markiert die Linie in den RookHub-Gegenstücken: Kurs-Linie gilt als gelöst (idempotenter CoursePuzzleResult, bewusst OHNE CourseAttempt), im Repertoire-Trainer wird eine neue Linie „gelernt" (Stufe 1) bzw. eine FÄLLIGE eine SR-Stufe vorgerückt (Chessable ersetzt das Review; nicht fällige/pausierte bleiben unangetastet). CardKey = serverseitiger SPIEGEL des Frontend-Linien-Hashs (`ChessableTrainedLineService.LineKeyFromSans` ↔ `repertoire-line-key.util.ts`, cyrb53 — MUSS synchron bleiben, Vektoren-Test); Linie via PGN-Header `[ChessableOid]`. Unbekannte bid/oid = kein Fehler |
| POST | `/api/extension/chessable/problem-moves` | „Schwierige Züge" ablegen (Batch-Upsert je User+bid+oid): `{ bid, entries: [{ oid, nHard?, problemMoves?, lastReviewed? }] }` — nHard aus getList, Zug-Details (`game.problemMoves.thisUser`, opakes JSON ≤16 KB, `{}` löscht alte Fehlzüge) + lastReviewed ("never"→null) aus getGame; fehlende Felder lassen den gespeicherten Wert stehen. Quelle: RepCheck-Capture beim Training/Kurs-Holen |
| POST | `/api/extension/chessable/session-moves` | „Sitzungszüge" ablegen (APPEND-ONLY, kein Upsert): `{ bid, entries: [{ oid, moves }] }` — je trainierter Linie der rohe `moves`-Block aus Chessables eigenem Session-Report (`saveProgressAndReturnNewProgressInfo`-REQUEST, von RepCheck v1.54.0 mitgeschnitten; die Antwort enthält Konto-Daten und bleibt tabu). Enthält je Halbzug u. a. `wrong[]` (falsch gespielte Züge), Overstudy-/Alternative-Flags, Level, Punkte. Opak (nur Array-Form + ≤64 KB geprüft), jeder Durchlauf = eigene Zeile in `ChessableSessionMoves` (Auswertung offen); per-User-Deckel 200k Zeilen (älteste raus). NUR authentifiziert — kein Anon-Pfad |
| POST | `/api/extension/chessable/review-lines` | „getReview-Linien" ablegen (Batch je User+bid+oid): `{ bid, entries: [{ oid, json }] }` — das ROHE getReview-JSON EINER trainierten Linie (opak, ≤256 KB/Linie), erst beim Kurs-Aufbau geparst (`ChessableReviewParser`). Zweite Linien-Quelle NEBEN getGame: `UpsertBatchAsync` legt/aktualisiert die Roh-Zeile ab, dann best-effort `MergeIntoCourseAsync` — die Lücken (oid noch kein BookPuzzle) werden ins Kurs-Buch `chessable-u{userId}-{bid}.pgn` als `BookPuzzle.Source="review"` eingespielt. **getGame gewinnt** (oid-basiert): ein echter getGame-Import ersetzt einen Review-Füller IN-PLACE (dieselbe Zeile, `Source→null`, Fortschritt/FKs bleiben) statt ein Duplikat anzulegen. Antwort `{ stored, merged }`. Quelle: RepCheck-Capture beim Training. Wird von RepCheck erfasst beim Durchtrainieren (getReview) |
| POST | `/api/extension/chessable/review-lines/anon` | **AllowAnonymous** (per-IP-RL) — token-lose Ablage von getReview-Linien für Nutzer OHNE RookHub-Token: `{ uid, bid, entries: [{ oid, json }] }`. Statt eines Accounts identifiziert die **Chessable-uid** (client-seitig aus dem Chessable-JWT decodiert) die Linien; sie landen in `AnonymousChessableReviewLines` (Upsert je uid+bid+oid), **kein** Merge (kein Zielkonto). Werden GECLAIMT beim **erfolgreichen Chessable-Bearer-Test** (`POST /api/chessable/test`): dort ist die uid von Chessable BEWIESEN zurückgegeben (`TestAsync`) — NICHT aus dem ungeprüften JWT decodiert (sonst könnte man per gefälschtem JWT fremde Anon-Daten claimen). Der Test setzt `ChessableCredential.ChessableUid` und ruft `ClaimAnonForUidAsync` (übernimmt in `ChessableReviewLines` + baut die Kurse). Missbrauchs-Schranken: per-IP-RL, 16 MB/Request, **`MaxAnonRowsPerUid`=5000** (kein neuer oid je uid darüber), Retention 90 Tage, für uids ohne verknüpftes Konto 14 Tage (täglich, `AnonymousDataRetentionService` — läuft IMMER, auch mit `Chessable:Enabled=false`; bis 2026-09-30 hing sie am Kurslisten-Refresh und lief auf PROD deshalb nie). Akzeptiertes Rest-Risiko: wer eine fremde numerische uid KENNT, kann Linien unter ihr vorbelegen (getGame gewinnt, Inhalt löschbar, gedeckelt). RepCheck sendet hierher NUR nach expliziter Einmal-Zustimmung; Ziel = konfigurierte URL, sonst Default `rookhub.oberschmid.homes`. Antwort `{ stored }` |
| POST | `/api/extension/chessable/cached-lines` | Welche Linien liegen schon im **geteilten piratechess-Rohdaten-Cache**: `{ oids: ["…"], bid }` → `{ oids }` (gecachte Teilmenge, nur Existenz, nie Inhalt; ≤10000, oid = positive Ganzzahl). **Nur für einen Kurs aus der bestätigten Kursliste des Nutzers** (`ChessableCredential.CachedCoursesJson`, `ChessableImportService.HasVerifiedCourseAsync`, ohne Live-Abruf): ohne `bid` oder für einen anderen Kurs → leere Liste; sonst fragt RookHub piratechess mit dem `bid` (nur Linien, mit denen piratechess diesen Kurs füllt). Dieselbe Schranke im Ingest: eine Linie `null` (aus dem Cache) für einen nicht bestätigten Kurs → **403**, der Chunk-Import wird dann geschlossen — vorher holte sich jedes Konto über oids die Linien fremder, bezahlter Kurse (Codereview 2026-09-29). Auf PROD (`Chessable:Enabled=false`, kein „Testen") gilt das praktisch für fast alle: die Extension holt dann alle Linien selbst. RepCheck ≥1.57.0 überspringt beim „Kurs holen" für Treffer den Chessable-Abruf und schickt im Ingest (`ingest`, `ingest/chunk`, `ingest/live`) je Kapitel `lineOids` parallel zu `lines` — eine Linie `null` = Inhalt aus dem Cache (RepCheck schickt `bid` bei cached-lines noch nicht mit → bis dahin keine Treffer). piratechess ordnet die Linien dann **über die oid** statt positionsbasiert zu (vorher landete ein Teil der Linien eines Kapitels unter der oid/dem Namen der ersten Einträge) und legt Browser-Linien seinerseits im Cache ab (nie überschreibend). Der FINALE Chunk trägt bei vollständig geholtem Buch `courseJson` + `complete: true` → piratechess cacht den Kurs als Ganzes (nur ohne Lücken, gleiche Kapitelzahl, noch kein Eintrag). piratechess nicht erreichbar → leere Liste (Extension holt alles selbst) |
| POST | `/api/extension/chessable/unexpected-response` | RepCheck ≥1.60.0 hat beim „Kurs holen" eine **unerwartete Chessable-Antwort** bekommen und den Abruf gestoppt: `{ bid, courseName?, endpoint (getCourse/getList/getGame), lid?, oid?, status?, reason (http/json/error/shape), message?, snippet? (≤2000; RepCheck entfernt E-Mail- und IP-Adressen), extensionVersion? }` → `{ banned, adminNotified }`. Jede Meldung ist eine Warnung im Log (`ChessableUnexpectedResponse`, Tags `chessable,extension,crawl`) und wird regelmäßig durchgesehen (TODO.md „Periodisch") — nur am Wortlaut ist eine Anti-Crawling-Maßnahme von einer harmlosen Abweichung zu unterscheiden. **Sieht die Antwort nach einer Sperre aus** (`ChessableResponseAlertService.LooksBanned`: Fehlermeldung bzw. Nicht-JSON-Ausschnitt mit banned/suspended/blocked/deleted — dieselbe Wortliste wie `looksBanned` in RepCheck, nur gemeinsam ändern), geht eine Admin-Nachricht im Thread des Nutzers raus (`SendFromUserAsync`, Glocke bei allen Admins), höchstens eine je Nutzer in 24 h (erkannt am Präfix `[RepCheck] Chessable-Sperre gemeldet`). Anlass: `{"error":{"message":"User is banned or deleted"}}` lag seit dem 30.06.2026 für 31 Linien als „gecacht" im Linien-Cache |
| POST | `/api/chessable/admin/repertoire-cleanup?dryRun=true` | **Admin** — Altlasten-Bereinigung in Chessable-Repertoires (`RepertoirePgnCleanup`, v0.477.0): **nie löschen, nur ausblenden** über den Header `[RookHubHidden "…"]` IN der Partie (alle Leser gehen über `RepertoirePgnCleanup.WithoutHidden`: kombinierte PGN-Auslieferung, Datei-Download, `RepertoireLineSource`, Positions-Set der Extension; Header entfernen = wieder sichtbar). Regel 1: oid an Partien mit verschiedenen Linien (Stellung+Hauptvariante) → Wahrheit aus dem piratechess-Linien-Cache (`ChessableProxyService.GetCachedLinePgnsAsync`), sonst Ausschlussregel (Inhalt steht schon unter anderer eindeutiger oid), sonst früheste Partie; den übrigen oid weg (`[RookHubRemovedOid]`), ausgeblendet nur wenn ihr Inhalt schon sichtbar existiert. Regel 2: gleiche Züge mit genau EINER bekannten oid → früheste Partie trägt sie, Rest ausgeblendet. Gleiche Züge unter verschiedenen oids (gewollte Chessable-Wiederholungen) und oid-lose Dubletten bleiben. `dryRun=true` (Standard) = Bericht über alle Dateien; `false` = anwenden. Läuft zusätzlich einmal nach dem Start (`RepertoireCleanupBackfillService`, 90 s verzögert, Merkfeld `RepertoireFile.CleanupVersion`) und inline nach jedem Live-Append. Browser-Importe (`ingest`, `ingest/chunk`) in ein VORHANDENES Repertoire laufen seit v0.477.1 über denselben Anhänge-Weg — vorher ersetzten sie die Datei, und ein Mitschnitt mit einzelnen Kapiteln löschte alle übrigen Partien; ersetzt wird nur noch beim serverseitigen Neuabruf. piratechess nicht erreichbar → Datei bleibt unverändert, nächster Start |
| GET | `/api/extension/remembered-lines?take=200` | Gemerkte Stellungen des Users (neueste zuerst) |
| DELETE | `/api/extension/token/self` | „Trennen" in RepCheck (Codereview S1-007): widerruft GENAU den API-Token, mit dem die Anfrage kommt (Claim `api_token_id` aus `ApiTokenAuthenticationHandler`), ohne Id vom Client — ein Extension-Token kann nur sich selbst widerrufen, `DELETE /api/profile/tokens/{id}` sperrt ihm der Scope-Zaun. 204 auch, wenn der Token inzwischen schon weg war; 400 mit JWT (kein Token zum Widerrufen). RepCheck ≥ 1.68.11 ruft das vor dem lokalen Löschen und wertet 404 als älteren Server („nur hier getrennt") |
| POST | `/api/extension/games` | Speichert die aktuell auf chess.com/lichess angeschaute Partie (Button „Partie speichern") `{ source, moves[], externalId?, white?, black?, result?, sourceUrl?, playedAt?, whiteElo?, blackElo?, timeControl? }` → `SavedGames`. Server baut das PGN aus der SAN-Zugliste + Headern und vergibt ein `ShareToken`. Dedup über (UserId, Source, ExternalId) — aber nur, solange die neuen Züge die gespeicherten fortsetzen (dann heilt ein längerer bzw. erstmals bewerteter Save in place, gleicher Link) oder deren Anfang sind (dann bleibt die gespeicherte); eine ANDERE Zugfolge wird eine eigene Partie ohne ExternalId (Warnung im Log) und bekommt ihren eigenen Link — RepCheck meldete auf dem lichess-Analysebrett jede Partie als `analysis` (N8-001). Bringt der Heal neue Züge, fallen wie beim Korrigieren Analyse-Verknüpfung, Fehler-Training und Nacherzählung weg (N8-002; nur Elo nachgetragen: bleiben). Sichtbar im Bereich „Partien" (`/api/games`). **`analyze: true`** (0.524.0) reiht die Partie gleich zur Analyse ein — scheitert das (keine Engine, Deckel), bleibt sie trotzdem gespeichert | **Seit 0.669.0 wird jede so gespeicherte Partie gleich analysiert** (`SavedGameService.AnalyzeAsync`, das Flag `analyze` ist nur noch Verträglichkeit; Fehler = Partie bleibt trotzdem gespeichert).
| POST | `/api/extension/share-line` | „Sharebar" im Popup: teilt die gespielte Zugfolge `{ moves[], title? }` als öffentliche Nur-Ansehen-Linie `/l/{token}` (`SharedLineService.CreateStandaloneAsync`) → `{ shareToken }`; dieselbe Zugfolge desselben Kontos = derselbe Link. Die Züge werden ab der Grundstellung nachgespielt und in der Schreibweise des Bretts geschrieben (Codereview N8-003): Zug länger als 16 Zeichen oder nicht legal → 400, PGN über 32 KB → 400, Rumpf höchstens 64 KB |
| POST | `/api/extension/games/known` | Welche Partien einer Übersicht liegen schon bei RookHub? `{ source, externalIds[] }` (≤ 300) → je bekannte Partie `{ externalId, id, analysis }`. Die Erweiterung zeigt daraufhin ein Häkchen statt des Sende-Knopfs — und den Analyse-Fortschritt, wenn einer läuft. Nur Existenz und Stand, nie Züge |
| POST | `/api/extension/games/{id}/analyze` | „In RookHub analysieren“ aus der Übersicht auf chess.com/lichess (0.528.0) — für eine Partie, die schon bei RookHub liegt, aber nie gerechnet wurde. Derselbe Weg und dieselbe Antwort wie `POST /api/games/{id}/analyze` (`GameAnalyzeResultDto`, Absage 400 `{ reason, message }`, fremde Partie 404); eigene Route, weil das API-Token der Erweiterung nur `/api/extension/*` erreicht (`PatScopeFenceMiddleware`) |

`UseForExtension` heißt in der Oberfläche seit 0.700.1 „Für Extension und Vorbereitung verwenden": solche Repertoires
nutzt RepCheck UND die Spielervorbereitung (Trainingslinien gegen einen Gegner, siehe „Spielervorbereitung (Prep)").
Feld- und API-Name bleiben.

**Der Browser-Import laeuft LAUFEND, nicht gepuffert (0.484.0).** Die Extension streamt einen Kurs
kapitelweise an `POST /api/extension/chessable/ingest/chunk`; jeder Chunk wird SOFORT geparst und
angehaengt (derselbe Weg wie der Live-Append: dedupliziert, je (User, bid) serialisiert). Der
`ChessableIngestSessionStore` haelt nur noch Zaehler, Ziel und den Kapitel-Versatz.

Vorher sammelte er die ROHEN Kapitel im Arbeitsspeicher und importierte erst beim letzten Chunk —
mit einem Deckel von 128 MB je Sitzung, bei dessen Erreichen der GANZE Puffer verworfen wurde.
Gemeldet am 2026-09-19 an „Lifetime Repertoires: King's Indian Defense - Part 2": 1881 Linien, im
Cache gemessene 455 KB Rohdaten je Linie, zusammen 835 MB. Nach rund 288 Linien kam
`400 Import session exceeds size limit`, nach 30–60 Minuten Crawlen und ohne eine einzige
importierte Linie — viermal hintereinander. Jetzt gibt es keinen Deckel mehr, und was geholt ist,
bleibt auch nach einem Abbruch.

Drei Dinge, die dabei nicht kippen duerfen:
* **Kapitelnummern fortschreiben** (`ChessableRoundOffset`): piratechess zaehlt die Kapitel je
  Parse-Aufruf von vorn, und die LineId eines Kurs-Puzzles ist `Datei:Round` — ohne Versatz
  ueberschriebe Chunk 2 die Linien von Chunk 1.
* **Inflight-Marke an der SITZUNG, nicht am Request**: zwischen zwei Chunks liegen Minuten (live
  gemessen bis 13), der Watchdog haelt einen Import aber nach `OrphanGrace` (10 min) ohne lokalen
  Treiber fuer verwaist und reiht ihn neu ein — die Fast-Lane starte dann einen echten
  Chessable-Abruf, waehrend der Browser noch streamt.
* **EIN Import-Datensatz je Sitzung**: angelegt beim ersten Kapitel, abgeschlossen beim finalen
  Chunk (Status, Kurs-Zuordnung, Benachrichtigung) — sonst gaebe es eine Benachrichtigung je Kapitel.

Ein abgelehnter Chunk wird ausserdem mit Grund geloggt: der 400er stand vorher nur als nackter
Statuscode im Zugriffslog, die Begruendung ausschliesslich im Quelltext.

**Eine Sitzung, die ihr Ende nie erreicht, wird geschlossen (0.484.1).** Drei Wege: (1) die Extension
schickt beim Stopp einen finalen Chunk mit `aborted: true` → der Import-Eintrag geht auf `Failed` mit
Zaehlern („Im Browser abgebrochen — n Kapitel, m Linien uebernommen"), die importierten Kapitel bleiben;
(2) ein Kapitel, das der Parser ablehnt (Form-Fehler), schliesst den Eintrag ebenso; (3) der Watchdog
raeumt Sitzungen ohne Kapitel seit `ChessableIngestSessionStore.Ttl` (30 min) ab
(`CloseExpiredBrowserSessionsAsync`) — Tab geschlossen, Anmeldung abgelaufen. Vorher stand der Eintrag
fuer immer auf `Running` und hielt die Inflight-Marke.

**Kapitelnummern relativ zur KLEINSTEN Nummer des Chunks fortschreiben** (`ChessableRoundOffset.NextOffset`):
piratechess beginnt jeden Einzel-Chunk bei 002, der Versatz um das Maximum ergab 002, 004, 006 … (Live-Import
2026-09-19, bid 55720); jetzt 002, 003, 004.

**Der Watchdog laeuft AUCH mit `Chessable:Enabled=false` (0.484.3).** Er stand bis dahin hinter dem
Schalter — auf PROD (Flag seit 2026-09-09 aus) lief also weder das Schliessen abgelaufener
Browser-Sitzungen noch das Aufraeumen verwaister Browser-Importe, und ein abgebrochener Import blieb
fuer immer auf `Running`. Das ist nicht nur Kosmetik: die Dedup-Regel in `EnqueueReimportAsync`
verweigert jeden weiteren Import desselben (User, bid), solange einer laeuft. `LanesEnabled` (aus dem
Schalter) trennt die Pflichten: die eigenen Lanes werden nur mit dem Schalter angetrieben (Drain,
Tageslimit-Freigabe), die Browser-Pflichten immer — und ein verwaister Import wird ohne Lanes BEENDET statt
zurueckgestellt (zurueckgestellt nimmt ihn dort niemand verlaesslich auf: kein Drain, kein Resume-Dienst, ein
Ticket reiht nur ein NEUER Admin-Download ein). Ausnahme seit dem Codereview 2026-09-29: ein voll
gecachter SERVER-Import (Admin „Kurse von Usern holen") geht zurueck in die Warteschlange — die netzfreie
Fast-Lane laeuft immer und nimmt ihn auf.

**Browser-Importe geraten nie in die Server-Lanes (`ChessableImport.FromBrowser`, Codereview 2026-09-29).**
`StartBrowserImportAsync`/`ImportPgnDirectAsync` setzen die Spalte (Migration `ChessableImportFromBrowser`, traegt
laufende/pausierte Browser-Saetze nach: Phase `importing` mit `Attempts = 0`). Sie sahen fuer die Lanes aus wie ein
voll gecachter Server-Import (`FullyCached=true`, kein `FetchedPgn`); drei Wege stellten sie auf `queued` —
Neustart (`ChessableImportResumeService`), verwaiste Sitzung (Watchdog mit Lanes an) und Pausieren/Fortsetzen —,
und die Download-Lane holte den Kurs dann mit dem Bearer des Nutzers bzw. piratechess ihn ganz aus dem Kurs-Cache,
an der Eigentumspruefung vorbei. Jetzt: `DrainNextAsync` und `RunAsync` nehmen sie nie, der Resume-Dienst laesst sie
liegen, der Watchdog SCHLIESST einen verwaisten Browser-Import auch mit Lanes, Pausieren/Fortsetzen bleiben ohne
Wirkung, und `GetOrCreate` uebergibt beim Aufraeumen abgelaufene Sitzungen an den Watchdog, statt sie zu verwerfen.

**Das SourcePgn eines Browser-Buchs wird ZUSAMMENGEFUEHRT, nicht ersetzt (Codereview 2026-09-29, A3-005).** Jeder
Chunk ersetzte `Book.Source.SourcePgn` durch sein eigenes Kapitel — nach einem 40-Kapitel-Import stand nur das letzte
darin, „Aktualisieren" erneuerte nur dessen Linien und setzte das Buch trotzdem auf die aktuelle Version (Prod: vier
Buecher betroffen). Jetzt reichen alle Browser-Wege (`ingest/chunk`, `ingest/live`, `ingest` = `FromBrowser`)
`mergeSourcePgn` an `PgnImportService.ImportFileAsync`: `CachedSourceRebuild.MergeByOid` ersetzt eine Partie mit
bekannter oid an ihrer Stelle und haengt den Rest an; der Server-Abruf (ganzer Kurs) ersetzt weiter. Ist das Buch
veraltet, laeuft die ZUSAMMENGEFUEHRTE Quelle durch den Import — die Version wird danach gehoben, also muessen alle
Linien der Quelle die aktuelle Aufbereitung haben. Bestehende Buecher mit abgeschnittener Quelle heilt erst ein
vollstaendiges „Kurs holen" (Daten-Nacharbeit).

**Nach einem API-Neustart mitten im Import geht der Kapitel-Versatz nicht verloren (Codereview 2026-09-29, A3-006).**
Der Versatz lebt nur im `ChessableIngestSessionStore` (Arbeitsspeicher). Der erste Chunk einer Sitzung, die der Server
nicht kennt, fragt deshalb `ChessableImportService.ResumeChapterOffsetAsync`: trifft er mit Versatz 0 auf belegte
LineIds UND keine seiner oids steht im Buch, setzt er hinter der hoechsten Kapitelnummer des Buchs fort — vorher fiel
jede seiner Linien auf „uebersprungen" (unbekannte oid, kein Teil-Import), und alle folgenden Kapitel fehlten still.
Bekannte oids (erneutes Holen) behalten ihre Nummern, ein Teil-Import (`partial`) ist ausgenommen (dort bekommt eine
kollidierende neue Linie ohnehin einen freien Platz). Der alte Import-Datensatz schliesst weiter der Watchdog.
„Belegt" zaehlt nur eine LineId, die eine oid TRAEGT: ein Alt-Buch ohne oids (vor piratechess v1.29.0) schickt beim
erneuten Holen `partial=false`, und sein erster Chunk trifft nur auf oid-lose Linien — dort muss der Versatz 0 bleiben,
sonst greift der oid-Nachtrag (gleiche LineId + Zuege + StartPly) nicht und der ganze Kurs stuende doppelt im Buch.

**Ein Showstopper steht AM EINTRAG, nicht als Zahl im Banner (0.484.4).** `StaleContentRule` ist die
EINE Regel (`Refetch` / `Cache` / `Local` / `Manual`, seit 0.509.0 vier Faelle) fuer den Reprocess-Status, den
Reprocess-Lauf UND die Listen. `Manual` heisst: weder aus der gespeicherten Quelle aufbereitbar noch holbar — solche Eintraege
tragen `NeedsReimport` im `CourseListItemDto`/`RepertoireDto` und in der Liste ein (!) mit dem Hinweis
auf die RepCheck-Erweiterung. Das Banner zeigt nur noch, was der Knopf wirklich kann (`reprocessableLocally
+ refetchable`); der frühere wegklickbare Hinweis „N brauchen einen Re-Import" ist weg — er nannte nie,
WELCHER Eintrag gemeint war. Die Listen-Abfrage ist dabei auf die VERALTETEN Eintraege eingeschraenkt,
damit das `LIKE` nicht ueber das `SourcePgn` jedes sichtbaren Buchs bzw. jede PGN-Datei laeuft.
Ein Chessable-REPERTOIRE, das nicht holbar ist, wird ausserdem nicht mehr stillschweigend auf die
aktuelle Version gesetzt — das verdeckte die fehlenden `[%alt]`-Varianten.

**Der Reprocess-Status sagt ohne eigenen Chessable-Weg die Wahrheit (0.484.3).** `ActionFor` ist die
EINE Regel fuer Anzeige und Lauf (Refetch / Local / Manual). Ohne den eigenen Weg gibt es kein
Refetch: ein veraltetes Buch mit gespeicherter Quelle wird LOKAL aufbereitet (das bringt die
Zug-Kommentare, nur die `[ChessableOid]` fehlen weiter), eines ohne Quelle zaehlt als „braucht
Re-Import". Vorher zaehlte der Status es als aktualisierbar, der Lauf sprang es an und meldete
„uebersprungen" — das Banner „1 Kurs kann aktualisiert werden" stand dauerhaft (gemeldet 2026-09-20,
Prod-Log: `0 lokal, 0 eingereiht, 6 uebersprungen`). `EnqueueRefetchesAsync` legt ohne den Schalter
ausserdem gar keinen Auftrag mehr an — er wuerde nie abgearbeitet und blockierte den Kurs.

**Chessable-Kurse mit oids kommen aus dem Linien-Cache (0.509.0, `StaleAction.Cache`).** Ein veralteter
Chessable-Kurs, dessen Quelle `[ChessableOid]` traegt, wird beim „Aktualisieren" nicht mehr nur lokal
umgeparst: je Linie holt `ImportReprocessService.RebuildFromCacheAsync` den ZUGTEXT frisch aus dem geteilten
piratechess-Linien-Cache (`ChessableProxyService.GetCachedLinePgnsAsync` ueber `ICachedLineSource`, mit der
AKTUELLEN piratechess-Logik), `CachedSourceRebuild.Rebuild` setzt ihn in den gespeicherten Block, und
`PgnImportService.ImportFileAsync` bereitet in-place auf. Vorher kamen Fixes an der PGN-Erzeugung in
piratechess (v1.0.45 Leerraum, v1.0.46 `[ChessableColor]`, v1.0.47 mehrdeutige Zuege als Kommentar) nie in
bestehende Kurse — der Re-Fetch-Weg ist auf PROD seit 2026-09-09 aus. Regeln:
* **Nur der Zugtext, nie die Header.** `Round` ergibt die LineId (Fortschritt!); die Cache-Antwort stammt
  aus einem Fake-Kapitel („x", ab `001.001`). Fehlende Header werden ergaenzt (`[ChessableColor]`), ausser
  Event/Round/White/Black; vorhandene gewinnen. Geschnitten wird an `[Event ` am Zeilenanfang, alles andere
  bleibt Zeichen fuer Zeichen.
* **Modus zum Kurs:** `[%tqu` irgendwo im gespeicherten PGN → `FirstKeyMove`, sonst `None` (umgewandeltes
  Repertoire). Traegt je Linie nur eine Seite den Marker, bleibt sie (Zaehler `ModeMismatch`); ebenso bei
  anderer Startstellung oder einer oid an mehreren Partien (`Conflicts`).
* **Erst pruefen, dann schreiben:** `GetCachedLineOidsAsync` vorab — keine Linie gecacht (oder piratechess
  weg) → `Skipped`, Buch bleibt veraltet. PGN-Abfragen in Portionen zu `CacheRebuildBatchSize` = 100
  (piratechess laedt je oid ~455 KB Roh-JSON); wirft eine, wird NICHTS geschrieben → `Failed`. Einzelne
  fehlende Linien behalten ihren Text, das Buch gilt trotzdem als erneuert. Wird gar keine Linie
  uebernommen, bleibt es veraltet. Hat sich `Book.UpdatedAt` seit dem Laden geaendert (ein Browser-Import
  hat waehrend der Abfragen Linien angehaengt), wird ebenfalls nichts geschrieben — sonst ueberschriebe der
  umgeschriebene Text die angehaengten Linien.
* **Kein einziger Treffer im Cache → Markierung `Book.CacheMissAt`** (0.674.1): Kurse aus der Zeit VOR dem Linien-Cache
  (14.09.) tragen oids, aber keine ihrer Linien liegt dort — der Status bot sie bei jedem Aufruf als aktualisierbar an,
  jeder Lauf übersprang sie („keine der N Linien im Linien-Cache"), das Banner blieb für immer (gemeldet 2026-10-05,
  19 Kurse auf Prod). Findet der Lauf keine einzige Linie, setzt er `CacheMissAt`; solange es nicht älter als
  `UpdatedAt` ist, zählen Status und Kursliste das Buch als `Manual` (`ActionForBook(…, cacheMissed)`, (!) „braucht
  Re-Import"). Ein neuer Import setzt `UpdatedAt` und hebt die Markierung auf. Der LAUF fragt ohne den Schalter und
  versucht den Cache weiter — die einzige gewollte Abweichung zwischen Anzeige und Ausführung: der Lauf kann mehr,
  als das Banner verspricht, nie weniger. Ein piratechess-Ausfall liefert ebenfalls „nichts gecacht" und markiert
  damit zu Unrecht — der nächste Lauf heilt das.
* **Dasselbe für Repertoires: `Repertoire.CacheMissAt`** (0.693.3, gemeldet 2026-10-06: 5 Repertoires auf Prod, 4× „keine
  der N Linien im Linien-Cache", 1× „keine Kurs-Id"). `RebuildRepertoireFromCacheAsync` markiert bei jedem
  DAUERHAFTEN Nichts — keine Kurs-Id, keine oids, kein Cache-Treffer, keine Linie übernehmbar —, nicht aber, wenn das
  Repertoire währenddessen geändert wurde oder eine Portion warf. Status (`ActionFor(r, forDisplay: true)`) und
  `RepertoireService.MarkNeedsReimportAsync` reichen `cacheMissed` an `ActionForRepertoire`; der Lauf fragt ohne.
* **Nicht hinter `Chessable:Enabled`**, und `localOnly` („Aus Cache") schliesst den Weg ein — beides meint
  „ohne Chessable-Abruf". Status: `ReprocessableLocally` zaehlt `Cache` mit (Banner unveraendert),
  `FromCache` weist ihn gesondert aus; Ergebnis: `RebuiltFromCache`, `CacheLinesReplaced`.
* Der Text wird dafuer an EINER weiteren Stelle geladen (Include-Allowlist in `BookSourceIncludeGuardTests`),
  ungetrackt; `ImportFileAsync` laedt ihn ein zweites Mal getrackt und schreibt nur, wenn er sich unterscheidet.
* **Repertoires gehen denselben Weg (0.510.0)** — `StaleContentRule.ActionForRepertoire`: kein Chessable-Repertoire →
  Versions-Mark (`Local`, auch mit oids); Chessable (Kurs-Id ODER Dateiname `chessable-…`) und eine Datei traegt
  `[ChessableOid]` → `Cache`; ohne oids → `Refetch` bzw. ohne eigenen Weg `Manual`. Ein Chessable-Repertoire MIT oids
  bekommt NIE mehr nur den Versions-Mark (sonst fuer den Cache-Weg verbrannt, wie bei Kursen).
  `ImportReprocessService.RebuildRepertoireFromCacheAsync` nutzt DIESELBE Textfunktion (`CachedSourceRebuild`), nur ohne
  Import-Kern: Text je Datei schreiben (`FileSize` in UTF-8-Bytes), dann `ImportVersion` + `UpdatedAt` (der Trainer
  wertet live aus). Was dabei anders ist als bei Kursen, und warum:
  - **Modus je DATEI**, nicht je Repertoire: ein aus einem Kurs umgewandeltes Repertoire traegt `[%tqu`
    (`CoursePgnExporter` schreibt es mit), ein von Chessable geholtes nicht — beide koennen in EINEM Repertoire liegen.
    Je Modus eigene Abfragen, EINE Existenzpruefung je Repertoire.
  - **Ausgeblendete Partien (`[RookHubHidden]`) werden nie ersetzt** und gar nicht erst im Cache nachgefragt (Zaehler
    `Hidden` in `CachedSourceRebuild.Result`); als zweite Partie einer oid zaehlen sie auch nicht (sonst bliebe die
    sichtbare als „Konflikt" stehen). Partien mit `[RookHubRemovedOid]` tragen keine oid mehr und bleiben ohnehin —
    die entfernte oid kommt nicht ueber „fehlende Header ergaenzen" zurueck. In Kursen gibt es beide Header nicht.
  - **Gleichzeitiges Schreiben erkennt `Repertoire.UpdatedAt`** (vor dem Laden der Texte gemerkt, vor dem Schreiben neu
    gelesen): alle Schreiber des Repertoire-PGN setzen es (Upload, Datei loeschen, Live-Append, Bereinigung).
  - **Status und Lauf laden KEINEN PGN-Text** — Projektion mit Kurs-Id, den `chessable-`-Dateinamen (fuer die bid) und
    `SourceModern` als SQL-`LIKE`, statt wie vorher `.Include(r => r.Files)` fuer jedes Repertoire (Prod: ~250 MB
    Chessable-PGN; dieselbe Klasse Fehler wie 0.508.3 bei den Buechern). Der Versions-Mark laedt nur die
    Repertoire-Zeilen. Den Text laedt nur der Cache-Weg, Datei fuer Datei und nur Dateien mit oids; der Tracker wird
    nach jedem Repertoire geleert.
  - `RepertoireFile.ChessableOidsCache`/`ChessableOidsPgnLength` und `CleanupVersion` bleiben unberuehrt: die
    oid-Menge und die Ausblend-Header aendert der Rebuild nicht.

**Der Kursname kommt aus den LINIEN** (`ChessableLineJson.ResolveCourseName`, alle drei Ingest-Wege): Chessable
schreibt ihn in jede getGame-Antwort (`game.name`; `game.title` ist der Linientitel), die Extension liest
ihn bei Kursen ausserhalb des Kontos vom Seitentext, und die Kurskachel klebt Titel und Fortschrittsbadges
zusammen („Short & Sweet0%Priority0/15variations✓ 0/15", Repertoire 265). Reihenfolge: Linie > Extension >
piratechess (erstes Kapitel). Der Anhaenge-Weg setzt ausserdem `Book.DisplayName` nach, wenn er noch vom
Dateinamen stammt (`chessable-u5-55720`, `IsFileNameDerived`).

### Gespeicherte Partien (auth + öffentlicher Teilen-Link)
Bereich „Partien" (`/games`): zeigt die über die RepCheck-Extension von chess.com/lichess gespeicherten Partien (dazu eingelesene Formulare und hochgeladene PGNs). Das ⋮-Menü der Partieseite bietet seit 0.553.0 „PGN kopieren" und „PGN herunterladen" — für JEDEN Betrachter, auch auf dem Teilen-Link (das PGN liegt dort ohnehin im Browser). Seit 0.592.0 dazu „Im Analysebrett öffnen" (ganzes PGN per Router-State, „Zurück" führt auf die Partie); das Analysebrett nimmt eine Partie außerdem über `?pgn=` an (für Sprünge von LeagueHub, der Router-State kommt über Seitengrenzen nicht an) und lässt das PGN nach einem Sprung im PGN-Feld stehen. Nachspielen als eigene SEITE `/games/{id}` (seit 0.513.0 dieselbe Komponente wie der Teilen-Link `/g/{token}`, `data.mode = 'own'`; vorher ein PGN-Viewer-Dialog — der Nutzer wollte eine Seite), „In Analyse öffnen" (PGN via Router-State an `/analysis`), Löschen, und Teilen über einen eindeutigen öffentlichen Link `/g/{shareToken}` (kein Login). Logik in `SavedGameService`; Menü-Key `games` (Default `Registered`).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/games?take=200` | Auth | Eigene gespeicherte Partien (neueste zuerst, ohne PGN). Seit 0.515.0 je Partie `analysis` = Stand der VERKNÜPFTEN Analyse (`status`, `analyzed`/`total`, `accuracyWhite`/`accuracyBlack` bei `done`) oder `null` — EINE gruppierte Zählung über alle verknüpften Ids, kein Abruf je Partie; fertige Analysen ohne abgelegte Genauigkeit (vor 0.515.0) werden dabei nachgerechnet (`AccuracyBackfillPerCall` = 10 je Aufruf) |
| GET | `/api/games/shared/{token}` | AllowAnonymous | Öffentliche Sicht einer geteilten Partie inkl. PGN (ohne Besitzer-Daten). Literal-Route VOR `{id}`. Seit 0.526.3 `ownGameId` = die Partie-Id, NUR wenn der angemeldete Aufrufer der Besitzer ist — die Seite leitet dann auf `/games/{id}` um (eigener Link = dieselbe Ansicht wie aus „Partien") |
| GET | `/api/games/shared/{token}/evals` | AllowAnonymous | Bewertungen der geteilten Partie für Kurve/Genauigkeit/Zug-Klassen (`GameEvalsDto`). Anonym NUR die vom Besitzer verknüpfte Analyse; angemeldet ersatzweise die EIGENE mit gleichem PGN. Globaler IP-Limiter wie `GET shared/{token}` |
| POST | `/api/games/shared/{token}/analyze` | Auth | „Partie analysieren" auf der geteilten Partie — jeder Angemeldete. Antwort `GameAnalyzeResultDto { analysis, reason?, reused }`, Absage 400 `{ reason, message }` wie `POST /api/game-analyses/guess` |
| GET | `/api/games/{id}` | Auth | Detail einer eigenen Partie inkl. PGN (Nachspielen/Analysieren) |
| GET | `/api/games/{id}/evals?book=0` | Auth | Bewertungen einer eigenen Partie (Nachspiel-Dialog). **`?book=0` (0.664.0) lässt die Buchzüge weg** — sie brauchen das Stellungs-Set der Repertoires, das nach 5 min Leerlauf neu gebaut wird (gemessen 2–4 s bei 13 MB PGN, Prod 05.10., sonst ~5 ms) und hielt die Antwort auf. Die Seite fragt erst `?book=0`, dann einmal ohne Parameter und trägt `bookPlies` nach (gilt ebenso für `…/shared/{token}/evals`) |
| POST | `/api/games/{id}/analyze` | Auth | „Partie analysieren" an einer eigenen Partie (Liste + Nachspiel-Dialog). Rumpf optional `{ lang }` (0.540.0, auch beim Teilen-Link, gemerkt nur beim Besitzer als `SavedGame.ReviewLanguage`) — darin entstehen danach Erklärungen und Roasts |
| GET | `/api/games/{id}/mistakes` | Auth | Stand des Fehler-Trainings dieser Partie (`total`/`solved`/`open`/`solvedPlies`); 404, solange nie trainiert |
| POST | `/api/games/{id}/mistakes` | Auth | Fortschritt melden `{ total, solved[] }` — die in DIESEM Durchlauf SELBST gefundenen Halbzüge. **Additiv und idempotent**: der Server vereinigt sie mit dem Stand, ein zweiter Durchlauf nimmt nichts weg, dieselbe Meldung zweimal ändert nichts. `total` wird auf ≥ gefundene und ≤ 600 geklemmt, Halbzüge außerhalb der Partie fallen weg (`GameMistakeProgressService`). `/api/games` trägt den Stand je Partie als `mistakes` mit; die Übersicht zeigt seit 0.526.3 nur „Fehler nachgespielt" (die Zahlen im Tooltip) |
| DELETE | `/api/games/{id}` | Auth | Eigene Partie löschen |
| — | Klassifizierer (0.661.0) | — | `GET /api/games` und Detail tragen je Partie `classifier1`/`classifier2` (GELTENDER Wert) plus `classifier1Set`/`classifier2Set` (nur vom Nutzer gesetzt). Online-Partien (chess.com/lichess): Seite + Modus, abgeleitet in `GameClassifier` (Modus = Grundzeit + 40 × Inkrement: < 180 s Bullet, < 480 Blitz, < 1500 Rapid, sonst Classical, `1/86400` Daily; ohne Bedenkzeit leer). Ligapartien: Liga + Jahrgang, vom Nutzer gesetzt über `PUT /api/games/{id}` (`classifier1`/`classifier2`: `null` = unverändert, leer = zurücknehmen, ≤ 80 Zeichen; Spalten `SavedGames.Classifier1/2`). `/games` zeigt sie als Chips und filtert je Klassifizierer (clientseitig, `classifier.util.ts`); der Editor schlägt Werte aus den eigenen Partien und den Jahrgang aus dem Datum vor (ab August 2026/27) |
| — | Tags (0.662.0) | — | `tags[]` je Partie in Liste und Detail (nur Besitzer); `PUT /api/games/{id}` mit `tags` (`null` = unverändert, sonst die ganze neue Liste). `GameTags`: höchstens 10 je Partie, je ≤ 30 Zeichen, ohne Doppelte (Groß-/Kleinschreibung egal), Spalte `SavedGames.Tags` zeilengetrennt. `/games`: Chips + Tag-Filter, der Editor schlägt vergebene Tags vor (`tags.util.ts`) |
| — | Klassifizierer der Vereinspartien (0.666.0) | — | `LeagueClubGames.Classifier1/2` (Liga/Jahrgang) — NIE in einer Vereins-DTO ausgeben (Liga + Saison + Gegner machten die „Schwaz“-Seite im Spielplan wieder auffindbar). Sie gehen nur in die Kopie nach „Meine Partien": `ImportPgnAsync` setzt `SavedGame.Classifier1/2` aus der verbundenen Vereinspartie, `LinkClubCopiesAsync` trägt nach (`??=`, überschreibt nichts). Befüllt per Skript `scripts/club-classify-backfill.py` (Gegner-FIDE gegen Spielpläne, nur eindeutige Treffer) |
| POST | `/api/games/import` | Auth | **PGN hochladen** (0.553.0, Knopf „PGN hochladen" auf `/games`: Datei wählen ODER einfügen) `{ pgn, ownerSide? }` → `{ imported, duplicates, truncated, ids[], failed[{ index, white, black, reason }] }`. `ownerSide` (`white`/`black`, sonst ignoriert; 0.644.0, „Partie analysieren" nach dem Maia-Sparring) wird die festgelegte Seite jeder NEU angelegten Partie — eine Dublette bleibt, wie sie ist. Ein alleinstehendes Punkte-Token (chess.js schreibt „4. ... Bc5") ist ein Zugnummern-Rest, kein Zug (`PgnParser.IsDotsOnly`, 0.644.0 — vorher `illegal`). Jede Partie des Textes wird eine eigene Partie mit Quelle `pgn`: HAUPTVARIANTE samt Kommentaren und allen Kopfdaten (Elo, Bedenkzeit, FEN …), Varianten fallen weg (Analyse/Kurve/Fehler-Training arbeiten auf einer Zugfolge). Eine Partie, deren Hauptvariante nicht bis zum Ende legal ist, wird NICHT gekürzt angelegt (`reason` `illegal`/`noMoves`/`tooLong`/`badFen`). Zweimal hochgeladen = einmal da: `ExternalId` = Hash über Seven-Tag-Kopfdaten, FEN und Züge (`SavedGameService.ImportKey`, eindeutig je Nutzer). Deckel `MaxImportGames` 200 (`truncated`), `MaxImportChars` 5 Mio. (400 `tooLarge`; leer → 400 `empty`). Der Dialog schließt bei vollem Erfolg (genau eine neue Partie → gleich geöffnet) und bleibt sonst offen mit der Liste der nicht übernommenen |
| GET | `/api/games/{id}/explanations?lang=` | Auth | „Warum war das ein Fehler?" (0.534.0): gespeicherte Erklärungen der verknüpften Analyse in der Sprache (`{ available, canGenerate, running, language, items[{ ply, class, text, master? }] }`; `master` = der mitgegebene Meisterkommentar, 0.542.0: `{ libraryGameId, white, black, event, year, annotator, text }`) |
| POST | `/api/games/{id}/explanations?lang=` | Auth | Erzeugen anstoßen (Hintergrund, nur Besitzer). 503 `notConfigured` ohne Modell auf eigener Hardware, 404 fremde Partie, 409 ohne fertige verknüpfte Analyse, 429 `{ reason: "tooManyRunning", maxRunning: 2 }` bei zwei laufenden Aufträgen des Kontos (dasselbe Paar Analyse/Sprache noch einmal = „läuft schon", kein 429; Codereview A6-005) |
| GET | `/api/games/shared/{token}/explanations?lang=` | AllowAnonymous | Dasselbe lesend für den Teilen-Link (`canGenerate` immer false) |
| GET | `/api/games/{id}/roasts?lang=` | Auth | „Roast my game" (0.535.0): die gewürfelten Kommentare der eigenen Partie `{ available, hasAnalysis, items[{ style, language, text, createdAt }] }`; 404 fremde Partie |
| POST | `/api/games/{id}/roasts?style=&lang=` | Auth | Würfeln (ersetzt den vorigen Text desselben Stils; `style` ∈ friendly/cheeky/russian). Absagen mit `reason`: 503 notConfigured, 404 notFound, 409 noAnalysis, 400 invalidStyle, 429 dailyLimit (`MaxPerDay` 60), 502 failed |
| GET | `/api/games/{id}/similar` | Auth | „Ähnliche Meisterpartien" (0.544.0, `SimilarGamesService`): `{ opening?, sharedPlies, sharedLine?, items[{ game (wie die Bestandssuche, inPool/requested/gameAnalysisId), sharedPlies, lastSharedMove?, masterMove?, gameMove? }] }` — kommentierte Partien des Rohbestands mit der LÄNGSTEN gemeinsamen Zugfolge (binär gesucht über `OpeningLine`, dort die besten nach Note, aufgefüllt aus flacheren Stufen bis 5, mindestens 4 gemeinsame Halbzüge); 404 fremde Partie. Bewusst über die Zugfolge, nicht über den Kommentar-Index: eine eigene Partie hat keine Kommentare, und der Index hängt an Zugnummern (siehe `MasterComments`) |
| GET | `/api/games/shared/{token}/similar` | AllowAnonymous | Dasselbe für den Teilen-Link (Kopfdaten ohne Züge, derselbe Zuschnitt wie die anonyme Bestandssuche; anonym trägt nur `inPool`) |
| GET | `/api/library-games/{id}/view?lang=` | Auth | „Anschauen" einer Meisterpartie (0.567.0, `LibraryGameService.ViewAsync`) → `{ id, pgn, language, languages }`: PGN aus der HAUPTVARIANTE und den Kommentar-Sätzen in `lang` (`CommentSetService.ForLibraryAsync`: dieselbe Regel wie beim Nachspielen, fehlende Halbzüge aus der Quelle, Quell-Sätze entstehen beim ersten Bedarf), nicht das rohe Quell-PGN (zwei Sprachen hintereinander, ChessBase-Figurenschrift). Nur angemeldet wie das Anfordern; 404 unbekannt/aussortiert/nicht nachspielbar |
| GET | `/api/games/{id}/recap` | Auth | „Kurz erzählt" (0.541.0): die Nacherzählung der eigenen Partie `{ available, hasAnalysis, text?, language?, createdAt?, pending }`. Fehlt sie bei fertiger Analyse (Analyse von vor 0.541.0, gescheiterter Lauf), stößt schon dieser Abruf sie im Hintergrund an (`pending: true`, die Seite fragt alle 15 s nach, höchstens achtmal); 404 fremde Partie. Der Teilen-Link bekommt denselben Text als `recap` in `GET /api/games/shared/{token}` |

**Deckel je Konto** (Codereview 2026-09-29, A6-007): höchstens `SavedGameService.MaxGamesPerUser` (5 000) Partien
und `MaxPgnCharsPerUser` (100 Mio. Zeichen PGN, Summe) je Konto, alle Quellen zusammen. `POST /api/games/import`
meldet jede neue Partie darüber in `failed` mit `reason: "quota"` (HTTP 200); `PUT /api/games/{id}` antwortet bei
wachsendem PGN über dem Zeichendeckel 400 `{ reason: "quota" }` (Kürzen geht immer); `POST /api/extension/games`
400 am Zähldeckel. Züge der Extension höchstens 16 Zeichen (`MaxSanLength`, sonst 400 „Invalid move.").

**„Warum war das ein Fehler?" (0.534.0, `GameMoveExplanationService`).** Zu jedem Fehler der verknüpften Analyse
(Ungenauigkeit/Fehler/grober Fehler/verpasste Chance, die schwersten `MaxPerGame` = 15) schreibt das Sprachmodell auf
EIGENER Hardware ein, zwei Sätze — NUR mit `IClaudeJsonClient.IsLocal` (Spark), über Claude liefe es auf Kosten. Regeln:
* **Fakten statt Rechnen:** `GameMistakes.Find` (Server-SPIEGEL von `classify`/`CLASS_LIMITS`/Miss-Regel in
  `game-review.util.ts`, literale Grenzwerte in `GameMoveExplanationTests` ↔ `game-review.util.spec.ts`) liefert je Fehler
  Stellung, gespielten Zug, Bestzug + Engine-Linie (SAN, Rochade König-schlägt-Turm aufgelöst), Widerlegung (Linie der
  NÄCHSTEN Stellung) und Bewertung vorher/nachher. Das Modell soll erklären, nicht rechnen.
* **Der GRUND, nicht die Bewertung** (0.572.0, `Services/ExplanationFacts.cs`, gemeldet 2026-09-28 an Prod-Partie 34): mit
  Gewinnchance und SAN-Linien begründete das Modell jeden Fehler mit genau dem („ein Fehler, weil er seine Gewinnchancen
  senkt") und erfand Material („starker Bauernvorteil", wo eine Figur fiel). Jetzt bekommt es GEPRÜFTE Fakten: `Situation`
  (die Lage in Worten aus Sicht des LESERS, Stufen ab 2,5 / 1 / 0,4 Bauern: „from clearly worse to lost", „still winning,
  but gave away part of the advantage", „the opponent's move made the win even easier"), `FirstMoveAttacks` (was der erste
  Zug der Antwort bzw. der besseren Linie angreift, ohne Bauern/König) und `LineEvents` (Schach im ersten Zug, Matt,
  Umwandlung, Materialbilanz am Ende — nur, wenn die RICHTIGE Seite vorne liegt: in der Antwort-Linie der Gegner, in der
  besseren der Ziehende; einzelne Schlagfälle bewusst nicht, das Modell verwechselte damit die Seiten). **Die Bilanz der
  Antwort-Linie zählt ab der Stellung VOR dem Fehler, samt dem gespielten Zug** (`LineEvents(…, lead: PlayedSan)`, 0.585.1):
  ab der Stellung danach war ein Zurückschlagen ein Gewinn — Prod-Partie 35, 22.Txc4 dxc4 stand als „Black ends up ahead by
  5", das Modell schrieb „du gewinnst sofort einen Turm". Ohne solche Fakten
  sagt der Auftrag „positional, name the moves, do not invent reasons". Der Auftrag verbietet die Bewertung als Grund, Zahlen
  nur als „+5.8 → +4.4". **Schon verloren = keine Erklärung** (`AlreadyLost`: vorher UND nachher Stufe −3, Wunsch „von −4
  auf −6 muss das nicht kommentiert werden"). **Fassung** `GameMoveExplanation.Revision` (`CurrentRevision` = 2 seit 0.585.1): ältere
  Texte zeigt `GetAsync` nicht, `GenerateAsync` räumt sie weg — wer den Auftrag spürbar ändert, erhöht die Zahl.
* **Meisterkommentar zur SELBEN Stellung** (0.542.0, `Services/MasterComments.cs`): vor dem Schreiben sucht der Dienst
  im Rohbestand kommentierte Partien mit GENAU derselben Zugfolge bis zur Stellung vor dem Fehler (`LibraryGame.OpeningLine`,
  Präfix-Suche, 40 beste nach Note) und gibt den treffendsten Kommentar dem Modell mit: (1) Meister spielte denselben Zug
  und der Kommentator urteilt dazu, (2) der Kommentar nennt den gespielten Zug (meist als Variante, auch in den Buchstaben
  seiner Sprache), (3) Meister spielte den Bestzug, (4) Kommentar zur Stellung davor (≥ 40 Zeichen). Aufgeräumt: Markup,
  ChessBase-Symbolzeichen (kommen als hebräische Buchstaben), Zugketten über drei Tokens → „…", unter 25 Buchstaben Prosa
  = kein Kommentar. Das Modell darf Ideen übernehmen, Züge NUR aus den Engine-Linien; nennt der Text einen Zug aus dem
  Kommentar, fragt der zweite Versuch OHNE Kommentar nach (die Quelle wird dann nicht vermerkt). Gespeichert
  `MasterLibraryGameId` + `MasterText`, die Seite zeigt die Quelle unter der Erklärung (Wortlaut zum Aufklappen).
  **Bewusst KEINE Vektorsuche**: am 2026-09-26 auf Prod gemessen fand der Kommentar-Index zu Erklärungstexten vor allem
  Kommentare mit denselben Zugnummern/Zügen aus ANDEREN Stellungen („23...Qh4 lässt die Grundreihe ungeschützt" →
  „23. Qc2: Under time pressure …") — dem Modell mitgegeben, vermischte es zwei Partien. Preis: Treffer fast nur in der
  Eröffnung (Amateurpartien verlassen den Meisterbestand meist zwischen Halbzug 6 und 16; gesucht wird bis
  `LibraryGameReader.OpeningPlies`, ab dem ersten Halbzug ohne Treffer gar nicht mehr).
* **Figurenbuchstaben beim LESEN** (0.541.1, gilt für Erklärungen, Roasts und „Kurz erzählt"): gespeichert wird in
  englischer SAN (so stehen die Züge in den Fakten, und nur so prüft `MentionsOnly`), `PieceLetters.Convert(text, "en", lang)`
  setzt beim Ausliefern die Buchstaben der Sprache („Sf3"). NIE umgestellt speichern: ein zweites Umstellen machte im
  Französischen/Spanischen/Italienischen aus dem König („R") einen Turm. Qwen auf der Spark stellt sie selbst nie um.
* **Kein erfundener Zug** (`IsGrounded`): jeder Figuren-/Schlag-/Rochadezug im Text muss in diesen Linien stehen (bloße
  Felder wie „e4" zählen nicht — vom Feldnamen im Satz nicht zu unterscheiden); sonst eine Nachfrage, dann verworfen.
* **Speicher an der ANALYSE** (`GameMoveExplanations`), gilt für alle Betrachter; ERZEUGEN nur der Besitzer, im Hintergrund
  (`Task.Run` mit eigenem Scope, `GameExplanationJobs` gegen Doppelstarts; ein Neustart verwirft das Laufende, der nächste
  Klick erzeugt nur das Fehlende), 4 Anfragen parallel.
* **Aus der Sicht des BESITZERS** (0.540.0, `GameMoveExplanation.Viewpoint`): „you" ist immer er — auch bei den Fehlern
  seines Gegners („your opponent", wie er sie nutzt). Vorher schrieb das Modell jeden Fehler an den, der ihn gemacht hatte
  („Your move 14. Nb5" an Weiß, obwohl der Besitzer Schwarz war). Die Seite kommt aus `SavedGameService.DetermineOwnerSide`
  (festgelegt > Plattform-Name), unbekannt = leer = neutral in der dritten Person. Die Fakten nennen deshalb die FARBEN statt
  „the player", die letzte Zeile (`Perspective`) sagt, wer der Leser ist. `GetAsync` liefert nur Texte der AKTUELLEN Sicht
  (legt der Besitzer später eine andere Seite fest, erscheint der Knopf wieder), `GenerateAsync` räumt die der alten weg.
  Migration `ExplanationViewpoint` hat alle Texte von vorher gelöscht.
* **Entstehen von selbst** (0.540.0, `GameReviewTexts` + `IGameReviewTextScheduler`): die Pumpe (`GameAnalysisService`)
  stößt nach dem ersten Durchgang (`Done`) und nach der Vertiefung (`RefinedAt`) je EINMAL an — seit 0.541.0 zuerst die
  Nacherzählung („Kurz erzählt", unten), dann die Erklärungen, dann die drei Roasts (nur fehlende, `GameRoast.Automatic`, nicht im Tagesdeckel). Nach der Vertiefung werden die Erklärungen in
  JEDER vorhandenen Sprache neu geschrieben (Bestzug/Klasse können sich verschieben). Nur für die mit dem BESITZER der
  Analyse verknüpfte Partie. Sprache: `SavedGame.ReviewLanguage` (die Seite schickt `{ lang }` mit „Partie analysieren"),
  sonst die jüngste gemerkte des Nutzers, sonst `en`. Die Pumpe kennt nur die Schnittstelle — die Texte hängen über
  `GameRoastService` → `SavedGameService` wieder an ihr (Zyklus), deshalb löst der Auslöser sie in einem eigenen Scope auf.
* **Sperrzeiten der Spark** (0.546.0, `Services/QuietHours.cs`, Wunsch des Nutzers: „gleiches zeitfenster" wie die
  Übersetzungen): `TextLlm:QuietHours` (Vorgabe `Mon-Thu 08:00-17:00; Fri 08:00-14:00`, Ende ausschließlich, LEER = nie
  gesperrt) in `TextLlm:TimeZone` (Vorgabe `Europe/Vienna` — der Server läuft in UTC, die Sommerzeit verschöbe sonst die
  Fenster). **Sie gilt NUR für die Hintergrund- und Massenläufe, nicht für Aufträge auf Zuruf** (0.585.0, Wunsch des
  Nutzers: „Spark steht unter Tag durchaus für On-demand-Aufträge zur Verfügung — nur nicht für die Patchläufe"). Gesperrt:
  die automatischen Texte nach einer Analyse — der `GameReviewTextScheduler` stellt, was die Pumpe in der Sperrzeit
  anstößt, ZURÜCK (je Analyse einmal, „vertieft" gewinnt beim Zusammenführen) und lässt es nach dem Ende los; ein
  Wartender je Prozess, schläft höchstens 10 min am Stück, ein Neustart verliert die Liste —, der Kurs-Übersetzungsdienst
  (0.548.0, `CourseTranslationWorker`: Aufträge warten, ein Lauf bricht beim Beginn ab und kommt zurück in die Schlange;
  auch ANGEFORDERTE Kurs-Übersetzungen — ein Kurs ist Stunden Arbeit) und der Bibliothekslauf (hält die Fenster außerhalb
  der App über die Schaltuhr `.jobs/spark-uebersetzung.sh` ein). FREI: „Fehler erklären lassen" (`POST …/explanations`),
  „Roast my game" (`POST …/roasts`) und die Nacherzählung beim Öffnen einer Partie (`GET …/recap` → `ScheduleRecap`, läuft
  sofort) — die DTOs tragen dafür kein `quietUntil` mehr, `quietHours` gibt es als Absage nicht mehr. Puzzle-Tipps hingen
  nie an der Sperrzeit. Eine unlesbare Angabe wirft beim Start (`FormatException`) statt still „nie gesperrt" zu bedeuten.
  Die Seite zeigt „wieder ab Fr., 14:00" nur noch bei der Kurs-Übersetzung (`quiet-hours.util.ts`).
* Frontend: `GameReviewComponent` lädt die Erklärungen, sobald die Analyse `done` ist (und bei Sprachwechsel), zeigt den
  Text unter dem Abzeichen des aktuellen Zugs, den Knopf „Fehler erklären lassen" nur mit `canGenerate`, keine Erklärung
  vorhanden und Fehlern in der Partie; fragt alle 5 s nach, solange es läuft; im Fehler-Training aus (nennt den besseren Zug).

**„Roast my game" (0.535.0, `GameRoastService`).** Ein frecher Kommentar zur EIGENEN Partie, ⋮-Menü von `/games/:id`
und der Partienliste (`GameRoastDialogComponent`), nur über das Modell auf eigener Hardware (`IsLocal`) und nur mit fertiger
Analyse. Drei Stile (Wunsch des Nutzers): `friendly`, `cheeky`, `russian` — der gnadenlose sowjetische Trainer, derb, mit
Kraftausdrücken, hinterfragt offen die geistige Kapazität („free for all"); die EINE Grenze im Auftrag: keine Angriffe auf
Herkunft, Nationalität, Religion, Geschlecht, Sexualität, Behinderung — der Text wird geteilt. Fakten: Kopfdaten, Ergebnis
aus Sicht des Spielers (`OwnerSide`), Genauigkeit je Seite, die fünf schlimmsten eigenen Züge mit besserem Zug und
Widerlegung (`GameMistakes`); genannte Züge müssen in der Partie oder diesen Linien stehen
(`GameMoveExplanationService.MentionsOnly`, eine Nachfrage). Synchron (ein Aufruf, Sekunden), je Partie/Sprache/Stil ein
Text („Neu würfeln" ersetzt), `MaxPerDay` 60 je Nutzer. Nichts wird automatisch veröffentlicht: Kopieren (mit Partie-Link)
bzw. Teilen-Blatt des Geräts.

**„Kurz erzählt" (0.541.0, `GameRecapService`).** Die Partie in zwei, drei Sätzen für die Link-Vorschau
(og:description von `/g/{token}` statt „1-0 · lichess · Partie auf RookHub nachspielen") und oben auf der Partieseite —
geschrieben vom Modell auf eigener Hardware (`IsLocal`), im Muster des Roasts. Regeln:
* **Fakten aus der Kurve** (`Course`): die Lage NACH jedem Halbzug in fünf Stufen aus der Gewinnchance von Weiß
  (≥ 80 gewinnt, ≥ 60 besser, > 40 ausgeglichen, gespiegelt), zu Abschnitten zusammengefasst („after 13.Nxe5: Black is
  winning"); ein Abschnitt aus EINEM Halbzug zwischen zwei gleichen fällt weg (Schlagen vor dem Zurückschlagen), mehr als
  `MaxCourseSegments` (8) = Anfang + die letzten. Dazu Kopfdaten, Eröffnungsname aus `[Opening]` bzw. chess.coms
  `[ECOUrl]`, die ersten zehn Halbzüge, Genauigkeit, die drei schwersten Fehler/Misses beider Seiten mit NUMMERIERTEM
  Bestzug und Widerlegung (`Numbered`), das Ende (Matt auf dem Brett > `[Termination]` außer „Normal" > nur das Ergebnis).
* **Dritte Person mit Namen** — den Text liest, wer den Link bekommt. Genannte Züge müssen in der Partie oder den
  Engine-Linien stehen (`MentionsOnly`, eine Nachfrage, sonst kein Text). Gespeichert in englischer SAN; die
  Figurenbuchstaben der Sprache setzt erst das Lesen (`CurrentAsync` → `PieceLetters.Convert`, Regel unten bei den Erklärungen).
* **Speicher je Partie und Sprache** (`GameRecaps`, geht mit der Partie). Gezeigt wird überall DERSELBE Text
  (`GameRecapService.CurrentAsync`): der in der Sprache der Partie (`SavedGame.ReviewLanguage`), sonst der jüngste.
* **Entsteht von selbst**: `GameReviewTexts` schreibt ihn als ERSTES nach der Analyse (ein Aufruf, und wer gleich teilt,
  hat ihn schon), nach der Vertiefung in jeder vorhandenen Sprache neu (`replace`). Für ältere Analysen stößt
  `GET /api/games/{id}/recap` ihn an (`IGameReviewTextScheduler.ScheduleRecap` → `WriteRecapAsync`). Doppelt gleichzeitig
  verhindert `GameExplanationJobs` mit dem Schlüssel (Partie-Id, `recap:<lang>`). Kein Knopf, kein Tagesdeckel.
* **Vorschaubild mit Kurve** (`OgImageService.RenderBoard(fen, flip, curve)`): mit FERTIGER Analyse rückt das Brett nach
  links, rechts die Kurve wie `EvalGraphComponent` (Lichess-Fläche von der Mittellinie, monotone kubische Glättung,
  linear bis ±10 Bauern, Matt am Rand; Lücken übernehmen den Wert davor), weiter ohne Schrift. Weil das Bild
  `immutable` gecacht ist und Discord & Co. Bilder nach der ADRESSE merken, trägt og:image dann `?v={analysisId}-{refined}`
  (`OgMetaService.CurveVersion`). Eine halbe Kurve (Analyse läuft) kommt nicht ins Bild.

**Bewertungskurve aus der EIGENEN Analyse (0.512.0).** „Partie analysieren" (`/g/…`, Liste, Nachspiel-Dialog)
wirft die Partie über denselben Weg wie die Punktepartie-Seite ein (`GameAnalysisService.CreateForGuessAsync`:
Haus-Engine, fünf Linien, gemeinsamer Deckel — aber **Tiefe 30** statt 20, `GameAnalysisDefaults.SavedGameTargetDepth`
(0.514.2 mit 30 eingeführt, 0.518.0 auf 25 — auf 30 brauchte eine Partie mit 47 Stellungen eine halbe Stunde —, seit 0.555.2 wieder 30, weil
das inzwischen die Vertiefung im Hintergrund ist, siehe „Zwei Durchgänge"): hier ist die Bewertung das Ergebnis, und ein Opfer, das die Engine erst zwei Züge später versteht,
stünde bei 20 als Fehler in der Kurve; eine schon vorhandene Analyse wird trotzdem wiederverwendet, auch eine
flachere), aber mit eigenem Ursprung
**`GameAnalysisOrigin.SavedGame`** — die Analyse gehört zur Partie und steht NICHT in „Eigene Analysen"
(`GameAnalysisService.ListAsync` lässt sie weg, für `/guess` UND `/analysis/games`). Regeln in `SavedGameService`:
* **Verknüpfung** `SavedGame.GameAnalysisId` (kein FK — die Analyse darf gelöscht werden; jeder Leser prüft,
  ob es sie noch gibt, und antwortet sonst `none`). Gesetzt NUR, wenn der BESITZER klickt; ein Gast bekommt
  seine eigene Analyse, die öffentliche Kurve bleibt die des Teilenden.
* **Mehrfach klicken = einmal rechnen** (`AnalyzeCoreAsync`): (1) die verknüpfte, solange nicht `Failed` —
  gleich, wer klickt; (2) sonst eine eigene des Aufrufers mit EXAKT gleichem `Pgn` (auch eine über `/guess`
  eingeworfene), neueste nicht gescheiterte, beim Besitzer verknüpft; (3) erst dann anlegen. Zwei Klicks binnen
  Millisekunden fängt der Client (Knopf gesperrt, solange der Aufruf läuft).
* **Perspektive**: die Kandidatenlisten stehen aus Sicht der Seite am Zug; `GameEvalsDto` liefert ALLES aus
  WEISS-Sicht (`Services/GameEvals.cs`, gedreht mit derselben FEN-Regel wie beim Einlesen). `cp/mate` = bester
  Kandidat, `played*` = Kandidat des Partiezugs (fehlt er unter den fünf → `null`), `second*` = zweiter (seit
  0.514.0 im Client für Great/Brilliant, siehe `src/frontend/CLAUDE.md`), `candidates` = alle (seit 0.519.0,
  für die gleichwertigen Züge in „Eigene Fehler nachspielen"). `final` = gespielter Kandidat der LETZTEN Zeile (für die Endstellung gibt es
  keine Zeile). Nicht gerechnete und aufgegebene (`[]`) Zeilen fehlen in `plies` — der Client lässt dort eine Lücke.
* Weder die Partie noch die Analyse bringen beim Nachfragen ihr PGN mit: der Rückfall (b) vergleicht per
  Unterabfrage in SQL (`QueryTranslationTests.GespeichertePartie_…` prüft die Übersetzung gegen MariaDB).
* Frontend: `features/games/game-review.util.ts` (Formeln), `game-review.component.ts` (lädt, fragt alle 10 s
  nach, solange `pending`/`running`), `shared/pgn-viewer/eval-graph.component.ts` (SVG-Kurve). Siehe
  `src/frontend/CLAUDE.md`.
* **Zwei Durchgänge** (0.523.0, gewünscht 2026-09-24): „Partie analysieren" (`Origin.SavedGame`) rechnet ERST schnell
  (`SavedGameFastDepth` 20, `SavedGameFastMultiPv` 1 — Kurve, Genauigkeit und Fehler stehen nach Minuten, Status `done`),
  DANN im Hintergrund die Vertiefung (`GameAnalysis.RefineDepth` = `SavedGameTargetDepth` 30 seit 0.555.2, vorher 25, `RefineMultiPv` 5): jede
  Stellung wird neu gerechnet und ERSETZT (`GameAnalysisPosition.Refined`), am Ende `RefinedAt` + Genauigkeit neu.
  Regeln: (1) Vertiefungs-Aufträge sind `AnalysisJob.Background` — `PickNextForEngineAsync` nimmt sie erst, wenn kein
  normaler wartet (die warme Hashtabelle zählt nur innerhalb derselben Stufe); (2) je Partie höchstens
  so viele Aufträge offen, wie der Engine-Besitzer Hintergrund-Engines hat (`RefineJobCap`: mindestens `MaxOpenRefineJobsPerGame` = 8, höchstens `MaxOpenJobsPerGame` = 32; bis 0.567.2 fest 8 — bei 16 Engines lag die Hälfte brach);
  (3) vertieft wird erst, wenn KEINE Partie des Nutzers mehr im ersten Durchgang steckt, dann die älteste
  (`IsOwnersRefineTurnAsync`) — seit 0.647.0 mit Schwanz: haben die älteren Vertiefungen ZUSAMMEN weniger unvertiefte
  Stellungen als Engines da sind (`UnrefinedPliesOfOlderGamesAsync`, `TailMayAdvance`), fängt die nächste schon an
  (Anlass 04.10.2026: Analyse 5540 hing eine Stunde an ihrer letzten Stellung, die geteilte Partie dahinter bekam
  keinen Auftrag, 15 von 16 Engines rechneten Meisterpartien); (4) scheitert die Vertiefung einer Stellung, bleibt das
  erste Ergebnis; (5) Zeitgrenze je Stellung (0.647.0, `RefineMaxSecondsPerPosition` = 900 s `AnalysisJob.SecondsSpent`
  über alle Läufe, Warten zählt nicht): danach übernimmt `IngestFinishedAsync` das Erreichte (`ResultJson` bei
  `ReachedDepth`), wenn es tiefer ist als der erste Durchgang, sonst bleibt der erste — der Lauf wird angehalten
  (`AnalysisJobService.Interrupt`) und der Auftrag entfernt. Anlass derselbe: Tiefe 26 nach elf Minuten, zweimal
  von einem schnellen Durchgang verdrängt, jedes Mal von vorn. `GameEvalsDto`
  meldet `Refining`/`Refined`, der Client fragt dann einmal je Minute nach. Punktepartie und von Hand eingereihte Partien
  bleiben bei einem Durchgang (`RefineDepth` null), Altbestand ebenso.
* **Buchzüge** (0.522.0): `GameEvalsDto.BookPlies` = die Halbzüge, deren Stellung DANACH in einem für die Erweiterung
  markierten Repertoire (`UseForExtension`, alle Arten, Zugumstellungen) des AUFRUFERS steht, vor der Abweichung —
  `RepertoireAnalyzeService.BookPliesAsync` (dasselbe gecachte Positions-Set wie die Extension-Abweichungsanalyse).
  Anonym leer — ein Teilen-Link darf nicht verraten, was der Teilende vorbereitet hat. Der Client zeigt die Klasse
  „book" (schlägt jede andere, Grundklasse + Genauigkeit bleiben), der Fehler-Trainer überspringt Buchzüge.
* **Computer-Linien** (0.521.0): `GameEvalCandidateDto.Pv` = die Variante der Engine je Kandidat (UCI ROH vom
  Broker, Rochade ggf. König-schlägt-Turm — der Client spielt sie nach, `normalizeCastlingUci`), abgelegt in
  `CandidatesJson` als `"pv":[…]` (`BrokerCandidates.MaxPvPlies` 16), gelesen mit `BrokerCandidates.PvsFromJson`.
  Die Wertung (`FromJson`) ignoriert das Feld. Analysen von vor 0.521.0 haben keine Varianten — die Partieseite
  zeigt dort je Kandidat nur den Zug mit Bewertung; wer Linien will, lässt neu rechnen (Restart).
* **Restdauer DIESER Partie** (0.517.0, `GameEvalsDto.EtaMinutes`, `GameEvals.EtaMinutes`): solange die Analyse
  läuft, aus den `AnalyzedAt` der jüngsten 12 gerechneten Stellungen (seit 0.521.2 über `AnalysisPace`: nur der
  zusammenhängende Lauf, eine Pause > 15 min davor zählt nicht) — gemessen vom ältesten davon bis JETZT
  (hängt die Engine, wächst die Schätzung), mindestens 60 s Spanne (die Pumpe liefert oft mehrere im selben
  Takt), nie unter 1 min; unter zwei Ergebnissen `null`. Die Wartezeit vor dem ersten Ergebnis zählt nicht.
  Bewusst NICHT `GET /api/game-analyses/throughput`: das ist das Tempo aller Analysen EINES Nutzers und braucht
  Anmeldung — `/g/` ist öffentlich. Die Schreibweise („11 min", „3 h 20 min") teilen sich beide über
  `shared/eta.util.ts`.
* **Genauigkeit je Seite liegt AN DER ANALYSE** (0.515.0, `GameAnalysis.AccuracyWhite/AccuracyBlack`,
  `Services/GameAccuracy.cs` = Server-SPIEGEL der Client-Formeln mit denselben LITERALEN Testwerten in
  `GameAccuracyTests` ↔ `game-review.util.spec.ts`): gerechnet in `PumpOneAsync` beim Übergang auf `Done`, damit die
  Partienliste sie zeigen kann, ohne je Partie die Stellungen zu laden (sie fragt während einer Rechnung alle 10 s).
  Die Liste zeigt statt des Analysieren-Knopfs den Fortschritt in %, fertig die beiden Genauigkeiten neben
  Ergebnis/Zugzahl, gescheitert wieder den Knopf. `GET /api/game-analyses?includeSavedGames=true` nimmt die
  `SavedGame`-Analysen mit — die Seite „Partie-Analysen" zeigt sie (Fortschritt), die Punktepartie-Seite nicht.

Akzeptiert sowohl JWT (User-Login) als auch ApiToken (`Authorization: Bearer rkh_…`). Bei ApiToken muss `scope=extension` sein (sonst 403); ein Token mit Scope `engine` erreicht ausschließlich `/api/external-engine/*` (`PatScopeFenceMiddleware.AllowedPrefixesByScope`, siehe „Eigener Engine-Broker“). Policy-Scheme im Auth-Stack routet das Bearer-Format automatisch zum passenden Handler.

CORS (`ExtensionPolicy`, nur für `ExtensionController`): erlaubt `https://www.chess.com`, `https://lichess.org`, `https://www.chessable.com`, `https://chessable.com` mit `GET`+`POST`, ohne `AllowCredentials` (Auth strikt über Bearer-Header). Gilt für den Userscript-`fetch`-Pfad; die Extension-Variante geht ohnehin CORS-frei über ihren Background-Worker. Die Default-CORS-Policy (Frontend) erlaubt `http://localhost:4200` + `http://localhost:8085`.

### Turnier-Proxy (leitet an Crawler weiter; lesen und Turnier holen ohne Anmeldung)
| Methode | Endpoint | Crawler-Route | Auth |
|---------|----------|---------------|------|
| GET | `/api/tournaments` | `/api/tournaments` | Auth |
| GET | `/api/tournaments/{id}` | `/api/tournaments/{id}` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/players?team=&sortBy=` | `/api/tournaments/{id}/players` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/teams[/{snr}]` | `/api/tournaments/{id}/teams` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/pairings?round=` | `/api/tournaments/{id}/pairings` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/players/{snr}/results` | `/api/tournaments/{id}/players/{snr}/results` | AllowAnonymous (`anonymous-tournament`) |
| GET | `/api/tournaments/{id}/rounds/check` | `/api/tournaments/{id}/rounds/check` | Auth |
| GET/POST | `/api/tournaments/{id}/clubs` | Vereine nachtragen | Auth (POST `user-crawl`) |
| POST | `/api/tournaments/crawl` | `/api/crawl` | **AllowAnonymous** (`user-crawl`, seit 0.643.0) |
| GET | `/api/tournaments/crawl/{jobId}` | Stand eines Holen-Auftrags | **AllowAnonymous** (`directory-read`, seit 0.643.0) |
| POST | `/api/tournaments/crawl/player-details` | `/api/crawl/player-details` | Auth |

**Turnier holen ohne Konto** (0.643.0, Wunsch „Turnierseite voll ohne Anmeldung benutzbar"): ein Gast kann ein noch
nicht geholtes Turnier über „Teilnehmer und Ergebnisse" bzw. „Aktualisieren" anfordern. `user-crawl` zählt dann je IP
(`RateLimitPartitions.UserOrIp`), dieselben 10/min wie je Konto; die Zuordnung hält `AnonymousRateLimitTests` fest,
die Liste der anonymen Endpunkte `EndpointAuthInventoryTests`.

**Crawler-Aufträge je Konto gedrosselt** (Codereview 2026-09-29, A5-004): Policy `user-crawl` (10/min je Konto,
`RateLimitPartitions.CrawlerRequest`, ein gemeinsames Fenster) auf `POST /api/tournaments/crawl`,
`/crawl/player-details`, `/{id}/clubs` und `POST /api/tournament-monitors/{id}` — zusätzlich zum globalen Deckel je IP;
die Crawler-Warteschlange (500 Plätze) und der chess-results-Takt gehören auch den Hintergrunddiensten.

### Chessable-Integration (auth, leitet an piratechess-API weiter)

> **Abschaltbar: `Chessable:Enabled=false`** (`CHESSABLE_ENABLED=false`, Vorgabe an). Dann antwortet
> `/api/chessable/*` mit **404** (ausser `/api/chessable/admin/*`), und es fallen weg: der Drain des Watchdogs,
> der Resume-Dienst und der naechtliche Kurslisten-Refresh. **Die Download-Lane selbst laeuft weiter**, bewusst:
> ein NICHT gecachter Admin-Import („Kurse von Usern holen") reiht ueber `ChessableImportQueueService.EnqueueNextAsync`
> ein Ticket ein, der `BackgroundTaskWorker` ruft `RunNextAsync` ohne Schalterpruefung, und der Kurs wird dabei
> ECHT von Chessable geholt (ueber piratechess/VPN, mit dem Bearer des Ziel-Users). Der Schalter sperrt also die
> Nutzer-Wege, nicht den Admin-Download. Der Weg ueber die **RepCheck-Extension** (`/api/extension/*`) bleibt
> UNBERUEHRT — genau darum geht es: **auf PROD seit 2026-09-09 abgeschaltet**, alle sollen vorerst
> die Extension benutzen. Der Schalter schliesst die Endpunkte und den Nachtlauf. **Die Seite
> `/chessable` selbst zeigt seit 0.478.0 nur noch den Hinweis auf die Extension** (Links in beide
> Stores + kurze Begruendung) — kein Bearer-Formular, kein Bookmarklet, keine Importliste, keine
> API-Aufrufe. **Der Menue-Eintrag `chessable` steht auf PROD seit 2026-09-14 auf `Registered`**
> (Zeile in `MenuItemSettings`, entspricht der Vorgabe aus `MenuRegistry`; vorher `Admin`, umgestellt
> erst NACH dem Deploy von 0.478.0): jeder angemeldete Nutzer landet dort auf dem Hinweis. Wer den
> alten Import-Bildschirm zurueckholt, stellt den Eintrag VORHER wieder auf `Admin` — sonst sehen
> alle Nutzer ein Formular, dessen Endpunkte auf Prod 404 liefern. Der Admin-Tab „Kurse von Usern
> holen" und das Dashboard-Widget bleiben unveraendert. **Deshalb laeuft die netzfreie Fast-Lane
> (`ChessableImportFastLaneService`) IMMER** (Codereview 2026-09-29): ein voll gecachter Admin-Import bekommt
> kein Queue-Ticket und stand ohne sie fuer immer auf „wartend"; der Watchdog stellt einen verwaisten, voll
> gecachten Server-Import auch ohne Lanes zurueck statt ihn zu schliessen. Einen haengenden Import bricht
> `POST /api/chessable/admin/imports/{id}/cancel` ab (der Nutzer-Weg `imports/{id}/cancel` antwortet 404).

RookHub speichert nur den per-User Chessable-Bearer (AES-verschlüsselt via `EncryptionService` → `ChessableCredentials.EncryptedBearer`). Alle Chessable-HTTP-Calls (curl-impersonate gegen Cloudflare) liegen im piratechess-Stack; `ChessableProxyService` reicht den Bearer pro Request an `POST /api/chessable/direct/*` durch und authentifiziert sich mit dem `X-Service-Key`-Header (`Chessable:ServiceKey` ↔ piratechess `Service:ApiKey`). Netzwerk: externes Docker-Netz `chessable-bridge` (von piratechess_docker bereitgestellt). **Admin-Download „im Namen eines Users"**: `ChessableImport.BearerUserId` (nullable) entkoppelt Bearer-Quelle von Besitzer — der Service lädt den Bearer von `BearerUserId ?? UserId`. Admin-Import setzt `UserId`=Admin (Repertoire + Notification beim Admin), `BearerUserId`=Ziel-User; piratechess ist stateless, der gespeicherte Bearer des Ziel-Users genügt.

**Abbrechen, Pausieren und Stillstand halten den piratechess-Job mit an** (Codereview 2026-09-29, S2-008): vorher setzte rookhub nur den DB-Status und hörte auf zu pollen, piratechess holte den ganzen Kurs trotzdem weiter über die VPN-IP (und der Abruf zählte nicht gegen das Tageslimit). `ChessableProxyService.CancelCourseJobAsync` schickt `DELETE /api/chessable/direct/course/{jobId}` (Dienst-Schlüssel am typisierten Client), best effort: 5 s Timeout (`CancelTimeout`), 404 (Job weg) und 405 (älterer piratechess ohne den Endpunkt) still, andere Fehler nur geloggt — der Abbruch in rookhub hängt nie daran. Aufgerufen (1) von der Poll-Schleife, sobald sie einen fremden Status sieht (Nutzer/Admin bricht ab oder pausiert), (2) bei ECHTEM Stillstand (`FetchStallPolls`, der nächste Versuch bekommt 404 und startet einen neuen Job; nach der Absolut-Grenze `FetchMaxPolls` läuft ein Job, der noch Fortschritt macht, bewusst weiter), (3) bei den internen Pausen (Tageslimit, Bearer gesperrt), falls vom vorigen Versuch noch ein Job läuft, und (4) von `POST imports/{id}/cancel|pause` bzw. `admin/imports/{id}/cancel` über `ChessableImportQueueService.CancelIdleFetchJobAsync` — aber nur, wenn dieser Prozess den Import NICHT gerade treibt (`IsDrivenLocally`): sonst kreuzte sich der Abbruch mit dem nächsten Poll (Job weg → null → die Schleife startete einen neuen), und die Schleife hält den Job ohnehin selbst an. `FetchJobId` bleibt stehen. Deploy-Reihenfolge: piratechess zuerst.

**Wem gehoert der erste Zug einer Linie? (`[ChessableColor]`, 0.482.0)** Chessables Partie-Kurse
stellen die Aufgabe oft als „der Gegner hat gerade 10…Sd4 gespielt, widerlege das" — der erste Zug
der Linie gehoert dann dem GEGNER und wird vorgespielt. Im REPERTOIRE-Modus („None") schreibt
piratechess aber bewusst keinen `[%tqu]`-Marker, und ohne den galt beim Umwandeln in einen Kurs jede
Linie als „ab der FEN loesen" (`StartPly = -1`) — im Kurs stand die falsche Seite am Zug (gemeldet
2026-09-18). piratechess gibt die Solverfarbe deshalb ab v1.0.46 in JEDEM Modus als Header mit, und
`PgnImportService.StartPlyFromSolverColor` leitet daraus den Trainingsstart ab. Fuer FRUEHER geholte
Repertoires holt `CourseService` die Farbe beim Umwandeln aus dem geteilten Linien-Cache
(`GetCachedLinePgnsAsync` im Modus `FirstKeyMove`, siehe `ChessableTrainingStart`); ist piratechess
nicht erreichbar, bleibt es beim bisherigen Verhalten. `CoursePgnExporter` schreibt `[ChessableOid]`
und den `[%tqu]`-Marker mit, damit Kurs → Repertoire → Kurs die Extension-Verknuepfung UND den
Trainingsstart behaelt.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/chessable/credentials` | Status + maskierter Bearer (`{ hasCredentials, maskedBearer }`) |
| POST | `/api/chessable/credentials` | Bearer setzen/überschreiben `{ bearer }` |
| DELETE | `/api/chessable/credentials` | Bearer löschen |
| POST | `/api/chessable/test` | Bearer-Validität + Kursanzahl (`{ uid, courseCount }`) |
| GET | `/api/chessable/courses` | Liste der Kurse des Users (`[{ bid, name }]`) |
| GET | `/api/chessable/admin/imports` | **Admin**: alle Importe ALLER User (Verlauf, max. 200, neueste zuerst) inkl. `username`/`createdAt`/`completedAt` + globaler Queue-Position |
| GET | `/api/chessable/admin/active` | **Admin**: nur aktive (laufende/pausierte) Importe aller User — fürs Dashboard-Widget |
| POST | `/api/chessable/admin/imports/{id}/cancel` | **Admin**: bricht einen wartenden/laufenden/pausierten Import eines beliebigen Users ab (`Error` = „Vom Admin abgebrochen"); 404 unbekannte Id. Läuft auch mit `Chessable:Enabled=false` |
| GET | `/api/chessable/admin/credentialed-users` | **Admin**: User mit hinterlegtem Bearer (Auswahl für „Kurse von Usern holen") |
| GET | `/api/chessable/admin/users/{userId}/courses?refresh=` | **Admin**: Kursliste eines Users (mit dessen Bearer; Import-Status gegen die eigenen Admin-Importe markiert) |
| POST | `/api/chessable/admin/users/{userId}/import/{bid}` | **Admin**: lädt Kurs `{bid}` eines Users ins EIGENE Admin-Konto — als Repertoire ODER Buch (`{ name?, target? }`; `target` "repertoire"/"book", Default "repertoire"). Import-Besitzer = Admin (`UserId`), Bearer vom Ziel-User (`BearerUserId`). 404 unbek. User, 400 wenn Ziel-User keinen Bearer hat / `target` ungültig |

### Turnierverlauf (auth)
Welche Turniere ein Spieler gespielt hat, welche noch kommen, und in den gespielten Punkte, Platz
und Performance-Rating. Umschaltbar auf Freunde.

**Zwei Quellen, zwei Kostenklassen** (gemessen 2026-09-07 an einem echten Konto): die
chess-results-SPIELERSUCHE liefert in EINEM Abruf ALLE Teilnahmen eines Spielers — vergangene und
kuenftige, je Zeile mit Datum, Platz, Runden-/Teilnehmerzahl und der STARTNUMMER (die steht in
keiner Spalte, nur im Link auf den Namen). Punkte, Performance-Rating und Elo-Aenderung stehen
dagegen nur auf der SPIELERKARTE (`art=9&snr=`), und die kostet EINEN Abruf je Turnier. Bei dem
gemessenen Konto: 23 Turniere, 12 davon gespielt — also 1 + 12 Abrufe fuer die vollstaendige
Historie, rund zwanzig Sekunden hinter dem Rate-Limiter des Crawlers.

Daraus die Aufteilung im `TournamentHistoryService`: die LISTE wird beim Aufruf geholt, wenn sie
aelter als `ListTtl` (12 h) ist — ein Abruf, die Ansicht steht damit sofort mit Termin und Platz.
Die KARTEN laufen ueber die `IBackgroundTaskQueue` nach (`MaxCardsPerRequest` = 25 je Aufruf); ein
abgeschlossenes Turnier aendert sich nie wieder, der Zwischenspeicher gilt also fuer immer. Die
Antwort nennt `pending`, damit die Ansicht sich selbst nachladen kann statt eine halbe Tabelle als
endgueltig auszugeben. Ein KUENFTIGES Turnier bekommt gar keinen Kartenabruf (kein Ergebnis, Platz
in der Trefferliste „-") — das sparte 11 der 23.

**Der Zwischenspeicher haengt am SPIELER, nicht am Konto** (`PlayerTournamentResult.PlayerKey` =
`fide:1693034` bzw. `cr:144749`): die Historie ist fuer jeden dieselbe, ein Freund soll denselben
Speicher benutzen. Die FIDE-Kennung hat Vorrang, weil bei einem AUSLANDS-Turnier in der
chess-results-Ident-Spalte „0" steht und dann allein sie die Identitaet traegt. Ohne Nachnamen im
Profil gibt es keinen Verlauf (Status `noName`) — die Spielersuche sucht ueber den NAMEN, es gibt
dort keine Suche ueber eine Nummer. Ohne Kennung gilt der Name allein, und dann sieht man
Namensgleiche mit; die Auswahl markiert das (`exact: false`).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/tournament-history?userIds=` | Verlauf; ohne Parameter der eigene. Fremde Konten muessen **angenommene Freunde** sein (sonst 403, fuer die ganze Anfrage — ein still uebersprungenes Konto waere eine Luecke ohne Erklaerung); max. 20 je Aufruf. Je Konto `status` (`ok`/`noName`/`sourceUnavailable`), `entries` und `pending` |
| GET | `/api/tournament-history/friends` | Welche Freunde ueberhaupt einen Verlauf haben (Nachname im Profil), je mit `exact` — traegt das Profil eine Kennung? |
| GET | `/api/tournament-history/tracked` | Die verfolgten Spieler dieses Kontos (siehe unten) |
| POST | `/api/tournament-history/tracked` | Einen beliebigen Spieler verfolgen `{ lastName, firstName?, fideId?, chessResultsId?, displayName? }` — das, was `GET /api/profile/player-search` zurueckgab. **Idempotent**: derselbe Spielerschluessel gibt den vorhandenen Eintrag zurueck (kein 409 — die Oberflaeche muesste sonst einen Fehler zeigen, wo nichts fehlt). 400 ohne Nachnamen (min. 2 Zeichen) und ab `MaxTracked` (20) Eintraegen, dann mit `limit` im Rumpf |
| DELETE | `/api/tournament-history/tracked/{id}` | Nicht mehr verfolgen (404 bei fremdem Eintrag). Der geholte Verlauf BLEIBT — er gehoert dem Spieler, nicht dem Reiter |
| GET | `/api/tournament-history/tracked/{id}` | Der Verlauf eines verfolgten Spielers (`PlayerHistoryDto` mit `userId: 0`). Eigener Endpunkt statt eines Parameters an `/api/tournament-history`: dort ist die Zahl ein KONTO, hier ein Eintrag der eigenen Liste |

Sichtbarkeit wie bei `/api/friends/{userId}/stats`: die Daten sind auf chess-results oeffentlich,
die VERKNUEPFUNG von Konto und Spielerkennung ist es nicht (eine anonyme Profil-Sicht mit den
Kennungen gibt es bewusst nicht — `GET /api/profile/{username}` ist seit A2-008 entfernt —, und dabei bleibt es).

**Verfolgte Spieler: der Verlauf haengt nicht mehr an KONTEN** (`TrackedPlayers`, seit 0.463.0).
Bis hierher gab es genau zwei Quellen fuer einen Reiter — das eigene Konto und angenommene
Freunde. Die Leute, deren Ergebnisse man tatsaechlich verfolgt, haben aber meist gar kein Konto
hier: das eigene Kind, ein Vereinskamerad, der Gegner der naechsten Runde. Sie einzuladen, damit
man ihre auf chess-results OEFFENTLICH stehenden Turniere sehen kann, ist keine Loesung.

Gespeichert wird eine **SUCHE, kein Personendatensatz**: genau die vier Felder, aus denen
`IdentityOf` einen Spielerschluessel baut (Nachname, Vorname, FIDE-Nummer, chess-results-Nummer).
Damit faellt der Verlauf selbst in denselben Zwischenspeicher wie der eines Kontos — wer denselben
Spieler verfolgt wie jemand anderes, loest keinen zweiten Abruf aus. Angelegt wird aus der
bestehenden Spielersuche (`GET /api/profile/player-search`, chess-results UND FIDE): die
Kennung kennt niemand auswendig, und ohne sie zeigt der Verlauf Namensgleiche mit (`exact:
false`, die Ansicht sagt es).

Zwei Dinge, die dabei nicht kippen duerfen: (1) **Verfolgte laufen im naechtlichen Durchgang MIT**
(`RefreshAllAsync` nimmt ihre Identitaeten zu denen der Profile) — sonst stuende ihr Verlauf nur
so weit, wie ihn jemand durch Ansehen gefuellt hat (gedeckelt auf `MaxCardsPerRequest` je Aufruf),
und bei einem Vielspieler bliebe die Tabelle dauerhaft halb leer. (2) Die Liste ist auf
`MaxTracked` (20) gedeckelt und wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) — jeder
Eintrag kostet den Nachtlauf mindestens einen Seitenabruf, eine Liste ohne Deckel waere ein Weg,
den Crawler mit einem einzigen Konto auszulasten.

**Der Verlauf entsteht im HINTERGRUND** (`PlayerHistoryScheduler`, 04:30 UTC nach dem
Verzeichnis-Sweep, plus ein Lauf zehn Minuten nach dem Start): `RefreshAllAsync` frischt jede
Identitaet mit Nachnamen auf und holt die fehlenden Seiten sequenziell. `PlayerHistory:MaxCardsPerRun`
(200) deckelt die SUMME der Abrufe eines Laufs (Karten + Bedenkzeiten), damit ein Vielspieler die
uebrigen Konten nicht aushungert; die LISTEN laufen auch nach dem Deckel weiter, denn sie machen
neue Turniere ueberhaupt sichtbar. Vorher entstand der Verlauf nur beim Ansehen (25 Karten je
Aufruf, Nachfragen rund eine Minute) — wer die Seite schloss, liess den Rest liegen.

**Laufende Turniere haben einen Zwischenstand** (0.552.0, `NeedsCard`): Ende heute oder später →
Karte holen, danach alle `RunningCardTtl` (2 h), sobald Partien draufstehen, sonst alle
`UpcomingCardTtl` (20 h); vorbei → einmal neu, wenn die Karte noch WÄHREND des Turniers geholt
wurde (`CardFetchedAt`-Datum ≤ Ende). Den Beginn kennt die Trefferliste nicht (eine Liga „läuft"
über Monate) — ob gespielt wird, sagt die Karte; die Ansicht stellt solche Zeilen unter „Läuft
gerade" (`running()` = nicht vorbei UND `hasResult`). Der Nachtlauf filtert dafür im Speicher
(dieselbe Methode), vorgefiltert je Spieler in der Datenbank.

**Danach holt derselbe Lauf die GANZEN Turniere** (`CrawlHistoryTournamentsAsync`, seit 0.551.0):
Teilnehmer und Paarungen standen vorher erst nach einem Klick im Verlauf bereit („wird gerade
geholt", bis zu zwei Minuten). Reihenfolge: zuerst LAUFENDE (Ende zwischen −3 und +14 Tagen) —
die werden jede Nacht neu angefordert, auch wenn sie schon da sind —, dann fehlende, neueste
zuerst. `PlayerHistory:MaxTournamentCrawlsPerRun` (40, 0 = aus) deckelt die Anforderungen eines
Laufs (eine Olympiade sind ~15 Seiten). `HistoryTournamentCrawls` merkt sich je Turnier, ob es
beim Crawler steht (`FoundAt` → nie wieder nachfragen, ausser laufend) und wie oft es erfolglos
angefordert wurde (Pause 7 Tage, nach 3 Versuchen aufgegeben) — sonst fraessen dauerhaft
scheiternde Abrufe jede Nacht den Deckel.

**Auto-Favoriten: man selbst, Freunde UND Verfolgte** (`AutoSubscriptionService.AutoFavoritePlayersAsync`,
seit 0.551.0). Laeuft beim Oeffnen eines Turniers (`GET /api/tournament-favorites?tournamentId=`)
und nachts fuer alle laufenden/kommenden Abos (vorher nur fuer Abos OHNE jeden Favoriten — ein
spaeter angemeldeter Freund bekam dort nie seinen Stern). Ein selbst entfernter Spieler-Stern
landet in `TournamentFavoriteDismissals` und wird nicht wieder gesetzt; wer ihn neu setzt, loescht
die Zeile. Abgleich: tragen BEIDE eine FIDE-ID, entscheidet sie allein (verschieden = nicht der
Kandidat, auch bei gleichem Namen); sonst Nachname exakt + erstes Vornamens-Token.

**Ein Kartenabruf haengt am TERMIN, nicht am Platz.** Frueher stand dort `Rank is not null`, weil
ein kuenftiges Turnier in der Trefferliste auf „-" steht — dasselbe „-" steht dort aber auch bei
jedem MANNSCHAFTSturnier (chess-results weist in der Spielersuche keinen Einzelplatz aus). Am
echten Konto waren das acht von elf offenen Zeilen (Ligen), deren Spielerkarte Punkte und
Performance sehr wohl kennt. Geholt wird ab dem Tag NACH dem Ende, sonst friert ein Zwischenstand
ein; und `MergeAsync` setzt den Platz nur, solange keine Karte da ist (der Kartenwert ist der
genauere).

**Die BEDENKZEIT ist eine zweite Seite** (Crawler `GET /api/tournament-search/tournament-info?id=`):
die Spielersuche fuehrt sie nicht, und ohne sie stehen eine Blitz- und eine
Turnierschach-Performance in derselben Spalte, als waeren sie vergleichbar. Sie landet in
`TournamentTimeControls` (je Turnier einmal) und reist als `speed` mit jedem Verlaufs-Eintrag mit.

**Zwei Fallen, die dabei live aufgetreten sind:**
1. **Ein GET liefert die Turnierdetails NICHT.** chess-results blendet sie bei allem, was laenger
   als fuenf Tage vorbei ist, hinter einem Knopf aus („all links for tournaments older than 5 days
   are shown after clicking the following button") — dahinter ein ASP.NET-Postback auf
   `cb_alleDetails`. Ohne ihn kommt die Startrangliste zurueck und jedes Feld ist `null`.
2. **Das Label traegt die KLASSE**: „Time control (Standard)", nicht „Time control". Diese Angabe
   schlaegt die Ableitung aus dem Freitext (`TournamentSpeedClassifier` rechnet nur, wenn sie
   fehlt) — „90 Min. / 40 Zuege + 30 Min. + 30 Sekunden ab Zug 1" richtig zu addieren ist Raten.

**JEDER gecrawlte Bestand traegt eine FASSUNG, nicht nur einen Zeitstempel.** „Geprueft am 15:50"
sagt NICHT, WOMIT geprueft wurde: faellt ein Parser-Fehler spaeter auf, ist der Vermerk eine Luege,
die jede Wiederholung verhindert — und der Behelf waere ein manueller Schalter, den jemand kennen
und ausfuehren muss. Steht die Fassung unter der aktuellen, holt der naechtliche Durchgang den
Eintrag genau EINMAL nach.

| Datenart | Fassung | Erhoehen, wenn sich aendert |
|---|---|---|
| Spielerkarte | `TournamentHistoryService.CurrentCardVersion` | die Karten-Felder oder ihr Parser |
| Bedenkzeit | `TournamentHistoryService.CurrentTimeControlVersion` | der Weg zu den Turnierdetails oder ihr Parser |
| Rundenplan | `TournamentRoundPlanService.CurrentVersion` | `FindTableByHeaders` / `ParseRoundPlanAsync` |
| Spielort ueber Vereinsnamen | `VenueDisambiguationService.CurrentVersion` | die Aufloesungs-Regel |
| FIDE-Details | `FideEventDetailService.CurrentVersion` | `ParseEventAsync` oder der Weg zum Fragment |
| Polen-Detailseite | `ChessArbiterDirectorySweepService.CurrentDetailVersion` | `ParseDetail` im Crawler liest mehr oder anderes |

**Eine Fassung JE DATENART, keine globale Crawler-Version**: ein Fix am Rundenplan-Parser sagt
nichts ueber die Spielerkarte aus, und eine globale Zahl holte bei jedem Crawler-Release Tausende
Seiten neu (bei ~6 s je Abruf hinter dem Rate-Limiter: Stunden).

**`RoundPlanVersion` wird MIT `RoundPlanCheckedAt` geleert**, wenn der Sweep eine Terminaenderung
sieht: die beiden beantworten verschiedene Fragen („fuer diesen Termin schon nachgesehen" und „mit
welchem Parser"), und eine Fassung ohne Vermerk behauptete etwas ueber einen Abruf, den es nicht
mehr gibt.

Die drei Belege, an denen das Muster entstand: der Bestand haette die nachtraeglich ergaenzte
Partienzahl NIE bekommen; ein Postback-Fehler waere als „dieses Turnier nennt keine Bedenkzeit"
eingefroren; und 452 Eintraege trugen einen Rundenplan-Vermerk aus der Zeit vor der
Parser-Reparatur (tnr1438343: null gespeicherte von neun abrufbaren Terminen).

**Die PARTIENZAHL kommt von der Spielerkarte** (`GamesPlayed`), nicht aus der Rundenzahl: in einer
Liga wird ein Spieler an einem TEIL der Termine aufgestellt, die Trefferliste meldet trotzdem alle
elf Runden. Gezaehlt werden die Kartenzeilen MIT Gegner — ein Freilos ist keine Partie.

### Turnier-Abos + Favoriten + Monitor (auth)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET/POST/DELETE | `/api/subscriptions[/{id:int}]` | Abonnierte Turniere verwalten |
| DELETE | `/api/subscriptions/by-tournament/{crawlerTournamentId}` | Abo ueber die TURNIER-Nummer loesen statt ueber die Abo-Id — die Kurzansicht auf Karte, Liste und Kalender kennt das Turnier, nicht das Abo. **Idempotent** (204 auch ohne Abo): der Merken-Knopf ist ein Umschalter, und „war schon nicht gemerkt" ist kein Fehlerfall. Literal-Route VOR `{id:int}` |
| GET/POST/DELETE | `/api/tournament-favorites[/{id}]` | Favoriten verwalten |
| GET/POST/DELETE | `/api/tournament-monitors/{tournamentId}` | Per-Turnier-User-Einstellungen + Runden-Monitor (Round-Watch, Auto-Subscribe). POST: Rate-Limit `user-crawl`; höchstens 10 aktive Monitore je Konto (`MaxActiveMonitorsPerUser`, sonst 409 — auch beim Wiederbeleben eines abgelaufenen Monitors; einen aktiven verlängern geht immer, abgelaufene zählen nicht) |

**Eine Kennung je Turnier** (Codereview 2026-09-29, A5-001): `POST /api/subscriptions` speichert IMMER die
chess-results-Nummer — eine Crawler-DB-Id löst der Crawler auf, ein Alt-Abo unter der DB-Id wird umgeschlüsselt.
Runden-Monitor und Abo-Refresh lösen die Kennung vor jedem Crawl über `CrawlQueueClient` auf.
**Runden-Monitor** (A5-007): `RoundMonitorService` prüft je Turnier (`CrawlerTournamentDbId`) einmal je Durchlauf und
meldet nur Runden über `LastKnownRounds` hinaus — jede Runde genau einmal. Crawl-Aufträge der Hintergrunddienste
laufen über `CrawlQueueClient.RequestAsync`; 409 vom Crawler heißt „läuft schon", kein Fehler.

### Turnierverzeichnis / Turnierkalender (lesen ohne Anmeldung, schreiben mit Konto)
Gefuellt vom naechtlichen Sweep der chess-results-Turniersuche (`TournamentDirectoryScheduler`,
03:00 UTC; Nachbarlaender taeglich, uebrige Foederationen rotierend). Rein lesend — hier wird
nichts gecrawlt.

**Ohne Anmeldung** (seit 0.643.0): Suche, Karte, Kalender, Einzelturnier und die beiden Orts-Endpunkte sind
`[AllowAnonymous]` mit der Policy `directory-read` (`RateLimitPartitions.DirectoryRead`, 120/min je Konto bzw. IP,
mal `RateLimitScale`). Was am NUTZER haengt, faellt fuer Gaeste still weg statt zu scheitern: keine ausgeblendeten und
keine gemerkten Turniere (`IgnoredIdsAsync`/`SubscribedIdsAsync` liefern leer — `GetUserId()` wirft ohne Anmeldung und
waere ein 500), kein `ForUserId`. Ein `profileId` ohne Anmeldung ist ein **400**, nie das Profil eines anderen.
Ausblenden, Melden, „Mein Turnier fehlt" und die Suchprofile bleiben `[Authorize]`
(`PersonalActions_StillRequireAnAccount_ReadsDoNot` prueft die Attribute).

> **Abschaltbar: `TournamentDirectory:Enabled=false`** (`TOURNAMENT_DIRECTORY_ENABLED=false`,
> Vorgabe an). Dann laeuft weder der naechtliche Durchgang noch der Aufhol-Lauf nach einem
> Neustart — die `/api/admin/tournament-directory/...`-Endpunkte bleiben aber vollstaendig
> bedienbar. Dasselbe fuer die Spielerkarten des Turnierverlaufs ueber
> `PlayerHistory:Enabled=false` (04:30 UTC). **Auf DEV seit 2026-09-10 beides abgeschaltet**: dort
> stoert ein Sweep zur Unzeit die Messung, an der gerade jemand arbeitet, und die Laeufe werden
> ohnehin von Hand angestossen. Prod bleibt unveraendert an.

**Verortung (`GeocodingService`, ueberarbeitet in 0.418.0).** Der Spielort ist Freitext und nennt
bei Ligen MEHRERE Orte. Die Reihenfolge:

1. **Postleitzahl** — auf wenige Kilometer genau. Mehrere weit auseinanderliegende PLZ = mehrere
   Spielorte. Dieser Weg laeuft VOR der Zerlegung, weil Adressen dieselben Trennzeichen benutzen
   („Halle 1, Eichetstrasse 29, 5020 Salzburg" ist EIN Ort, nicht drei). Der Ortsname des Treffers
   muss im Text vorkommen — sonst gewinnt eine Hausnummer, die wie eine PLZ aussieht. Als
   Bestaetigung zaehlt auch ein WORTANFANG ab 4 Zeichen: chess-results schreibt „9300 St.Veit",
   im Lexikon steht „St. Veit an der Glan".
2. **Ortsnamen je Abschnitt** (`GeoTextNormalizer.VenueSegments`, Trenner `, ; / und &`). Ohne die
   Zerlegung bildete die Kandidatenerzeugung Wortfolgen ueber das Komma hinweg, und weil laengere
   Wortfolgen kuerzere schlagen, gewann in „Mayrhofen, St.Veit" das „st veit" — der Pin sass
   250 km entfernt in Tirol, obwohl der erste Ort im Text Mayrhofen ist.

   **Adresse oder Ortsliste? Eine ZIFFER im Text entscheidet das** (0.419.2). Eine Liste von
   Spielorten nennt Ortsnamen („Mayrhofen / St. Veit/Glan", „Bad Haering/Schwaz/Jenbach/Absam/
   Kufstein"), eine Adresse hat eine Hausnummer. Am Dev-Stand nachgemessen: von 262 als mehrortig
   erkannten Eintraegen hatten **191 eine Ziffer und waren durchweg Adressen**, die die Zerlegung
   zerschnitten hat („Festsaal der Gemeinde Schwarzach, Marktplatz 4, Schwarzach" wurde zu ZWEI
   Spielorten desselben Ortes). Am haertesten traf es BRA und ARG — die zwei groessten
   Foederationen im Bestand, fuer die keine Postleitzahlen importiert sind, sodass der PLZ-Weg
   dort nie greift. Mit Ziffer gilt deshalb der LETZTE Treffer als der EINE Spielort: vorn stehen
   Gebaeude und Strasse, der Ort weiter hinten.
3. **Regionsmitte** aus der Bundesland-Spalte.

**Vereinsnamen als letzter Entscheider (`VenueDisambiguationService`, 0.419.0).** Es gibt einen
Fall, den keine Namensregel loesen kann: die Abkuerzung im Ortstext ist ZUFAELLIG exakt der Name
eines anderen Ortes. Nachgestellt an tnr1405166 — Ortstext „Mayrhofen, St.Veit", im Lexikon heisst
genau ein Eintrag „St. Veit" und der liegt in TIROL; gemeint ist laut Ausschreibung „St. Veit an
der Glan" in Kaernten, 250 km entfernt. Das sieht nicht einmal mehrdeutig aus. Die Vereinsliste des
Turniers nennt dagegen „SV ASKOE St. Veit/Glan".

Der Dienst erweitert die Kandidaten deshalb auf laengere Namen (alles, was mit dem gesuchten
beginnt) und setzt einen Pin **nur mit Beleg**: ein Vereinsname muss ein UNTERSCHEIDENDES Wort des
laengeren Namens als GANZES Wort enthalten („glan" — nicht als Teil von „Glanegg"). Fuellwoerter
(an, der, sankt, bad, neu …) zaehlen nicht, Gleichstand zwischen verschiedenen Orten entscheidet
nichts. Ohne Beleg bleibt alles, wie es war. Ergebnis: `GeoSource.TeamHint`.

**Ohne Pin zuerst**: der Topf enthaelt Eintraege ganz ohne Koordinaten (dort ist alles zu gewinnen) und schon
gepinnte, bei denen der Vereinsname nur KORRIGIERT — am 2026-09-09 auf Dev 153 gegen 919. Allein nach Termin
sortiert kamen die 153 verstreut dran, bei 50 Abrufen je Nacht also ueber Wochen.

Kosten: EIN Seitenabruf je Turnier (Crawler-Endpunkt `GET /api/tournament-search/teams?id=`,
zustandslos). Deshalb gedeckelt — `TournamentDirectory:DisambiguationBatchSize` (Vorgabe 50, 0 =
aus) je Nacht nach dem Sweep, und `TournamentDirectoryEntry.TeamHintCheckedAt` verhindert, dass
dieselben Seiten wieder geholt werden. Von Hand: `POST /api/admin/tournament-directory/disambiguate?limit=`.

**Mehrdeutige Namen bekommen KEINEN Pin.** Liegen die gleichnamigen Kandidaten weniger als 5 km
auseinander (24 Wiener PLZ), ist die Wahl gleichgueltig und die Einwohnerzahl entscheidet wie
bisher. Sonst entscheidet die **Turnierdichte**: wie viele Turniere mit VERLAESSLICHER Verortung
(PLZ oder von Hand) liegen im Umkreis von 12 km? Wo ein Schachklub Turniere austraegt, stehen
mehrere. Bewusst nur verlaessliche Pins — zaehlte man alle mit, bestaetigten die falschen Pins sich
selbst (an „Baernbach" nachgestellt: alle Pins waehlen den falschen Ort, verlaessliche den
richtigen). Bleibt es unentschieden, gibt es keine Koordinaten und `GeoSource.Ambiguous` als
Vermerk fuer die Arbeitsliste. Gemessen am Dev-Stand vor der Aenderung: 171 Eintraege mit
Kandidaten > 50 km auseinander, davon 29 mit nachweislich falschem Pin (bis 489 km, „Muenster"
gibt es 19-mal).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/tournament-directory?from&to&lat&lon&radiusKm&fed&speed&q&weekendOnly&minPlayers&profileId&kinds&ageGroups&genders&adultsOnly&hideLeagues&includeImplausible&page&pageSize` | Turnierliste; Umkreis via Bounding-Box (SQL) + Haversine (C#). `q` sucht in Name, Ort UND Veranstalter. Die fuenf Publikums-/Formatfilter binden als EIN Objekt (`DirectoryAudienceQuery`) und gelten fuer Liste, Karte und Kalender gleich: `kinds` individual/team/unknown — **„individual" schliesst `unknown` MIT ein**: die Quelle sagt nur „ist MANNSCHAFTS-Turnierart", alles andere ist Einzel, und `unknown` heisst „Abfrage lief hier noch nicht / fiel aus" (kein eigener Fall fuer den Suchenden; als eigener gefuehrt lieferte „Einzel" eine halb leere Liste). In der SPALTE bleibt `Unknown` stehen, damit ein Netzausfall den Bestand nicht auf Einzel umschreibt, `ageGroups` u8…u20/youthUnspecified/senior (**Ueberschneidung**, nicht Gleichheit — „u12" findet die U8-U18-Meisterschaft), `genders` open/female/male, `adultsOnly` (kein JUGENDmerkmal; Senioren bleiben sichtbar), `hideLeagues`. Ein unbekannter Wert ist ein **400**, kein stilles Ignorieren. **Reihenfolge mit `from` (UX-039):** erst was im Zeitraum BEGINNT (nach Start), dann als Block was schon laeuft (`ongoing: true`), zuletzt Eintraege ohne Beginn; letzter Schluessel die Id (Seiten ueberschneidungsfrei), gilt auch im Umkreis. **Unplausible Laufzeiten** (> 1 Jahr UND ohne Spieltermine oder schon > 1 Jahr vor `from` begonnen, `TournamentDirectoryQueryService.Plausible`) sind in Liste, Karte und Kalender standardmaessig AUSGEBLENDET; `includeImplausible=true` zeigt sie mit `implausible: true`. Die Detailseite filtert nicht |
| GET | `/api/tournament-directory/map?bbox=minLat,minLon,maxLat,maxLon` | Kartenmarker im Ausschnitt (gedeckelt, `limit` Vorgabe 2000, höchstens 5000 Zeilen); Antwort `{ items, truncated }` — `truncated` = es fehlen die spätesten Turniere (Codereview F6-006, VERTRAGSÄNDERUNG: vorher eine nackte Liste) |
| GET | `/api/tournament-directory/calendar?year&month` | Ein Monat: `tournaments` (jedes Turnier EINMAL) + `days` (je Tag nur die Nummern der laufenden). Mehrtaegige Turniere stehen an JEDEM ihrer Tage — voll ausgeschrieben waren das 5962 Eintraege fuer 200 Turniere, also ~3 MB je Monat; das Frontend setzt es in `expandCalendar` wieder zusammen. Höchstens 5000 Turniere je Monat (`CalendarMaxTournaments`), darüber `truncated`; Einträge ohne jeden Termin fehlen (Codereview A5-002 — vorher still bei 200 gekappt) |
| GET | `/api/tournament-directory/{publicId}` | Einzelnes Turnier (auch abgesagte). `publicId` ist die IDENTITAET; erlaubte Formen (chess-results-Nummer, `f<FIDE-Nummer>`, Kürzel der Verbandsquellen …) stehen an EINER Stelle in `Services/DirectoryPublicId.cs` — dieselbe Prüfung für Detailseite, Ausblenden und Melden (Codereview A5-003) |
| GET | `/api/tournament-directory/places?q=` | Ortsvorschlaege aus dem Gazetteer (PLZ oder Name) |
| GET | `/api/tournament-directory/places/nearest?lat&lon` | Naechstgelegener Gazetteer-Ort zu Koordinaten — fuer das Ortsfeld, wenn der BROWSER den Standort liefert (die Koordinaten des Nutzers verlassen den Server nicht). 204, wenn im Umkreis von 200 km kein Ort im Lexikon liegt |
| POST | `/api/tournament-directory/{publicId}/report` | „Falsches Event melden" — Rueckmeldung zu einem Eintrag `{ message?, location?, kind?, ageGroups?, gender?, speed?, isLeague?, namePattern?, sourceLink? }`, ALLE Felder freiwillig. Landet im bestehenden **Admin-Nachrichtenkanal** (`AdminMessageService.SendFromUserAsync`) statt in einer eigenen Tabelle: dort gibt es Oberflaeche, Glocke und — entscheidend — einen Rueckweg zum Melder. `namePattern` ist die Lern-Frage („bei uns heissen die Jugendturniere Schachrallye") und wandert in die Wortlisten des `TournamentClassifier`. Rate-Limit `user-message` (gemeinsamer Topf mit `POST /api/messages/reply`, Codereview F5-001) |
| POST/DELETE | `/api/tournament-directory/{publicId}/ignore` | Ein Turnier FUER MICH ausblenden bzw. wieder zeigen (idempotent). Es verschwindet aus Liste, Karte und Kalender — und aus der naechtlichen Umkreis-Meldung; nur mit `audience.includeIgnored=true` kommt es mit (und traegt dann `ignored: true`). Die DETAILseite zeigt es immer, dorthin ist man absichtlich gegangen |
| POST | `/api/tournament-directory/suggest-source` | „Mein Turnier fehlt" `{ link, message? }` — Hinweis auf eine noch nicht gecrawlte Quelle. Der **Link ist Pflicht** (nur absolutes http/https): ein Verbandskalender laesst sich zusaetzlich auswerten, eine Aufzaehlung im Freitext nicht. Rate-Limit `user-message` wie „Melden" |
| GET/POST/PUT/DELETE | `/api/tournament-search-profiles[/{id}]` | Gespeicherte Umkreise; steuern Ansicht UND naechtliche Meldung |
| GET | `/api/admin/tournament-directory/status` | Sweep-Zustand je Foederation + Geocoding-Quote |
| POST | `/api/admin/tournament-directory/sweep` | Sweep fuer 1–20 Foederationen sofort ausfuehren |
| POST | `/api/admin/tournament-directory/gazetteer/postal/{iso2}` | GeoNames-PLZ eines Landes importieren |
| POST | `/api/admin/tournament-directory/gazetteer/cities` | GeoNames-Ortsliste (cities15000) importieren |
| POST | `/api/admin/tournament-directory/gazetteer/transcribe` | **Einmalig nach dem Deploy von 0.456.0**: rechnet die zweite Schreibweise (`GeoPlace.NameTranscribed`) fuer den vorhandenen Bestand nach. Rein lokal, kein Netzabruf; mehrfach ausfuehrbar |
| GET | `/api/admin/tournament-directory/ungeocoded` | Eintraege ohne Koordinaten (Arbeitsliste) |
| POST | `/api/admin/tournament-directory/geocode-missing?limit=&force=` | Nicht verortete Eintraege erneut aufloesen — **ohne `limit` den GANZEN Bestand**: der Lauf braucht kein Netz (lokales Lexikon, 6598 Eintraege in 14 s), und ein Deckel liefert KEINE zweite Portion (die Auswahl hat keine Fortschrittsmarke, ein zweiter Aufruf sieht wieder dieselben ersten N — am 2026-09-09 live erlebt). **`force=true`** nimmt auch schon verortete vor — gebraucht, wenn sich die REGELN aendern (der Sweep verortet einen bestehenden Eintrag nur bei geaendertem Ortstext neu, ein Pin aus einer alten Regel bliebe sonst fuer immer). Entfernt dabei Pins, die nach der neuen Regel Rateentscheidungen sind; `GeoSource=Manual`, `SourceProvided` und `TeamHint` bleiben in jedem Fall unberuehrt — der Vereinsnamen-Beleg ist eine Auskunft, die die Namensregel nicht reproduzieren kann |
| POST | `/api/admin/tournament-directory/backfill-sources` | Herkunftsvermerk fuer den Altbestand nachtragen (jeder bestehende Eintrag stammt aus chess-results). Braucht kein Netz; der Sweep tut es von selbst, aber erst nach einer Rotationswoche |
| POST | `/api/admin/tournament-directory/round-plans?limit=&retryEmpty=` | SPIELTERMINE langlaufender Turniere nachtragen — ein Seitenabruf je Turnier (chess-results art=14), gedeckelt. Siehe unten |
| POST | `/api/admin/tournament-directory/fide?years=` | Den FIDE-Kalender sofort lesen (Vorgabe: laufendes Jahr + 2). Ein Seitenabruf je Jahr; neue Ereignisse kommen mit `ChessResultsId = null` dazu, erkannte werden mit dem bestehenden Eintrag verschmolzen |
| POST | `/api/admin/tournament-directory/fsi?months=` | Den Kalender des ITALIENISCHEN Verbands lesen (ein Abruf, alle Felder inline). Siehe „Zusatzquellen" unten |
| POST | `/api/admin/tournament-directory/szs` | Den Kalender des SLOWENISCHEN Verbands lesen (vier Seitenabrufe, PLZ steht in der Liste) |
| POST | `/api/admin/tournament-directory/icu?details=` | IRLAND — 81 kuenftige, 94 % neu; 30 bringen Koordinaten aus der Trefferseite mit |
| POST | `/api/admin/tournament-directory/ffe?months=&details=` | FRANKREICH — 12 Monatsseiten; die Turnierseite je Turnier einmal (Enddatum, Bedenkzeit, PLZ gibt es nur dort) |
| POST | `/api/admin/tournament-directory/sjakk?details=` | NORWEGEN — chess-results kennt fuer NOR null; nur ~25 % verortbar |
| POST | `/api/admin/tournament-directory/chess-scotland?details=` | SCHOTTLAND — 44 bis 2028, Bedenkzeit-Klasse strukturiert; Ort nur bei ~19 % |
| POST | `/api/admin/tournament-directory/frsah` | RUMAENIEN — EIN Abruf, 31 kuenftige samt der Mannschaftsligen |
| POST | `/api/admin/tournament-directory/wcu` | WALES — EIN Abruf, 38 Turniere, 30 mit Postleitzahl |
| POST | `/api/admin/tournament-directory/knsb` | NIEDERLANDE — 177 kuenftige in zwei Abrufen; OHNE Spielort (siehe unten) |
| POST | `/api/admin/tournament-directory/ecf` | Den Kalender des ENGLISCHEN Verbands lesen — 256 kuenftige Turniere, als einzige Quelle MIT Koordinaten (`Located` in der Antwort) |
| POST | `/api/admin/tournament-directory/schachbund` | Die Turnierdatenbank des DEUTSCHEN Schachbunds lesen — zwei Abrufe je Region mit der Wartezeit aus ihrer robots.txt, dauert einige Minuten |
| POST | `/api/admin/tournament-directory/chess-arbiter?details=` | Den Kalender des POLNISCHEN Verbands lesen — 611 kuenftige Turniere in einem Abruf; `details` deckelt die Detailseiten (ein Abruf je noch unbekanntem Turnier) |
| POST | `/api/admin/tournament-directory/cfc` | Den Ankuendigungskalender der CHESS FEDERATION OF CANADA lesen — 171 kuenftige gegen 68 auf chess-results, zwei Abrufe |
| POST | `/api/admin/tournament-directory/chess-cz` | Den Terminkalender des TSCHECHISCHEN Verbands lesen — bringt neben Turnieren die SPIELTERMINE der drei Mannschaftsmeisterschaften mit (`RoundDates` in der Antwort) |
| POST | `/api/admin/tournament-directory/chess-hu` | Den Kalender des UNGARISCHEN Verbands lesen (ein POST beim Crawler; die Quelle braucht dafuer rund 75 Sekunden) |
| POST | `/api/admin/tournament-directory/chess-sk?details=` | Den Kalender des SLOWAKISCHEN Verbands lesen — ein Abruf der Schnittstelle plus einer je Turnier fuer die Detailseite; `details=false` laesst den teuren Teil weg |
| POST | `/api/admin/tournament-directory/classify` | Publikum + Format des GANZEN Bestands aus den Turniernamen neu ableiten (Jugendklasse, Geschlechtsklasse, Liga) — braucht kein Netz. Der Weg, eine nachgeruestete Wortliste im `TournamentClassifier` auf den Altbestand anzuwenden; die Turnier**art** bleibt unangetastet (die kommt aus der Quelle) |
| POST | `/api/admin/tournament-directory/disambiguate?limit=` | Spielort ueber die VEREINSNAMEN aufloesen (Abkuerzungs-Fall, siehe unten) — ein Seitenabruf je Turnier, gedeckelt |
| PUT | `/api/admin/tournament-directory/{id}/coordinates` | Koordinaten von Hand setzen (GeoSource=Manual, ueberlebt den Sweep) |

Die `/api/admin/...`-Routen haengen an der Permission `tournaments.manage`.

**Spieltermine: warum Start und Ende nicht genuegen.** Ein Eintrag traegt Start und Ende, und der
Kalender zeichnet ein mehrtaegiges Turnier an JEDEM Tag dazwischen. Bei einem Wochenend-Open ist
das richtig. Bei einer LIGA ist es falsch: „26.09.2026 bis 17.04.2027" sind elf Runden mit zwei
bis fuenf Wochen Abstand — die Liga stand damit an rund 200 Kalendertagen, an denen nichts
gespielt wird, und verdeckte die Turniere, die es wirklich gibt. Am Dev-Stand gemessen: **610
offene Eintraege laufen laenger als acht Tage, 527 davon ueber 40 Tage.**

`TournamentRoundPlanService` holt die Termine (chess-results `art=14`, ~17 kB — die kleinste
Ansicht, die sie traegt; `art=2` kostet 33 kB, `art=3` 182 kB) und legt sie in
`TournamentDirectoryRounds` ab. Die Auswahl haengt an der **DAUER, nicht am Liga-Merkmal**: ob
etwas eine Liga IST, wird geraten, ob sein Zeitraum luegt, steht fest — mehr als
`MinSpanDays` (8) Tage und mehr als eine Runde. Eine monatelange Vereinsmeisterschaft ist keine
Liga und braucht die Termine genauso. `RoundPlanCheckedAt` verhindert Wiederholungen und wird
**auch bei einem leeren Plan** gesetzt (der haeufige Fall, der sich merken lassen muss); ein
NETZfehler laesst ihn dagegen leer. Der Sweep leert ihn, wenn sich der Termin geaendert hat.
`TournamentDirectory:RoundPlanBatchSize` (Vorgabe 200, 0 = aus) je Nacht.

**`retryEmpty=true`** nimmt auch Eintraege vor, die als geprueft gelten und KEINEN Termin haben.
Gebraucht, weil das Holen selbst kaputt sein kann: chess-results baut sein Layout aus Tabellen, die
Rundenplan-Tabelle steckt drei Ebenen tief, und `FindTableByHeaders` nahm die erste Tabelle, deren
Kopfzeile die Spaltennamen „enthaelt" — `TextContent` ist REKURSIV, also enthaelt schon die
aeusserste Wrapper-Tabelle sie. Ergebnis: leere Liste, ohne Fehler (auf Dev gemessen: 337 geprueft,
0 Termine). Behoben im Crawler (eine Datentabelle ist ein BLATT, `FindTableByHeaders` bevorzugt
Tabellen ohne verschachtelte Tabelle) — aber der Vermerk „geprueft" haette jede Wiederholung
verhindert, deshalb dieser Schalter. Bewusst opt-in: ein Turnier ohne veroeffentlichten Plan ist
der haeufige Fall.

**MIT `retryEmpty` kann `checked` NIE 0 werden** — und damit ist die Abbruchbedingung „wiederholen,
bis 0 kommt" dort unerreichbar. Genau das ist der Punkt des Schalters: ein Turnier ohne Plan bleibt
Kandidat. Am 2026-09-09 gemessen: von 580 Kandidaten bekamen 308 in den ERSTEN ZWEI Durchgaengen
ihren Plan, die restlichen 272 haben auf chess-results keinen — die Durchgaenge 3 bis 11 waren rund
1800 Seitenabrufe fuer null neue Termine (13 min je Durchgang), und ohne Eingriff waeren es 20
geworden. `scripts/directory-runs.sh` bricht deshalb auch ab, wenn `IDLE_ROUNDS` Durchgaenge
(Vorgabe 2) nacheinander keinen ZUGEWINN (`withPlan`) melden; ein Zugewinn setzt den Zaehler
zurueck, `IDLE_ROUNDS=0` schaltet die Regel ab. Fuer den naechtlichen Durchgang gilt das nicht — der
laeuft ohnehin nur eine Charge.

**Zwischengespeichert wird alle `SaveEvery` (25) Turniere** — und im `finally` auch beim Verlassen
eines Abbruchs, dort mit `CancellationToken.None` (ein abgebrochener Token wuerde genau den
Schreibvorgang verhindern, der die Arbeit retten soll). Grund: ein Durchgang ueber 200 Turniere
dauert **rund zwanzig Minuten** — die Seite ist in 25 ms da, den Rest machen der 1500-ms-Limiter
des Crawlers und die VPN-Rotation nach JEDEM Abruf (gemessen ~5,7 s je Turnier). Wurde erst am
Ende geschrieben, verwarf ein Abbruch, ein API-Neustart oder ein Deploy in dieser Zeit alles, und
der naechste Durchgang begann bei denselben Turnieren (auf Dev nachgemessen: nach sechs Minuten
null Spieltermine in der Datenbank). `Checked` im Ergebnis zaehlt bewusst die VERSUCHTEN, nicht
die erfolgreichen — daran haengt die Abbruchbedingung „nochmal starten, bis 0 kommt", und ein
Durchgang mit lauter Fehlschlaegen meldete sonst „nichts mehr zu tun".

Im Kalender gilt: **hat ein Eintrag Spieltermine, zaehlen NUR sie** (`Covers`). Die Termine gehen
als `roundDates` im DTO mit, die Detailseite zeigt sie.

**Ohne Termine gilt der Zeitraum nur, solange er plausibel ist** (0.437.1). Gemeldet an
tnr1474416 („II Vipiteno Chess Festival - 2° torneo rapid", 10min + 5sec): Zeitraum 11.08. bis
20.09., also 41 Kalendertage, an denen der Eintrag alles andere verdeckte. Die Angabe stammt so
von chess-results (per `tournament-info` gegengeprueft), und einen Rundenplan gibt es dort nicht —
die Abfrage liefert eine LEERE Liste, es ist also nicht „noch nicht geholt". Ein solcher Eintrag
steht nur an seinem STARTtag; ihn ganz wegzulassen waere falsch, er findet ja statt.

**Die Grenze haengt an der TURNIERART, nicht nur an der Dauer** (`MaxSpreadDays` 21,
`MaxFastSpreadDays` 8): „laenger als acht Tage" allein traefe auch das ehrliche mehrtaegige Open
(neun Tage, eine Runde pro Tag, kein hinterlegter Plan) — es verschwaende an acht von neun Tagen,
und das faellt weniger auf als der Fehler davor. Ein Schnell- oder Blitzturnier ueber mehr als eine
Woche kann dagegen nicht durchgehend sein. Am Dev-Stand gemessen: 603 der 715 termin-losen
Langlaeufer ziehen sich auf ihren Starttag zusammen, die 112 mehrtaegigen Standard-Turniere
zwischen 9 und 21 Tagen bleiben unangetastet.

**Woher ein Eintrag stammt (`TournamentDirectorySources`).** Der Sweep vermerkt bei jedem
Turnier, auf welcher Seite es gefunden wurde (`NoteSource`) — n-zu-n, weil dasselbe Turnier auf
mehreren steht und es mehr werden.

**Die IDENTITAET eines Eintrags ist `PublicId`, nicht die chess-results-Nummer** (0.428.0). Die
Nummer war gleichzeitig Schluessel, Routen-Parameter, Ausblend- und Melde-Schluessel, Abo und
Crawl-Ziel — ein FIDE-Ereignis hat keine. `PublicId` ist die chess-results-Nummer, wo es eine gibt,
sonst `f<FIDE-Nummer>`; `ChessResultsId` ist **nullbar** und alles, was chess-results wirklich
braucht, ist ausdruecklich darauf abgefragt (Rundenplan, Vereins-Aufloesung des Spielorts, Abo,
Merken, der Link dorthin). Die Migration `AddDirectoryPublicId` traegt die Spalte fuer den
Altbestand VOR dem Unique-Index nach (`UPDATE … SET PublicId = ChessResultsId`) — mit 4930
bestehenden Zeilen und Vorgabe `''` waere sie sonst gescheitert; ein Integrationstest fuehrt genau
diesen Weg vor.

**Der FIDE-Kalender ist die zweite Quelle** (`Services/FideDirectorySweepService.cs`,
Crawler-Endpunkt `GET /api/fide-calendar?year=`). Wichtig ist der RICHTIGE Aufruf: mit
`show=table`/`show=apilist` liefert `calendar_server.php` 661 Ereignisse, **alle aus 2025** — das
war die Grundlage des frueheren Urteils „bringt heute nichts", und es war falsch. Gepflegt wird
`show=showYear&page=<Jahr>` (die Ansicht hinter `majorcalendar.php`): **143 Ereignisse fuer 2026,
139 davon lesbar, und 132 fehlten im Verzeichnis** — Kontinental-/Weltmeisterschaften und
Titelturniere, die nicht auf chess-results ausgeschrieben sind. `cat_filter`/`cat_cont` gehoeren
NICHT in den POST (Antwort 500). Ein Ereignis wird einem bestehenden Eintrag zugeordnet, wenn die
Termine hoechstens `MatchDayTolerance` (1) Tag auseinanderliegen UND die Namen mindestens zwei
unterscheidende Woerter teilen (oder eines plus denselben Ort); sonst kommt es als neuer Eintrag
mit `ChessResultsId = null` dazu. Online-Ereignisse (`ONL`) bekommen keine Koordinaten. Geplant
ueber `TournamentDirectory:FideYears` (Vorgabe 3 Jahre) nach dem naechtlichen Sweep, von Hand
`POST /api/admin/tournament-directory/fide`.

**Die VERBANDSKALENDER sind die dritte Quellenart** (`Services/ExternalDirectorySource.cs` traegt
das Gemeinsame, je Land ein `…DirectorySweepService`). Jede stellt dieselben vier Fragen: kenne ich
das Turnier schon (Termin ±1 Tag + zwei unterscheidende Woerter, ODER — nur chess.sk — die
mitgelieferte chess-results-Nummer, dann exakt), habe ich es selbst schon angelegt
(`<praefix><fremde-id>`), hat die Turniersuche es eingeholt (dann eigenen Eintrag zurueckziehen),
und woher kommt die Angabe (`TournamentDirectorySources`). **Eine Zusatzquelle fuellt nur
LUECKEN** (`FillIfEmpty`) — ersetzte sie vorhandene Werte, entschiede die Reihenfolge der
naechtlichen Durchgaenge, welche Angabe gilt. Die Turnier**art** bleibt dabei immer `Unknown`:
keine dieser Quellen sagt etwas darueber.

| Land | Praefix | Kosten | Was sie kann, was die anderen nicht koennen |
|---|---|---|---|
| Italien (FSI) | `it` | 1 Abruf (1,25 MB) | Von 285 Eintraegen verlinkt KEINER nach chess-results — Italien faehrt auf Vega/vesus. Keine PLZ, dafuer die Provinz (loest „Marino" auf) |
| Slowenien (SZS) | `sl` | 4 Abrufe | Faktor elf gegenueber chess-results; die PLZ steht schon in der TREFFERLISTE. Absagen stehen nur im Namen („ODPADE") — solche Turniere werden nicht angelegt |
| Slowakei (chess.sk) | `sk` | 1 + je Turnier 1 | Die einzige mit angebotener Schnittstelle. Liefert als einzige Anschrift MIT PLZ, Bedenkzeit, Rundenzahl, System und Bedenkzeit-Klasse — aber erst die Detailseite. Nennt bei einem Drittel die chess-results-Nummer selbst. Schulungen/Trainingslager stehen mit drin und bleiben draussen |
| Ungarn (chess.hu) | `hu` | 1 Abruf (POST, ~75 s) | Vor allem VORLAUF: ab November 2026 41 Turniere gegen 5. Nur Name, Termin, Ort — die Detailseite bringt gemessen nichts (46 von 52 Ortsnamen sind im Lexikon eindeutig). Ihr Feld „ifjusagi" heisst NICHT Jugendturnier (77 von 107 „ja") und wird nicht uebernommen |
| Irland (ICU) | `ie<nr>` | 5 + je NEUEM Turnier 1 | 81 kuenftige, 94 % nicht auf chess-results. 30 bringen Koordinaten aus einem eingebetteten `map-data`-Block der TREFFERSEITE mit — die Detailseite liefert KEINE zusaetzlichen, nur den Dedup-Schluessel und die Meldezahl. Die ICU ist gesamtirisch: 11 Turniere liegen in Nordirland, der Eintrag bleibt `IRL`, der Geocoder bekommt `ENG` |
| Frankreich (FFE) | `fr<ref>` | 12 + je NEUEM Turnier 1 | Der groesste Ertrag: von 40 gegengeprueften stehen ZWEI auf chess-results. Enddatum, Bedenkzeit und PLZ gibt es NUR auf der Turnierseite — ohne sie staende jedes Wochenend-Open an einem Tag und ganz Frankreich auf `Speed.Unknown`. Die FFE schreibt das Inkrement mit zwei Apostrophen (`60' + [30'']`), das normalisiert die Quelle vor dem Klassifizierer |
| Norwegen (sjakk.no) | `no`+Hash | 1 + je NEUEM Turnier 1 | chess-results kennt fuer NOR NULL kuenftige Turniere. Der Feed hat kein Ortsfeld; an allen 81 Turnieren nachgemessen (2026-09-09): **17 verortbar (21 %)**, nach einem NO-PLZ-Import **26 (32 %)**. Die Detailseite traegt dazu nur **zwei** bei — sie rechnet sich ueber Veranstalter (52 von 80) und Bedenkzeit (38), nicht ueber den Ort. Ihre FUSSLEISTE nennt die Verbandsanschrift in Oslo: `ParseDetail` liest ausschliesslich die Zeile „Spillsted", eine Suche ueber die ganze Seite pinnte ganz Norwegen nach Oslo |
| Schottland (Chess Scotland) | `sc`+Hash | 1 + bis 43 | 44 kuenftige bis 2028, Bedenkzeit-Klasse STRUKTURIERT. Die Liste nennt keinen Ort; in den Ausschreibungen steht bei 8 von 43 eine Postleitzahl — **die Verortung bleibt schwach** **Vermerkt wird der KURZSCHLUESSEL, nicht der Slug** (seit 2026-09-15): ein Slug mit 71 Zeichen sprengte die 60-Zeichen-Spalte, und die Quelle brach vom 10. bis 15.09. jede Nacht komplett ab. Die 43/44 Altvermerke (Dev/Prod) mit rohem Slug stellt der Lauf selbst um (`MigrateLegacyNotesAsync`) — ohne das haette jeder Eintrag einen zweiten Vermerk bekommen und `HasOtherNoteOfSameKind` jede Zuordnung zu chess-results blockiert. |
| Rumaenien (FRSah) | `ro<nr>` | 1 Abruf | Wie England „The Events Calendar" — aber die Spielstaette steckt schon IM Ereignis, der zweite Abruf entfaellt. Die 5 Eintraege OHNE Spielstaette sind genau die nationalen Mannschaftsligen, und genau die fehlen auf chess-results. `robots.txt` antwortet selbst mit 403 (siehe Doku) |
| Wales (WCU) | `wl`+Hash | 1 Abruf | Die billigste Quelle. 30 von 38 mit vollstaendiger Postleitzahl. KEINE eigene Kennung — sie entsteht aus Termin + Anschrift, bewusst OHNE den Namen (derselbe Veranstaltungsort erscheint als „Best Western", „Bst Western", „Bet Western"; ein korrigierter Tippfehler im Namen darf keine Dublette erzeugen). Weil dieser Schluessel laenger ist als die 60 Zeichen von `TournamentDirectorySource.ExternalId`, vermerkt die Quelle ihren KURZSCHLUESSEL (`PublicIdOf`) — roh gab es jede Nacht „Data too long for column" |
| Niederlande (KNSB) | `nl`+Hash | 2 Abrufe (15 s Pause) | 177 kuenftige gegen 12. Bedenkzeit-Klasse kommt strukturiert aus einer Taxonomie. **Der Spielort fehlt strukturell** — er stuende nur auf der Detailseite, und 177 × 15 s waeren 45 Minuten; bewusst nicht gebaut. Die Eintraege stehen in Liste und Kalender, nicht auf der Karte **Seit 2026-09-14 gesperrt fuer unseren VPN-Ausgang**: statt JSON kommt eine JavaScript-Warteseite (HTTP 200, text/html, „One moment, please..."), von einer privaten IP dieselbe Adresse unveraendert als JSON. Das ist eine IP-Sperre, keine geaenderte Schnittstelle, und sie wird NICHT umgangen; der Crawler meldet sie als 502 mit Auszug (`SourceResponse.EnsureJson`) statt als Parser-Absturz. |
| England (ECF) | `en<nr>` | 12 Seiten, **~200 s** | 278 kuenftige Turniere (gemessen 2026-09-09), **86 % nicht auf chess-results**. Die mit ABSTAND langsamste Quelle, und nicht wegen des Servers: ihre robots.txt verlangt „Crawl delay: 10", das Warten IST die Laufzeit. Sie ist der Grund, warum `TournamentDirectoryService.DefaultCrawlerTimeoutSeconds` 600 s betraegt — mit den frueheren 180 s lief sie in JEDER Nacht in den Timeout, ohne je ein Turnier zu liefern. Die EINZIGE Quelle mit Koordinaten (`geo_lat`/`geo_lng` im Spielstaetten-Endpunkt, 168 von 256) → `GeoSource.SourceProvided`, kein Geocoding. Ihre Schlagworte sind gepflegt: „Meeting" ist kein Turnier, „Online" hat keinen Ort, „Juniors Only" ist eine verlaessliche Jugend-Angabe |
| Deutschland (schachbund) | `de`+Hash | 2 je Region (25) | Ein reines MELDE-System: hier stehen Vereins-Abendturniere, Jugend-Cups, Fernschach, Problemschach und Schach960 — Arten, die chess-results nie fuehrt. Die SEITE traegt die Anschrift (105 von 106), der FEED Rundenzahl und Bedenkzeit; beides wird gebraucht. „europa"/„welt" sind keine Laender und bekommen keine Foederation. Der Slug ist laenger als 60 Zeichen: vermerkt wird der Kurzschluessel (`PublicIdOf`) Vier Regionen der Uebersichtsseite haben KEINEN Feed (schach960, problemschach, blindenschachbund, fernschachbund — gemessen 2026-09-15, beide Adressvarianten 404); sie werden ohne Ausschreibung gelesen (`RegionsWithoutFeed`). Bewusst eine feste Liste: ein 404 auf einer gewoehnlichen Region bleibt eine Warnung. |
| Polen (chessarbiter) | `pl<jahr>-<nr>` | 1 + je NEUEM Turnier 1 | Die ergiebigste Einzelquelle: 611 kuenftige Turniere in EINEM Abruf. Die Liste nennt kein Jahr (kommt aus der Sortierung); die Detailseite bringt Enddatum, Bedenkzeit, Rundenzahl, System — und als einzige Quelle die TEILNEHMERZAHL schon vor dem Turnier. **Nur ~20 % der Turniere haben ueberhaupt eine server-gerenderte Datenseite**, die uebrigen liefern eine JavaScript-Huelle: der Crawler antwortet dort mit **204** (gefragt, nichts da — endgueltig) statt 404 (nicht zu holen — wiederholen), und `ChessArbiterDetailVersion` vermerkt es. Die `Url` im Herkunftsvermerk heisst weiter GELESEN, nicht bloss gefragt |
| Kanada (CFC) | `ca`+Hash | 2 Abrufe | **171 kuenftige gegen 68** — chess-results wird dort kaum als Kalender benutzt. Der Datensatz liegt als STATISCHE Datei `/ext/cfc-data.<hash>.js` (`window.ws_cfc_data`), der Hash wechselt bei jedem Site-Build und wird aus dem HTML gelesen. KEINE Kennung: `oid` ist die Listenposition, also Schluessel aus Termin + Ort + NAME — Termin und Ort allein haben 13 Kollisionen (zwei Turniere am selben Tag in derselben Stadt), und eine Anschrift gibt es nicht. Gefiltert wird ueber die PROVINZ, nicht den Typ (ein FIDE-Turnier in Usbekistan traegt `type=OTB` und nur `prov=FO`). Ort ist eine Stadt ohne PLZ; 15 % der Namen tragen nur EIN unterscheidendes Wort und bekommen daher ihren eigenen Eintrag statt den chess-results-Treffer |
| Tschechien (chess.cz) | `cz`+Hash | 1 Abruf | Klein im Volumen (38 echte Turniere, 13 neu), aber sie liefert SPIELTERMINE: 33 Ligarunden werden zu drei Eintraegen mit je elf Runden — sonst je Turnier ein eigener `art=14`-Abruf. Ergaenzt nur FEHLENDE Runden. Ihre Kennung ist ein bis zu 57 Zeichen langer Slug, deshalb als Kurzwert gespeichert |

**Kein VPN-Ausgang erreicht alle Quellen — deshalb wird gewechselt und wiederholt.** Mehrere
Verbandsseiten sperren ganze Hosting-Netze, und zwar verschiedene: am 2026-09-09 gemessen kam vom
deutschen AirVPN-Ausgang (alle auf M247) keine TCP-Verbindung zu `federscacchi.com` (ITA) und
`frsah.ro` (ROU) zustande, vom niederlaendischen (Global Layer) keine zu `chess.sk` (SVK). Ein
fester Standort tauscht also nur ein Land gegen ein anderes. Der Crawler wiederholt einen
Quellen-Abruf deshalb bis zu fuenf Mal und wechselt zwischen den Versuchen den Ausgang
(`RotateOnConnectFailureHandler`, an allen 16 Quellen-Clients). Wiederholt wird NUR das
Nicht-Zustandekommen der Verbindung — eine Antwort der Quelle, auch 403 oder 500, ist eine Auskunft
und wird durchgereicht. Die Rotation selbst liegt in `VpnReadinessGate` und haelt dabei den
Crawl-Riegel (`CrawlerService.CrawlGate`): waehrend stop→pause→start ist der Tunnel unten.

Vollstaendige Messungen und die Rechtslage je Quelle: `docs/turnierquellen.md`.

**Publikum und Format eines Turniers — was aus der Quelle kommt und was aus dem Namen.**
`Kind` (Einzel/Mannschaft) ist QUELLENDATUM: die chess-results-Turniersuche hat ein Turnierart-Feld
(`combo_art`), und die Arten **2** („Rundenturnier fuer Mannschaften") und **3** („Schweizer System
fuer Mannschaften") sind genau die Mannschaftsturniere. Der Sweep fragt sie deshalb in **zwei
zusaetzlichen Durchgaengen** je Foederation ab (`&art=2`, `&art=3`) — also drei Crawler-Requests
statt einem. Faellt ein solcher Durchgang aus ODER laeuft er in die 2000-Zeilen-Grenze, bleibt die
gespeicherte Art **unangetastet** (`Unknown` heisst „noch nicht geklaert"): ein Netzausfall darf
nicht den halben Bestand auf „Einzel" umschreiben.

`AgeGroups`, `Gender` und `IsLeague` sind ABLEITUNGEN (`Services/TournamentClassifier.cs`, rein
statisch, Wortlisten an EINER Stelle zum Nachruesten). Alter und Geschlecht stehen nirgends als
Spalte — auch nicht auf der Turnierseite — sondern nur im Namen, und der traegt sie erstaunlich
verlaesslich („Landesmeisterschaft U12 weiblich", „UNDER 16 GIRLS"). Zwei Fallen sind dort bewusst
geschlossen und je mit einem Test festgenagelt: **„U2000" ist eine Ratinggrenze, kein Alter**
(hoechstens zwei Ziffern + Wortgrenze), und **„men" steckt in „women" und „Damen", „male" in
„female"** — als Teilzeichenkette gesucht war jedes Frauenturnier gleichzeitig ein Herrenturnier
und damit (beides gesetzt) wieder ein offenes; diese beiden zaehlen deshalb nur als ganzes Wort.
Ein freistehendes „w"/„m" ist in diesen Namen eine GRUPPE („Open Braunau 2026 B") und zaehlt nur
direkt hinter der Altersklasse („U12w"). Liga = Liga-Wort im Namen ODER Mannschaftsturnier ueber
mindestens 35 Tage (eine Saison laeuft Oktober bis April, ein Mannschaftsturnier am Wochenende
einen Tag); die Dauerregel gilt NUR fuer Mannschaftsturniere, sonst waere jede monatelange
Vereinsmeisterschaft eine Liga. Nachwuchs ohne genannte Klasse (`YouthUnspecified`) faengt eine
Wortliste, in der auch **regionale Eigennamen** stehen — „Schachrallye" ist in Tirol immer
Nachwuchs. Solche Kennungen kann niemand von aussen erraten; genau dafuer fragt das Melde-Formular
(`POST .../report`, Feld `namePattern`) danach, und `POST .../classify` wendet die erweiterte Liste
auf den Altbestand an.

### Anzeige-Zustand einer Seite je NUTZER (auth)
Die Filterleiste des Turnierkalenders lag nur im `localStorage` und war damit an ein GERAET
gebunden — der Umkreis vom Schreibtisch war am Handy weg, obwohl es die Einstellung eines Nutzers
ist. `UserViewStates` haelt sie serverseitig.

**Der Inhalt ist fuer den Server OPAK** (geprueft werden nur JSON-Gueltigkeit, Objekt-Form und
`ViewStateService.MaxJsonLength` = 8192): er wird nirgends abgefragt oder gefiltert, und die
Filterleiste bekommt weiter Felder — jedes als Spalte zu fuehren waere eine Migration je Feld ohne
jeden Nutzen in SQL (dasselbe Vorgehen wie beim Analysebaum, `CalculationTree.TreeJson`). Die
Spalte ist `text`, nicht `varchar(8192)`: letzteres zaehlt in utf8mb4 mit 32 KB gegen das
64-KB-Zeilenlimit von MariaDB.

**`ViewKey` ist eine ERLAUBTE Kennung** (`ViewStateService.AllowedKeys`, heute nur
`turnier.directory`) — ohne diese Liste waere der Endpunkt ein Schluessel-Wert-Speicher je Nutzer
fuer beliebige Inhalte.

Frontend (`core/view-state.service.ts` + `TournamentDirectoryComponent`): beim Oeffnen gilt ZUERST
die lokale Kopie (ohne Abruf, damit die Vorgabefilter nicht aufblitzen), danach der Server-Stand —
weicht er ab, gewinnt ER (ihn schrieb das Geraet, an dem zuletzt gefiltert wurde) und NUR dann wird
neu geladen. Hat der Server nichts, wird der lokale Zustand hinaufgeschoben. Geschrieben wird
gedrosselt (`PersistDebounceMs` 1200), weil `storeView()` bei jeder Aenderung der Leiste laeuft.
Fehler sind still: der Zustand ist eine Bequemlichkeit, kein Inhalt.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/view-state/{key}` | Gespeicherter Zustand; **204**, wenn es keinen gibt (Normalfall beim ersten Aufruf, kein Fehler). 404 bei unbekannter Kennung |
| PUT | `/api/view-state/{key}` | Zustand speichern — der Rumpf IST der Zustand (ein JSON-Objekt), nicht ein DTO mit einem Feld darin |
| DELETE | `/api/view-state/{key}` | Zustand verwerfen (idempotent) |

### Book-Puzzles (offen + Admin)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/book-puzzles/{id}` | AllowAnonymous | Puzzle by ID |
| GET | `/api/book-puzzles/{id}/next` | AllowAnonymous | Nächstes Puzzle im selben Buch (Loop am Ende) — **buch-gegatet** (`BookAccess`); **Kalkulationsbücher → 404** (Solver-Weg, liefert `Moves`) |
| GET | `/api/book-puzzles/{id}/random` | AllowAnonymous | Zufälliges Puzzle aus demselben Buch — **buch-gegatet** (`BookAccess`); **Kalkulationsbücher → 404** |
| POST | `/api/book-puzzles/{id}/attempt` | Auth | Lösungsversuch erfassen `{ solved, timeSeconds }` (Tagespuzzle) |
| POST | `/api/book-puzzles/{id}/flag-hints` | Auth | Tipps als „dumm/schlecht" markieren/aufheben `{ flagged }` — jeder eingeloggte User (Review-Flag `BookPuzzle.HintsFlagged`; 404 wenn Puzzle fehlt) |
| POST | `/api/book-puzzles/{id}/attempt/anonymous` | Anon (`anonymous-write`) | Anonymer Versuch (Session-ID, je Session/Puzzle dedupliziert). Nur für Puzzles aus anonym lesbaren Büchern (`BookAccess`) oder je zugeordnete Tagespuzzles, sonst **404**. Der Bot-Webhook anonymer Solves ist entprellt (`AnonymousSolveNotifyThrottle`: höchstens einer je Puzzle und 30 s, der Rest als EINE Nachmeldung am Fensterende — sie liest die Löser frisch) — sonst trieb ein Skript mit frischen Session-Ids Zähler, Aggregationen und Discord-Edits hoch (A2-003) |
| GET | `/api/book-puzzles/{id}/results?since=` | AllowAnonymous | Solver-Liste (je User; Discord-ID/-Name NUR für den signierten Bot und Eingeloggte, siehe „Discord-Verknüpfung in den Ergebnis-Endpunkten“) + Versuchs-/Lösungszähler + `anonymousSolvedCount`. Löser-Status: nur wer im **ersten** Versuch löste, gilt als Löser. Ohne Bot-Signatur nur für Puzzles aus lesbaren Büchern (`BookAccess`) oder je zugeordnete Tagespuzzles, sonst **404** (auch unbekannte Id) — sonst waren die Löser privater Buchlinien per Id-Aufzählung abrufbar (A2-002) |
| POST | `/api/book-puzzles/{id}/track` | AllowAnonymous (`anonymous-write`) | „Track solves" eines per Link geteilten Puzzles: erfasst den **Erstversuch** des Besuchers (eingeloggt via Token, sonst `{ solved, sessionId }`) in `SharedPuzzleAttempts` (Unique `(BookPuzzleId, IdentityKey)` → nur 1. Versuch zählt; `solved=false` = Fehlzug/Aufgeben/Reset) und liefert `{ solved, failed }` |
| GET | `/api/book-puzzles/{id}/track-counts` | AllowAnonymous | Aktuelle „Track solves"-Zähler `{ solved, failed }` |
| GET | `/api/book-puzzles/daily/leaderboard?month=yyyy-MM` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | Monats-Wertung des Tagespuzzles (für den Bot): je User Punkte (10 je Erstversuch-Lösung + Tages-Rang-Bonus 5/3/1), `solved`, `golds`; absteigend nach Punkten. Default = laufender UTC-Monat. Literal-Route **vor** `daily/{date}` |
| GET | `/api/book-puzzles/daily/hall-of-fame?top=5` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | All-time-Bestenlisten: meiste gelöste Dailies, meiste 🥇 (Tage als schnellster Erstversuch-Löser), schnellste je gelöste Lösung. `top` 1–25 |
| GET | `/api/book-puzzles/daily/{date}` | AllowAnonymous | Tagespuzzle für UTC-Datum (`yyyyMMdd` oder `today`); legt on-demand eine persistierte Zuordnung in `DailyPuzzles` an — aber NUR für heute/gestern (ältere Daten: gespeicherte Zuordnung oder 404; verhindert anonyme Write-Amplification per Datums-Enumeration) |
| GET | `/api/book-puzzles/by-line-id?lineId=xxx` | AllowAnonymous | Lookup für schach-bot |
| GET | `/api/book-puzzles/books` | AllowAnonymous | Buch-Liste mit Counts — nur **lesbare** Bücher (`BookAccess`) |
| POST | `/api/admin/book-puzzles/import` | Admin | Bulk-Import aus JSON |
| POST | `/api/admin/book-puzzles/daily/{date}/regenerate` | Admin | Tagespuzzle eines UTC-Datums neu generieren: Datum/Link bleibt, bisheriges Puzzle wird `Retired=true` gesetzt (nie wieder in Daily/Random/Blind), neues aus dem forDaily-Pool zugeordnet |
| POST | `/api/admin/book-puzzles/{id}/regenerate-hints` | Admin | Tipps eines einzelnen Buch-Puzzles synchron (neu) generieren (force). 400 ohne `Anthropic:TextApiKey`, 404 wenn Puzzle/keine Tipps; sonst die generierten Tipps |
| POST | `/api/admin/books/{bookId}/generate-hints?force=` | Admin | Tipps für ein ganzes Buch im Hintergrund erzeugen (eigene Tipp-Queue `HintTaskQueue`, ungedeckelt); `force` regeneriert auch vorhandene, sonst nur fehlende/veraltete. Antwort `{ queued }` |

**Discord-Verknüpfung in den Ergebnis-Endpunkten (S4-001, `Authorization/BotRequestSignature.cs`)**: die vier
anonym erreichbaren Ergebnis-Endpunkte (`/api/book-puzzles/{id}/results`, `daily/leaderboard`, `daily/hall-of-fame`,
`/api/weekly-posts/{id}/results`) liefern `discordId`/`discordUsername` nur noch an den **signierten Bot** und an
**eingeloggte** Nutzer (die Wochenpost-Bestenliste der App zeigt `discordUsername || name`); anonym stehen sie auf
`null` — sonst war die Zuordnung RookHub-Konto ↔ Discord-Konto samt Lösezeiten für jeden Unangemeldeten per
Aufzählung abrufbar (aus demselben Grund gibt es keine anonyme Profil-Sicht mehr, A2-008). Bot-Vertrag (== schach-bot `puzzle/rookhub.py` `_bot_auth_headers`):
`X-Bot-Timestamp` = Unix-Sekunden, `X-Bot-Signature: sha256=<hex(HMAC_SHA256(SchachBot:StatsSecret, "<ts>.<path>"))>`,
`path` = `Request.Path` OHNE Query (eine Signatur taugt nicht für eine andere Puzzle-ID), ±300 s — dieselbe
Prüfung wie `player-progress` (dort ist die Discord-ID das signierte Objekt). Header mitgeschickt, aber falsch
(oder Secret serverseitig leer) → **401**; der Bot holt dann einmal unsigniert nach und warnt. Kein Header →
gewöhnlicher Aufruf. Die DTOs sind je Abruf frisch gebaut (`RemoveDiscordLinks()` mutiert sie) — wer hier einmal
eine Antwort cacht, muss vorher kopieren.

**Zugriff auf die offenen Buch-Endpoints (`Services/BookAccess.cs`, seit 0.317.1)**: EINE Regel für
`{id}/next`, `{id}/random`, `/random?bookId=`, `/books` und (ohne Bot-Signatur, Tagespuzzles ausgenommen) `{id}/results`. Anonym sichtbar ist ein Buch nur, wenn ein Admin
es bewusst geöffnet hat — `Book.IsPublic` (öffentlicher Kurs) oder Mitgliedschaft in einem offenen Pool
(`ForDaily`/`ForRandom`/`ForBlind`); eingeloggte sehen zusätzlich eigene (`OwnerUserId`), per `CourseShare`
geteilte und über `BookGroupAccess` (inkl. „Everyone") freigegebene Bücher; Admins alles. Altbestand ohne
`Book`-Zeile bleibt ungegatet (dort kann keine Freigabe hängen). **Bewusst weiter offen**: `GET
/api/book-puzzles/{id}` (Einzel-Puzzle per Id) — Basis für Teilen-Links, Tagespuzzle, OG-Vorschau und den
Bot-Lookup per LineId. Bewusst NICHT identisch mit `CourseService.CanAccessAsync`: die Pool-Flags öffnen nur
Einzel-Puzzles/Zufallsziehungen, nicht den strukturierten Kurs (Kapitel/Fortschritt/Offline-Export). Folge für
den schach-bot: sein `/kurs`-Katalog (`/books` + `?bookId=`) enthält nur noch Pool-/öffentliche Bücher.

### KidHub — Kinderseite (offen, 0.554.0)
Eigene Oberfläche (`kidhub(-dev).oberschmid.homes`, drittes Angular-Projekt, siehe „Drei Oberflächen"). Spielen geht
**ohne Anmeldung** — der Fortschritt liegt dann nur auf dem Gerät; angemeldet gleicht KidHub ihn mit dem Konto ab
(0.563.0, `/api/kids/progress`). `KidsController` + `KidsPuzzleService` + `KidsProgressService`.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/kids/levels` | AllowAnonymous (`kids-read`) | Stufen der Leiter in Reihenfolge `[{ level, theme, puzzleCount }]` — leer, solange die Leiter nicht aufgebaut ist. Wie `levels/{level}` und `courses` mit `Cache-Control: public, max-age=300` + ETag (304 bei `If-None-Match`); eine leere Leiter wird nicht zwischengespeichert |
| GET | `/api/kids/levels/{level}` | AllowAnonymous | Eine Stufe am Stück `{ level, theme, puzzles[{ id, fen, moves }] }` (Lichess-Form: `moves[0]` stellt die Aufgabe), leichteste zuerst; 404 unbekannt |
| GET | `/api/kids/courses?lang=` | AllowAnonymous | Kinderkurse, die GERADE gezeigt werden `[{ bookId, title, description, puzzleCount }]` — freigegeben (`Book.ForKids`), ohne Kalkulationsbücher, ohne leere, und seit 0.565.0 erst, wenn sie in jeder Sprache aus `Kids:RequiredCourseLanguages` (Vorgabe `de`) vorliegen: Quelle in der Sprache ODER ein fertiger Übersetzungsauftrag (`Done`, auch „nichts zu tun"). `title` = Kindertitel in `lang` → `en` → `de` → Buchname (`KidsTitles.Pick`), sortiert nach diesem Titel; `puzzleCount` ohne Info-Linien
| GET | `/api/kids/courses/{bookId}/puzzles?lang=` | AllowAnonymous | Aufgaben eines Kinderkurses in Lesereihenfolge (`BookPuzzleDto`, OHNE `IsInfoOnly`); `lang` wie bei den Kursen. 404 wenn nicht `ForKids`/Kalkulationsbuch/noch nicht in der geforderten Sprache; `bookTitle` = Kindertitel wie oben |
| GET | `/api/kids/progress` | Auth | Fortschritt im Konto `{ levels[{ level, stars, runIndex, runMistakes, runAt }], courses[{ bookId, resetAt, solved[{ id, at }] }] }` — Zeiten in ms seit 1970 (0.563.0) |
| PUT | `/api/kids/progress` | Auth | Den GANZEN Stand des Browsers schicken → zusammengeführt gespeichert, Antwort = gemeinsamer Stand (`KidsProgressMerge`, Regeln unten). 400 über den Deckeln (`MaxLevels` 1000, `MaxCourses` 500, `MaxLinesPerCourse` 10 000); Zeiten über jetzt + 1 Tag werden gekappt. Je KONTO gedeckelt (Codereview F7-001): der eingehende Stand behält nur Kinderkurse (`Book.ForKids`, kein Kalkulationsbuch) und nur deren eigene Linien-Ids, Stufen nur 1..1000 (Gespeichertes bleibt); Rumpf höchstens 1 MB (`RequestSizeLimit`) |
| GET | `/api/kids/language-hint` | AllowAnonymous | Land der Besucher-IP und passende Kindersprache `{ country, language }` (0.560.0) — lokal nachgeschlagen; ein bekanntes Land ohne eigene Kindersprache → `en` (0.560.1); beides `null` bei LAN-Adresse, unbekanntem Land oder ohne Länderliste |
| POST | `/api/kids/endless/batch` | AllowAnonymous (`anonymous-read`) | Endlos-Modus (0.566.0): `{ windows[{ minRating, maxRating }] (≤ 40), exclude[] (≤ 1000) }` → je Fenster ein kindgerechtes Lichess-Puzzle `[{ id, fen, moves, rating }]` in Fensterreihenfolge, im Lauf keins doppelt; Fenster ohne Treffer fehlen. 400 über den Deckeln |
| POST | `/api/admin/kids/rebuild` | `puzzles.manage` | Leiter sofort neu rechnen → `{ levels, puzzles }` (nach einem Neuimport der Standard-Puzzles, der sie per Cascade leert) |

**Rate-Limit nach Zweck** (Codereview 2026-09-29, A10-003): die Lese-Endpunkte (`language-hint`, `levels`, `levels/{level}`,
`courses`, `courses/{id}/puzzles`) hängen an `kids-read` — 240/min je IP und AUS dem globalen 100/min-Topf genommen
(`RateLimitPartitions.Global`: die Policy ist dort selbst die Obergrenze je Adresse). Vorher teilten sie sich mit 15 anderen
offenen Endpunkten `anonymous-puzzle` (30/min je IP), und eine Schulklasse hinter einer NAT-Adresse sah ab dem 16. Kind das
Fehlerbild. KidHub schickt bewusst keine `X-Visitor-Id`, deshalb zählt hier die Adresse. Die übrigen offenen Endpunkte:
`anonymous-read` (60/min) und `anonymous-write` (30/min) zählen je Konto, sonst je IP + gültiger `X-Visitor-Id` (die
RookHub-App schickt sie) — die Obergrenze je Adresse ist dort weiter der globale Limiter; `anonymous-puzzle` (30/min, je
Konto bzw. je IP) bleibt für Client-Log, Bot-Statistik, Token-Test, Extension-Senke und Bestandssuche. Schlüssel und Deckel
stehen in `Services/RateLimitPartitions.cs`, die Zuordnung je Endpunkt hält `AnonymousRateLimitTests` fest.

**Die Leiter** (`KidsPuzzles`, `Services/KidsCurriculum.cs`) = als „besonders einfach" markierte Lichess-Puzzles.
Vorgabe des Nutzers (2026-09-27): die mit dem niedrigsten Rating, deren Lösung 1 (höchstens 2) eigene Züge lang ist
und bei denen wenig Figuren auf dem Brett stehen. Vorfilter in der DB (Rating ≤ 900, RD ≤ 90, Popularity ≥ 80,
NbPlays ≥ 50, `Moves` ≤ 23 Zeichen), dann in C#: 2 oder 4 Züge, ≤ 12 Figuren, ohne en passant/Rochade/
Unterverwandlung. Thema je Puzzle: Ein-Züger `promote` (Lösungszug macht eine Dame) vor `mate1`; Zwei-Züger `mate2` >
`fork` > `skewer` > `pin` > `discovered` > `capture` (hangingPiece UND der erste eigene Zug schlägt). Innerhalb eines
Themas zählt die **Figurenzahl vor dem Rating** (`Score = Figuren·40 + Rating`). 40 Stufen à 10 im Themen-Wechsel
(`KidsCurriculum.Levels`), jedes Thema teilt sich die `PoolFactor`·10·(Stufen) leichtesten Aufgaben der Reihe nach,
gleichmäßig verteilt — die Stufen eines Themas werden also merklich schwerer. Zu dünne Themen (< 3) fallen weg, die
Nummern bleiben lückenlos; Gleichstand → `LichessId`, damit Dev und Prod dieselbe Leiter bauen.
Zwei Befunde, die den Lehrplan geformt haben (Dev-Bestand): unter Rating 700 ist praktisch ALLES „Matt in 1" (eine
reine Rangliste wäre 30 Stufen lang dasselbe), und „freie Figur schlagen" gibt es als Ein-Züger ohne Matt so gut wie
nicht (1 Stück unter 900) — Lichess-Puzzles ohne Matt brauchen fast immer zwei eigene Züge, deshalb ist `capture`
ein Zwei-Züger (250 Kandidaten ≤ 12 Figuren).
**Aufbau ohne Handgriff**: `KidsPuzzleSeeder` (BackgroundService, 20 s nach dem Start) baut die Leiter, wenn sie
fehlt oder `CurriculumVersion` ≠ `KidsCurriculum.Version`. **Wer Auswahl oder Stufenfolge ändert, erhöht
`KidsCurriculum.Version`** — sonst bleibt überall die alte Leiter stehen. Ersetzt wird in EINER Transaktion
(Execution-Strategy-Muster).

**Fortschritt im Konto** (0.563.0, Wunsch 2026-09-27): drei Tabellen (`KidsLevelProgresses`, `KidsCourseProgresses`,
`KidsCourseLines`), abgeglichen per „ganzer Stand hinauf, zusammengeführter zurück" statt einzelner Ereignisse — so
übernimmt der erste Abgleich nach dem Anmelden auch, was das Kind vorher ohne Konto gespielt hat, und ein zweites Gerät
bekommt denselben Stand. Die EINE Regel (`Services/KidsProgressMerge.cs`, SPIEGEL `mergeProgress` in
`src-kidhub/app/core/kids-progress.store.ts`, LITERALE Fälle A–E auf beiden Seiten): Sterne nach Höhe; der laufende
Durchgang der JÜNGERE (`runAt`, Gleichstand: der gespeicherte); Kurs-Linien vereinigt (je Linie die jüngste Zeit), aber
nur NACH dem jüngsten „Von vorn" (`resetAt`) — ohne die Marke brächte ein anderes Gerät verworfene Linien zurück. Eine
Linie ohne Zeit (im Browser vor 0.563.0 gelöst) gilt als 1 ms, nicht 0 (sonst fiele sie durch `at > resetAt`). Zwei
Geräte gleichzeitig → eindeutiger Index schlägt an → neu laden, einmal wiederholen. `KidsCourseLine.BookPuzzleId` hat
bewusst keinen FK; Buch löschen räumt beides ausdrücklich ab (`BookAdminService`, InMemory kaskadiert nicht).
Browser: `KidsProgressSync` (hinauf beim Anmelden, 800 ms nach jeder Änderung, bei sichtbarem Tab und zurückkehrendem
Netz). Abmelden LEERT den Stand im Browser (er liegt im Konto; auf einem geteilten Tablet soll das nächste Kind nicht
mit fremden Sternen anfangen), und ein Stand mit fremdem `owner` wird beim Anmelden verworfen statt übernommen.

**Kindertitel + „erst, wenn Deutsch"** (0.565.0, Wunsch 2026-09-27: „benenne alle Kurse für die Kinder um, blende sie
vorerst aus, bis sie deutsch sind"): `Book.KidsTitles` (JSON `{"de":"…","en":"…","hr":"…","hu":"…"}`, `Services/KidsTitles.cs`)
gibt einem Kinderkurs eigene Titel je KidHub-Sprache, OHNE den Buchnamen in RookHub anzufassen; gepflegt in der
Bücherverwaltung (Stift neben „Kinder", fragt die vier Sprachen nacheinander). Sichtbar ist ein Kinderkurs erst, wenn er
in jeder Sprache aus `Kids:RequiredCourseLanguages` vorliegt (Vorgabe `de`, leer = sofort) — auch der direkte Link ist
bis dahin 404; die Regel steht EINMAL in `KidsPuzzleService.VisibleKidsBooks`.

**Kinderkurse**: `Book.ForKids` setzt nur ein Admin (Bücherverwaltung, Spalte „Kinder"). Das Flag öffnet die Aufgaben
auf der Kinderseite bewusst OHNE `IsPublic` — wie die Pool-Flags eine absichtliche Freigabe; Kalkulationsbücher bleiben
trotz Flag draußen (ihre Zugfolge ist die Lösung, die der Kalkulations-Modus zurückhält).

### ClubHub — Kartei der Kinder und Jugendlichen des Vereins (0.613.0)

Verwaltung der Kinder/Jugendlichen (Wunsch 2026-09-30: „anlegen, Telefonnummern mit Hinweis hinterlegen — mehrere, Hinweis
klassisch ‚Mutter Daniela' oder ‚Vater Franz', auch E-Mail; am Freitag abhaken, wer da ist"). Eigene Oberfläche
`clubhub(-dev).oberschmid.homes` (fünftes Angular-Projekt), dieselbe API/DB/Konten.

- **Ein Kind ist ein KARTEIBLATT, kein Konto** (`Models/Club.cs`): `ClubMembers` (Name, Geburtsdatum ODER nur Jahrgang,
  Stufe als freier Text, `Archived`, seit 0.640.0 wieder `FideId` + `NationalId` — Notiz und Foto-Einwilligung gab es bis
  0.617.1, der User wollte sie nicht: „weitere Angaben entfernen", Migration `ClubMemberWithoutExtras`; die beiden Nummern
  fielen damals mit und kamen auf Wunsch zurück), `ClubContacts` (beliebig viele je
  Kind: `phone`/`email` + Wert + Hinweis, Reihenfolge wie eingegeben — der erste steht in der Liste), `ClubGroups`
  (+ `Weekday` 1 = Mo … 7 = So, `Schedule` Freitext), `ClubGroupMembers`, `ClubGroupTrainers` (Konto ↔ Gruppe),
  `ClubSessions` (eine Einheit je Gruppe und TAG, unique), `ClubAttendances` (Present/Absent), `ClubNotes`
  (datierte Trainer-Notizen zum Lernstand).
- **Trainer als PERSONEN** (0.620.0, Wunsch „liste unter den Kindern auch die Trainer auf bei der Anwesenheit"):
  `ClubMember.IsTrainer` — dieselbe Kartei (Kontakte, Archiv), aber ohne Gruppe, ohne Stufe/Lernstand/Konto, für jedes
  Club-Recht sichtbar, und in JEDER Gruppe unter den Kindern in Anwesenheitsliste und Tabelle (`ClubGroupDto.Coaches`).
  Nicht zu verwechseln mit `ClubGroupTrainers` = KONTEN mit Zugriff auf eine Gruppe (in der Oberfläche seit 0.620.0
  „Konten mit Zugriff", nicht mehr „Trainer"). Kartei: eigener Block „Trainer" unter den Registern, `/kind/neu?trainer=1`.
- **Fotos zur Einheit** (0.620.0, Wunsch „zu Trainings mehrere Fotos hochladen"): `ClubSessionPhotos` (LONGBLOB, wie die
  Formular-Fotos in der DB; aufrecht + auf 1600 px verkleinert über `ScoresheetImage.Prepare`, dazu ein 320-px-Vorschaubild),
  höchstens 30 je Einheit, EIN Bild je Anfrage (`POST /api/club/sessions/{id}/photos`, 15 MB wie der nginx-Deckel der
  `/api/`-Location), `GET …/photos/{photoId}[?thumb=1]` (privat, `Cache-Control: private`), `DELETE …`. Die Oberfläche holt
  die Bilder als Blob über die HTTP-Kette (ein `<img src>` schickt keinen Bearer) — `shared/session-photos.component.ts`,
  Objekt-Adressen werden beim Verlassen freigegeben. Hochladen auf der Abhak-Liste (speichert vorher die Einheit, wenn es sie
  noch nicht gibt), ansehen auch im Trainingstagebuch der Gruppe. Löschpfade (Einheit, Gruppe) entfernen Fotos über
  Stellvertreter mit Schlüssel — die Bytes werden nie geladen (`RemovePhotosWithoutLoading`).
- **Bild am Blatt + Nummern** (0.640.0, Wunsch 2026-10-02 „beim Anlegen/Bearbeiten ein Bild hinterlegen", „ein Feld für
  Personennummer/FIDE-Nr."): EIN Bild je Blatt in der eigenen Tabelle `ClubMemberPhotos` (PK = `MemberId`; JPEG ≤ 1200 px +
  Vorschaubild ≤ 256 px über `ScoresheetImage.Prepare`) — eigene Tabelle, damit keine Listen-Abfrage die Bytes lädt; ersetzt
  und gelöscht wird über Stellvertreter mit Schlüssel, ohne zu laden. Die Listen tragen nur die MARKE
  `ClubMember.PhotoVersion` (Unix-ms des Hochladens, streng steigend, `null` = kein Bild): daran sieht die Kartei, wer ein
  Bild hat, und die Oberfläche hängt sie als `?v=` an die Bildadresse (der Browser darf das Bild einen Tag behalten,
  `Cache-Control: private, max-age=86400`; ein ersetztes hat eine neue Adresse). `POST …/members/{id}/photo` (multipart
  `file`, 15 MB), `GET …/photo[?thumb=1]`, `DELETE …/photo` (idempotent) — wer das Blatt sieht, darf alle drei, auch bei
  Trainern (Personen). Oberfläche: `core/member-photo.store.ts` (EIN Speicher der Seite: je Blatt und Marke ein Abruf als
  Blob mit Anmeldung, höchstens 4 gleichzeitig — die Kartei zeigt viele auf einmal, und der globale Deckel ist 100
  Anfragen/min je IP —, beim Abmelden geleert) und `shared/member-photo.component.ts` (`ch-member-photo`: Vorschaubild oder
  Anfangsbuchstabe, mit `zoom` ein Tipp fürs große Bild). In der Kartei steht das Porträt vor dem Namen, sobald MINDESTENS
  ein Blatt ein Bild hat; am Blatt im Kopf; im Formular wählen/ersetzen/entfernen — hochgeladen wird NACH dem
  Speichern des Blatts (es hängt an dessen Kennung). Scheitert nur das Bild, bleibt das Formular als „Blatt ändern" offen
  (ein zweites Speichern legte sonst dasselbe Kind noch einmal an). `FideId` nur Ziffern (sonst 400, die Maske prüft es
  vorher), `NationalId` = Personennummer beim ÖSB, freier Text; beide ≤ 16 Zeichen, am Blatt als „PNr. …" und Link aufs
  FIDE-Profil. **Die Einwilligung der Eltern zum Bild ist Sache des Vereins** — ein Feld dafür gibt es (wieder) nicht.
- **Kreis ums Gesicht** (0.641.0, Wunsch 2026-10-03 „mit einem Kreis sein Gesicht auswählen — das soll beim Abhaken vorn
  beim Namen dabei sein"): `ClubMemberPhoto.FaceX/FaceY/FaceR` — Mittelpunkt als Anteil von Breite/Höhe, Radius als Anteil
  der KÜRZEREN Seite (0,05 … 0,5), also unabhängig von der Pixelgröße. `Services/Club/ClubFace.cs` holt den Kreis ins Bild
  (`Normalize`) und nennt das Quadrat um ihn (`Square`); daraus schneidet `ScoresheetImage.CropSquare` das VORSCHAUBILD
  (≤ 256 px, nie hochgerechnet). Das Bild selbst bleibt ganz — rund wird das Porträt erst im Browser (CSS). Ohne Kreis
  (alle drei `null`: Bilder von 0.640.0, Hochladen ohne `face`) ist das Vorschaubild das ganze Bild verkleinert.
  `POST …/members/{id}/photo` nimmt den Kreis als Formularfeld `face` (JSON-TEXT `{"x":…,"y":…,"r":…}` — keine
  Zahlenfelder, die würden nach der Sprache des Servers gelesen); `PUT …/members/{id}/photo/face` setzt ihn im VORHANDENEN
  Bild (lädt die Zeile, schreibt nur Vorschaubild + Kreis, die Marke `PhotoVersion` wechselt; 404 ohne Bild). Ein neues
  Bild ohne `face` löscht den alten Kreis. `ClubMemberDto.PhotoFace` bringt ihn ins Formular, `ClubGroupMemberRowDto.PhotoVersion`
  das Porträt in die Abhak-Liste (Kinder UND Trainer; wie in der Kartei erst, wenn EINER auf der Liste ein Bild hat, sonst
  der Anfangsbuchstabe). Oberfläche: `shared/face-picker.component.ts` (`ch-face-picker`: Bild groß, Kreis darüber —
  ziehen verschiebt, Griff am Rand und Regler ändern die Größe, ein Tipp ins Bild setzt ihn dorthin, Pfeiltasten und
  Plus/Minus; `touch-action: none` NUR am Kreis, damit ein Wisch übers Bild weiter die Seite rollt) und `core/face.ts`
  (`clampFace` = Spiegel von `ClubFace.Normalize`, LITERALE Werte in `face.spec.ts` ↔ `ClubFaceTests`). Im Formular: ein
  neu gewähltes Bild beginnt mit dem Anfangskreis (`DEFAULT_FACE`) und schickt ihn mit; für ein vorhandenes Bild holt das
  Formular das große Bild als Blob und speichert NUR einen angefassten Kreis. Alle kleinen Porträts sind seither rund
  (`.avatar`), am Kopf des Blatts 96 px — ein Tipp zeigt weiter das ganze Bild.
- **„Weiteres Kind anlegen"** (0.640.0, Wunsch „speichert altes Kind + gleich neue Maske"): zweiter Knopf in der klebenden
  Leiste von `/kind/neu` (bei Trainern „Weiteren Trainer anlegen"). Speichert, bleibt auf `/kind/neu` und leert das
  Formular — Gruppen und Kind/Trainer bleiben (man trägt meist eine ganze Gruppe hintereinander ein), der Cursor steht im
  Vornamen, darüber „<Name> ist gespeichert" mit Link aufs Blatt; „Abbrechen" heißt danach „Fertig".
- **Zwei Rechte**: `club.manage` (Leitung, Admin eingeschlossen — alles: alle Kinder, Gruppen anlegen, Trainer zuteilen,
  Blätter löschen) und `club.trainer` (nur die Gruppen, denen das Konto als Trainer zugeteilt ist, und deren Kinder). Die
  Zuteilung allein öffnet nichts — das Konto braucht eine Rolle mit `club.trainer`. Weil JEDE Action eines von beiden
  annimmt, hängt am `ClubController` kein `[HasPermission]`: die Rechte kommen live aus dem `PermissionResolver`, die Regeln
  stehen in `Services/Club/ClubService.cs` (`ClubActor`). **Sichtbarkeit ist die eine Regel**: ohne Recht 403, ein fremdes
  Kind/eine fremde Gruppe ist 404 wie ein unbekanntes — ein Trainer erfährt nicht, dass es das Blatt gibt. Ein Trainer legt
  Kinder nur in EIGENEN Gruppen an (sonst sähe er das Blatt selbst nicht) und lässt die Zugehörigkeit zu fremden Gruppen
  beim Speichern unberührt; löschen darf nur die Leitung.
- **Nur der Vorname ist Pflicht** (0.615.0, Wunsch „Nachname optional, ich weiß den oft nicht"): `LastName` ist dann ein
  LEERER Text (Spalte bleibt NOT NULL, keine Migration). Geordnet wird nach dem Nachnamen und ohne ihn nach dem Vornamen —
  am Server `ClubService.SortName` (CASE in SQL), im Browser `sortName`/`nameHead`/`nameTail` in `core/club-format.ts`
  (Register-Buchstabe, fetter Namensteil). Wer einen Namen anzeigt, nimmt diese Helfer, nie `lastName` direkt.
- **Kontakte**: leer gelassene Zeilen fallen still weg, ein Wert, der weder Nummer noch Adresse ist, ist 400 mit
  Nutzertext (`NormalizeContacts`). Die Kartei-Liste liefert die Kontakte mit — sie ist zugleich die Telefonliste. Gesucht
  wird im BROWSER: eine Namenssuche am Server hätte Namen von Kindern in die Adresse und damit in jedes Zugriffsprotokoll
  gebracht.
- **Anwesenheit**: `POST /api/club/groups/{id}/sessions` legt die Einheit des Tages an ODER ersetzt sie; nur Kinder der
  Gruppe zählen, leerer Status nimmt den Eintrag zurück. **Zwei Zustände, da oder nicht da** — „entschuldigt" gab es nur
  in 0.613.0 und ist seit 0.615.0 weg (Wunsch: „interessiert mich nicht"); die Migration `ClubAttendanceTwoStates` ist
  eine reine Datenkorrektur (Status 2 → 3), `excused` ist seither ein 400. Jede Einheit trägt **Thema** (`Topic`, eine
  Zeile) und **„Was wurde gemacht?"** (`Notes`, bis 2000 Zeichen) — beides auf der Abhak-Liste einzugeben, auf der
  Gruppenseite als Trainingstagebuch (neueste zuerst) zu lesen. Die Oberfläche schickt für JEDES Kind der Gruppe einen Status —
  wer nicht abgehakt ist, hat gefehlt (sonst wäre die Quote immer 100 %). **Vor dem ersten Tipp fragt die Liste
  `GET …/sessions/by-date/{datum}`** (204 = keine): ein Speichern ersetzt, eine leer geöffnete Liste überschriebe sonst
  eine erfasste Einheit. Die Gruppenseite zeigt die letzten 12 Einheiten als Tabelle (älteste links), die Quote zählt über
  alle. **Blättern** (0.640.0, Wunsch „eine Blätterfunktion, um zu alten Trainings zu kommen"): `GET
  …/groups/{id}/sessions/dates` liefert ALLE Tage mit Einheit (älteste zuerst); zwei Knöpfe über dem Datumsfeld führen zur
  Einheit davor/danach (`dateNeighbours` in `core/club-format.ts`: die Tage mit Einheit plus der Tag, für den die Liste
  von selbst aufgeht — von der letzten Einheit führt „weiter" dorthin zurück). Der Tag steht als `?datum=` in der Adresse;
  mit ungespeicherten Haken wird vor dem Wechsel gefragt (auch beim Datumsfeld). **Einen Knopf „Alle da" gibt es seit
  0.640.0 nicht mehr** (Wunsch: „das gibt's eigentlich nie").
- **Konto verknüpfen nur per Einwilligung**: der Trainer gibt einen Einmal-Code aus (10 Zeichen ohne 0/O/1/I, 14 Tage,
  `POST …/members/{id}/link-code`), EINLÖSEN muss ihn das Konto selbst (`POST /api/club/link`, „auth"-Limiter, nicht unter
  Impersonation). So hängt niemand fremde Konten an ein Blatt und liest deren Fortschritt. Ein Konto hängt an höchstens einem
  Blatt; trennen können beide Seiten (`DELETE …/members/{id}/link`, `DELETE /api/club/link`). Das verknüpfte Konto sieht von
  der Kartei nur den eigenen Vornamen. `GET …/members/{id}/progress` (204 ohne Verknüpfung) bündelt `PuzzleStatsService`,
  `TrainingGoalService.GetTrackerAsync(4 Wochen)` und `KidsProgressService` zu Summen (`ClubProgressService`) — keine
  eigene Zählung, keine Einzelversuche.
- **Daten von Minderjährigen**: nie anonym erreichbar, nie im Log (nur Ids), kein Service Worker (nichts bleibt auf einem
  geteilten Gerät liegen), `noindex`. Blatt löschen entfernt Kontakte, Notizen und Anwesenheit. Konto-Löschung
  (`ProfileService.DeleteAccountAsync`) löst die Verknüpfung und entfernt Trainer-Zuteilungen — das Blatt gehört dem Verein
  und bleibt; Notizen bleiben ohne Autorennamen.
- **Endpunkte** (`Controllers/ClubController.cs`, alle `[Authorize]`): `GET/POST /api/club/members`, `GET/PUT/DELETE
  …/members/{id}`, `POST …/members/{id}/notes`, `DELETE …/notes/{noteId}`, `POST …/link-code`, `DELETE …/link`, `GET
  …/progress`, `POST/GET/DELETE …/members/{id}/photo`, `PUT …/members/{id}/photo/face`; `GET/POST /api/club/groups`, `GET/PUT/DELETE …/groups/{id}`, `POST/DELETE …/groups/{id}/trainers[/{userId}]`
  (Zuteilen über den Benutzernamen), `POST/DELETE …/groups/{id}/members/{memberId}`, `POST …/groups/{id}/sessions`, `PUT
  …/sessions/{sessionId}` (auch Datum ändern; 409, wenn der Tag belegt ist), `GET …/sessions/by-date/{datum}`, `GET …/groups/{id}/sessions/dates`, `GET/DELETE
  /api/club/sessions/{id}`; vom Konto aus `GET/POST/DELETE /api/club/link`.
- **Oberfläche** (`src-clubhub/`, `public-clubhub/`, Image `ghcr.io/kahalm/rookhub-clubhub:{dev,latest}`, `APP_PROJECT=clubhub`,
  Host-Port Dev **8101** / Prod **8102**): `/` Kartei (Register nach Anfangsbuchstaben, erste Telefonnummer samt Hinweis als
  `tel:`-Link, Suche im Browser, Gruppenfilter als KNÖPFE mit Kinderzahl je Gruppe (0.640.0, Wunsch „Filter auf
  Anfänger/Fortgeschrittene" — vorher eine Auswahlliste; ein zweiter Tipp auf dieselbe Gruppe zeigt wieder alle), Archiv; am Trainingstag oben der Streifen „Heute ist Freitag: … —
  Anwesenheit abhaken"), `/kind/neu` + `/kind/:id` (Blatt ansehen/ändern: Kontakte-Editor, Notizen, Anwesenheit, Konto +
  Lernstand), `/gruppen`, `/gruppen/:id` (Anwesenheitstabelle, Kinder dazunehmen, Leitung: Gruppe ändern + Trainer),
  `/gruppen/:id/anwesenheit[?datum=]` (die Abhak-Liste: geht für den jüngsten Trainingstag auf, der nicht in der Zukunft
  liegt — `trainingDate` in `core/club-format.ts`), `/verknuepfen[?code=]` (braucht nur ein Konto). Seite deutsch, hell/dunkel
  über den geteilten `ThemeService`, Gestaltung `src-clubhub/clubhub.scss` (Zilla Slab + Atkinson Hyperlegible, beide OFL,
  in `public-clubhub/fonts/` — Atkinson wegen eindeutiger Ziffern in Telefonnummern). Reine Regeln mit Vektoren in
  `core/club-format.ts` (Altersklasse nach Jahrgang, „Geburtsdatum oder Jahrgang" lesen, `tel:`-Link, Trainingstag).
  `ClubHubAssetTests` hält Symbol und Schriften gegen die Dateien auf der Platte.
- **Noch nicht**: Elo-Verlauf je Kind, Turniere/Termine, Eltern-Sicht, Sprung aus RookHub (kein Menüpunkt), eigener
  Datenschutz-Text für die Kartei (die geteilte Datenschutzseite beschreibt sie nicht). Deploy: Dev seit 30.09. (Port 8101,
  NPM-Host 43), Prod seit 30.09. mit v0.616.0 (Port 8102, NPM-Host 44) — beide Stacks haben den Dienst `clubhub`.

### LeagueHub — Tiroler Ligen, Aufstellungs-Prognosen (Admin + öffentliche Teilen-Links, 0.569.0)

Portierung der Python-Fassung (`~/claude/league-analyzer`, live bis zum Prod-Tag unter
`leaguehub.oberschmid.homes` als eigener Stack `/opt/stacks/leaguehub`). Rechte: `league.view` lesen,
`league.manage` aktualisieren/teilen/importieren, `league.contribute` Vereinspartien beitragen (0.573.0) — Admin erfüllt
alles; die Vereinsgruppe bekommt eine Rolle mit `league.view` + `league.contribute` (legt der Admin in der
Rollenverwaltung an).

- **Tabellen** (`Models/League.cs`): `LeagueTournaments` (PK = chess-results-tnr bzw. 900 000 000 + Ligamanager-Id, Season/Level/League/Grp/Stage,
  Source/SourceRef/Boards — siehe „Ligamanager"; **`Level`** Tirol/Österreich seit 0.719.0: 1 = 1. Bundesliga, 2 = 2. Bundesliga,
  3 = Landesliga, 4 = 1. Klasse, 5 = 2. Klasse, 6 = Gebietsklasse — vorher Landesliga 1 … Gebietsklasse 4, siehe „Österreichische
  Bundesliga"; Bayern 1 = Oberliga … 9 = C-Klasse),
  `LeagueRounds` (Datum je Runde), `LeagueMatches`, `LeagueGames` (Brettpartien, Spieler null = „Brett nicht
  besetzt", Forfeit 0/1/2 — 2 = „- - -", z. B. Corona-Abbruch 2019/20), `LeaguePlayers` (Meldeliste;
  `NameKey` ohne akad. Titel = Schlüssel zu den Brettpaarungen), `LeaguePlayerProfiles` (PK FIDE-ID:
  Eröffnungsprofil als JSON + alle Partien als PGN), `LeagueOnlineAccounts` (Online-Konten je FIDE-ID — aus dem Import NUR
  selbst offengelegte: Klarname im Profil, Land passt, Name unter FIDE-Spielern eindeutig; keine Minderjährigen; seit 0.605.0
  zusätzlich von Verwaltern gepflegte, seit 0.610.0 auch Minderjähriger — deren Konto bleibt verborgen, siehe „Online-Konten +
  Online-Partien"), `LeagueOnlineGames` (deren geholte Partien),
  `LeagueShares` (Token 144 Bit, eine Begegnung, läuft 7 Tage nach der Runde ab — frühestens 7 Tage nach dem Anlegen; ein abgelaufener, noch nicht aufgeräumter Link wird beim erneuten Teilen durch einen frischen ersetzt), `LeagueViews`
  (fertig gerechnete Liga-Ansicht als JSON — gerechnet beim Aktualisieren, nicht je Aufruf).
- **Rechenkern** `Services/League/LeagueEngine.cs`: 1:1-Portierung von `features.py`/`model.py` —
  Merkmale NUR aus Wissen vor der Runde, logistische Regression mit den eingebetteten Gewichten
  `Assets/league-model.json` (trainiert in Python, `export_weights.py`; Bayern seit 0.708.0 eigenes Modell, siehe „Modell je Region"), Normierung je Match auf B Bretter,
  Brett-Wahrscheinlichkeiten über elementarsymmetrische Polynome (Bretter folgen der Meldeliste),
  Sonntag vorab = Mischung aus „Samstag gespielt ja/nein". **Tor**: `LeaguePythonParityTests` gegen den
  echten Bestand (`LEAGUE_BUNDLE`, `LEAGUE_PY_DATA`, sonst übersprungen) — 0.569.0: 88 Begegnungen,
  1 676 Wahrscheinlichkeiten, größte Abweichung 0,000.
- **Freigabe-Regel** (Wunsch des Nutzers): die NÄCHSTE Runde einer Liga ist „offen" (`rounds[].open`, Vorauswahl; Landesliga
  Samstag + Sonntag gemeinsam). Spätere Runden sind seit 2026-10-06 nicht mehr „gesperrt" (Wunsch: „lass mich auch zukünftige
  Runden sehen — Prognosen kannst du machen und dann anpassen"): `status: "open"` + `provisional: true` + `unlock_after`, dieselbe
  Rechnung mit dem Wissen von heute (`LeagueFeatures.RowsFor` zählt nur gespielte Runden), neu gerechnet bei jedem Aktualisieren.
  Ansichten von vorher tragen noch `locked`, bis „Daten aktualisieren"/`admin/rebuild` läuft. JSON-Feldnamen der
  Ansicht bewusst wie in Python (snake_case), damit die Parität direkt prüfbar ist.
- **Partien je Quelle** (0.626.0, Wunsch „auf der Hauptseite ausweisen: x Spiele aus Lumbra, y aus ChessBase, z aus Lichess,
  w aus chess.com"): `GET /api/league/sources` (league.view, `Services/League/LeagueGameSources.cs`) →
  `{ board[{ key, label, games }], boardTotal, online[…], onlineTotal, countedAt }`, 30 min im IMemoryCache. Brettpartien
  liegen je SPIELER in `LeaguePlayerProfiles.Pgn` (eine Partie zweier Ligaspieler steht zweimal da) — gezählt wird jede
  einmal (Schlüssel: beide Namen nur aus Buchstaben + Datum + Runde, nur Kopfzeilen gelesen), Quelle nach
  `LeagueProfileBuilder.StoredSource` (Kopf `LeagueSource`, sonst FIDE-IDs = Lumbra, sonst chess-results), dazu
  `LeagueClubGames` als „Vereins-Datenbank"; online je Seite `COUNT(DISTINCT ExternalId)`. Prod 01.10.2026: rund 34.800
  Lumbra, 18.800 ChessBase-Megabase, 4.600 chess-results, 212 Lichess-Übertragungen; 667.881 Lichess, 29.528 chess.com.
  Oberfläche: zwei Zeilen unter „Stand der Daten" auf der Startseite (`core/game-sources.ts`); fehlt die Zählung, fehlt nur
  die Zeile. Seit 0.627.0 auch auf dem Teilen-Link: `GET /api/league/s/{token}/sources` (anonym, nur mit gültigem Token,
  sonst 404; nur Zahlen), Zeile unter „Geteilte Begegnung … Stand der Daten" in `share-page.component.ts`.
  **Seit 0.628.0 eine Tabelle** (`shared/game-sources.component.ts`, Regeln rein in `core/game-sources.ts` → `sourceGroups`;
  Fassung B von Entwurf 2 vom 01.10.2026): Quelle | Gesamt | Liga | Begegnung, nach Brett/Online gruppiert, Anteil an Gesamt als
  Balken (größte Quelle rot). „Liga" = Block `league` (alle `LeaguePlayers` dieser Turnier-Nr. mit FIDE-ID, 30 min gemerkt je
  Liga), „Begegnung" = Block `opponent` (Meldeliste des Gegners, jedes Mal frisch) — beide `{ players, board{Quelle: n},
  boardTotal, online{Seite: {games, accounts}}, onlineTotal, onlineAccounts }` aus `LeagueGameSources.PlayersAsync`; eine
  Partie zweier Spieler derselben Gruppe zählt einmal. Angemeldet schickt die Startseite `?tnr=` und die Meldeliste der
  gewählten Begegnung als `?fides=` (höchstens 40; neu geholt, wenn sich Liga oder Meldeliste ändert; späte Antworten
  verworfen); über den Link nimmt der SERVER Liga und Meldeliste der geteilten Begegnung (`LeagueService.ShareTnrAsync`) und
  zählt online nur gesicherte Konten. „–" = auf dieser Seite hat keiner der Spieler ein Konto (≠ 0 Partien). Prod 01.10.:
  Landesliga 177 Spieler / 46.058 Brett, 1. Klasse 226 / 18.783.
  **Seit 0.650.0 hinter zwei (i) in der Begegnung** (Wunsch 2026-10-04: „versteck den Text am Anfang hinter 2 (i) …"):
  `fixture-view.component.ts` bekommt `[sources]` von Startseite/Teilen-Seite und zeigt „Partien {Gegner}: N (i)" (N = Brett +
  online des Blocks `opponent`) — das (i) klappt die Tabelle auf — und „Prognose · bisher x % der Aufgestellten richtig · hier
  a von b (i)": das (i) hat den früheren Erklärtext plus die **Treffer-Statistik** `GET /api/league/forecast-stats` (league.view)
  bzw. `GET /api/league/s/{token}/forecast-stats` (anonym, gültiger Token; nur Zahlen + Liga-Namen) aus
  `LeagueService.ForecastStatsAsync`: je Runde (alle Ligen), je Liga (mit Runden), gesamt — seit 0.658.0 `{ fixtures, top1, top2,
  top3, of, e1, e2, e3 }` (Platz des echten Spielers in der Vorschlagsliste des Bretts; `e*` = erwartete Treffer aus den
  angesagten Prozenten, Tausendstel) plus `calibration` (10 Stufen `{from, n, p, hits}` über alle Angaben ≥ 2 %, Wunsch 05.10.
  „wie genau passen die Prozentangaben"); vor 0.658.0 `{ fixtures, players, boards, of }` (alte `eval` zählen nicht mehr),
  über ALLE Begegnungen der laufenden Saison, jede aus Sicht beider Teams. Quelle ist das Feld `eval` einer gespielten Begegnung
  in der Ansicht (`LeagueViewBuilder.Evaluate`): `players` = Aufgestellte unter den B wahrscheinlichsten (dieselbe Größe wie
  der Backtest `Hits`), `boards` = erster Vorschlag genau am Brett (= „gespielt" auf Platz 1), `of` = besetzte Bretter. Ältere
  Ansichten haben kein `eval` → zählen erst nach „Daten aktualisieren". Gemerkt je Saison + Stand der Ansichten (6 h).
  Hinter jedem Prognose-Vorschlag seit 0.649.0 „(n)" = Partien im Bestand (`cand[].g`, nachgezogen wie `roster[].g`).
- **Stapel-Upload von Formular-Bildern (0.651.0**, Wunsch 2026-10-04: „beliebig viele Partien uploaden — nicht direkt
  verarbeiten, nur am Server ablegen und eine Admin-Message darüber verfassen"): `Services/League/LeagueBatchUploadService.cs`,
  Tabellen `LeagueBatchUploads` (Key 32 Hex, UserId? | ShareHash + AnonIpHash, Comment ≤1000, FileCount, TotalBytes,
  FinishedAt) + `LeagueBatchUploadFiles` (LONGBLOB, Cascade). Ablage in der DB (Entscheidung des Users 04.10., kein Volume).
  Endpunkte je unter `/api/league/club` (league.contribute) und `/api/league/s/{token}/club` (anonym): `POST batches`
  `{comment}` → `{key}`, `POST batches/{key}/files` (ein Bild/PDF je Anfrage, ≤30 MB, über 12 MB auf 3000 px verkleinert —
  nginx-Location wie `scans` 32M), `POST batches/{key}/finish` → angemeldet `AdminMessageService.SendFromUserAsync`
  (Text mit `/admin?tab=uploads`), anonym Glocke `league_batch_uploaded` an messages.admin. Deckel: 1000 Bilder / 2 GB je
  Stapel, anonym 1 GB je IP und 3 GB gesamt je Tag. Admin: `GET/DELETE /api/admin/league-uploads[/{id}]`,
  `GET …/{id}/zip` (über Zwischendatei, DeleteOnClose), Tab „Uploads" (`admin-league-uploads.component.ts`, Key `uploads`).
  Oberfläche: `shared/batch-upload.component.ts` im Bereich „Partieformular" der Upload-Seite.
  **Lesen ohne Modell** (0.684.0, Wunsch 2026-10-06, Skill `/formulare` in `~/.claude/skills/formulare/`): `POST
  /api/admin/league-uploads/manual-scan` `{ userId, fileIds[] (Seitenreihenfolge), transcription (Form der Modell-Antwort,
  Kästen in Pixeln des aufrechten, auf 2000 px verkleinerten Fotos), clubGameId? }` → `ScoresheetScanService.CreateManualAsync`:
  Liga-Einlesung des Besitzers mit `Model = "claude-manual"`, danach Auflösung + Engine-Prüfung wie gelesen, offen zum Prüfen
  in LeagueHub. Mit `clubGameId` sofort archiviert und an die schon übernommene Vereinspartie gehängt (`CloseLeagueScanAsync`) —
  „Korrigieren" zeigt dann Foto und Lesarten. Keine Tageszahl, keine Kostenbremse.
- **Formular-Archiv + Datums-Bias (0.655.0**, Wunsch 2026-10-04: „merk dir Bild und Erkennung für Optimierungen, vorerst
  365 Tage" / „beim Datum heavily bias zu heuer"): `ScoresheetScanService.CloseLeagueScanAsync(actor, id, finalPgn)` kopiert
  vor dem Leeren je Seite Foto + (Seite 1) TranscriptionJson/ResolutionJson/FinalPgn in `ScoresheetScanArchives`
  (Outcome saved/discarded, ExpiresAt = +`ArchiveRetention` 365 d, Cascade an der Einlesung, Konto-Löschung räumt mit);
  Abgelaufenes geht beim nächsten Abschluss (`PurgeExpiredArchiveAsync`). Keine Oberfläche — Auswertung per DB.
  Datum: `ScoresheetPrompt.DateHint(today)` in jedem `FirstRead` (heute + „assume {Jahr}"), Prüfseite `presetYear()`
  (`club-format.ts`): gelesen heuer/Vorjahr → so, sonst heuer + Hinweis „gelesen 2016 — auf heuer gesetzt".
- **Anonyme Uploads nach dem Anmelden zuordnen (0.656.0**, Wunsch 2026-10-04; Entscheidung des Users: bei „Schwaz" BEIM
  ANMELDEN FRAGEN): jede Speicherung über einen Teilen-Link (`ImportViaShareAsync` → `ClaimKey` im Ergebnis,
  `POST …/s/{token}/club/games` → `claimKey`) setzt `LeagueClubGames.ClaimKeyHash` = SHA-256 eines Zufalls-Schlüssels, den
  nur der Browser kennt (`core/claim-keys.ts`, localStorage `lh-claim-keys`, ≤ 200; `ClubClient` merkt ihn selbst). Nach
  dem Anmelden fragt `shared/claim-prompt.component.ts` (nicht beim Einstieg als Nutzer): `POST /api/league/club/games/
  claims/preview` `{keys}` → `{games, anonymized}`; JA → `…/claims` setzt `UploadedByUserId` (auch bei „Schwaz" — nur mit
  dieser Zustimmung), NEIN → `…/claims/forget`; beides leert den Hash und die Schlüssel im Browser. `CanDelete` = Verwalter
  ODER `UploadedByUserId == ich` (ohne die frühere Schwaz-Sperre — einen Hochladenden trägt eine Schwaz-Partie nur nach
  Zuordnung). Bearbeiten und Löschen brauchen seit 0.656.0 nur die Anmeldung (Regel im Dienst).
- **Lasche „Meine Partien" (0.652.0–0.673.0) ist ENTFERNT** (0.673.1, Wunsch 2026-10-05: „entferne /verein/meine"): keine
  Route, kein Reiter, Links führen auf `/verein`. Der Endpunkt `GET …/club/games/mine` steht noch (ohne Oberfläche).
- **Formular prüfen (0.673.1)**: ein ausgewählter Spieler belegt die Elo vor (`LeagueRosterPersonDto.Elo` = jüngste
  Meldeliste nach `Tnr`, `EloI ?? EloN`, nur Ligaspieler); das Elo-Feld ist ein Textfeld mit `inputmode="numeric"` (kein
  Zahlenfeld mit Pfeilen). Häkchen „Automatisch zu meinen Partien hinzufügen" (nur angemeldet, standardmäßig an, Abwahl im
  localStorage `lh-auto-my-games` = `'0'`): nach dem Übernehmen ruft die Seite `addToMyGames` selbst.
- **Taktik-Ernte (0.657.0**, Wunsch 2026-10-04 „Taktiken aus den Partien automatisch ernten … plan und bau", Kapitel je
  Ligarunde): `Services/Tactics/` — `TacticHarvest` (rein: Lichess-Puzzler-Schwellen ohne AGPL-Code: Gewinnchance −1..1,
  Fehler des Gegners > 0,6, nicht schon > +3, Matt ≤ 15 oder ≥ +2, eindeutig > 0,7 bzw. bei Matt kein zweites Matt; Themen
  mateInN/oneMove/short/long/promotion/check/hangingPiece/fork), `TacticHarvestService` (Scan: fertige Analysen Origin
  Club/Library/SavedGame — NICHT Guess/Manual, privat — einmal je Analyse `GameAnalyses.TacticsScannedAt`; Pump: je
  Löserzug ein Auftrag MultiPv 2, Tiefe 22, `background`, nicht in der Ruhezeit, höchstens 8 offen, Gegnerzug aus der
  Hauptvariante, Engine-Besitzer `TacticHarvest:OwnerUserId` → `MasterAnalysis:OwnerUserId` → Haus-Engine eines Admins;
  Publish: Bücher je Verein `tactics-club-{clubId}.pgn` „Taktiken aus Vereinspartien – {Verein}" (seit 0.698.0; vorher ein
  gemeinsames `tactics-club.pgn` — die Migration `LeagueClubs` hat es zu dem von Verein 1 umbenannt; frei für die Gruppen DES
  VEREINS (`LeagueClubMembers`), eine später zugeordnete Gruppe bekommt die Freigabe beim Zuordnen; Kapitel
  `LeagueRoundChapterAsync` „2026/27 · Landesliga · Runde 1" über Spieler + Farben + Saison, die anonymisierte Seite = eigener
  Verein (`LeagueClub.OwnsTeam`); sonst „Andere Partien"), `tactics-masters.pgn` (nur Admins), `tactics-u{id}.pgn` (Besitzer); Aufgabe = Fehler
  des Gegners + Lösung, StartPly 0, Tags gefunden/verpasst + Themen; verschwundene Taktik → `Retired`),
  `TacticHarvestScheduler` (60 s, `TacticHarvest:Enabled`). Tabelle `TacticCandidates` (Cascade an der Analyse), Aufträge
  aus der Job-Liste ausgeblendet. Messung 04.10. (nur erster Zug): ~700 Kandidaten, Verein 0,22/Partie, Meister 0,12.
- **Vereinspartien korrigieren + verbundene Kopien (0.660.0**, Wunsch 2026-10-05: „Ligagame aus Scoresheet in meine Partien
  kopiert — verbunden bleiben, Korrektur korrigiert alles; überall ein Korrigieren-Knopf wie beim initialen Beheben";
  Entscheidung: alle Kopien ziehen mit). `SavedGames.LeagueClubGameId` (beim Kopieren über `ClubGameForMovesAsync`, alte Kopien
  `ClubCopyLinkScheduler` täglich über den Dubletten-Schlüssel) und `ScoresheetScanArchives.LeagueClubGameId` (beim Übernehmen,
  `CloseLeagueScanAsync(…, clubGameId)`). `LeagueClubService.CorrectMovesAsync` (Hochladender/Verwalter; PGN, Plies, MovesHash
  neu; seit 0.694.1 wird die Analyse auf die neuen Züge umgebaut — `GameAnalysisService.RebaseClubGameAsync`: gleiche Stellungen
  behalten ihr Ergebnis, nur geänderte rechnet die Pumpe, Erklärungen/Taktiken der alten Zugfolge fallen weg; Karten neu) + `ClubGameCorrectionService` (Kopien via
  `SavedGameService.ApplyClubMovesAsync` — Kopfdaten/Datum der Kopie bleiben, Zugkommentare gehen; `FromCopyAsync` nach
  `PUT /api/games/{id}`: darf korrigieren → Vereinspartie + alle Kopien, sonst Kopie gelöst). Endpunkte `/api/league/club/games/
  {id}/sheet` (+`/photo`, `/resolve`, aus dem Archiv) und `PUT …/{id}/moves`; RookHubs `/api/games/{id}/scoresheet|photo|resolve`
  fallen für eine Kopie auf das Archiv der Vereinspartie zurück (`SavedGameDetailDto.ClubGameId/ClubSheet`). Oberfläche: LeagueHub
  Route `verein/partie/:id/korrigieren` = `club-scan-page` im Modus `gameId` (Namen bleiben bei „Bearbeiten"), Knopf „Korrigieren"
  in der Vereinsliste/„Meine Partien"; RookHub ⋮ der Vereinspartie → LeagueHub, `/games/:id/edit` öffnet das Formular der
  Vereinspartie und zeigt einen Hinweis zur Verbindung. Datenschutz: aufbewahrtes Foto sehen jetzt auch Hochladender/Verwalter.
- **Paarungen gespielter Runden** (0.673.0, Wunsch 2026-10-05: „bei vergangenen Runden oben unter dem Ergebnis auch die Paarungen
  direkt anzeigen, inkl. Link zu Partien"): `GET /api/league/{tnr}/round/{round}/games?team=` (league.view) bzw.
  `GET /api/league/s/{token}/games` (Begegnung des Links) → je Brett `{ board, white, whiteElo, black, blackElo, result, forfeit,
  pgn, source (club|profile), clubGameId }` aus `LeagueGames` (`Services/League/LeagueFixtureGames.cs`). Partie zuerst aus der
  Vereins-Datenbank (Jahr der Runde, Farben passen, je Seite FIDE-ID — auch intern hinter „Schwaz" — oder Nachname oder „Schwaz"
  für den eigenen Verein, mindestens eine Seite über ID/Namen; mehrere: die jüngste), sonst aus den Spielerkarten (Datum ±3 Tage
  um den Rundentermin, beide Nachnamen auf ihrer Farbe). Ausgegeben wird nur das PGN der Quelle. Oberfläche: Tabelle unter dem
  Ergebnis in `fixture-view.component.ts` (Eingabe `leagueTnr`), „Nachspielen" klappt `lh-game-replay` auf (Vereinspartie mit Kurve,
  nur angemeldet). Seit 0.675.0 an einer Vereinspartie (angemeldet, nicht über den Link) dazu „Analyse" (RookHub `club-games/{id}`)
  und mit `canEdit` (Verwalter oder Hochladender, Regel wie `CanDelete`) „Bearbeiten" → `/verein?bearbeiten={id}` (die Liste klappt
  die Partie auf, holt sie nach, wenn sie nicht auf Seite 1 steht) und „Korrigieren" → `/verein/partie/{id}/korrigieren`.
- **Feste Ligapaarung einer Vereinspartie** (0.678.0, Wunsch 2026-10-05: „wenn ich eine Partie von meinen Spielen in die
  Vereins-DB kopiere, kann ich sie keiner Ligarunde zuweisen — überleg dir da was"): `LeagueClubGames.LeagueGameId` (Index, KEIN
  FK) zeigt auf die Brettpaarung (`LeagueGames.Id`) und schlägt jede Raterei — `LeagueFixtureGames` nimmt fest zugeordnete
  Partien zuerst (das Raten nur noch unter Partien ohne lebende Zuordnung), `TacticHarvestService.LeagueRoundAsync` ebenso. Vorschläge
  rechnet `Services/League/LeaguePairingFinder.cs`: Paarungen mit mindestens einer FIDE-ID der Partie (auch der internen hinter
  „Schwaz"; die „Schwaz"-Seite passt zu jedem Spieler des eigenen Vereins), `Exact` = beide Seiten in ihren Farben UND Tag
  ±`DayTolerance` (3) um den Rundentermin (ohne Tag: Saison passt zum Jahr), höchstens `MaxOptions` (8); vorgewählt nur bei
  GENAU einem genauen (`AutoPick`). Wählen (`ApplyAsync`): Jahr aus dem Rundentermin (PGN-Datum wird mitgezogen),
  `Classifier1/2` = Liga/Saison; `0` = keine. Wege: Übersicht (`games/preview` → je Partie `pairings` + `pairingId`; Import
  `games[].leagueGameId`, fehlt = Auto), Formular (`POST …/club/pairings`, `POST …/club/games` mit `leagueGameId` + `date`),
  „Bearbeiten" (`GET …/club/games/{id}/pairings`, `PUT …/club/games/{id}` mit `leagueGameId`). Datenschutz wie die
  Klassifizierer: `leagueGameId`/`leagueGameLabel` im DTO nur mit `CanDelete` (Hochladender/Verwalter). Oberfläche: Auswahl
  „Ligapartie" in `club-import-review` (unter dem Turnier), `club-scan-page` und dem Bearbeiten-Feld von `club-games-page`;
  eine gewählte Paarung setzt die noch nicht angefassten Spieler aus dem Spielplan (`ImportReview.setPairing`).
  **Stabil über jedes Aktualisieren** (0.716.1, gemeldet 2026-10-07: „gefühlt zum dritten Mal zugewiesen — die Zuordnung
  verschwindet immer wieder"; `Services/League/LeagueGameLinks.cs`): bis dahin legte jedes Aktualisieren alle `LeagueGames` einer
  Liga neu an, jede Zuordnung zeigte ins Leere, und weil `LeagueFixtureGames` nur unter `LeagueGameId == null` riet, verschwand die
  Partie ganz aus den Paarungen. Jetzt: (1) **Ersetzen erhält die Id** je Schlüssel (Tnr, Runde, Begegnung, Brett) —
  `LeagueGameLinks.Merge` in `LeagueRefresh.ReplaceAsync` (also auch Ligamanager/Zugspitze) und im Bundle-Import; Runde + Brett
  allein reichen NICHT, jede Begegnung hat ihre Bretter 1…n. (2) **Sicherheitsnetz**: `LeagueClubGames.LeagueTnr/LeagueRound/
  LeagueMatchNo/LeagueBoard` (nullable, Index) — gesetzt NUR über `LeagueGameLinks.Set` (auch in `ApplyAsync`), nach jedem Ersetzen
  löst `RelinkAsync(tnr)` die Id aus dem Schlüssel neu auf (Paarung weg → Id `null`, Schlüssel bleibt, kommt sie wieder, findet der
  nächste Lauf sie). **Jeder Leser** geht über `LeagueGameLinks.ResolveAsync`/`FindAsync` (Id, wenn sie lebt und zum Schlüssel
  passt, sonst Schlüssel, sonst „nicht zugeordnet" + Warnung): `LeagueFixtureGames` (tote Id = raten erlaubt — eine Partie
  verschwindet nie wegen einer toten Id), `TacticHarvestService.LeagueRoundAsync`, `PairingsForGameAsync`, die Bezeichnung im DTO
  (`FillPairingLabelsAsync` gibt die aufgelöste Id aus) und `ArchiveOlderVersionsAsync` (gleiche Paarung = gleiche aufgelöste Id
  bzw. gleicher Schlüssel; eine tote Id ist keine „andere feste Paarung"). Wer einen neuen Leser baut, nimmt `ResolveAsync`, nie
  `LeagueGameId` roh. (3) **Heilung beim Start** (`LeagueGameLinks.HealOnStartupAsync` in `Program.cs`, idempotent, wirft nie):
  alle mit Schlüssel neu auflösen; gültige Ids ohne Schlüssel bekommen ihn; tote Ids ohne Schlüssel werden über
  `LeaguePairingFinder` neu gefunden (`AutoPick`, genau EIN genauer Treffer; PGN-Datum, sonst das Jahr) oder geleert (Warnung).
- **Ältere Fassungen archivieren** (0.691.0, Wunsch 2026-10-06: „wenn eine 2. Partie über ein Scoresheet hinzugefügt wird, die schon
  eingegeben ist, das alte archivieren"): `LeagueClubGames.ArchivedAt`/`ReplacedById`, gesetzt in `LeagueClubService.ArchiveOlderVersionsAsync`
  nach jedem Formular-Add (`AddAsync`, nicht beim PGN-Import). Dieselbe Partie = dieselbe feste Ligapaarung, oder (ohne zwei verschiedene
  Paarungen) dasselbe Jahr + dieselben Spieler (FIDE-ID inkl. der internen hinter „Schwaz", sonst Name) + dieselben ersten
  `SameGamePrefixPlies` (16) Halbzüge. Die Analyse der alten geht. **Globaler Query-Filter** `ArchivedAt == null` auf `LeagueClubGame`:
  archivierte sind überall unsichtbar; `IgnoreQueryFilters()` nur in Konto löschen (`ProfileService`), Rückbau eines Teilen-Links
  (`DeleteByShareAsync`) und Zuordnen nach dem Anmelden (Claims). Antwort von `POST …/club/games`: `replaced` = Zahl archivierter.
- **Endpunkte** (`Controllers/LeagueController.cs`): `GET /api/league/index`, `GET /api/league/sources`, `GET /api/league/{tnr}`,
  `GET /api/league/player/{fide}` (+`/pgn`), `POST/GET/DELETE /api/league/share`, `POST /api/league/update`
  (+`/status`; Knopf, KEIN Zeitplan — ein Lauf auf einmal, neuer Start frühestens nach 2 min),
  `POST /api/league/admin/import` (Bestand aus `export_bundle.py`, gzip, `?rebuild=true`),
  `POST /api/league/admin/rebuild`, `POST /api/league/admin/ligamanager/import` (Bayern, siehe unten),
  `POST /api/league/admin/zugspitze/import` (Schachkreis Zugspitze, siehe unten), `POST /api/league/admin/online-reports/zugspitze`
  (Online-Schach Oberbayern → Meldungen, siehe „Konto-Prüfung (i)"). Öffentlich (Rate-Limit `anonymous-tournament`):
  `GET /api/league/s/{token}` (+`/player/{fide}`, `/pgn`) — nur Spieler der geteilten Meldeliste, Online-Konten
  nur „sicher".
- **Aktualisieren** (0.570.0, `Services/League/LeagueRefresh.cs`): je Liga der laufenden Saison
  `GET {Crawler}/api/league/{tnr}` (vier Seiten, `zeilen=99999`), Zeilen der Liga ERSETZEN und dabei
  Brettpaarung ↔ Meldeliste über `(Team, NameKey)` verknüpfen (FIDE-ID, Meldebrett, Elo = EloI, sonst EloN),
  „Brett nicht besetzt"/„spielfrei" → null; Ansichten rechnen; dann `GET {Crawler}/api/league/games/{fide}`
  für die wahrscheinlichen Gegner offener Runden (p ≥ 0,15, `CrFetchedAt` älter als 14 Tage, max. 40; Gegner von
  JEDES Vereins in `LeagueClubs` zuerst wie in stale_players.py („Gegner von Schwaz zuerst"), dann höchste Wahrscheinlichkeit) → `LeagueProfileBuilder` führt Bestand + neue Partien zusammen (Dubletten über
  Datum + Nachnamen + Ergebnis; Farbe per FIDE-ID-Tag, sonst Nachname — 2022/23 ohne Komma) und baut die
  Spielerkarte neu; zuletzt Ansichten erneut. **Die Brettpaarungen werden seit 0.716.1 ZUSAMMENGEFÜHRT, nicht neu angelegt**: je
  (Tnr, Runde, Begegnung, Brett) behält eine Zeile ihre Id (`LeagueGameLinks.Merge`), danach `RelinkAsync` — siehe „Feste
  Ligapaarung einer Vereinspartie". Meldelisten, Begegnungen und Runden werden weiter ersetzt (an ihren Ids hängt nichts).
  HttpClient `LeagueCrawler` (5 min Timeout). Eine Liga bzw. ein Spieler,
  der gerade nicht zu holen ist, hält den Rest NICHT auf (Warnung im Log, Meldung „nicht aktualisiert: Liga …“); erst
  wenn KEINE Liga kommt, gilt der Lauf als gescheitert. Vier leere Seiten (Fehl-/Drosselseite) ersetzen nichts —
  und auch EINE leere Seite nicht, solange die Liga dafür Bestand hat (Paarungen art=2, Brettpaarungen art=3,
  Meldeliste art=16, Statistik art=20 = Punkte/Partien/Performance an der Meldeliste; Codereview N4-002): der Crawler
  holt die vier nacheinander und meldet eine Drosselseite als leere Liste, dann bleibt die Liga und steht unter „nicht
  aktualisiert“. Leer bleiben darf eine Seite nur, wenn auch der Bestand dafür leer ist (Saisonbeginn). Der
  Import (`admin/import`) ersetzt in EINER Transaktion (Execution-Strategy-Muster).
- **Ligamanager (Bayern) als zweite Liga-Quelle** (2026-10-07, Schritt 1 von „LeagueHub für SK Weilheim"):
  `Services/League/LigamanagerSource.cs` liest EINE Liga des SBV-Ligamanagers (`https://ligamanager.schachbund-bayern.de`,
  keine API, HTML per Regex; Muster am Ende der Klasse) direkt aus RookHub.Api — kein Crawler, kein VPN, eigener HttpClient
  `Ligamanager` (User-Agent, 1 s Pause zwischen den drei Abrufen). Adresse `/{region}/{saison}/{slug}-{id}/…` (`LeagueRef`,
  nur dieser Host). Seiten: `spielplan` (Runden mit Termin/Uhrzeit, Begegnungen, Mannschaftsergebnis, Spiellokal, je
  gespielter Begegnung Brett + Melde-Nr. beider Spieler + Ergebnis aus HEIM-Sicht), `mannschaften` (Nr., Name, Titel, DWZ =
  `EloN`, ELO = `EloI`, FIDE-ID aus dem Link `ratings.fide.com/profile/…` — **nur die laufende Saison verlinkt**, ältere haben
  keine IDs), `partien/download/alle.pgn` (Windows-1252, 404 solange leer; `[Round "r.b"]` = Runde.Brett). Daraus dieselben
  `LeagueRefresh.Pages` wie vom Crawler, geschrieben über das statische `LeagueRefresh.ReplaceAsync(db, pages, now)`
  (Verknüpfung über (Team, NameKey); Namen „Pieper, Thomas, Dr." → „Pieper, Thomas Dr."; Spieler am Brett über die Melde-Nr.).
  Statistik (Punkte/Partien) aus den Brettergebnissen, `EloPerf` leer. **Farben**: der Spielplan nennt keine — aus dem PGN
  (WhiteTeam), sonst die bayerische Regel **Heim hat an GERADEN Brettern Weiß** (`HomeWhiteByRule`; geprüft an 2025/26:
  360/360). **Turnier-Zeile**: `Tnr` = `LigamanagerSource.TnrOffset` (900 000 000) + Liga-Id des Ligamanagers
  (`TnrOf`/`IsLigamanagerTnr`/`LigamanagerIdOf`, seit 0.697.1; z. B. 900 002 573 — chess-results ist 7-stellig und reicht
  nie dorthin; die Adresse lässt max. 7-stellige Ids zu, das passt weit in `int`). Hält eine Liga FREMDER Quelle schon
  dieselbe Nummer → `ConflictException`/409 (nur noch Sicherheitsnetz). **Altbestand ohne Versatz** (`Source = ligamanager`,
  `Tnr ≤ TnrOffset`; gab es nie — Dev/Prod hatten beim Umstellen keine Ligamanager-Ligen): bewusst KEINE
  Umschreibe-Migration; `LegacyTnrsAsync` meldet sie beim Start als Warnung, `LeagueRefresh` lässt sie aus (Tnr passt nicht
  zur Id in `SourceRef` → Liga gescheitert, sonst entstünde sie doppelt). Abhilfe: neu einspielen, alte Zeile samt
  Abhängigen löschen (`LeagueClubGames` hängt an `LeagueGames.Id` bzw. dem Schlüssel (Tnr, Runde, Begegnung, Brett) — nach dem
  Umschlüsseln der Liga `LeagueGameLinks.RelinkAsync` bzw. die Heilung beim Start). Neue Spalten `Source` (`null` = chess-results,
  `"ligamanager"`), `SourceRef` („bsb/2026-2027/landesliga-sued-2573"), `Boards` (Bretter je Begegnung, solange keine Runde
  gespielt ist: Anfrage `boards`, sonst die Vorsaison derselben Region+Slug; `LeagueWorld.BoardsOf` nimmt sie vor der
  Tiroler Stufen-Vorgabe). Season „2026/27", League = Überschrift („Landesliga Süd"), Grp leer. **Stufen**
  (`LigamanagerSource.LevelOf`): Oberliga 1, Regionalliga 2, Landesliga 3, Bezirks(ober)liga/Oberpfalz-/Schwaben-/
  Unterfrankenliga 4, Kreis(ober)liga 5, Kreisklasse/A-Klasse 6, B 7, C 8; `LeagueLevels.Max` = 8 — die Merkmalsschleifen
  (QHigher/QLower/NewEver) laufen bis dahin (für Tirol ohne Wirkung, dort gibt es über 4 keine Einsätze), Kurznamen der
  Notizen je Quelle (`LeagueLevels.Short`), der Tiroler Backtest (`hit`) nur für chess-results-Ligen, Ansicht `source` =
  Spielplan-Link. **FIDE-IDs älterer Saisonen**: `FillMissingFideAsync` ergänzt nach jedem Import in ALLEN Ligamanager-Ligen
  fehlende IDs (Meldeliste + Brettpaarungen) bei gleichem Verein (`LeagueNames.Club`) + NameKey, wenn dort genau eine ID
  steht — deshalb ERST die laufende Saison einspielen, dann ältere (sonst fehlen deren Partien in den Karten; erneutes
  Einspielen holt es nach). **Partien → Spielerkarten**: echte Partien (Ergebnis + Züge) mit FIDE-ID aus der Meldeliste im
  Kopf über `LeagueProfileStore.ImportGamesAsync(…, "Ligamanager", skipSameMoves: true)`, Kopf `[LeagueSource "Ligamanager"]`
  (Startseiten-Zählung „SBV-Ligamanager"). Turnier-Zeile + Ersetzen + FIDE-Ergänzung in EINER Transaktion
  (Execution-Strategy), dann Karten, dann `RebuildViewsAsync`. **Endpunkt** `POST /api/league/admin/ligamanager/import`
  (league.manage) `{ url }` oder `{ region, season, slug }` (+ `boards`), `?dryRun=true` liest + zählt nur → `{ tnr, name,
  season, level, dryRun, counts{ rounds, matches, boardGames, boardGamesPlayed, boardPlayersUnmatched, players,
  playersWithFide, pgnGames, pgnGamesWithMoves, pgnGamesUnmatched, colorFromPgn, colorRuleMismatches, boards },
  profileGames, profileGamesWithFide, profilesTouched, fideFilled, views }`; 400 `invalidLeague`, 404 `notFound`, 409
  `conflict`, 503 `unreachable`. **Aktualisieren**: `LeagueRefresh.RunAsync` holt Ligen mit `Source = ligamanager` über
  `LigamanagerSource.ImportAsync` statt über den Crawler (nie `api/league/{tnr}` für sie); Ligen ohne Source bleiben
  chess-results. Erledigt im Mandanten-Schritt (0.698.0): kein `OwnTeam` mehr, die Startseite zeigt nur Ligen der Quelle des
  Vereins (siehe „LeagueHub — Vereine als Mandanten"). **Je Quelle getrennt (0.697.2)**: `LeagueWorld.Mpt` (Mannschaftskämpfe je Team, Schlüssel
  (Quelle, Saison, Stufe), lesen über `MptOf`) und `ClubTeams` (Schlüssel (Quelle, Saison, Verein), `ClubTeamsOf`); die
  Tiroler Vereinsnamen-Regeln von `LeagueNames.Club(team, source)` gelten nur für chess-results (`source` null), in Bayern
  fällt nur die Mannschaftsnummer weg („hall" träfe sonst „Bad Reichenhall").
  Gemessen 07.10.2026 (Probelauf gegen die echten Seiten): Landesliga Süd 2026/27 — 9 Runden, 45 Begegnungen, 0
  Brettpartien, 218 Spieler (196 mit FIDE-ID), kein PGN; 2025/26 — 9 Runden, 45 Begegnungen, 360 Brettpartien (alle mit
  Farbe aus dem PGN, 0 gegen die Regel), 218 Spieler (0 mit FIDE-ID; 100 bekommen sie aus 2026/27), 360 PGN-Partien
  (354 mit Zügen, 280 davon nach der Ergänzung mit FIDE-ID).
- **Schachkreis Zugspitze als dritte Liga-Quelle** (2026-10-07, Schritt 2 von „LeagueHub für SK Weilheim"): die unteren
  Mannschaften von SK Weilheim (II Zugspitzliga, III A-Klasse, IV B-Klasse) spielen im Schachkreis Zugspitze
  (`https://schachkreis-zugspitze.de`, WordPress, kein Ligamanager, kein chess-results). `Services/League/ZugspitzeSource.cs`
  nach dem Muster des Ligamanager-Lesers (HttpClient `Zugspitze`, User-Agent, 1 s Pause, nur dieser Host, Pfade aus Zahlen).
  Seiten je Liga + Saison: `ergebnisse/?Saison=Y&Liga=N` (Liga-Name aus dem zweiten `<title>`, Runden „1.Runde am Sonntag,
  11.10.2026, 10:00 Uhr", Begegnungen mit Mannschaftsergebnis, „6:0 kl" = kampflos, „spielfrei" auch als HEIM → wird zum Gast
  gedreht), `…&Runde=r` NUR für gespielte Runden (Bretter: Pos = Ranglisten-Nr., „Nachname,Vorname", Titel als Präfix „IM …",
  DWZ, Ergebnis Heim-Sicht „1:0"/„½"/„+:-"/„0:0kl"; **Farbe = Feldfarbe am Namen**, `#d47844` dunkel = Schwarz), `ligadaten/?Liga=N`
  NUR laufende Saison (der Parameter Saison wirkt dort nicht; Aufstellung + Spiellokal — die ersten zwei Zeilen, kein
  Telefon/Kontakt; nie anmelden). Ältere Saisonen: Meldeliste aus den Brettern (wer gespielt hat, Nr. = Pos, DWZ der Seite).
  Ersatzspieler der laufenden Saison, die nicht in der Aufstellung stehen, bleiben ohne Meldebrett (`boardPlayersUnmatched`).
  DWZ → `EloN`; **keine FIDE-IDs, kein PGN** (keine Partien in den Spielerkarten). **Tnr** = `ZugspitzeSource.TnrOffset`
  (910 000 000) + Saisonjahr·1000 + Liga-Id (`TnrOf`/`IsZugspitzeTnr`/`RefOf`; Saison 1990–2199, Liga-Id 1–999 → 911 990 001 …
  912 199 999; Zugspitzliga 2026/27 = 912 026 001 — die Liga-Ids des Kreises gelten über die Saisonen, eine Saison braucht die
  Jahreszahl im Schlüssel). Dafür endet der Ligamanager-Bereich jetzt bei 909 999 999 (`MaxLigamanagerId` = 9 999 999, so viel
  lässt seine Adresse ohnehin zu; `IsLigamanagerTnr` prüft beide Grenzen). `SourceRef` „zugspitze/2026-27/1", Ansicht `source` =
  Ergebnis-Seite. **Stufen** (`ZugspitzeSource.LevelOf`, nach dem Liga-Namen): Zugspitzliga (früher „Kreisliga") 5, Kreisklasse 6,
  A-Klasse 7, B-Klasse 8, C-Klasse 9 (Vorrunde Nord/Süd, Endrunde A/B → `League` „C-Klasse", `Grp` „Vorrunde Nord") — anders als im
  Ligamanager sind Kreisklasse und A-Klasse zwei Stufen (ein Verein hat Mannschaften in beiden); `LeagueLevels.Max` = 9, Kurznamen
  ZL/KK/A-Kl/B-Kl/C-Kl, darüber die Ligamanager-Namen. **Nicht eingespielt** (400 `unsupportedLeague`): Senioren-Kreisliga, U12/U16,
  4er-Pokal und die vom Kreis nur gespiegelten Verbandsligen (Oberliga … Bezirksliga, kommen aus dem Ligamanager) — keine Stufe
  in der Liga-Leiter, ihre Einsätze verfälschten QHigher/QLower, das Modell ist an Erwachsenen-Ligen gerechnet. **Farbregel**:
  am echten Bestand (2025/26 + 2026/27, Ligen 1/2/3/5, 946 Bretter) 0 Abweichungen von „Heim hat an GERADEN Brettern Weiß"
  (Ligamanager-Regel); gelesen wird die Feldfarbe, die Regel gilt nur ohne sie. **Endpunkt** `POST /api/league/admin/zugspitze/import`
  (league.manage) `{ url }` oder `{ ligaId, season? }` (leer = laufende Saison) `+ boards`, `?dryRun=true` → `{ tnr, name, season,
  level, dryRun, counts{ rounds, roundsPlayed, matches, boardGames, boardGamesPlayed, boardPlayersUnmatched, players, rosterFrom
  (ligadaten|boards), colorFromPage, colorRuleMismatches, boards }, fideFilled, playersWithFide, views }`; 400
  `invalidLeague`/`unsupportedLeague`, 404 `notFound` (auch: der Kreis zeigt eine andere Saison), 409 `conflict`, 503
  `unreachable`. `LeagueRefresh` holt Ligen mit `Source = zugspitze` über diesen Leser (Tnr muss zu `SourceRef` passen).
  **Region statt Quelle** (`Services/League/LeagueRegions.cs`, die EINE Abbildung Quelle → Region: `null` → `tirol`,
  `ligamanager`/`zugspitze` → `bayern`): `LeagueWorld.Mpt`/`ClubTeams` sind je (Region, Saison, …) geschlüsselt — SK Weilheim 1
  (Ligamanager) und SK Weilheim II (Zugspitze) sind EIN Verein, `sameDay`-Konflikte und Einsätze höher/tiefer sehen beide
  Quellen (die Einsätze `AppsLvl` hingen schon immer nur an Pid). `LeagueNames.Club` streicht in Bayern arabische UND römische
  Mannschaftsnummern („SK Weilheim II" → „SK Weilheim"; „SK Weilheim II S" bleibt). **FIDE-IDs**: `LeagueRegions.FillMissingFideAsync`
  (vorher `LigamanagerSource.FillMissingFideAsync`, das jetzt dorthin weiterreicht) füllt in ALLEN Ligen der Region nach —
  gleicher Verein + NameKey, genau eine ID; daher zuerst die Ligamanager-Ligen der Vereine einspielen. Probelauf 07.10.2026
  gegen die echten Seiten (mit Landesliga Süd 2026/27 aus dem Ligamanager davor): Zugspitzliga 2026/27 (Liga 1) — 9 Runden, 0
  gespielt, 45 Begegnungen, 194 Spieler (25 mit FIDE-ID, Weilheim II 12 von 20); A-Klasse (3) — 1 gespielt, 18 Brettpartien,
  198 Spieler (5; Weilheim III 4/18); B-Klasse (5) — 6 Runden, 1 gespielt, 12 Brettpartien, 101 Spieler (9; Weilheim IV 3/28);
  Vorsaison 2025/26: Zugspitzliga 352 Brettpartien/150 Spieler (25 FIDE), A-Klasse 252/128 (7), B-Klasse 114/70 (3),
  Kreisklasse 168/84 (0); 0 Ersatzspieler ohne Meldeliste, 0 Farben gegen die Regel.
- **Training + Backtest je Region ohne API-Instanz** (0.707.0, Schritt „eigenes Prognose-Modell für Bayern"): `tools/LibraryImport`
  (öffnet nur einen DbContext, `ConnectionStrings__DefaultConnection`; NIE eine zweite `RookHub.Api` gegen Dev) kennt
  `migrate` (Schema per Migrationen — nur für eine WEGWERF-MariaDB), `league-import --source ligamanager --url <Liga-URL>
  [--boards n] [--profiles]`, `league-import --source zugspitze --liga <id> --season <JJJJ/JJ>` und `league-import --batch <datei>`
  (je Zeile `ligamanager <url>` bzw. `zugspitze <liga> <saison>`; ruft `LigamanagerSource`/`ZugspitzeSource.ImportAsync` mit
  `rebuildViews: false`, Spielerkarten nur mit `--profiles` → neuer Parameter `importProfiles`), sowie `league-train --region
  bayern [--holdout S[,S2…]] [--out datei] [--features a,b,…]`. Der Rechenteil steht testbar in der API
  (`Services/League/LeagueTraining.cs`): `LoadWorldAsync` (nur die Ligen der Region), `Dataset` (Python `features.dataset`: je
  gespielter Runde jedes Teams einer Liga-Stufe „Liga", nicht abgebrochen, je Gemeldetem eine `FeatureRow` aus `RowsFor`, Label =
  in `LineupOf`), `Fit` (Python `model.fit`: Newton, L2 = 1 ohne Achsenabschnitt, ≤ 30 Schritte, Abbruch bei Schritt < 1e-6 —
  `LeagueTrainingTests` rechnet die Tiroler `rows.json` nach und trifft `Assets/league-model.json` auf 6 Stellen, läuft nur, wenn
  `~/claude/league-analyzer/rows.json` bzw. `LEAGUE_ROWS_JSON` da ist), `Backtest` (je Mannschaftskampf normiert auf B: „Treffer" =
  Aufgestellte unter den B wahrscheinlichsten wie Tirols `Hits`, Top-1/Top-3 je besetztem Brett, Log-Loss, Kalibrierung in 10
  Stufen; Basis = Meldelisten-Reihenfolge: die ersten B, Brett k ← Gemeldeter k bzw. k…k+2, Log-Loss gleichmäßig B/N) und
  `TrainAsync` (Holdout = jüngste vollständige Saison, je Holdout-Saison nur auf FRÜHEREN gefittet; Vergleich mit dem Tiroler
  Modell auf denselben Gruppen; endgültige Gewichte auf allen Saisonen außer der laufenden, JSON wie `league-model.json`).
  `LeagueModel.Vec` kennt zusätzlich `lvl_n` ((Stufe−1)/8), `kreis` (Stufe ≥ `LeagueModel.KreisLevel` = 5), `kreis_q` (QSame ·
  kreis) und `lvl5`…`lvl9` — die Tiroler Liste bleibt unberührt. **Ligamanager-PGN älterer Saisonen**: der Download antwortet bis
  2018/19 dauerhaft mit 403 („Fehler | Ligamanager") — `FetchAsync` behandelt 403 dort wie 404 (Farben nach der Regel, keine
  Partien für die Karten); Spielplan und Meldelisten sind offen. **Zugspitze-Archiv**: Bretter (Spieler je Runde) gibt es erst ab
  2024/25 — ältere Saisonen kommen nur mit Begegnungen und Mannschaftsergebnis (0 Bretter, 0 Spieler) und taugen weder für
  Merkmale noch fürs Training.
- **Modell je Region** (0.708.0, Wunsch 2026-10-07 „ein eigenes Prognose-Modell für die Region Bayern"): Tirol rechnet weiter
  mit `Assets/league-model.json` (Python, unverändert — `LeaguePythonParityTests`/`LeagueEngineTests` gleich), Bayern
  (Ligamanager + Zugspitze) mit `Assets/league-model-bayern.json` (Embedded Resource). `LeagueModel.FromEmbedded(region)` lädt
  `league-model-{region}.json` (`null` = keins), `LeagueModels` wählt nach `LeagueRegions.Of(tournament.Source)` und fällt für eine
  Region ohne eigenes Modell auf Tirol zurück (Warnung einmal je Region). `LeagueService.RebuildViewsAsync` baut den
  `LeagueViewBuilder` mit `LeagueModels.WithEmbedded(<Tiroler Modell aus DI>)`; der alte Konstruktor mit EINEM Modell bleibt für
  Tests/Parität. Erwartete Treffer `hit` je Region (`LeagueViewBuilder.ExpectedHits`: Tirol `Hits`, Bayern `HitsBayern`) — nur,
  wenn die Region mit ihrem eigenen Modell rechnet; die Treffer-Statistik (`ForecastStatsAsync`) war schon je Region getrennt.
  **Merkmale Bayern** (`LeagueTraining.BayernFeatures`): die Tiroler ohne `lvl2`–`lvl4`/`gk_q`, dazu `kreis`, `kreis_q`,
  `kreis_top`, `kreis_pos` (Wechselwirkungen der Kreisebene ≥ Stufe 5). Reine Stufen-Konstanten (`lvl_n`, `lvlN`) änderten am
  Backtest nichts — die Normierung je Mannschaftskampf hebt eine Konstante je Liga auf. **Datenbasis** (Wegwerf-MariaDB, 07.10.2026):
  Ligamanager Oberliga, Regionalliga Süd-Ost + Süd-West, Landesliga Nord + Süd, Bezirksliga Oberbayern 2019/20 + 2021/22–2026/27
  (2020/21 gab es nicht; bis 2018/19 zeigt der Ligamanager Aufstellungen und Einzelergebnisse NUR angemeldet → leer), Zugspitze
  Ligen 1/2/3/5 2024/25–2026/27 (ältere ohne Bretter). 6 Trainingssaisonen, 3 696 Mannschaftskämpfe (je Team), 71 679 Zeilen; Gemeldete mit
  FIDE-ID 34 % (2019/20) … 52 % (2025/26), 59 % (2026/27). **Backtest** (`league-train --region bayern --holdout
  2022/23,2023/24,2024/25,2025/26`, je Saison nur auf früheren gefittet, 2 702 Kämpfe; Modell Bayern / Tiroler Modell / Basis
  Meldeliste): Treffer 76,9 / 76,5 / 70,5 %, Top-1 je Brett 42,3 / 39,6 / 25,6 %, Top-3 78,5 / 74,2 / 66,2 %, Log-Loss 0,426 /
  0,439 / 0,667 (gleichmäßig); Kalibrierung Bayern-Modell in allen Stufen ±2 Punkte, das Tiroler überschätzt unten (7,6 % → 4,5 %)
  und unterschätzt oben (84,8 % → 88,3 %). Stufen 1–4 klar besser (Top-3 +3…+6 Punkte), Kreisebene 5–8 etwa gleich bis leicht
  schlechter (Top-1 Stufe 7: 35,5 vs. 37,0 %) — dort gibt es erst zwei Saisonen. **Neu trainieren** nach jeder abgeschlossenen
  Saison (frühestens Sommer 2027, dann mit drei Zugspitze-Saisonen) oder wenn neue Ligen/Bezirke dazukommen: Wegwerf-MariaDB
  (`docker run -d --rm --name rh-train-mariadb -e MARIADB_ROOT_PASSWORD=test -p 3399:3306 mariadb:11.8`, Datenbank anlegen,
  `migrate`), `league-import --batch` (je Liga JÜNGSTE Saison zuerst — FIDE-Nachfüllung; Ligamanager-Liga-Ids je Saison auf
  `/{saison}/gesamt`), `league-train --region bayern --holdout … --out src/api/RookHub.Api/Assets/league-model-bayern.json`,
  `HitsBayern` aus der Ausgabe übernehmen, Container entfernen.
- **Österreichische Bundesliga** (0.719.0, Wunsch 2026-10-08: „ergänz LeagueHub in Österreich um die höheren Ligen (Bundesliga 1 & 2)"):
  1. Bundesliga und 2. Bundesliga (Ost/Mitte/West) kommen wie die TMM von chess-results (Veranstalter ÖSB, `Source` null = Region
  `tirol`, Tnr = chess-results-Nummer, „Daten aktualisieren" holt sie über den Crawler mit). **Stufen um zwei verschoben**
  (Entscheidung „(a)", `LeagueLevels`): 1 = 1. BL, 2 = 2. BL, 3 = Landesliga, 4 = 1. Klasse, 5 = 2. Klasse, 6 = Gebietsklasse; Migration
  `LeagueTirolLevelsBundesliga` (`Level + 2 WHERE Source IS NULL`, Bayern bleibt; Ansichten tragen die alte Stufe bis zum nächsten
  Rechnen), Bündel-Import der Python-Fassung über `LeagueLevels.FromTmm` (1–4 → 3–6). **Modell unverändert**: `Assets/league-model.json`
  heißt `lvl4`/`lvl5`/`lvl6` statt Pythons `lvl2`–`lvl4` (Gewichte gleich, Feld `levels` erklärt es), `gk_q` = q_same in Stufe 6 —
  `LeagueBundesligaTests.TirolModel_LogitOfAShiftedRow_EqualsThePythonLogit`, `LeagueTrainingTests` (rows.json über `FromTmm`) und die
  Python-Parität (lokal mit dem Prod-Bündel: 88 Begegnungen, 1 676 Wahrscheinlichkeiten, Abweichung 0,000000 — der Test selbst scheitert
  seit 0.66x an den alten `locked`-Status der Python-Ansicht) liefern dieselben Zahlen. Backtest `Hits` sitzt auf 3–6, für die
  Bundesliga gibt es keinen (`hit` fehlt). Bretter ohne Brettpaarungen `LeagueLevels.DefaultBoards` (Tirol: 1–4 → 6, 2. Kl 5, GK 4;
  BL 2024/25–2026/27 nachgezählt 6; Bayern unverändert). **Runden-Blöcke** (`LeagueLevels.HasRoundBlocks`/`Consecutive`, ersetzt die
  Regel `level == 1`): Tirol Stufen 1–3 (Bayern weiter nur Stufe 1); Runden am Tag danach — in der Bundesliga auch am SELBEN Tag
  (Doppelrunde; die TMM hat Platzhalter-Termine wie 01.01.2022 für drei Runden, dort nicht) — bilden einen Block: offen ist der ganze
  Block der nächsten Runde, `unlock_after` = die Runde vor dem Block, die Vorab-Mischung „Vortag gespielt ja/nein" rechnet rekursiv
  über den Block (`SundayAdvance(rowsSat, pSat, rowsSun)`; Landesliga = dieselben Zahlen), eine am selben Tag schon gespielte Runde
  zählt mit (`asof` = Tag danach). Spielpläne laut Crawler: 1. BL 2026/27 25.–29.11.2026 + 10.–14.03.2027 (Runden 8 und 9 am 12.03.),
  2. BL Fr–So und Sa–So. **Einsätze je Region** (`LeagueWorld.AppsLvl` = (Region, Saison, Stufe, Spieler), `Apps(source, …)`): BL-Spieler
  stehen auch in bayerischen Ligen. QHigher der Landesliga zählt jetzt Bundesliga-Einsätze der Vorsaison mit (so ist das Merkmal
  gemeint; ohne eingespielte BL-Saisonen = 0). **Vereinsnamen** (`LeagueNames.Club`, Tirol): „Schachklub/Schachclub Schwaz" → „Schwaz",
  „Innsbruck Pradl" → „Innsbruck-Pradl", „rum|hall" nur noch an Wortgrenzen (traf „SK Elektro Strobl Hallein") — an allen 56 TMM-Namen
  seit 2009 unverändert; `LeagueClub.OwnsTeam` vergleicht in Tirol zusätzlich den kanonischen Verein (Schwaz spielt 2026/27 als
  „Schachklub Schwaz" in der 1. Bundesliga, 2024/25 + 2025/26 als „Schachclub Schwaz" in der 2. BL West → die Startseite zeigt die BL
  dem Verein ohne „alle Ligen").
- **Oberfläche** (0.571.0): viertes Angular-Projekt `leaguehub` (`src-leaguehub/`, `public-leaguehub/`, Image
  `ghcr.io/kahalm/rookhub-leaguehub:{dev,latest}` aus demselben Dockerfile, `APP_PROJECT=leaguehub`, Host-Port Dev
  **8099** / Prod **8100** — 8098 hält bis zum Umschalten noch der Python-Stack). Routen `/` (Liga/Runde/Verein,
  `authGuard`; ohne `league.view` „Nicht freigeschaltet"), `/verein*` (Vereins-Datenbank, siehe unten), `/s/:token` (geteilte Begegnung OHNE Anmeldung), dazu RookHubs
  Masken über `@rh/*` (`/login`, `/register`, …, `/impressum`, `/privacy`; `/privacy` zeigt mit `LEGAL_SITE.kind`
  `'leaguehub'` den Abschnitt über die Ligaspieler ohne Konto (Art. 14 DSGVO, Codereview F7-006) — bei neuen
  Datenquellen/Freigaben dort nachziehen; die Frist des IP-Prüfwerts ist nach dem Ist-Stand beschrieben (geleert nur
  nach dem nächsten anonymen Upload), nach einer Retention-Senke den Text kürzen). Die Seite ist deutsch
  (`LocaleService.applyUnsaved('de')`, die Wahl aus RookHub bleibt), hell/dunkel über den geteilten `ThemeService`,
  eigene Gestaltung in `src-leaguehub/leaguehub.scss` (Barlow, nach `src/styles.scss` geladen). Kein Service Worker —
  die Prognosen sollen frisch vom Server kommen. `partner-site.ts` kennt `leaguehub(-dev)` nur fürs geteilte
  Sprach-/Design-Cookie, einen Sprung aus RookHub gibt es nicht. Bausteine: `shared/fixture-view.component.ts`
  (Bretter, Meldeliste, WhatsApp-Text = drei Kandidaten je Brett, „Link teilen" nur mit `league.manage`),
  `shared/player-card.component.ts` (Dialog, Vorgabe = Farbe an diesem Brett), reine Regeln in
  `core/league-format.ts`. „Daten aktualisieren" fragt alle 4 s `/api/league/update/status` nach und lädt danach frisch.

### LeagueHub — Vereine als Mandanten (0.698.0)

Wunsch 2026-10-07: „LeagueHub für mehrere Vereine — SK Weilheim (Bayern, Landesliga Süd im Ligamanager) bekommt dieselbe
Funktionalität, streng getrennt von Schwaz." Es bleibt EIN LeagueHub, EINE API, EINE Datenbank, EIN Konto. Was „wir" heißt,
hängt am Verein; die öffentlichen Liga-Daten (Spielpläne, Meldelisten, Spielerkarten samt der Vereinspartien darin,
Online-Konten, Übertragungen, Megabase) sind für alle Vereine dieselben.

* **Tabellen** `LeagueClubs` (Id, Name, TeamPrefix, AnonName, Region — bis 0.703.0 `Source`, siehe unten —, CreatedAt) und `LeagueClubMembers` (ClubId, GroupId; eine
  Gruppe gehört zu höchstens einem Verein). Migration `LeagueClubs`: 1 = SK Schwaz (`Schwaz`/`Schwaz`, chess-results), 2 = SK
  Weilheim (`SK Weilheim`/`Weilheim`, `ligamanager`); **alle Bestandszeilen** von `LeagueClubGames`, `LeagueClubDrafts`,
  `LeagueBatchUploads`, `LeagueShares` und die Liga-Einlesungen in `ScoresheetScans` bekommen `ClubId = 1`; **jede Gruppe, deren
  Rollen `league.view` tragen** (außer „Everyone"), gehört Verein 1 — aus dem Code nicht ableitbar, auf Dev und Prod ist das genau
  die Gruppe „Schwaz". Fremdschlüssel auf `LeagueClubs` mit Restrict. Weilheims Gruppe ordnet der Admin zu
  (`POST /api/league/admin/clubs/2/groups/{groupId}`).
* **Was am Verein hängt**: Vereinspartien, Entwürfe, Partieformulare (Liga-Einlesungen), Stapel-Uploads, Teilen-Links (ein Link
  gehört dem Verein, der ihn erzeugt — darüber laufen die anonymen Uploads), die Anonymisierung (`AnonName`), „einer von uns"
  (`LeagueClub.OwnsTeam`: Mannschaftsname = `TeamPrefix` oder beginnt mit `TeamPrefix` + Leerzeichen/„/", ohne Groß/klein —
  „SK Weilheim 1" ja, „SK Weilheimer" nein), der Taktik-Kurs (`tactics-club-{id}.pgn`), die Zeile „Vereins-Datenbank" der
  Quellen-Tabelle, die Paarungen gespielter Runden (Vereinspartie nur aus dem eigenen Verein), die Startseite (nur Ligen der Region,
  seit 0.710.0 nur die mit eigener Mannschaft), die Treffer-Statistik (nur Ligen von `Source`). **Global** bleiben: `LeagueNameAliases` (PGN-Name → Spieler ist
  eine Aussage über öffentliche Ligaspieler, sie verweist bewusst weder auf Partie noch auf Verein oder Nutzer —
  vereinsübergreifend harmlos), `LeagueSelfReports`/Online-Konten (hängen an FIDE-IDs), der Meldelisten-Index (je Verein nur mit
  eigener `OwnClub`-Regel, gecacht je Verein), Zuordnen nach dem Anmelden (der Schlüssel des Browsers sagt, was ER hochgeladen
  hat), „Daten aktualisieren" und die Admin-Importe.
* **Zugehörigkeit + Rechte**: über die Gruppen des Kontos (`UserGroups` → `LeagueClubMembers`); Admins gehören zu ALLEN Vereinen.
  Die Rechte `league.view/contribute/manage` bleiben Rollen wie bisher und wirken in den Vereinen des Kontos (kein Recht je
  Verein). Ein Konto mit Recht, aber ohne Vereinsgruppe, bekommt 403 `noClub` (auf Prod hatten die direkt vergebenen
  League-Rollen nur Admins). „Everyone" zählt nicht (trägt auch keine Rollen, `PermissionResolver`).
* **Der Verein einer Anfrage — EINE Stelle**: `Services/League/LeagueClubResolver.cs`, im Controller über
  `BaseApiController.LeagueClubAsync` aufgelöst und als `LeagueClub` in die Dienste gereicht (kein Dienst fragt selbst nach
  Zugehörigkeit). `?club=<id>` (LeagueHub schickt ihn an JEDEN `/api/league/…`-Aufruf außer `/me` und `/s/…`); ohne Parameter
  der einzige Verein des Kontos, bei einem Admin der einzige Verein SEINER Gruppen; sonst 400 `clubRequired`. Fremder Verein →
  403 `forbidden` (Admin: unbekannter → 404 `unknownClub`), keine Zahl → 400 `invalidClub`. Ausnahme `GET …/club/games/{id}` und
  `…/evals` (RookHubs Partie-Seite `/club-games/{id}` und die Taktik-Links kennen keinen Verein): ohne `?club=` und bei mehreren
  Vereinen gilt der der Partie, wenn das Konto dazugehört (`preferred`). Teilen-Link-Wege (`/api/league/s/{token}/…`): der
  Verein kommt aus `LeagueShares.ClubId` (`LeagueService.ShareContextAsync`), nie aus der Anfrage.
* **Kein zweiter globaler Query-Filter** für `ClubId` (Kommentar in `LeagueClubGameConfiguration`): EF Core kennt je Typ EINEN
  Filter, ein Filter auf einen Wert der Anfrage hinge am DbContext (Hintergrund-Dienste lesen alle Vereine), und ein
  `IgnoreQueryFilters` würde den Archiv-Filter mit abschalten. `LeagueClubService.Games(club)` ist der Einstieg jedes Wegs;
  `IgnoreQueryFilters` (Rückbau eines Links) hält den Verein ausdrücklich in der Bedingung. Trennung geprüft in
  `LeagueClubTenancyTests`.
* **Hintergrund über alle Vereine**: `MasterAnalysisScheduler`/`ClubSecondEngineScheduler` (alle Vereinspartien),
  `LeagueAnalysisQueue` (Gegner der nächsten Runde JEDES Vereins zuerst), `LeagueRefresh.StalePlayersAsync` (ebenso),
  `TacticHarvestService` (je Partie der Kurs ihres Vereins; Umbenennen alter Titel je Verein).
* **RookHub**: eine Kopie in „Meine Partien" wird nur mit einer Vereinspartie aus einem Verein ihres Besitzers verbunden und
  übernimmt nur deren Analyse (`SavedGameService.ClubGameForMovesAsync`/`ClubAnalysisForMovesAsync`/`LinkClubCopiesAsync`,
  `LeagueClubResolver.ClubIdsOfAsync`); korrigieren über die Kopie darf der Hochladende oder ein Verwalter DES Vereins der Partie
  (`LeagueClubResolver.CanManageAsync`). Stapel-Uploads zeigen im Admin-Tab den Verein (`club`), die manuelle Einlesung
  (`manual-scan`) gehört dem Verein des Stapels (Bilder zweier Vereine → 400 `mixedClubs`).
* **Verwaltung** (`Services/League/LeagueClubAdminService.cs`, nur Admins mit `league.manage`): Verein anlegen/ändern, Gruppe
  zuordnen (eine andere Zuordnung derselben Gruppe wird ersetzt; der Taktik-Kurs wird freigegeben bzw. beim Lösen entzogen),
  Kursname folgt dem Vereinsnamen.
* **Oberfläche (0.699.0)**: `ClubContextService` + `leagueClubInterceptor` in `src-leaguehub` (Details in
  `src/frontend/CLAUDE.md`): `GET /api/league/me` einmal je Konto, `?club=` an jeden Aufruf, Umschalter im Kopf bei mehreren
  Vereinen (gemerkt in `lh-club`, Wechsel lädt neu), alle Texte mit „Schwaz" lesen Name/`AnonName` des Vereins, Teilen-Seiten
  den Verein des Links.
* **Region statt Quelle (0.704.0)**, Wunsch 2026-10-07 („Weilheim muss Ligamanager UND Zugspitze sehen"): `LeagueClub.Region`
  (`tirol` | `bayern`, NOT NULL, Vorgabe `tirol`) ersetzt `Source`; Migration `LeagueClubRegion` benennt die Spalte um (kein
  Drop+Add) und schreibt null → `tirol`, `ligamanager` → `bayern`. Welche Quellen eine Region hat, steht NUR in
  `Services/League/LeagueRegions.cs` (`SourcesOf`, `InRegion` als SQL-Bedingung — Tirol `Source IS NULL`, Bayern `IN (…)`).
  Startseite (`IndexAsync`: laufende Saison = jüngste der Region, Ligen nach Stufe, dann Quelle, dann Gruppe) und
  Treffer-Statistik (`ForecastStatsAsync(region)`) filtern nach der Region; `ClubJson` trägt `region`. Verwaltung: Auswahl
  „Region" statt „Liga-Quelle", 400 `invalidRegion` (eine Quelle wie `ligamanager` ist KEINE Region). Geprüft in
  `LeagueClubTenancyTests.Index_BavarianClubSeesLigamanagerAndZugspitze_TyroleanClubNeither`, gegen MariaDB in
  `LeagueRegionSqlTests` und `MigrationsTests.LeagueClubRegion_MachtAusDerQuelleDieRegion`.
* **Nur Ligen mit eigener Mannschaft (0.710.0)**, Wunsch 2026-10-07 („Zeig bei der Ligaauswahl nur die Ligen, in denen der
  Verein vertreten ist"): `LeagueService.IndexAsync(club, ct, all)` filtert die Ligen der Region auf die mit einer Mannschaft
  des Vereins (`LeagueClub.OwnsTeam` über DISTINCT `LeagueMatches.Home/Away` + `LeaguePlayers.Team` — drei Abfragen
  für alle Ligen zusammen, nicht je Liga; das `teams` der Ansicht stammt aus demselben Spielplan, hieße aber jede Ansicht samt
  Prognosen zu laden). `?all=true` liefert für Verwalter (`league.manage` live über `PermissionResolver`, Admin) alle Ligen der
  Region; ohne das Recht wird der Schalter still übergangen statt 400 (ein gemerkter Schalter nach entzogenem Recht soll die
  Seite nicht leer machen). Antwort `filtered` + `total`. Oberfläche: Verwalter-Schalter „alle Ligen der Region"
  (`lh-all-leagues`, Vorgabe aus), gemerkte fremde Liga → erste eigene, ohne eigene Liga die Karte „Noch keine Liga mit einer
  Mannschaft von <Verein>" (Verwalter: „Alle Ligen zeigen"). Teilen-Links und die geteilten Liga-Daten (`/{tnr}`) bleiben
  ungefiltert — eine fremde Liga per `?liga=` öffnet die Seite nicht mehr, `GET /api/league/{tnr}` liefert sie aber weiter.
  Geprüft in `LeagueClubTenancyTests.Index_OnlyLeaguesWithAnOwnTeam_AllOnRequest`/`IndexEndpoint_AllOnlyForManagers_…` und
  gegen MariaDB in `LeagueRegionSqlTests`.
* **Vereinsverwaltung `/vereine` (0.700.0)**: Reiter „Vereine" in LeagueHub, nur Admins mit `league.manage` (sonst
  Sperrkarte; der Server verlangt beides). Liste (Name, `anonName`, `teamPrefix`, Quelle, Gruppen, Vereinspartien, angelegt),
  Anlegen/Ändern (400/409 `reason` → Klartext; Quelle umstellen mit Rückfrage — `PUT` darf alle vier Felder ändern, die
  Startseite filtert nach `Source`), Gruppen zuordnen über eine Suche in `GET /api/admin/groups` (`groups.manage`, Admins haben
  jedes Recht; „Everyone" und Gruppen ANDERER Vereine nicht wählbar, obwohl der Server eine andere Zuordnung ersetzen würde)
  und lösen mit Rückfrage. Nach jeder Änderung `ClubContextService.reload()` (neues `/api/league/me`, gewählter Verein
  bleibt). Seite `src-leaguehub/app/features/clubs/clubs-page.component.ts`.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/index` | `league.view` | Startseite `{ season, club, generated, filtered, total, leagues[{ tnr, name }] }` — seit 0.710.0 nur Ligen der Region, in denen der Verein eine Mannschaft hat (`OwnsTeam` über Spielplan + Meldelisten); `?all=true` = alle der Region, nur mit `league.manage` (sonst still übergangen, `filtered: true`, kein 400); `total` = Ligen der Region mit Ansicht |
| GET | `/api/league/me` | angemeldet | `{ clubs[{ id, name, anonName, teamPrefix, region }], current }` (`region` seit 0.704.0, vorher `source`) — `current` = Verein ohne `?club=` (`null` bei mehreren ohne Vorgabe) |
| GET | `/api/league/admin/clubs` | Admin + manage | Alle Vereine `[{ id, name, anonName, teamPrefix, region, createdAt, clubGames, groups[{ id, name, members }] }]` — `clubGames` ohne archivierte, `members` = Konten der Gruppe (0.700.0) |
| POST | `/api/league/admin/clubs` | Admin + manage | `{ name, teamPrefix, anonName, region }` (`tirol`/`bayern`, leer = `tirol`) → Verein; 400 `invalidName`/`invalidTeamPrefix`/`invalidAnonName`/`invalidRegion`, 409 `duplicate` |
| PUT | `/api/league/admin/clubs/{id}` | Admin + manage | Ändern (fehlende Felder bleiben, `region: ""` = `tirol`); 404 |
| POST | `/api/league/admin/clubs/{id}/groups/{groupId}` | Admin + manage | Gruppe zuordnen → 204; 404 `clubNotFound`/`groupNotFound`, 400 `everyone` |
| DELETE | `/api/league/admin/clubs/{id}/groups/{groupId}` | Admin + manage | Gruppe lösen → 204 / 404 |

Alle anderen angemeldeten LeagueHub-Endpunkte (`/api/league/index`, `/sources`, `/forecast-stats`, `/{tnr}/round/{r}/games`,
`/share`, `/api/league/club/…`) nehmen `?club=`; ohne Wirkung ist er bei den geteilten Liga-Daten (`/{tnr}`, `/player/…`,
`/accounts/…`, `/suggestions/…`, `/admin/…`). `GET /api/league/index` trägt zusätzlich `club` (der Verein der Anfrage),
`GET /api/league/s/{token}` trägt `club: { id, name, anonName }` (der Verein des Links).

### LeagueHub — Vereins-Datenbank (0.573.0, Übersicht/Teilen-Link/Megabase 0.574.0)

Mitglieder von SK Schwaz laden Partien hoch — viele auf einmal als PGN oder EIN Partieformular (Foto, gelesen vom
Formular-Leser, geprüft in LeagueHub) —, angemeldet ODER ohne Konto über einen Teilen-Link, und die Spielerkarten der
Gegner zeigen sie mit (Quelle „Verein"). Regeln (`Services/League/LeagueClubService.cs`, alle vom Nutzer vorgegeben,
2026-09-28):

* **Beide Namen gegen die Meldelisten** (`LeagueRosterIndex`, alle Saisonen): FIDE-ID aus der Partie zuerst, dann der
  Name in drei Stufen (alle Namensteile in beliebiger Reihenfolge → Nachname + erster Vorname → Nachname +
  Anfangsbuchstabe), ohne Groß/klein, Akzente, akad. Titel, Umlaute in beiden Schreibweisen. Nennt die Partie NUR einen
  Nachnamen („Kostic", 0.576.0), gilt als vierte Stufe der Nachname allein — eindeutig nur bei genau einem Ligaspieler
  (`Hit.LastNameOnly`, die Übersicht zeigt „nur Nachname — prüfen"), sonst mehrdeutig mit Kandidaten. Mehrdeutig = Ligaspieler
  OHNE FIDE-ID, die Kandidaten gehen zur Auswahl mit. Eine Meldelisten-Zeile OHNE FIDE-ID gehört zu der Person MIT
  FIDE-ID, deren Name genau so lautet, wenn es genau eine gibt (0.575.1 — 2022/23 steht „Hengl Philip" ohne Komma und ID
  neben „Hengl, Philip" mit ID; als zwei Personen war jeder Abgleich „mehrdeutig"). **Dieselbe Person zweimal** (0.597.0,
  gemeldet 2026-09-29): ohne FIDE-ID zählt der NAME (alle Namensteile, Reihenfolge und Komma egal), nicht seine Schreibweise —
  „Lenk Markus" und „Lenk, Markus" waren zwei Personen, auf Dev 123 solcher Paare, jedes „mehrdeutig"; und gleicher Name +
  gleicher Verein (ohne Mannschaftsnummer, `ClubBase`) unter verschiedenen FIDE-IDs ist EIN Mensch, es gilt die ID der
  jüngsten Saison, die übrigen führen über `ByFide` zu ihm (`CanonicalFides`; „Forster, Stephan" 24649651 → 24652091 — die
  alte kennt FIDE nicht —, „Perez Rodriguez" 168265 → 1682865). Namensvettern in verschiedenen Vereinen bleiben mehrdeutig. Eine fremde FIDE-ID in der Partie lässt nur Ligaspieler ohne
  eigene ID als Namenstreffer zu. **Wer in keiner Meldeliste steht, wird im Megabase-Verzeichnis gesucht** (0.576.0,
  Wunsch „standardmäßig auf Megabase matchen, wenn in Tirol kein Treffer"; `LeagueMegaPlayers.Lookup`, EINE Abfrage je
  500 Namen für die ganze Übersicht): über die FIDE-ID, sonst den Namen — „Nachname, Vorname" wie im Verzeichnis, ohne
  Komma beide Reihenfolgen. Eindeutig heißt: die Treffer tragen höchstens EINE FIDE-ID (dann gilt der mit ihr), ganz ohne
  ID nur bei einem einzigen Namen; zwei Namensvettern mit verschiedenen IDs sind „nicht erkannt". So eine Seite heißt
  „nicht in Liga" (`match.mega`), Name und FIDE-ID kommen von dort. **Bekannt = Ligaspieler ODER Megabase**: ist keine
  Seite bekannt → `noLeaguePlayer`; bleibt nach dem Ersetzen keine bekannte übrig → `onlyOwnClub`. Eine Partie, deren
  Gegner nur die Megabase kennt, ist übernehmbar, die Übersicht wählt sie aber NICHT vor (`optionalGame` in
  `import-review.ts`); wer an einer Partie einen Spieler wählt oder einen Namen übernimmt, wählt sie damit zum Import aus.
* **Ähnliche Namen zur Schnellauswahl** (0.596.0, Wunsch „wenn du Namen nicht direkt findest, schau, ob ein ähnlicher Name
  bei den Ligaspielern existiert, und stell ihn zur Schnellauswahl — ohne dass man bearbeiten muss"; `LeagueRosterIndex.Similar`):
  bleibt eine Seite unerkannt (weder Liga noch Megabase noch gemerkte Zuordnung, nicht mehrdeutig), trägt ihr Abgleich
  `similar` = höchstens drei Ligaspieler. Verglichen wird je Namensteil (Reihenfolge egal, Titel weg, Umlaute in beiden
  Schreibweisen) mit Tippfehler-Abstand (Damerau, benachbarte Vertauschung = ein Fehler), erlaubt je Teil nach Länge: unter 5
  Buchstaben KEINER („Wolf" ≠ „Golf"), bis 7 einer, sonst zwei; ein Anfangsbuchstabe muss passen; ein exakter Treffer ist
  keiner (den findet schon der Abgleich). In der Übersicht (`quickPicks` in `import-review.ts`) und der Formular-Prüfung stehen
  sie als „Meintest du …"-Knöpfe unter dem Namen — ein Klick ist `choosePerson`, also samt Weitergabe an gleichnamige Seiten und
  Merken beim Import. Je PGN-Name wird einmal gerechnet (Übersicht mit 500 Partien).
* **Gemerkte Zuordnungen** (0.579.0, Wunsch „wenn ich einen Spieler umbenenne, merk dir das zum Original und matche das
  zukünftig bei allen selbst"; `LeagueNameAliases`, Tabelle `LeagueNameAliases`): landet beim Import eine Korrektur (Spieler
  gewählt oder Name getippt) bei jemand ANDEREM als die Vorgabe, merkt `ImportPgnAsync` den PGN-Namen (`KeyOf`: klein, ohne
  Akzente und Titel) → FIDE-ID + Name; die jüngste Korrektur gewinnt. Abgleich-Reihenfolge (`LeagueClubService.Resolve`):
  FIDE-ID der Partie, die ein Ligaspieler trägt → gemerkte Zuordnung → Meldelisten → Megabase; eine vom Nutzer gewählte
  FIDE-ID schlägt die Zuordnung. Gilt in Übersicht, Import, Formular-Abgleich (`match`) — für ALLE, auch über Teilen-Links,
  gemerkt wird aber nur mit Konto (ein Teilen-Link soll nicht festlegen, wer ein Name für alle ist). Die Zeile verweist
  bewusst weder auf eine Partie noch auf den, der korrigiert hat. Im selben Import überträgt die Seite eine Korrektur auf jede
  andere noch nicht gesetzte Seite mit demselben PGN-Namen (`ImportReview.propagate`). Ein Formular merkt (noch) nichts — der
  gelesene Name geht nicht mit.
* **Spieler von Schwaz werden durch „Schwaz" ersetzt** (Vorgabe: jeder, der in seiner JÜNGSTEN Saison für Schwaz gemeldet
  ist — `Person.OwnClub`; wer weggegangen ist, ist jetzt ein Gegner —, dazu der Hochladende laut Profil; je Seite
  umschaltbar): ohne Elo und FIDE-ID, die Veranstaltung fällt weg, und es wird **weder gespeichert, wer dahinter steht,
  noch wer hochgeladen hat oder wann** (`UploadedByUserId`/`CreatedAt` leer, auch nicht versteckt). Ohne Konto
  (Teilen-Link) wird der Hochladende nie gespeichert. **Seit 0.648.0 steht der ECHTE Spieler hinter „Schwaz" intern**
  (`LeagueClubGame.WhiteRealName/WhiteRealFide/BlackRealName/BlackRealFide`, Wunsch 2026-10-04: „für spätere
  Auswertungen, niemals in der GUI ausgeben") — gesetzt in `Build` und beim Korrigieren (eine unveränderte Seite behält
  ihn). Diese Spalten gehen in KEIN DTO, kein PGN, keine Analyse, keine Spielerkarte, keinen Teilen-Link, keine Suche
  (`Import_Replaced_KeepsTheRealNameInternally_ButNoOutputCarriesIt`); Häkchen-Text und Datenschutzerklärung nennen es.
  Partien von vor 0.648.0 haben sie leer.
* **Über einen Teilen-Link hochgeladen** (Codereview 2026-09-29, A2-009 — vorher 500 Partien je Aufruf ohne jede Herkunft):
  jede Partie trägt den Link als SHA-256 (`UploadShareHash`, `LeagueClubService.ShareHashOf`; der Link selbst steht
  nirgends) — AUCH eine „Schwaz"-Partie: der Link ist der Weg, nicht die Person, und nur so entfernt der Rückbau alles.
  Den Zeitpunkt bekommt nur eine Partie ohne „Schwaz", eine IP wird bewusst NICHT vermerkt (ein Absender-Pseudonym an der
  Partie widerspräche „der Hochladende wird nie gespeichert"). Gedeckelt (`LeagueShareUploadQuota`, Singleton, im
  Arbeitsspeicher — über `CreatedAt` gezählt, liefen „Schwaz"-Partien durch): höchstens 50 Partien je Aufruf und 200 je
  Link und UTC-Tag, auch das Formular-Add ohne Foto; der Rest steht mit Grund `shareLimit` in `failed` (bzw. 400). Die
  Plätze werden vorher reserviert und, was nicht gespeichert wird, zurückgegeben (ein abgerissener Aufruf verbraucht
  nichts). „Alle Partien dieses Links entfernen": `DELETE /api/league/club/admin/shares/{token}/games` (Verwalter).
  Hash und Deckel hängen am Token der Link-ZEILE (`LeagueService.ValidShareTokenAsync`), nie am Wert aus der Route:
  `LeagueShares.Token` vergleicht in MariaDB groß/klein- und akzent-blind, „abc…"/„Ábc…" sind derselbe gültige Link wie
  „AbC…" — sonst hätte jede Schreibweise ihren eigenen Topf, und der Rückbau per Original fände ihre Partien nicht. Der
  Rückbau löst das Token ebenso über `LeagueShares` auf (ohne Ablauf-Filter; ist die Zeile weg, zählt der Wert, wie er kommt).
* **PGN = zwei Schritte**: `games/preview` liest und gleicht ab, speichert NICHTS — je Partie wer gegen wen, Abgleich,
  Vorgabe „ersetzen", `duplicate`, harter Fehler (`illegal`/`noMoves`/`tooLong`/`fromPosition`). Die Seite
  (`features/club/import-review.ts`, dieselbe Regel wie `Build`) zeigt je Partie, ob sie übernommen wird; der Nutzer
  korrigiert unerkannte/falsche Spieler (Kandidat, Vorschlag aus der Meldeliste, getippter Name), schaltet „ersetzen"
  um, wählt Partien ab. Filter (0.577.0, je mit Anzahl): alle · nicht importiert · noch nicht vorhanden (keine Dublette)
  · nicht erkannt; die Liste zeigt das Turnier (`[Event]`) zum Zuordnen — gespeichert wird es weiter nur ohne „Schwaz". `games/import` bekommt DENSELBEN PGN-Text + je übernommener Partie die Entscheidung
  (`LeagueClubSideDecision`: FIDE-ID eines gewählten Ligaspielers schlägt den Namen; ohne Angabe die Kopfzeile) und
  prüft alles noch einmal. Nummerierung 1-basiert, gleich in beiden Schritten.
  **Große Listen in Paketen** (0.598.1, Wunsch „auch beim PGN-Upload alle einlesen und dann in Paketen anbieten"): passt
  eine Liste nicht in EINE Übersicht (`MaxImportGames` 500, `MaxImportChars` 5 Mio.), teilt die Seite sie VORHER
  (`src-leaguehub/app/core/pgn-portions.ts`, `pgnPortions`: 500 Partien bzw. 4,5 Mio. Zeichen je Paket) — für JEDE Quelle
  (Datei, eingefügt, Lichess-Studie, ChessBase), in `startPreview`. Das erste Paket geht in die Übersicht, die übrigen
  werden danach als offene Listen abgelegt („Datei.pgn (Teil 2 von 3)", Quelle wie das Original); am Deckel der Entwürfe
  (20 je Konto, 5 je IP) sagt die Seite, wie viele fehlen. Getrennt wird mit der Regel des Servers
  (`splitGameBlocks` = SPIEGEL von `PgnParser.SplitGamesCore`: Kopfzeile nach Zugtext, wiederholte Kopfzeile, nichts in
  einem offenen `{…}`), Partien ohne Zugtext zählen nicht — sonst hätte ein Paket beim Server mehr als 500 und würde
  gekappt. Eine Liste, die passt, geht UNVERÄNDERT raus. `truncated` der Übersicht bleibt als Rückfall stehen.
  **Importiert wird PORTIONSWEISE** (0.590.0, Wunsch „damit Progress nicht verloren geht“): die Übersicht liefert je
  Partie ihren eigenen PGN-Text (`games[].pgn`, Kopfzeilen roh wie gelesen, Zugtext unverändert; fehlt bei harten
  Fehlern), die Seite (`club-import-review.component.ts`) schickt je 10 Partien genau deren Text mit Nummern 1…n —
  jede Portion wird sofort gespeichert. 10 statt 1, weil der globale Deckel 100 Anfragen/min je IP ist (Teilen-Link 60):
  500 Partien sind so 50 Anfragen. Eine abgerissene Portion wird zweimal wiederholt (429 wartet 20 s), eine Absage (400)
  nicht; war sie schon gespeichert und nur die Antwort verloren, zählt der Server sie als doppelt. Scheitert es endgültig,
  bleibt das Gespeicherte, und „Weiter importieren (n übrig)“ schickt nur den Rest. Ohne `pgn` (älterer Server) geht
  alles in einer Anfrage wie vorher.
* **Entwürfe** (0.595.0, Wunsch „wenn jemand eine neue Ligapartie einträgt — egal wie — soll sie gleich online abgelegt
  werden, damit ein Admin den Import fertigstellen kann“; `Services/League/LeagueClubDraftService.cs`, Tabelle
  `LeagueClubDrafts`): beim Lesen der Übersicht legt die Seite die Liste sofort als Entwurf ab (Datei, eingefügt,
  Lichess-Studie, Sprung aus RookHub), speichert den Stand der Übersicht gedrosselt (`ImportReview.snapshot`, für den
  Server opak) und nach jeder Portion die importierten Nummern. „Deine offenen Listen“ nimmt sie wieder auf
  (`ImportReview.restore` auf einer frischen Übersicht; schon importierte Partien sind dann Dubletten; seit 0.597.1 kommt aus
  dem Stand nur, was der Nutzer SELBST gesetzt hat — jede andere Seite nimmt den frischen Abgleich, „ersetzen" bleibt, solange
  die Seite gleich erkannt wird), Verwalter sehen
  unter „Offene Listen anderer“ ALLE (auch über Teilen-Links) und stellen fertig. **Mit `draftId` rechnen Übersicht und
  Import für den EINREICHER** (`ActingUserAsync`): seine Seite „du selbst“ wird ersetzt, die Partien tragen ihn als
  Hochladenden, ohne Konto eingereicht niemanden — nicht den Verwalter. Fertig importiert oder verworfen wird die
  Zeile GELÖSCHT (der Rohtext nennt die Spieler von Schwaz mit Namen), ebenso nach 30 Tagen ohne Bewegung und beim
  Kontolöschen. Deckel: 20 offene je Konto, 5 je IP ohne Konto (HMAC wie bei den Formularen); ohne Konto gehört der
  Entwurf dem Browser mit dem Schlüssel (`lh-anon-drafts`). „Schließen" in der Übersicht lässt den Entwurf liegen;
  endgültig verwerfen nur aus „Deine offenen Listen“/„Offene Listen anderer“ mit Rückfrage (bei fremder Liste mit dem
  Namen des Einreichers; Codereview F7-004).
* **ChessBase-Datenbanken** (0.598.0, Wunsch „ein Import für 2cbh und cbh zusätzlich zu PGN — bau es selbst in C#
  nach"; `Services/ChessBase/`): `POST …/club/games/chessbase` (angemeldet und über den Teilen-Link) nimmt die Dateien EINER
  Datenbank (einzeln, einzeln gepackt als `name.2cbg.gz` oder als ZIP) und antwortet mit einem PGN — gespeichert wird dabei
  nichts, danach ist es derselbe Weg wie eine PGN-Datei. Regeln, die dabei nicht kippen dürfen:
  - **Eigener Code nach Morphys reverse-engineerter Formatbeschreibung** (`format/v1`, `format/v2`; ChessBase dokumentiert
    keins der beiden). Morphy hat KEINE Lizenz — übernommen sind nur die Fakten (Offsets, Tabellen, Kodierung), kein Code,
    und seine Testdatenbanken gehören NICHT ins Repo. oschess (C) war bis v1.0.3 MIT (ab v1.0.4 AGPL) und diente nur zum
    Gegenlesen. Die Datenbanken des Nutzers auch nicht: sie tragen echte Namen — `ChessBaseFixtureTests` läuft nur lokal
    mit `CHESSBASE_FIXTURES=<Ordner>` (je Datenbank ihre Dateien + ein PGN-Export aus ChessBase) und vergleicht Kopfdaten
    und Hauptvariante als UCI. Stand 29.09.: `.2cbh` 78/78 gleich; `.cbh` gleich bis auf den auf 40 Zeichen gekürzten
    Turniernamen (steht so in der `.cbt`); Morphys WM-Sammlung in beiden Formaten (2 × 1025) mit denselben Zügen.
  - **Hauptvariante = alles vor dem ersten Linienende** — klassisch das erste `255` (Kodierung 0/4/5 über `CbhTables`,
    Chess960-Kodierungen 10/11 werden übersprungen), `.2cbg` die Wörter bis zum ersten `ffff`. Varianten und Anmerkungen
    (`.cba`/`.2cba`) liest niemand; dafür schickt die Seite nur `ChessBaseFiles.Upload` (SPIEGEL von
    `CHESSBASE_UPLOAD_EXTENSIONS` in `src-leaguehub/app/core/chessbase-upload.ts`, beide mit literalem Test).
  - **Das 2CBH-Zugwort wird aus Regeln aufgezählt** (`Cb2MoveTable.Build`, 0xb129 Einträge — König, Dame, Springer, Läufer,
    Turm je Farbe, dann Bauern, dann Rochaden); die Blockgrenzen stehen literal in `ChessBaseReaderTests`. Wer daran dreht,
    lässt den Fixture-Test laufen.
  - **Gespielt wird auf Gera.Chess direkt** (`MainlineBoard.Play`: `Move(new Move(von, nach))`, SAN aus `ExecutedMoves[^1]`)
    — die Kandidaten der Figur aufzuzählen und für jeden die SAN zu rechnen war sechsmal so langsam (3,5 ms statt ~1 ms je
    Partie). Nur Umwandlungen gehen über die Liste (direkt wählt Gera immer die Dame). Ein Zug, der nicht geht, macht die
    PARTIE zur übersprungenen (`ChessBaseGame.Error`, in der Antwort `skipped` mit Nummer), nie den ganzen Upload.
  - **Grenzen**: Rumpf 15 MB (`ChessBaseImportService.MaxBodyBytes` = die allgemeine `/api/`-Regel des Frontend-nginx —
    die Seite packt deshalb jede Datei per `CompressionStream('gzip')`, eine kommentierte `.cbg` auf ein Siebtel),
    ausgepackt 64 MB (`ChessBaseFiles.MaxTotalBytes`, beim LESEN gezählt — ZIP- und gzip-Bomben), 5000 Partien
    (`MaxGames`, `truncated`), höchstens 2 Umwandlungen gleichzeitig (sonst 429 `busy`). Das Aufteilen in Pakete ist
    derselbe Weg wie bei jeder Liste (siehe „Große Listen in Paketen").
* **Nur das JAHR** (`Date "2024.??.??"`), nur die Hauptvariante OHNE Kommentare, nur ab der Grundstellung
  (`fromPosition`). Dubletten: gleiche Züge (`MovesHash`) im gleichen Jahr; unter 20 Halbzügen zusätzlich gleiche Namen.
* **Spielerkarten** (`Services/League/LeagueProfileStore.cs`): `LeaguePlayerProfile.Pgn` hält NUR die fremden Partien
  (Lumbra, chess-results, Megabase); Karte, Partienzahl, PGN-Download und Eröffnungsbaum nehmen die Vereinspartien dazu
  (`WithClub`, Doppelte über Jahr + Hauptvariante — die fremde Fassung gewinnt). Nach jedem Upload/Löschen werden die
  betroffenen Karten neu gerechnet und die Partienzahl `g` in den fertigen Ansichten nachgezogen (`PatchViewCountsAsync`).
  Teilen-Links liefern die Vereinspartien im PGN mit („pgn sind nicht geschützt").
* **Partieformular**: dieselbe Einlesung wie in RookHub (`ScoresheetScan.Purpose = "league"`) und dieselbe Kostenbremse,
  aber eine EIGENE Tageszahl: **10 je Nutzer und 24 h** (`Scoresheet:LeagueDailyLimit`, RookHub bleibt bei 1); KEINE
  Partie in „Meine Partien", keine Glocke, nicht in `GET /api/scoresheets`. **Ohne Konto** (Teilen-Link):
  `ScoresheetScan.UserId` ist `null`, die Einlesung gehört dem Browser mit dem geheimen `AccessKey` (32 Hex, einmal beim
  Hochladen ausgegeben, LeagueHub merkt ihn im localStorage `lh-anon-scans`); **höchstens 10 je IP und 100 je Tag für
  alle zusammen** (`AnonPerIpDailyLimit`, `AnonDailyLimit`, Absage `dailyLimit` bzw. `anonDailyLimit`), drei offene je
  IP, Geld: das eigene Tagesbudget ohne Konto (`Scoresheet:AnonDailyUsd`, 3 $) UND das Gesamtbudget; Zählprüfung und
  Anlegen laufen nacheinander (Semaphor, parallele Uploads schlüpfen nicht durch). Gezählt wird über `AnonIpHash` =
  HMAC der IP (Schlüssel `Jwt:Key`; IPv4 bzw. IPv4-in-IPv6 als Adresse, echtes IPv6 je /64) — die Adresse selbst
  steht nirgends, der Vermerk wird nach zwei Tagen geleert. Übernehmen oder Verwerfen schließt die Einlesung
  (`CloseLeagueScanAsync` = `DetachWithoutLoading` + Dateiname „∅" + Schlüssel weg): Foto und Lesung gehen, die Zeile
  bleibt fürs Kontingent — und nichts verbindet sie mit der Partie.
* **Nie geprüfte Formulare** (0.581.0, Wunsch „damit die nicht im Limbo sind"): Verwalter (`league.manage`) sehen unter
  `GET /api/league/club/admin/scans` ALLE offenen Liga-Einlesungen (auch über Teilen-Links, `viaShareLink`, `mine`) und
  dürfen jede öffnen, übernehmen, verwerfen (`ScanActor.ManagerOf`, `LeagueOwned` ohne Eigentümer-Filter; der Controller
  nimmt ihn für jeden Verwalter). LeagueHub zeigt sie im Formular-Reiter unter „Offene Formulare anderer".
* **Nach dem Prüfen** (0.581.0): „PGN herunterladen" / „PGN kopieren" (`sheetPgn` in `core/club-format.ts`, Namen wie im
  Formular — die Datei bleibt beim Nutzer) und angemeldet „Zu meinen Partien hinzufügen" (`POST /api/games/import`,
  Link zurück nach RookHub über `rookHubUrlForLeagueHub`). Nach dem Übernehmen bleibt die Seite dafür offen.
* **Dauer** (0.581.0): Hinweis „etwa 1–2 Sekunden pro Zug" und eine Uhr ab dem Hochladen (`scoresheet-timing.ts`:
  `serverTime` liest Zeiten ohne Zone als UTC — aus der Datenbank kommen sie so —, `SecondsTicker` läuft nur, solange
  gelesen wird). Gemessen auf Prod: 43/59/104 Einträge in 22/31/73 s.
* **Löschen**: `league.manage` alles, sonst nur eigene Partien ohne „Schwaz". Konto löschen setzt `UploadedByUserId` null.
  Alles eines Teilen-Links auf einmal (samt Analysen, Karten neu gerechnet): `DeleteByShareAsync`.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/club/games?fide=&q=&page=` | view | Liste (50 je Seite, Jahr absteigend) mit `opening`, `canDelete`, `uci` und `pgn` — „Analyse" öffnet RookHubs Analysebrett mit dem GANZEN PGN (`/analysis?pgn=`, 0.592.0; über 6 000 Zeichen Adresse nur die Züge `?moves=`), `analysis` (Stand der Hintergrund-Analyse, 0.593.0), `whiteInRoster`/`blackInRoster` = Seite ohne FIDE-ID, deren Name in einer Meldeliste steht (0.594.0) — dort zeigt die Seite „(ohne FIDE-ID)“ statt des Zuordnen-Bleistifts, den behalten nur Namen, die niemand kennt |
| GET | `/api/league/club/games/{id}` | view | Eine Partie wie in der Liste, mit frischem `analysis` (0.593.0) |
| GET | `/api/league/club/games/{id}/evals` | view | Bewertungen aus der Hintergrund-Analyse (`GameEvalsDto`, ohne Buchzüge; Status `none` ohne Analyse, 404 unbekannt) |
| GET | `/api/league/club/games/pgn?fide=&q=` | view | Alle (gefilterten) als PGN |
| POST | `/api/league/club/games/preview` | contribute | `{ pgn }` → `{ games[{ index, year, result, event, plies, opening, error, duplicate, white/black{ raw, elo, match{ league, ambiguous, name, fide, club, candidates }, owner, replace } }], truncated }`; 400 `empty`/`tooLarge` (5 Mio. Zeichen), höchstens 500 Partien |
| POST | `/api/league/club/games/import` | contribute | `{ pgn, games?[{ index, white{ name, fide, replace }, black{…} }] }` (fehlt `games` = alle mit Vorgaben) → `{ added, duplicates, anonymized, truncated, ids, failed[{ index, white, black, reason }] }` |
| POST | `/api/league/club/games` | contribute | EINE Partie `{ moves[] (SAN), white, black, whiteFide, blackFide, whiteElo, blackElo, whiteReplace, blackReplace, result, event, year, scanId }` → `{ id, anonymized }`; 400 `reason` wie oben + `duplicate`, `illegal` (mit Meldung); schließt die Einlesung |
| DELETE | `/api/league/club/games/{id}` | contribute | 204 / 403 / 404 |
| POST | `/api/league/club/pairings` | contribute | Ligapaarungen für eine noch nicht gespeicherte Partie `{ white, whiteFide, black, blackFide, date, year }` → `[{ id, label, white, whiteFide, black, blackFide, result, whiteOwnClub, blackOwnClub, exact }]` (0.678.0; auch `POST /api/league/s/{token}/club/pairings`) |
| GET | `/api/league/club/games/{id}/pairings` | view | Ligapaarungen einer gespeicherten Partie (die aktuelle immer dabei); 404 ohne Bearbeiten-Recht (0.678.0) |
| PUT | `/api/league/club/games/{id}` | contribute | Namen/Ergebnis korrigieren `{ white?{ name, fide }, black?, result? }` (0.582.0; wer löschen darf; fehlende Seite = unverändert) → Partie; 400 `anonymous` („Schwaz" bleibt), `noLeaguePlayer`, `onlyOwnClub`, `invalidResult`; ein vorher unzugeordneter Name (ohne FIDE-ID) wird als Zuordnung gemerkt; ein Spieler von Schwaz (oder `replace`) wird zu „Schwaz" samt Wegfall von Veranstaltung und Hochladendem (0.583.0) — eine Korrektur anonymisiert nur, nie zurück |
| GET | `/api/league/club/players?q=&all=` | contribute | Ligaspieler-Vorschläge (jedes Wort irgendwo im Namen, Wortanfänge zuerst) samt `club`; `all=true` dazu das Megabase-Verzeichnis |
| POST | `/api/league/club/games/lichess` | contribute | `{ url }` einer öffentlichen Lichess-Studie → `{ pgn }` |
| POST | `/api/league/club/games/chessbase` | contribute | Multipart `files` (Dateien einer ChessBase-Datenbank, einzeln `.gz` oder ZIP) → `{ format, name, pgn, games, converted, deleted, truncated, skippedCount, skipped[{ id, white, black, reason }] }`; 400 `reason` ∈ `noFile`/`noDatabase`/`multipleDatabases`/`missingFile`/`tooLarge`/`invalidZip`/`invalidFile`/`unreadable`, 429 `busy` (0.598.0) |
| POST | `/api/league/club/match` | contribute | `{ white, black }` → je Seite `{ league, ambiguous, name, fide, club, candidates, lastNameOnly, mega }` |
| GET | `/api/league/club/scoresheet/status` | contribute | Tageszahl dieses Wegs (10) |
| GET | `/api/league/club/admin/scans` | manage | Alle offenen Liga-Einlesungen `[{ scan, viaShareLink, mine }]` (jüngste 50) |
| POST/GET | `/api/league/club/drafts` | contribute | Entwurf ablegen `{ pgn, source, label }` (400 `tooManyDrafts`) / die eigenen offenen (0.595.0) |
| GET/PUT/DELETE | `/api/league/club/drafts/{id}` | contribute | Entwurf öffnen (Rohtext, Stand, importierte) / Stand speichern `{ state?, imported? }` / löschen — Einreicher oder Verwalter |
| GET | `/api/league/club/admin/drafts` | manage | Alle offenen Entwürfe (jüngste 100) mit `owner`, `viaShareLink`, `mine` |
| DELETE | `/api/league/club/admin/shares/{token}/games?dryRun=` | manage | Alle Partien entfernen, die über diesen Teilen-Link kamen (auch nach seinem Ablauf, auch „Schwaz"-Partien) → `{ count, dryRun }`; `dryRun=true` zählt nur; 400 `invalidToken` (über 64 Zeichen) |
| GET/POST | `/api/league/club/scans` | contribute | offene Liga-Einlesungen / Foto hochladen (multipart wie `POST /api/scoresheets`) |
| GET | `/api/league/club/scans/{id}` (+`/photo`, `POST /resolve`, `DELETE`) | contribute | Stand / Foto / Rest neu aufbereiten / verwerfen |
| POST | `/api/league/s/{token}/club/games/preview`, `/games/import` | Teilen-Link | wie oben, ohne Konto; der Import speichert höchstens 50 je Aufruf und 200 je Link und Tag (Rest: `failed` mit `shareLimit`), jede Partie mit `UploadShareHash` |
| POST | `/api/league/s/{token}/club/games?scanKey=` | Teilen-Link | eine Partie, schließt die Einlesung mit diesem Schlüssel; zählt gegen denselben Deckel (400 `shareLimit`) |
| POST | `/api/league/s/{token}/club/games/lichess` | Teilen-Link | Lichess-Studie laden, wie oben |
| POST | `/api/league/s/{token}/club/games/chessbase` | Teilen-Link | ChessBase-Datenbank → PGN, wie oben |
| GET | `/api/league/s/{token}/club/players`, `POST …/match`, `GET …/scoresheet/status` | Teilen-Link | wie oben (Status: je IP) |
| POST | `/api/league/s/{token}/club/scans` | Teilen-Link | Foto hochladen → `{ key, scan }` |
| POST | `/api/league/s/{token}/club/scans/lookup` | Teilen-Link | `{ keys[] }` → die offenen Einlesungen dazu |
| POST | `/api/league/s/{token}/club/drafts` (+`/lookup`), GET/PUT/DELETE `…/drafts/{key}` | Teilen-Link | Entwürfe ohne Konto, über den Schlüssel (0.595.0) |
| GET | `/api/league/s/{token}/club/scans/{key}` (+`/photo`, `POST /resolve`, `DELETE`) | Teilen-Link | wie angemeldet, über den Schlüssel |

Der Teilen-Link muss gültig sein (`LeagueService.ShareValidAsync`, sonst 404); Rate-Limit `anonymous-tournament`. Lesen
der Vereinspartien gibt es über den Link NICHT.

**Megabase + andere Sammlungen** (0.574.0): `POST /api/league/admin/games?source=Mega` (`league.manage`, PGN, gern
gzip) spielt eine fremde Partiesammlung in die Karten ein — zugeordnet NUR über `WhiteFideId`/`BlackFideId` im Kopf,
zusammengeführt wie ein chess-results-Abruf, die Quelle steht als Kopfzeile `[LeagueSource "Mega"]` im gespeicherten PGN
(`LeagueProfileBuilder.StoredSource` liest sie VOR der FIDE-Regel, sonst sähe die Megabase wie Lumbra aus). Den Filter
auf die TMM-Spieler macht `mega_decide.py` im league-analyzer (Stand 28.09.: 11,5 Mio. Partien, 46 642 übernommen für
328 Spieler; ausgelassen 235 nur über den Namen, 262 mit Namen, der nicht zur Meldeliste passt — ChessBase hängt
modernen FIDE-IDs gelegentlich Partien von Namensvettern an —, 293 ohne Züge, 46 über 30 Jahre vor dem Median-Jahr des
Spielers). Die ganze Megabase liegt NICHT in RookHub.

**Spieler korrigieren über die Megabase** (0.575.0): beim Korrigieren eines Namens (Übersicht und Formular-Korrektur,
`features/club/player-search.component.ts`) sucht LeagueHub unter den Personen der Liga und — Häkchen, seit 0.576.0
standardmäßig an, auf der Seite des Partieformulars seit 0.672.4 aus (`searchMega`, Wunsch 2026-10-05 „nimm
standardmäßig nur Namen aus der Liga") — im Spielerverzeichnis der GANZEN Megabase (`LeagueMegaPlayers`, Tabelle `LeagueMegaPlayers`: Name, `NameKey` klein ohne
Akzente mit Index, FIDE-ID, Partien, jüngstes Jahr, höchste Elo). `GET …/club/players?q=&all=true` hängt die Treffer
(`source: "mega"`) an die Ligaspieler an; trägt ein Treffer die FIDE-ID eines Ligaspielers, gilt er als dieser. Gesucht
wird seit 0.576.0 wie mit `LIKE` („bert rud" findet „Bertl, Rudolf"): jedes getippte Wort (ab zwei Zeichen, höchstens
vier) muss IRGENDWO im Namen stehen — in der Megabase `NameKey LIKE '%wort%'` je Wort (bei 432 000 Zeilen Zehntelsekunden,
höchstens 300 nach Partienzahl), Wortanfänge zuerst, dann meistgespielte; die Liga ebenso. Die Suche startet gleich beim
Öffnen bzw. Hineinklicken ins Namensfeld (`autoSearch`, `onFocus`). Seit 0.577.1: jedes Wort in BEIDEN Umlaut-Schreibweisen
(`LeagueRosterIndex.Spellings`, „Höcher" → „hocher"/„hoecher" — das Megabase-Verzeichnis enthält kein einziges ä/ö/ü,
ChessBase schreibt „Hoecher, Michael"; der Rückweg „oe" → „o" ist bewusst weg, er machte „Michael" zu „Michal"), eine
reine Zahl (4–12 Ziffern) sucht die FIDE-ID, und Titel vor/hinter dem Namen fallen beim Abgleich und in der Suche weg
(`LeagueNames.StripTitles`: Schachtitel nur in Großschreibung — „Im, Seong" bleibt ein Name —, akademische vorn; NICHT in
`NameKey`, der Brettpaarung und Meldeliste beim Aktualisieren verknüpft). Eine
gewählte FIDE-ID ohne Ligaspieler bleibt an der Partie stehen (`Side.Fide`). Eingespielt über
`POST /api/league/admin/mega-players` (TSV, gern gzip; ersetzt alles) aus `scan_mega_players.py` im league-analyzer.

**Lichess-Studien** (0.575.0): `POST …/club/games/lichess { url }` (angemeldet und über den Teilen-Link) holt das PGN
einer ÖFFENTLICHEN Studie bzw. eines Kapitels (`LichessStudySource`, nur `lichess.org/study/{8}` bzw. `…/{8}/{8}` —
der Server ruft ausschließlich `/api/study/{id}[/{kapitel}].pgn` der festen Lichess-Basis `Lichess:SiteUrl` auf, kein
freier Abruf); danach wie ein Upload (Übersicht, Import). Absagen `invalidUrl`, `lichessNotFound` (privat/fehlt),
`lichessFailed`, `tooLarge`.

**Eröffnungsbaum** (0.574.0): `GET /api/league/player/{fide}/tree?color=w|s&line=e4 e5` (und `/api/league/s/{token}/player/{fide}/tree`
für Spieler der geteilten Meldeliste) → `{ total, ended, moves[{ san, n, score (Punkte aus SEINER Sicht, %), last (Jahr) }] }`
über alle seine Partien (fremde + Verein), höchstens 30 Halbzüge, Züge aus dem Partietext (ohne Brett, schnell genug für
2000 Partien je Klick). Oberfläche: Knopf „Eröffnungsbaum anzeigen" auf der Spielerkarte (`shared/opening-tree.component.ts`).
**Filter** (0.605.0, Wunsch 2026-09-30: „online ja/nein, wenn online: Zeitformat; nur Partien der letzten x Jahre"):
`source` = `board` (Vorgabe, wie vorher) / `both` / `online`, `speeds` = Komma-Liste aus `bullet,blitz,rapid,classical,correspondence`
(leer = alle, gilt nur für Online-Partien), `years` = 1–50 (Online-Partien ab heute − x Jahre; Brettpartien tragen oft nur das Jahr,
dort zählt jedes Jahr ab dem Jahr von heute − x). Online-Partien zählen nur von GESICHERTEN Konten — angemeldet nimmt `unsure=true`
die unsicheren dazu (0.612.0, Wunsch „unsichere standardmäßig nicht in den Entwicklungsbaum, über einen Schalter dazu"; bis 0.611.0
war es umgekehrt, `sure=true` schränkte ein), über den Teilen-Link nie. Die Karte trägt dafür `onlineUnsure` (Partien der unsicheren
Konten), der Baum zeigt den Schalter „auch unsichere Konten (n Partien)" nur, wenn es welche gibt (`TreeFilter.withUnsure`, gemerkt). Unbekannte Werte fallen still auf die Vorgabe (`LeagueProfileStore.TreeFilter.Parse`). Antwort zusätzlich
`board`/`online` = wie viele der Partien in der Stellung von wo kommen. Online-Partien fragt der Baum in SQL ab (`Line` =
Präfix-Treffer `Line == pre || Line.StartsWith(pre + " ")`, Tempo, `PlayedAt`, Konto-Zuordnung) — `QueryTranslationTests`
prüft die Übersetzung gegen MariaDB. Oberfläche: Filterleiste über dem Baum (`core/tree-filter.ts` = die reinen Regeln, gemerkt
im localStorage `lh-tree-filter`); Quellen-Wahl erst, wenn der Spieler Online-Partien hat, ohne Brettpartien gleich „online".
**Filter auch fürs Eröffnungsprofil der Karte** (0.617.0, Wunsch 2026-09-30 mit Screenshot: „auch an der Stelle will ich die vollen
Filtermöglichkeiten"): `GET /api/league/player/{fide}/profile?source=&speeds=&years=&unsure=` (und `/api/league/s/{token}/player/{fide}/profile`,
dort nur gesicherte Konten) → dieselben Abschnitte wie die Karte (`white`, `black_e4`, `black_d4`, `black_other` samt `first`/`lines`) plus
`n`/`board`/`online`/`years` über die GEFILTERTEN Partien (`LeagueProfileStore.ProfileAsync`, gleiche Regeln wie der Baum; die Abschnitte
rechnet `LeagueProfileBuilder.AddSections` — dieselbe Funktion wie für die gespeicherte Karte). Züge wie im Baum aus dem Partietext bzw.
der gespeicherten Online-Zeile (ohne Brett), Partien ab eigener Stellung zählen nicht. Die Filterleiste sitzt seither auf der KARTE
(`shared/tree-filter-bar.component.ts`), die Karte hält den Stand (`lh-tree-filter`) und gibt dem Baum den wirksamen Filter als Eingabe;
ohne Filter (nur Brett, alle Jahre) zeigt sie die gespeicherte Karte ohne weiteren Abruf.

**Online-Konten + Online-Partien** (0.605.0, Wunsch 2026-09-30: „für einen User kann es eine Liste von Onlinekonten geben — Name +
Seite, gesichert oder unsicher + Kommentare; im Hintergrund holst du die Spiele dieser User und legst sie in der DB ab"):
* **Konten** (`Services/League/LeagueOnlineAccounts.cs`): je FIDE-ID wie die Karte. `LeagueOnlineSites` kennt die Seiten (heute
  `lichess`, `chess.com`; eine weitere braucht dort Kürzel/Namensregel/Profiladresse und in `LeagueOnlineSync` einen Abruf) und liest
  Name ODER kopierte Profiladresse (die Adresse schlägt die gewählte Seite). `Confidence` bleibt wie im Import `sicher` /
  `wahrscheinlich` (Oberfläche: gesichert / unsicher), `Evidence` ist der Kommentar (≤ 1000). Von Hand gepflegt = `Manual`: solche
  Zeilen lässt der Bundle-Import stehen; ein Import-Konto, das es schon gibt, wird nur aktualisiert (sonst gingen seine Partien),
  nur verschwundene Import-Konten fallen samt Partien weg. Höchstens 20 je Spieler. Andere Seite oder anderer Name = anderes Konto:
  Partien weg, Abruf von vorn; nur andere Groß/Kleinschreibung behält sie. Nach jeder Änderung werden die Konten in den fertigen
  Ansichten nachgezogen (`PatchViewsAsync`, `roster[].acc`) und der Abruf geweckt.
* **Sichtbarkeit**: angemeldet trägt die Karte je Konto `id`, `comment`, `games`, `syncedAt`, `error` (`ToJson(full: true)`), dazu
  `online` = Summe der Partien; über einen Teilen-Link nur gesicherte Konten und nur `site/user/url/conf`. Pflegen nur
  `league.manage`, nie über einen Teilen-Link (`shared/online-accounts.component.ts`, in der Spielerkarte).
* **Abruf** (`Services/League/LeagueOnlineSync.cs`, HttpClient `LeagueOnline`, eigener User-Agent): Lichess über
  `/api/games/user/{name}?since=…&sort=dateAsc&max=500` (ndjson, höchstens 4 Seiten je Lauf), chess.com über die
  Monatsarchive (höchstens 12 je Lauf). Nur Standardschach ab der Grundstellung, höchstens `LeagueOnline:MaxYears` (5) zurück;
  gespeichert je Partie Tempo (ultraBullet → bullet, daily → correspondence), Farbe, Ergebnis aus SEINER Sicht, Gegner + Wertungen,
  alle Züge und die ersten 30 Halbzüge als `Line` (für den Baum) — beide in derselben SAN wie die Brettpartien, OHNE
  Schach-/Mattzeichen (`PgnParser.ExtractMainlineSans`; Lichess liefert „Bb4+“, sonst stand derselbe Zug zweimal im Baum,
  Codereview N4-001 — den Altbestand bereinigt die Migration `LeagueOnlineLinesWithoutCheckSigns`). Der Stand steht am Konto
  (`SyncCursor` in ms, `SyncMore` = es gibt noch Rückstand, `SyncedAt`, `GameCount`, `SyncError`); 404 = „Konto nicht gefunden", ein 429 beendet den ganzen Lauf.
* **Takt** (`LeagueOnlineSyncScheduler`): zwei Minuten nach dem Start, dann je Konto alle `LeagueOnline:IntervalHours` (12), sofort
  nach einem Weckruf; solange ein Konto Rückstand hat, eine Minute Pause zwischen den Läufen (je Lauf höchstens 10 min), sonst
  schaut er alle 30 min. `LeagueOnline:Enabled=false` schaltet ihn ab. Die Integrationstests nehmen alle Hosted Services heraus.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| POST | `/api/league/player/{fide}/accounts` | manage | Konto anlegen `{ site, user (Name oder Profiladresse), sure, comment }` → das Konto (volle Form); 400 `reason` ∈ `invalidSite`/`invalidUser`/`duplicate`/`tooMany`, 404 `unknownPlayer` |
| PUT | `/api/league/accounts/{id}` | manage | Ändern (fehlende Felder bleiben); 404 `notFound` |
| DELETE | `/api/league/accounts/{id}` | manage | Entfernen samt Partien → 204 |
| POST | `/api/league/accounts/{id}/sync` | manage | Nochmal holen (Fehler weg, Abruf geweckt) |

**Konto-Vorschläge** (0.607.0, Wunsch 2026-09-30 „zuerst die Konto-Vorschläge"; `Services/League/LeagueAccountFinder.cs`):
LeagueHub sucht selbst nach Konten und legt sie als VORSCHLAG ab (`LeagueAccountSuggestions`); ein Verwalter übernimmt
(unsicher/gesichert → normales Konto, Kommentar = die Hinweise) oder verwirft. Portiert aus `online_accounts.py`, aber lockerer
— dort zählten nur Konten mit Klarnamen (29 Konten bei 27 Spielern), hier entscheidet ein Mensch. Regeln:
* **Kandidaten**: Nutzernamen aus dem Namen (`Variants`: MaxMuster, Max_Muster, Max-Muster, MusterMax, Muster_Max, MMuster,
  MusterM; Umlaute als ae/oe/ue, Titel weg, ohne Komma „Nachname Vorname") auf BEIDEN Seiten; auf Lichess dazu die Suchvorschläge
  zum Nachnamen (`/api/player/autocomplete`, erst ab 5 Buchstaben, höchstens 12 Treffer). Lichess-Profile gesammelt über
  `POST /api/users`, chess.com einzeln `/pub/player/{name}` (300 ms Pause).
* **Urteil** (`Judge`, rein): raus bei gesperrtem/geschlossenem Konto, einem Profilnamen OHNE den Nachnamen (jemand anderes) und
  einem Land, das weder das Land seiner REGION (Tirol AT, Bayern DE — seit 0.712.0, siehe „Je Region" unter Team-Suche) noch die
  Föderation des Spielers (Meldeliste oder FIDE) ist. Hinweise: Klarname 3, nur Nachname 1,
  FIDE-Wertung im Profil ±250 zur Liste 2, Ort der Region in Ort/Bio 2 (Wortgrenzen — „Hallo" ist nicht Hall; Bayern auch „Gautinger"), Land 1. Ein Name
  aus dem Namen braucht ≥ 1 (plus 1 Punkt), einer aus der Suche ≥ 3.
* **Anderer Vorname = anderer Mensch** (0.611.0, `FirstNameMatch`, gesehen in der ersten vollen Suche: „Andreas Berchtold" für Axel,
  „Galin Georgiev" für Georgi): steht im Profil neben dem Nachnamen ein Vorname, muss es einer des Spielers sein (irgendeiner, auch
  der zweite); nur Initialen → eine muss passen; nur Nachname oder Titel („IM Muster") → schwacher Hinweis. Sonst fällt das Konto weg.
* **Online-Wertung gegen Elo** (0.609.0, Wunsch „ein Konto mit 500 auf einem 2000er ergibt keinen Sinn — nur niedriger ist ein
  Problem, alles droppen, was 400 niedriger ist"): die BESTE belastbare Wertung des Kontos (ab 10 Partien; seit 0.622.0 zählt auch „vorläufig", das Lichess nach langer Pause setzt —
  unbespielte Lichess-Kategorien stehen auf 1500 „prov"; Bullet zählt mit) darf höchstens `RatingBelow` 400 unter der Elo der
  Meldeliste liegen, sonst fällt das Konto weg; nach oben keine Grenze. Liegt sie 100 bis 300 DARÜBER (`FitMin`/`FitMax`), ist das
  der OPTIMALE Treffer (+2, `ScoreRatingFit`), jede andere Wertung darüber oder bis 400 darunter ein schwächerer (+1,
  `ScoreRatingWeak`) — `RatingEvidence`, Wunsch „normal ist Elo online ca. 200 höher, 100–300 wäre passend" und „das Band zeigt die
  optimalen Treffer, 400 unter FIDE schließt aus, alles andere ist halt Treffer, aber schwächer" (0.621.0; 0.619.0 gab außerhalb des
  Bands nichts, davor −250 bis +450 einen Punkt).
* **Gleicher Nutzername auf der anderen Seite** (0.621.0, Wunsch „wenn du einen Treffer hast, prüfe, ob der gleiche Username auf
  chess.com bzw. Lichess existiert und eventuell auch passt"; `LeagueAccountFinder.TwinAsync`): zu jedem Treffer holt die Suche das
  Profil desselben Namens auf der anderen Seite (`FetchProfileAsync`, nur wo dieser Name dort nicht ohnehin schon gefragt wurde) und
  urteilt nach denselben Regeln (`Judge` mit der Schwelle des abgeleiteten Namens, erster Hinweis „gleicher Nutzername wie das
  Lichess-Konto …", +`ScoreTwin` 1). Die Team-Suche tut dasselbe für jeden ihrer Vorschläge (Source `team`). Hakt die andere Seite,
  bleibt der Treffer selbst stehen; ein 429 beendet den Durchgang wie sonst. Lichess liefert die Wertungen in `POST /api/users` mit (`perfs`), chess.com nur über `/pub/player/{name}/stats` — ein Abruf
  mehr je gefundenem Konto (dort steht auch die selbst angegebene FIDE-Wertung).
* **Fassung** `LeagueAccountFinder.CurrentVersion` (8 seit 0.712.0 — Land/Orte nach der Region; 7 seit 0.622.0 — Lichess-„vorläufig" mit genug Partien zählt; 6 = Wertungsband
  optimal/schwächer, gleicher Name auf der anderen Seite) in `LeagueAccountScans.Version`: ältere Suchen sind sofort wieder
  fällig, und eine neue Suche entfernt OFFENE Vorschläge, die sie nicht mehr bestätigt (verworfene bleiben). Wer Kandidaten oder
  Urteil ändert, erhöht die Zahl.
* **Minderjährige: gesucht, aber VERBORGEN** (0.610.0, Wunsch „du linkst sie, aber zeigst niemandem den Namen/Account"; bis
  0.609.0 gar nicht gesucht): Jahrgang und Föderation über Lichess `/api/fide/player/{id}`, gemerkt in `LeagueAccountScans`. Unter
  18 (seit 0.616.0 NUR bei bekanntem Jahrgang — Wunsch „alle mit gesichertem Geburtsdatum unter 18 ausblenden, alle anderen
  anzeigen"; bis dahin galt auch „Jahrgang unbekannt" als verborgen) gilt `LeagueHiddenAccounts`: Seite, Nutzername, Adresse, Profilangaben und
  Kommentar verlassen den Server nie — Vorschläge (`hidden: true`, nur Hinweise + Spieler), Konto-JSON (`JsonAsync`/`ToJson(…,
  hidden)`), Karte (angemeldet nur DASS es ein Konto gibt, über einen Teilen-Link gar nichts), Meldeliste der Ansichten
  (`RebuildViewsAsync`, `PatchViewsAsync`: leer). Die Partien zählen nur im Eröffnungsbaum (Züge, keine Gegner/Links). Mit 18 wird
  das Konto von selbst sichtbar. Ohne Such-Eintrag (nie abgesucht) gilt ein Konto als sichtbar — die Suche erfasst jeden Spieler
  der laufenden Saison. In DB und Server-Log steht die Verknüpfung weiter (nur Betreiber). Testfall
  `Minors_AreSearched_ButNothingIdentifyingLeavesTheServer` prüft ALLE Ausgaben auf den Nutzernamen.
  **Ausnahme ADMINS** (0.625.0, Wunsch „Admins sollen auch bei Minderjährigen die Onlinekonten für die Prüfung auf
  sicher/unsicher/verwerfen sehen, sonst kann ich das nicht entscheiden"): Rolle `Admin` (`IsAdmin` im Controller → `reveal`)
  bekommt Karte, Vorschläge, Konto-Antworten und die Prüfung (i) VOLLSTÄNDIG, mit `minor: true` (Oberfläche: Etikett „minderjährig –
  nur für Admins sichtbar"). Wer nur `league.view`/`league.manage` hat, sieht weiter nichts; Teilen-Links (`onlySure`) und die
  Meldeliste der Ansichten zeigen es nie, auch nicht einem Admin.
* **Nicht wieder vorschlagen**: verworfene Vorschläge bleiben als `Rejected` stehen; ein ENTFERNTES Konto wird als verworfener
  Vorschlag gemerkt; ein angelegtes Konto erledigt den passenden Vorschlag (Vergleich ohne Groß/klein).
* **Takt**: im `LeagueOnlineSyncScheduler` nach jedem Abruf-Durchgang, je Runde höchstens 5 min (`SearchBudget`), 1 s Pause je
  Spieler; Spieler der laufenden Saison JE REGION (`LeagueOnlineRegions.CurrentSeasonTnrsAsync`, seit 0.712.0 — vorher die jüngste
  Saison überhaupt, mit Zugspitze 2026/27 vor Tirol 2025/26 fielen die Tiroler heraus) mit FIDE-ID, nie gesuchte zuerst, dann alle 90 Tage (`RescanDays`); ein Fehler versucht es
  am nächsten Tag. `LeagueOnline:Suggestions=false` schaltet die Suche ab. Ein 429 beendet den Durchgang.
* Oberfläche: auf der Spielerkarte unter den Konten „Vorschläge der Konto-Suche" mit „Jetzt suchen" (nur Verwalter), und der Reiter
  „Konto-Vorschläge" (`/konten`, `features/accounts/`) mit allen offenen Vorschlägen je Spieler und dem Stand der Suche.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/suggestions` | manage | Offene Vorschläge (stärkste zuerst, mit Name/Mannschaft) `{ items, scanned, total }` |
| GET | `/api/league/player/{fide}/suggestions` | manage | Offene Vorschläge eines Spielers `{ items }` |
| POST | `/api/league/player/{fide}/suggestions/scan` | manage | Jetzt suchen → `{ items, found, skipped }` (`skipped` seit 0.610.0 immer leer); 503 `rateLimited`/`unreachable` |
| POST | `/api/league/suggestions/{id}/accept` | manage | `{ sure }` → das Konto (wie Anlegen); 404 erledigt/unbekannt |
| POST | `/api/league/suggestions/{id}/reject` | manage | Verwerfen → 204 |

**Team-Suche** (0.612.0, Wunsch 2026-09-30 „auf Lichess gibt es Teams", „vor 5 Jahren gab es eine Online-TMM 2021", „schau, was
Schach Tirol sonst noch organisiert hat"; `Services/League/LeagueTeamScout.cs`, Tabelle `LeagueScoutAccounts`): die Namenssuche
findet nur Konten, deren Name aus dem Spielernamen kommt. Die Team-Suche nimmt die Konten aus dem Umfeld der Tiroler Vereine:
* **Bestand** (`RefreshPoolAsync`, höchstens alle `PoolEvery` 30 Tage — der Takt merkt es sich im Arbeitsspeicher): Lichess-Team-Suche
  nach Tiroler Orten (`DefaultPlaces`, abweichend `LeagueOnline:TeamPlaces`), nur Teams mit dem Ort als ganzem Wort im Namen
  (`ClubKeys`, beide Umlaut-Schreibweisen); deren Mitglieder (`Teams`) und alle, die in einem Team-Battle dieser Teams FÜR sie
  gespielt haben (`/api/team/{id}/arena` → Battles → `/api/tournament/{id}/results`, `PlayedFor`). Gemessen am 2026-09-30: die
  Online-TMM 2021 und die Quarantäne-Liga sind solche Battles.
* **Ein Team, das nichts hergibt, kostet nur sich selbst** (0.623.1, gemeldet 2026-09-30 als wiederholte
  „Team-Suche gescheitert"-Ausfälle im Log): Lichess antwortet **401**, wenn ein Team seine Mitgliederliste verborgen hat
  (`schachsport-union-innsbruck-team-2-mm-2021-osb-lv-tirol`). `SaveChangesAsync` steht erst am ENDE von
  `RefreshPoolAsync` — die Ausnahme flog also durch, kein Team NACH diesem einen wurde je gelesen, und
  `LeagueScoutAccounts` blieb seit der Einführung LEER (0 Zeilen auf Prod). Jetzt gehen die Abrufe je Team
  (`/users`, `/arena`) und je Battle (`/api/tournament/{id}`, `/results`) über `GetOpenAsync` (401/403 → `null` wie ein
  404) und stehen zusätzlich in einem `try/catch (HttpRequestException)` je Schleifendurchlauf, der nur diesen einen
  Eintrag verwirft und ihn als Warnung loggt; die Zahl steht als `{Skipped}` in der Abschlusszeile. **Ein 429 fliegt
  weiter** und beendet den Durchgang wie bisher — `LeagueOnlineSync.RateLimitedException` erbt von `Exception`, nicht
  von `HttpRequestException`, und genau das nagelt `Pool_ATooManyRequests_StillEndsTheRun` fest. Die Team-SUCHE selbst
  (`/api/team/search`) bleibt ungeduldet: antwortet die nicht, ist nichts zu holen, und ein Abbruch ist die richtige
  Meldung.
* **Ein 502 ist die Last, keine Auskunft — deshalb EIN Wiederholversuch** (0.624.2, am 01.10.2026 auf Prod gemessen):
  der erste Pool-Lauf mit 0.623.1 brachte 413 Konten (vorher 0) und meldete „30 Tiroler Teams, 155 Team-Battles,
  413 neue Konten, 37 uebersprungen" — die 37 waren KEIN 401/403, sondern **502 auf Team-Battles, alle binnen drei
  Sekunden**, und dieselben Turnier-Kennungen antworten einzeln abgefragt mit 200. Der Duldungs-`catch` schluckte sie
  endgültig, ihr `PlayedFor`/`Events` wäre erst beim nächsten Pool-Lauf (30 Tage) gekommen. `GetAsync` wiederholt
  deshalb EINMAL (`MaxAttempts` 2) nach `RetryPause` (2 s, im Test 0), aber NUR bei einem vorübergehenden Fehler —
  `IsTransient`: 5xx oder gar keine Verbindung (`HttpRequestException.StatusCode` ist dann `null`). Was eine ANTWORT
  ist, wird nie wiederholt: 404 (`null`), 401/403 (die Duldung oben) und 429 (`RateLimitedException`, beendet den
  Durchgang). Die Wiederholung sitzt in `GetAsync`, gilt also auch für die Team-Suche und den Partien-Abruf der
  Konto-Prüfung — dort kostete ein 502 bisher dem Konto einen Tag.
* **Der Bestands-Aufbau speichert ZWISCHEN und sitzt eine Drossel aus** (0.625.1, am 01.10.2026 auf Prod gemessen):
  mit 0.624.2 kam der Lauf zweimal (11:12, 11:43) überhaupt nicht mehr durch — `Lichess: zu viele Anfragen`, gemessen
  **54 Abrufe in 61 s** auf `/api/team/…`, beim zweiten Mal ohne jede Fremdlast (die (i)-Prüfungen des Nutzers endeten
  eine halbe Stunde davor). Weil `SaveChangesAsync` am ENDE stand, schrieb jeder abgebrochene Anlauf NICHTS und begann
  von vorn: der Takt klopfte alle 30 min vergeblich an, der Bestand blieb auf dem Stand des einen geglückten Laufs.
  Drei Änderungen, alle nur für den AUFBAU (`bulk: true` an seinen vier Abruf-Stellen; die Konto-Prüfung bleibt, wie sie
  war — dort beendet ein 429 den Durchgang weiter):
  - **`PoolPause`** (3 s statt `Pause` 1 s) — bei einer Sekunde Abstand trat die Drossel zu.
  - **Eine Drossel wird AUSGESESSEN**, nicht als Ende gelesen: `RateLimitWaits` (3) mal `RateLimitCooldown` (60 s,
    Lichess' Empfehlung), dann endet der Durchgang doch. Der Aufbau läuft nur alle `PoolEvery` (30 Tage) — die Minute
    Warten ist billig gegen einen verlorenen Lauf.
  - **Zwischengespeichert alle `SaveEvery` (25)** Teams bzw. Battles, UND im `finally` mit `CancellationToken.None` —
    dieselbe Lehre wie beim Rundenplan-Lauf des Turnierverzeichnisses: ein abgebrochener Token verhinderte genau das
    Speichern, das die Arbeit retten soll. Ein Fehler beim Speichern wird geloggt und verdeckt die ursprüngliche
    Ausnahme NICHT (der Takt muss eine Drossel als Drossel sehen). Die Abschlusszeile steht ebenfalls im `finally`,
    sagt also auch bei einem Abbruch, was erreicht wurde.
  **Was damit noch NICHT gelöst ist:** ein Anlauf beginnt immer bei Team 1 — die Abrufe der schon abgearbeiteten Teams
  laufen erneut (die DATEN bleiben, `Upsert` findet sie im Bestand). Erst wenn ein Lauf durchkommt, ist der Bestand
  vollständig; eine Fortschrittsmarke je Team gibt es nicht.
* **Prüfen** (`RunOnceAsync`, je Konto einmal, dann alle `RecheckDays` 90): Profile gesammelt über `POST /api/users`. (1) Steht ein
  Klarname im Profil, der zu einem Spieler der laufenden Saison passt (alle Nachnamen-Teile + Vorname voll, `FirstNameMatch`), gilt das
  Urteil der Namenssuche (`Judge` mit `lead` = die Team-Herkunft als erster Hinweis statt „Nutzername aus dem Namen"), +1 Punkt.
  (2) Sonst, wenn das Konto für einen Verein gespielt hat: seine letzten `MaxGames` 100 Lichess-Partien (mindestens `MinGames` 20
  ohne Bullet, `LeagueFingerprint.Usable`) gegen die Brettpartien JEDES Spielers dieses Vereins (alle Saisonen, Mannschaftsname enthält
  den Ort) — `LeagueFingerprint`: Stellungen nach den eigenen Zügen bis Halbzug 20, je Online-Partie die tiefste gemeinsame, gemittelt.
  Vorgeschlagen nur mit mindestens `ClubMargin` 1,3-fachem Abstand zum Zweiten (an der Meldeliste der Online-TMM 2021 geprüft: 11 von
  13 richtig, ab 2 alle 4) und plausibler Wertung (`RatingPlausible`); Punkte 4 ab Abstand 2, sonst 3. **Seit 0.614.0 zusätzlich**
  (die Kalibrierung stammt aus der Online-TMM 2021, wo JEDER Teilnehmer Ligaspieler war — in offenen Battles wie der Quarantäne-Liga
  sind es viele nicht, und „passt am besten unter den Vereinsspielern" traf schon mit dem ersten Zug allein): gemeinsame Stellungen im
  Schnitt mindestens bis Halbzug `MinDepth` 4 (falsche Treffer 1,6/2,4, bestätigte meist 4–11); kein Vorschlag, wenn der Nutzername
  einen anderen Vornamen aus den Meldelisten nennt (`OtherFirstName`, „Markus_Ragger" für Herbert — vermutlich der GM selbst); kein
  Vorschlag (auch über den Klarnamen) für ein Konto, das schon bei IRGENDEINEM Spieler eingetragen ist (ein selbst gemeldetes Konto
  kam sonst über die Stellungen beim Vereinskollegen noch einmal). Das Ergebnis je Konto steht in
  `LeagueScoutAccount.Result`. Ein Konto, dessen Partien nicht zu holen sind, kommt am nächsten Tag wieder; ein 429 beendet den Durchgang.
* **Eigene Vorschläge** (`LeagueAccountSuggestion.Source = "team"`; seit 0.716.0 auch `"report"` = aus einer Meldung, siehe
  „Online-Schach Oberbayern"): die Namenssuche räumt beim erneuten Suchen nur Vorschläge OHNE
  Source weg — sonst verschwänden die der Team-Suche bei jedem Rescan, weil sie sie nie bestätigen kann.
* **Minderjährige**: vor jedem Vorschlag holt die Team-Suche den Such-Eintrag samt Jahrgang (`LeagueAccountFinder.ScanRowAsync`, legt
  ihn mit Fassung 0 an, die Namenssuche bleibt damit fällig) — ohne Eintrag gälte das Konto als sichtbar, und die Team-Suche erreicht
  auch Spieler, die die Namenssuche noch nicht (oder als Spieler früherer Saisonen nie) abgesucht hat.
* **Takt**: im `LeagueOnlineSyncScheduler` nach der Namenssuche, je Runde höchstens `SearchBudget`; `LeagueOnline:TeamScout=false`
  schaltet sie ab.
* **Je Region** (0.712.0, Wunsch 2026-10-07 „Online-Konten-Zuordnung für die Region Bayern (SK Weilheim, Schachkreis Zugspitze,
  Bezirk Oberbayern)"; `Services/League/LeagueOnlineRegions.cs`, `LeagueOnlineRegion`): Tirol und Bayern haben je Orte der
  Team-Suche (`Places`; Tirol wie bisher, Bayern = Weilheim, Starnberg, Gräfelfing, Germering, Gröbenzell, Gauting, Gilching, Ammersee,
  Windach, Fürstenfeldbruck, Tölz, Tegernsee, Miesbach, Penzberg, Geretsried, Wolfratshausen, Garching, Dachau, Augsburg, Haunstetten,
  Kriegshaber, Landsberg, Kaufbeuren, Rosenheim, Freising, Ingolstadt, „München|Münchner|Münchener" — „|" = weitere Schreibweisen,
  Schlüssel ist die erste —, Zugspitze, Oberbayern; „Bayern" allein NICHT, 110 Fremdtreffer), feste Lichess-Teams (`FixedTeams`,
  Bayern 39 aus der Recherche 07.10.2026: Kreis/Bezirk `schachkreis-zugspitze`, `schachbezirk-oberbayern`, `…-lounge`,
  `schachkreis-ingolstadt-freising`, `schachkreis-inn-chiemgau` und die Vereins-Teams, deren Ort nicht im Namen steht wie
  `grobes-schach` = SC Gröbenzell, `windacher-chess-academy`), Land (AT/DE), Orts-Regex fürs Profil (Tirol `TirolPlace`, Bayern aus
  den Orten, ä/ae/a), Beschriftungen („Tiroler/Bayerische Lichess-Teams", „Tiroler/Bayerischer Ort im Profil") und Online-Liga
  (Tirol „Online-TMM 2021", Bayern „Online-Liga Zugspitze/Oberbayern" = Serien `ZugLiga`/`ObbLiga`/`ZugspitzProbeLiga`).
  Konfig je Region `LeagueOnline:TeamPlaces:{region}` (Tirol weiter auch `LeagueOnline:TeamPlaces`) und `LeagueOnline:Teams:{region}`
  (Komma-Listen). **Vereins-Schlüssel** (`LeagueOnlineRegion.ClubKeys`): in Bayern zählt auch das Adjektiv („Gautinger SC" → gauting,
  „Tölzer Schachtiger" → toelz), Tirol bleibt beim Ortsnamen; **Kreis-/Bezirks-Teams** („Schachkreis …", „Schachbezirk …",
  `LeagueOnlineRegions.IsAreaTeam`) und die Such-Orte Zugspitze/Oberbayern (`AreaPlaces`) sind NIE „sein Verein" — wer für den Kreis
  spielte, steht im (i) mit „warn" bzw. „info". **Jugend**: Teams und Team-Battles mit „Jugend" im Namen/der Kennung kommen nicht in
  den Bestand (alle Regionen; Minderjährige). **Pool-Lauf** (`RefreshPoolAsync(ct, region?)`) je Region, aber nur für Regionen mit
  Ligen im Bestand (ganz ohne Ligen alle); feste Teams nur mit `GET /api/team/{id}` für den Namen, wenn die Suche sie nicht fand.
  **Prüfung** (`RunOnceAsync`): Klarnamen gegen die Spieler der laufenden Saison JEDER Region, das Urteil nach der Region des
  Spielers; die Stellungs-Prüfung vergleicht mit den Vereinsspielern derselben Region (Schlüssel einmal je Durchgang gerechnet).
  **`EventSeries`** kennt die Kreis-Schreibweise: „1-ZugLiga 1-21 7+3 Team Battle" → „ZugLiga", „KEM2022 7+3" → „KEM",
  „Kreis-Vergleichskampf-OBB 2-21" → „Kreis Vergleichskampf OBB" (Runde vorn, Bedenkzeit/Saison-Teil hinten, Bindestrich-Ketten ab
  drei Wörtern, angehängte Jahreszahl); Tests mit den Literalen in `LeagueAccountChecksTests.EventSeries_ZugspitzeAndOberbayern`.

**Konto-Prüfung (i)** (0.619.0, Wunsch 2026-09-30: „mach bei den Konten immer ein (i) und zeig an, was alles geprüft wurde:
Selbstmeldung, TMM 2021, Name, Land, % Übereinstimmung Repertoire, Elo passend"; `Services/League/LeagueAccountChecks.cs`): je
eingetragenem Konto und je Vorschlag eine Liste von Prüfungen `{ key, label, status, text }` — `ok` spricht dafür, `weak` spricht
schwächer dafür (0.621.0), `warn` macht stutzig, `fail` spricht dagegen, `none` = nichts zu prüfen, `info` = zur Kenntnis. Reihenfolge
wie im Wunsch:
* **Selbstmeldung** (`LeagueSelfReports`, eingespielt je QUELLE über `POST /api/league/admin/self-reports`, z. B. die Meldeliste der
  Online-TMM 2021): dieses Konto von ihm gemeldet → ok; von einem ANDEREN Spieler → fail (Name nur, wenn dessen Konten nicht verborgen
  sind); er hat ein anderes Konto derselben Seite gemeldet → warn.
* **Online-TMM 2021** (seit 0.712.0 die Online-Liga der REGION des Spielers: Bayern „Online-Liga Zugspitze/Oberbayern", Serien
  ZugLiga/ObbLiga; Schlüssel bleibt `tmm2021`): das Konto steht im Bestand der Team-Suche und `LeagueScoutAccount.Events` nennt die Serie (der Scout merkt sich
  seit 0.619.0 je Team-Battle die Serie ohne Runde, `EventSeries`: „Online TMM 2021 Runde 3 Team Battle" → „Online TMM 2021"); für
  seinen Verein (`ClubKeys` des Teams gegen die Orte seiner Mannschaften) → ok, sonst warn. Bestand von vor 0.619.0 hat noch keine
  Serien — bis zum nächsten Pool-Durchlauf steht dort „info".
* **Name im Profil** (`FirstNameMatch` wie die Suche; nur Initiale oder Nachname = weak), **Nutzername** (aus dem Namen gebildet /
  anderer Vorname aus den Meldelisten = fail / enthält den Nachnamen = weak), **Land** (Land der Region — Österreich bzw. seit 0.712.0
  Deutschland für Bayern — oder Föderation laut Meldeliste bzw. FIDE; ein deutsches Profil ist bei einem Weilheimer ok, bei einem Schwazer
  ohne GER-Föderation fail).
* **Übereinstimmung Repertoire** (`LeagueFingerprint.Coverage`): Anteil der letzten 100 Online-Partien (ohne Bullet, wenn genug
  andere), die mindestens `RepertoireOwnMoves` (3) EIGENE Züge weit einer Stellung aus seinen Brettpartien folgen (nach einem eigenen
  Zug ist fast jede Partie „im Repertoire"); ab 35 % ok, 10–35 % weak, unter 10 % warn — online spielt man oft anderes, deshalb nie fail. Ein
  eingetragenes Konto nimmt die gespeicherten Partien, ein Vorschlag holt sie (Lichess ein Abruf, chess.com die jüngsten drei
  Monatsarchive). Unter 5 Brettpartien bzw. 10 Online-Partien: nichts zu prüfen.
* **Online-Wertung je Kategorie** (`Profile.Ratings`, alle Kategorien mit mindestens einer Partie): nur belastbare zählen (≥ 10
  Partien, nicht vorläufig); 100–300 über der Elo ok (optimal), jede andere bis 400 darunter weak, mehr als 400 darunter fail.
* **Gleicher Name auf der anderen Seite** (0.621.0): schon als sein Konto eingetragen → ok; gibt es und passt nach den Regeln der Suche
  → ok (mit den Hinweisen); gibt es, passt aber nicht → info; gibt es nicht / gesperrt → none.
* Dazu FIDE-Wertung und Ort der Region im Profil (Schlüssel `place`, bis 0.711.0 `tirol`; „Tiroler/Bayerischer Ort im Profil"),
  Lichess-Teams der Region („Tiroler/Bayerische Lichess-Teams") und andere Team-Battles, zuletzt aktiv, gesperrt, bei einem anderen
  Spieler eingetragen, das Ergebnis der Team-Suche und (Vorschlag) ihre Hinweise.
Das Profil wird dafür FRISCH geholt (dieselben Abrufe wie die Suche); ist die Seite nicht erreichbar, stehen die Profil-Prüfungen auf
„nicht geprüft — …" und das Ergebnis wird nur eine Minute gemerkt (ebenso, wenn die Partien eines Vorschlags gerade nicht kamen),
sonst `CacheFor` 10 min (IMemoryCache). Verborgene Konten (Minderjährige) → 404, außer für Admins (0.625.0). **Partien eines
Lichess-Vorschlags NUR mit `Accept: application/x-ndjson`** — ohne den Kopf liefert Lichess PGN, das Lesen scheiterte, und bis 0.624
zeigte JEDER Lichess-Vorschlag „Partien gerade nicht abrufbar". Lichess gibt Partien nur einem Abruf je Adresse zugleich heraus;
läuft der Hintergrund-Abruf, kommt 429 → Text „Lichess bremst gerade …". Oberfläche: rundes (i) in der Konto- bzw. Vorschlags-Zeile (`shared/account-checks.component.ts`, reine Regeln
in `core/account-checks.ts`), Liste darunter mit Zeichen (✓ ! ✕ – i) + Wort für Vorleser + Satz, oben die Zusammenfassung.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/accounts/{id}/checks` | view | Prüfung eines Kontos `{ site, user, url, player, elo, checkedAt, profileLoaded, items[] }`; 404 unbekannt/verborgen |
| POST | `/api/league/s/{token}/player/{fide}/accounts` | anonym (Teilen-Link) | **Online-Konto ohne Anmeldung eintragen** (0.630.0, Wunsch: „soll auch für nicht registrierte User möglich sein — direkt als sicher, beim Spieler vermerken, wer ihn hinzugefügt hat, in dem Fall dann anonym") `{ site, user, comment }` → sofort „gesichert", `AddedBy = "anonym"` + Hash des Links, Kommentar „Über einen Teilen-Link hinzugefügt (anonym)…"; nur Spieler der geteilten Begegnung (sonst 404), 400 wie beim Anlegen und `takenElsewhere` (Konto steht schon bei einem anderen Spieler). Ändern/Entfernen bleibt den Verwaltern. Angemeldet zeigt jedes Konto „hinzugefügt von …" (`addedBy` im Konto-JSON, nie über den Link), das (i) eine Zeile „Eingetragen"; Anlegen und Übernehmen eines Vorschlags vermerken den Nutzernamen |
| GET | `/api/league/suggestions/{id}/checks` | manage | Dasselbe für einen Vorschlag |
| POST | `/api/league/admin/online-reports/zugspitze?season=&dryRun=` | manage | **Online-Schach Oberbayern** (0.716.0, siehe unten) für EINE Saison (`20204`, `20211`, `20212`, `20213`, `20221`) → `{ season, source, dryRun, counts{ tournaments, skippedYouth, unreadable, rows, matched, ambiguous, notOnLichess, noRoster, rosterAmbiguous, otherClub, noFide, assigned, conflicting, reports }, reports{ added, updated, unchanged, removed, skipped, dryRun }, suggestions, items? }` (`items[{ fide, player, user, team, pageName, tournaments[] }]` nur bei `dryRun`); 400 `invalidSeason`, 404 `notFound` (keine Turnierliste), 503 `rateLimited`/`unreachable` |
| POST | `/api/league/admin/self-reports?dryRun=` | manage | Selbstmeldungen einer Quelle einspielen `{ source, reporter?, items[{ fide, site, user (Name oder Profiladresse), team, note? }] }` — ERSETZT die Einträge dieser Quelle → `{ added, updated, unchanged, removed, skipped[{ index, reason }], dryRun }` (`reason` ∈ invalidFide/unknownPlayer/invalidUser/duplicate); 400 `noSource`/`tooMany` (über 5000)/`invalidReporter` (über 60). Mit `reporter` (0.629.0) sind es Meldungen DRITTER: im (i) je Meldendem eine Zeile „Gemeldet von …" (`reported:<Name>`, gleich nach der Selbstmeldung; ok = für ihn gemeldet, fail = für einen anderen Spieler, warn = für ihn ein anderes Konto derselben Seite, sonst „nicht in der Liste von …"), die Selbstmeldung zählt nur Zeilen ohne `reporter` |

**Online-Schach Oberbayern** (0.716.0, Wunsch 2026-10-07 „Online-Konten-Zuordnung für die Region Bayern";
`Services/League/ZugspitzeOnlineReports.cs`): der Schachkreis Zugspitze hat 2020/Q4–2022/Q1 rund 150 Turniere auf Lichess
ausgerichtet (ZugLiga/Kreisliga, ObbLiga/Bezirksliga, Online-KEM, Kreis-Vergleichskämpfe, Blitz/Rapid/Klassik-Serien); je Turnier
steht unter `https://schachkreis-zugspitze.de/onlineergebnis/?saison=…&serie=…&turnier={id}` eine Einzelwertung mit Rang,
„Nachname,Vorname", Verein und Lichess-Wertung — aber OHNE Nutzernamen. Der Importer (Endpunkt oben) liest je Saison
`/onlineturniere/?saison=` (nur Zeilen mit `onlineergebnis`-Link — 4er-MM laufen bei einem anderen Ausrichter; Art aus dem Lichess-Link,
sonst „N Runden" = Schweizer System, bei 404 die andere Art), je Turnier die Ergebnisseite (drei Tabellen nebeneinander: links
Rang/Name/Team/Rating, rechts Punkte + „Perf" bzw. „S-B"; bei Teamkämpfen ab „Einzelwertung") und `lichess.org/api/{swiss|tournament}/{id}/results`
(ndjson). **Zuordnung** über Wertung + Punkte, bei Gleichstand dazu Performance bzw. Sonneborn-Berger — NIE über den Rang (nicht
angemeldete Lichess-Spieler fehlen auf der Seite); mehrdeutig → weg, ein Konto für zwei Zeilen → beide weg. Dann Name + Verein gegen
die Meldelisten der Region Bayern (`LeagueRosterIndex`, voller Vorname Pflicht — die Index-Stufe „Nachname + Initiale" fiele sonst auf
den falschen; mehrere gleichnamige → der mit demselben Verein laut `LeagueOnlineRegion.ClubKeys`; Verein der Seite passt zu keinem
seiner Vereine → `otherClub`; Kreis-Team auf der Seite widerspricht nie; ohne FIDE-ID → `noFide`, Meldungen hängen an der FIDE-ID).
Ein Konto, das in zwei Turnieren zwei Spielern zugeordnet würde → `conflicting`, weg. Ergebnis über `LeagueSelfReportImport`
(ERSETZT die Quelle) als Meldungen Dritter: `Source` „Online-Schach Oberbayern {saison}", `Reporter` „Schachkreis Zugspitze",
`Team` = Verein laut Seite, `Note` = erstes Turnier mit Rang + Wertung („+ n weitere") → im (i) die Zeile „Gemeldet von Schachkreis
Zugspitze". **Vorschläge**: Selbstmeldungen flossen bis dahin NICHT in die Konto-Vorschläge — der Importer legt je Meldung einen an
(`LeagueAccountSuggestion.Source = "report"`, Punkte 5, Hinweis „Gemeldet von Schachkreis Zugspitze (…): Name, Verein — Turniere"),
außer das Konto steht schon bei irgendwem oder es gibt den Vorschlag für ihn (auch verworfen); vorher der Jahrgang
(`ScanRowAsync`, Minderjährige verborgen). Die Namenssuche räumt ihn nicht weg (sie räumt nur Vorschläge ohne Source).
**Minderjährige**: Turniere/Serien mit „Jugend" werden nicht abgerufen. **Höflich**: 1 s Pause nach jedem Abruf (je Saison ~2 je
Turnier: 20204 39, 20211 28, 20212 13, 20213 1, 20221 11 Turniere ohne Jugend), Lichess 429 → Abbruch (503 `rateLimited`), geschrieben
wird erst am Ende. Am 07.10.2026 gegen zwei echte Seiten geprüft: Kreisliga-Teamkampf 21/21 zugeordnet, Blitz-Swiss 14/15 (einer
ohne passendes Konto). Reihenfolge: zuerst `?dryRun=true` je Saison ansehen, dann ohne.

**Lichess-Übertragungen** (0.608.0, Wunsch 2026-09-30; `Services/League/LeagueBroadcastImport.cs`, Tabelle `LeagueBroadcasts`):
Partien aus Lichess-Broadcasts von Turnieren am Brett kommen in die Spielerkarten (Quelle `Lichess-Übertragung`), zugeordnet über
`WhiteFideId`/`BlackFideId` wie die Megabase (`LeagueProfileStore.ImportGamesAsync`). Gemessen am 2026-09-30: in neun
Übertragungen 406 Partien mit Ligaspielern, davon fehlten 43 (21 aus der Bundesliga) — der Rest steht über chess-results schon
da; der Gewinn ist vor allem, dass die Partien schon WÄHREND des Turniers da sind. Regeln:
* **Finden**: Lichess-Suche (`/api/broadcast/search`) nach `LeagueBroadcasts:Queries` (Komma-Liste, Vorgabe Austria, Österreich,
  Tirol, Tyrol, Südtirol, Innsbruck, seit 0.712.0 auch Bavarian, Bayerische, Tegernsee, Munich, München — Bayerische
  Einzelmeisterschaften, Bavarian Open, Tegernsee Masters, Munich Chess Festival; „Bayern"/„Oberbayern"/„Landesliga Süd" bringen
  nichts, eine Lichess-Übertragung der Landesliga Süd/Oberliga/Zugspitzliga gibt es nicht; höchstens 10 Seiten je Wort), nur bis `LeagueBroadcasts:MaxYears` (5) zurück; am 30.09. 62
  Übertragungen. Der Takt sucht höchstens alle 20 h (merkt es sich selbst, ein Neustart sucht sofort). Turniere im Ausland per Link
  (`AddAsync`, Turnier- ODER Runden-Link — der Runden-Link wird über `/api/broadcast/-/-/{roundId}` aufgelöst).
* **Einspielen** (`CleanPgn`): nur fertige Partien (kein `*`), Standard ab der Grundstellung, mit mindestens einer FIDE-ID; Datum mit
  Punkten (ältere Übertragungen schreiben „2024-08-24" — sonst erkennt `Merge` die chess-results-Fassung nicht), fehlt es, gilt
  `UTCDate`; nur die Hauptvariante, ohne `[%eval]`/`[%clk]`/Anmerkungen; Kopfzeilen samt `GameURL`.
* **Dieselbe Partie mit ANDEREM Datum** (Kufsteiner Open 2026: Partien 30.05., Turnier 31.07.): `ImportGamesAsync(…, skipSameMoves:
  true)` verwirft sie über `LeagueProfileStore.SameGameKey` (beide Nachnamen + erste 20 Halbzüge, mindestens 10) gegen das, was die
  Karte schon hat (fremde + Vereinspartien).
* **Stand**: laufende alle 6 h neu, noch nicht begonnene warten; fertig = alle Runden `finished` (sonst 3 Tage nach dem letzten Tag)
  → nie wieder. **Falle**: `ImportGamesAsync` leert am Ende den ChangeTracker — den Stand der Übertragung schreibt `SaveAsync` deshalb
  ausdrücklich (`Update`). Ein Bündel-Import mit Profilen ersetzt die fremden Partien der Karten und setzt deshalb alle Übertragungen
  zurück (`Finished`/`ImportedAt`), sonst wären ihre Partien weg.
* **Takt**: im `LeagueOnlineSyncScheduler` nach Abruf und Konto-Suche, je Runde höchstens 5 min; `LeagueBroadcasts:Enabled=false`
  schaltet es ab. Oberfläche: Reiter „Übertragungen" (`/uebertragungen`, `features/broadcasts/`, nur Verwalter) mit Liste und
  „Hinzufügen und einspielen".

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/admin/broadcasts` | manage | Alle vorgemerkten Übertragungen (jüngste zuerst) mit Stand |
| POST | `/api/league/admin/broadcasts` | manage | `{ url }` (Turnier- oder Runden-Link, auch die nackte Kennung) → hinzufügen + einspielen; 400 `invalidUrl`, 404 `notFound`, 503 `rateLimited`/`unreachable` |

**Letzte Partien nachspielen** (0.578.0): `GET /api/league/player/{fide}/recent` (und `/api/league/s/{token}/player/{fide}/recent`)
→ `{ fide, games[{ date, vs, color, pgn }] }` — dieselbe Auswahl und Reihenfolge wie `recent` der Karte
(`LeagueProfileBuilder.Recent`, `RecentCount` = 8), aus dem aktuellen Bestand gerechnet (fremde + Vereinspartien). Die Karte holt
sie erst beim ersten Klick und findet die Zeile über Datum + Gegner + Farbe wieder (die gespeicherte Karte kann älter sein);
nachgespielt wird in der Karte selbst (`shared/game-replay.component.ts`: Brett aus Sicht des Spielers, Züge deutsch, Knöpfe,
Pfeiltasten, Pos1/Ende). **Nach Farbe** (0.592.0): ist oben Weiß oder Schwarz gewählt (aus einer Brett-Zeile geöffnet von
selbst), holt die Karte `…/recent?color=w|s` — die letzten acht Partien DIESER Farbe samt PGN und denselben Angaben wie die
Karte (`LeagueProfileBuilder.RecentEntry`); acht gemischte zu filtern ließe oft nur drei übrig. „Beide" zeigt die Liste der Karte. Angemeldet stehen darüber „Zu meinen Partien“ und „Partie teilen“ (0.587.0,
`core/my-games.service.ts`): beide legen die Partie über `POST /api/games/import` in RookHubs „Meine Partien“ (eine schon
vorhandene wird nicht doppelt angelegt, die Id kommt trotzdem zurück); „Zu meinen Partien“ springt dann per Einmal-Code
nach RookHub auf `/games/{id}` (`HandoffService.jumpToRookHub`), „Partie teilen“ holt den Teilen-Token (`GET /api/games/{id}`)
und teilt RookHubs öffentlichen Link `/g/{token}` — am Handy über das Teilen-Blatt, sonst in die Zwischenablage. Ohne
Anmeldung (Teilen-Link) und ohne RookHub zur Adresse (localhost/IP) fehlen beide Knöpfe.

**Aus RookHub** (0.580.0): das ⋮-Menü der eigenen Partie (`/games/:id`) bietet mit `league.contribute` „In die
Vereins-Datenbank (LeagueHub)" — Sprung per Einmal-Code (`HandoffService.jumpToLeagueHub`, Adresse aus `leagueHubUrl` in
`core/partner-site.ts`: rookhub ↔ leaguehub, rookhub-dev ↔ leaguehub-dev, sonst kein Menüpunkt) auf
`/verein/neu?partie={id}`; LeagueHub holt das PGN über `GET /api/games/{id}` (dasselbe Konto) und öffnet gleich die Übersicht.

Oberfläche (LeagueHub): Reiter „Prognosen · Vereinspartien · Partien hinzufügen" (`/`, `/verein`, `/verein/neu`,
`/verein/formular/:id`); ohne Konto `/s/:token/hochladen` und `/s/:token/formular/:key` — der Teilen-Link zeigt die
Aufforderung dazu ganz oben. Die Formular-Korrektur benutzt DIESELBE Sitzung wie RookHubs Korrekturseite
(`features/games/sheet-edit-session.ts`, siehe `src/frontend/CLAUDE.md`).

### Spielervorbereitung (Prep) — jeder Spieler des Partiebestands (0.631.0–0.639.0)

Ein Gegner lässt sich aus dem ganzen Partiebestand vorbereiten (ChessBase-Megabase + Lumbras GigaBase), nicht nur aus
den Tiroler Ligen. Rechte: `prep.view` (suchen, Karte lesen), `prep.manage` (einspielen, unsichere Konten, Konto-Suche
und -Pflege) — Admin erfüllt alles. Menüpunkt „Analyse & Sammlung" → „Gegner vorbereiten" nur mit `prep.view`.
Code: `Controllers/PrepController.cs`, `Services/Prep/*`, Oberfläche `features/prep/*` (siehe `src/frontend/CLAUDE.md`).

**Partiebestand (0.631.0).** Tabellen `PrepPlayers` (Identität `KeyHash` aus „#FIDE" bzw. NameKey, eindeutig),
`PrepEvents`, `PrepGames` (eine Zeile je Partie, `Sources` Bit 1 Mega / 2 Lumbra, `Moves` = SAN mit Leerzeichen,
`PlayedOn` JJJJMMTT, keine Fremdschlüssel), `PrepImports` (Quelle + Paket eindeutig).
`POST /api/prep/admin/games?source=Mega|Lumbra&chunk=N&first=M` (`prep.manage`, gzip, ≤ 15 MB, entpackt ≤ 64 MB,
≤ 20 000 Partien) → `{ read, added, duplicates, discarded, reasons, millis, already }`; ein schon eingespieltes Paket
liefert seine Zähler mit `already: true`, 409 `chunkMismatch` bei anderer erster Partie. `GET /api/prep/admin/imports?source=`
→ Pakete + Summen. Dubletten: gleicher Zug-Hash + Halbzüge + je Seite FIDE-ID bzw. Nachname; die Dublette mit FIDE-ID
holt die Partie zum FIDE-Spieler. Skript `scripts/prep-import.py` (7z über Docker-Named-Pipe, `--every/--offset`
Stichprobe, Fortsetzen über den Server, `--rate`, `--timeout`, `--chunk-size`; Kommentare raus, `{` verschachtelt
nicht). Messung 01.10.: 554 B/Partie, voller Bestand ≈ 15,2 Mio. Zeilen ≈ 8,4 GB (+ ≤ 1,4 GB Spieler/Turniere); bei
128 MB Buffer-Pool nur ~20 Partien/s — vor dem vollen Import Pool/Redo vergrößern.

**Lesen (0.633.0, 0.634.0)**, alles hinter `prep.view`:
- `GET /api/prep/players?q=&take=` (FIDE-ID oder Namens-Präfix über `IX_PrepPlayers_NameKey_Games`; Umlaute beidseitig,
  Rückschreibweise ae→a hinten gereiht) → `{ items[{ id, name, fide, games, firstYear, lastYear, maxElo }] }`.
- `GET /api/prep/player/{id}` sowie `/profile`, `/tree`, `/recent`, `/pgn`: Form und Filter der Liga-Karte, dazu
  `games`, `loaded`, `limited`, `limit`, `max`, `since`, `twin`, `twinIncluded`, `accountSearch`. Gemeinsame Schalter
  `all` und `twin`.
- Vorgabe die jüngsten 500 Partien (`Prep:CardLimit`), `all=true` bis 3 000 (`Prep:CardMax`). Kalt kostet jede Partie
  einen Plattenzugriff (2–8 ms); gemessen 02.10. am vollen Lumbra-Bestand mit 128 MB Puffer: Karte 1–4,4 s, 3 000
  Partien 21–24 s. Geladenes bleibt 15 min im IMemoryCache; Online-Partien fragt jede Anfrage neu.
- Mit FIDE-ID kommen dazu: Liga-, chess-results- und Vereinspartien ohne Dubletten (`LeagueProfileStore.GamesAsync`),
  Online-Partien über `LeagueProfileStore.OnlineGames` (gemeinsame Auswahl mit der Liga) und Konten über
  `LeagueService.CardAsync(…, prep: true)` ohne `reveal` (Minderjährige auch für Admins verborgen).
- Nur `prep.view` (0.634.0): Konten nur gesichert und ohne Kommentar, wie über einen Teilen-Link; `unsure=true` wirkt
  nur mit `prep.manage`.
- Namens-Zwilling: gleicher NameKey ohne FIDE-ID, nur wenn genau ein FIDE-Spieler so heißt; `twin=true`, Vorgabe aus.

**Oberfläche (0.636.0).** `/prep` (Suche, `?q=`; Treffer ohne FIDE-ID tragen „ohne FIDE-ID") und `/prep/:id` (die
Spielerkarte von LeagueHub inline, darüber der Umfang mit „Alle laden — höchstens N" und der Namens-Zwilling als
Schalter). Die Karte liegt dafür in `src/app/shared/player-card/` und bekommt ihre Daten über `PLAYER_CARD_API`.

**Online-Konten (0.637.0–0.639.0)** — nur `prep.manage` UND Schalter `Prep:AccountSearch` (Vorgabe aus → 404 `disabled`):
- `GET /api/prep/player/{id}/suggestions` → `{ items, perHour, remaining, accounts, leagueHub }`;
  `POST …/suggestions/scan` (nur Spieler mit FIDE-ID, nur auf Knopfdruck, kein Hintergrundlauf);
  `POST /api/prep/suggestions/{id}/accept|reject`; `GET /api/prep/suggestions/{id}/checks` (die Prüfung (i));
  `PUT /api/prep/accounts/{id}` `{ sure, comment }` und `DELETE /api/prep/accounts/{id}` (0.639.0).
- Türsteher `PrepAccountSearchGate` (Singleton): EINE Suche oder Prüfung zur Zeit (409 `busy`), Suchen höchstens
  `Prep:AccountSearchPerHour` (Vorgabe 20) je Verwalter und Stunde (429 `limit`; Prüfungen zählen nicht). Ein 429 der
  Seiten beendet Suche bzw. Prüfung → 503 `rateLimited`, kein zweiter Versuch (die (i)-Prüfung erkennt es am Text
  „bremst gerade"). Achtung: `GET …/checks` mit 503 wiederholt der `retryInterceptor` der App bis zu dreimal — die
  Wiederholungen treffen das gedrosselte Ergebnis im 1-min-Cache von `LeagueAccountChecks`, rufen also nicht neu ab.
- Gesucht und geprüft wird mit LeagueHubs `LeagueAccountFinder`/`LeagueAccountChecks`; Vorschläge und Konten liegen in
  dessen Tabellen (ein Spieler, ein Kontenbestand). Ein Spieler ohne Liga-Bezug ist
  `LeagueAccountFinder.Player.Local = false`: als Land zählt nur seine Föderation — über die vollständige Tabelle
  `Services/Prep/PrepFederations.Iso` (alle FIDE-Föderationen → ISO; ENG/SCO/WLS → GB, FID/unbekannt → kein Land) —,
  kein Österreich-Bonus, kein Tiroler Ort; Elo = die jüngste aus dem Bestand. Ligaspieler behalten `Fed2` (26 Einträge).
- LeagueHub kennt einen Spieler, den nur die Spielervorbereitung kennt, NICHT (0.638.0): Regel
  `LeagueOnlineAccountService.LeagueKnowsAsync` (Meldeliste `LeaguePlayers` oder Liga-Karte `LeaguePlayerProfiles`).
  Für ihn antworten LeagueHubs Endpunkte wie vor 0.637.0 — Suchen/Anlegen/Übernehmen „unknownPlayer", Vorschläge,
  Prüfung, Verwerfen, Karte, Baum, Profil, `accounts/{id}` (PUT/DELETE/sync/checks) 404, `sources` ohne seine Konten,
  Übersicht und Zähler ohne ihn. Den Rückfall schaltet nur der Prep-Weg ausdrücklich ein: `prep: true` an
  `CreateAsync`, `AcceptSuggestionAsync`, `RejectSuggestionAsync`, `SuggestionsAsync`, `UpdateAsync`, `DeleteAsync`,
  `LeagueService.CardAsync`; `ForSuggestionAsync` mit mitgebrachtem Spieler. Rescan und Team-Suche greifen ihn nicht auf.
- Pflegen (0.639.0): Prep stuft um und entfernt NUR Konten eines Spielers des Bestands, den LeagueHub nicht kennt —
  steht er auch in LeagueHub, 409 `leagueHub` (Pflege dort, `league.manage`). Entfernen löscht die schon geholten
  Online-Partien des Kontos (`ExecuteDelete`) und legt es als verworfenen Vorschlag ab — die Suche schlägt es nicht
  wieder vor; Umstufen lässt die Partien stehen.
- Minderjährige (bekannter Jahrgang unter 18, `LeagueHiddenAccounts`): über `/api/prep/*` nie ein Vorschlag, eine
  Prüfung, ein Konto in der Liste oder eine Pflege — auch nicht für Admins (404); entscheiden kann nur ein Admin in LeagueHub.
- Trainingslinien gegen einen Gegner (0.701.0/0.702.0): `TrainingLinesService` + `OpponentTrainingLines` (Services/Prep)
  hinter `GET /api/prep/player/{id}/training-lines` (prep.view; Partien wie die Karte inkl. `all`/`twin`, Filter wie das
  Profil) und `GET /api/league/player/{fide}/training-lines` (eigene Datei `LeagueTrainingController`, league.view, KEINE
  Fassung unter `/s/{token}`). Parameter: `repertoire` (optional, ohne = alle markierten, siehe unten), `color` w/b, `chapterColors` (JSON, eigene Kapitelfarben des
  Trainers), `take` (Vorgabe `Prep:TrainingLines` = 50, höchstens 5000), `source`/`speeds`/`years`/`unsure` (ohne `source`:
  Brett + online; die Karte schickt immer ihren Filter mit). Nur EIGENE Repertoires mit `UseForExtension` (Oberfläche:
  „Für Extension und Vorbereitung verwenden"), sonst 404. Rechnung: nur Partien des Gegners mit der anderen Farbe, je die
  ersten 40 Halbzüge als Präfixbaum durch EIN Brett, gezählt je Stellungsschlüssel (`RepertoireReach.Key`, Zugumstellungen
  inklusive); Wahrscheinlichkeit = Produkt über die Gegnerzüge (Anteil des Zugs unter den Partien, die die Stellung
  erreicht UND dort weitergespielt haben), eigene Züge 1; „nie erreicht" = keine Partie erreicht die Stellung nach dem
  letzten Gegnerzug (bleibt in der Liste, hinten). Linie = Hauptvariante eines Abschnitts (wie im Trainer, Info-Linien
  raus). Antwort `{ repertoires, repertoire, color, colors, games, total, lines[{ key, end, start, chapter, moves,
  probability, reached, lastYear, neverReached }], more }`; `key` = Trainer-lineKey (`LineKeyFromSans`). Gemessen mit
  echten Lumbra-Partien: 3000 verschiedene ≈ 0,75 s, 10 000 ≈ 1,7 s.
- Auffüllregel + Trainings-Repertoire (0.705.0): Je Linie `matched` (Gegnerzüge, die er von vorne weg getroffen hat —
  bis zur ersten Stellung, die er nie erreicht oder in der er nie den Zug der Linie spielt), `missing` (= Gegnerzüge der
  Linie − matched), `prefixProbability`, `prefixReached`. Reihung in Stufen: voll getroffen nach Wahrscheinlichkeit, dann
  1 fehlender Gegnerzug („als ob er einen Zug vorher abgewichen wäre") nach Präfix-Wahrscheinlichkeit, dann 2 …; gleich:
  Partien ↓, Repertoire-Reihenfolge; matched = 0 = „nie erreicht", ganz hinten. Achtung: `missing` zählt relativ zur
  LÄNGE der Linie — eine früh abweichende, kurze Linie kann in derselben Stufe stehen wie eine erst am letzten Zug
  abweichende. `POST /api/prep/player/{id}/training-repertoire` (prep.view) und `POST /api/league/player/{fide}/
  training-repertoire` (`LeagueTrainingController`, league.view, kein Teilen-Link), Rumpf wie die Abfrage
  (`TrainingLinesService.CreateRequest`) → `{ id, name, lines, replaced }`: eigenes Repertoire „Prep: <Gegner> <Jahr>"
  mit höchstens 50 Linien (min(`Prep:TrainingLines`, 50)) in der gereihten Reihenfolge, je Linie der UNVERÄNDERTE
  PGN-Abschnitt der Quelle; `UseForExtension=false`, `Kind` wie die Quelle. Gleichnamiges eigenes wird ersetzt
  (gleiche Id; Dateien über `RepertoireService.DeleteFileAsync`, neue per `UploadFileAsync` — SR-Stand je lineKey bleibt).
- Alle markierten statt Auswahl (0.709.0, Wunsch: „nicht ein repertoir auswählen sondern die markierten verwenden"): ohne
  `repertoire` sind die Quellen ALLE eigenen Repertoires mit `UseForExtension`, nach Name sortiert; je Repertoire die
  Kapitel der gewählten Farbe (Kapitelfarbe wie im Trainer), alle Linien in EINER Reihung mit der Auffüllregel. Gleicher
  Linien-Schlüssel in mehreren Repertoires: einmal, das nach Name erste gewinnt. Antwort: `repertoires[{ id, name,
  colors }]`, `repertoire` = `null` (alle) bzw. die Id (Filter), je Linie `repertoireId`/`repertoireName`.
  `chapterColors` flach (`{ Kapitel: "w" }`, nur für ein gewähltes Repertoire — so schickt es der Trainer) oder je
  Repertoire (`{ "7": { Kapitel: "b" } }`). Anlegen aus allen markierten: höchstens 50 Linien quer über alle Quellen,
  Beschreibung nennt sie, `Kind` gemeinsam sonst None; ein markiertes Repertoire mit dem Zielnamen wird als Quelle
  AUSGENOMMEN (und dann ersetzt), nur als einzige Quelle 400 `sameRepertoire`.
- Schätzung mit Lichess-Partien (0.715.0, Wunsch: „wenn gaaaanz wenig games … nimm lichesspartien, +100 - +400 elo … oder
  kombination"): JE GEGNER-STELLUNG einer Linie zählen seine Partien, wenn er dort mindestens `Prep:TrainingMinOwnGames`
  weitergespielt hat (Vorgabe 1 — seine Züge haben Vorrang, 0.715.1: „mach seine züge immer oberste priorität"), sonst der Explorer (mindestens `RepertoireReach.MinGames` = 10 Partien; fehlt der Zug in seiner
  Liste: 0,5/Total). Wahrscheinlichkeit = Produkt aus der jeweiligen Quelle; je Linie `source` own|mixed|lichess|none,
  `ownMoves`, `lichessMoves`, `lichessFrom` (Halbzug), `pending`; Kopf `ownGames`, `lichessBand` („2000–2300"),
  `explorerIncomplete`. Reihung: alle Linien MIT Quelle nach kombinierter p (dann mehr eigene Gegnerzüge, Partien,
  Reihenfolge), dahinter die ohne Quelle mit der Auffüllregel. Explorer: `ITrainingExplorer` → `TrainingExplorer` →
  `RepertoireExplorerService.BatchStatsAsync` — NUR der lokale Explorer (`LichessExplorer:LocalUrl`, seit 0.718.0 nie
  online; ohne `LocalUrl` ist `ITrainingExplorer.Available` falsch: keine Schätzung, keine Abfrage, kein Token; eigenes
  Budget `LocalBatchBudget` 60 s, 16 gleichzeitig; was nicht ankommt, ist `Pending`). Band = Elo+100…+400 → alle Stufen,
  deren Bereich es schneidet (oberste nach oben offen; lokal nur `LocalRatings`, z. B. 1500 → 1600+1800), Blitz/Schnell/
  Klassisch. Elo: Prep `PrepAccountSearch.LatestEloAsync` sonst `MaxElo`, Liga jüngste Meldeliste (`EloI ?? EloN`),
  sonst 1800. Abgefragt werden nur Gegner-Stellungen ohne genug eigene Daten (`OpponentTrainingLines.NeedsExplorer`),
  je Stellung einmal. Tests mocken den Explorer immer.
- Widerspruch als eigene Stufe (0.715.1): Hat er in einer Stellung weitergespielt, aber NIE den Zug der Linie, ist die
  Linie `source = deviates` — Stufe HINTER allen Linien, die ihm nicht widersprechen (own/mixed/lichess), VOR denen ohne
  jede Quelle. Darin: mehr übereinstimmende Gegnerzüge vor dem Widerspruch (`ownMoves`) zuerst, dann die Schätzung ab dem
  Widerspruch (Präfix aus seinen Partien × Explorer-Anteile für den Rest). `deviationPly`/`deviationSan`/`deviationGames`
  = sein häufigster Zug dort (Liste: „weicht ab: er spielt hier 1…c5 (1 Partie)"). `NeedsExplorer` fragt dafür auch die
  Widerspruchs-Stellungen ab.
- Anlegen mit Rückfrage nur bei Bedarf (0.718.0): `POST …/training-repertoire` hat `replace` (Vorgabe false); gibt es ein
  eigenes Repertoire mit dem Zielnamen und fehlt `replace` → 409 `{ reason: "exists", id, name }`, nichts geschrieben
  (`RepertoireExistsException`); die Karte fragt dann und schickt denselben POST mit `replace: true`. Ohne Treffer sofort
  anlegen. `sameRepertoire` (400) wird VOR `exists` geprüft.
- Tempo (0.718.0, gemessen auf Prod 21–22 s je Abfrage): das Ergebnis von `ComputeAsync` (Reihung samt Schätzung) bleibt
  15 min im `IMemoryCache`, Schlüssel = Nutzer, `scope` (Gegner + Filter, Prep mit all/twin — `TrainingLinesService.Scope`),
  Auswahl, Farbe, chapterColors, exclude und `Id@UpdatedAt` ALLER markierten Repertoires (geändert → sofort neu). Ohne
  `scope` (Tests) kein Cache. Je Rechnung eine INFO-Zeile „Trainingslinien: … ms gesamt — Repertoires laden+parsen …,
  Partien laden …, Zählen …, Explorer … (Stellungen, Treffer, offen, Budget erreicht), Reihen …". Gemessen lokal an den 4
  markierten Repertoires des Users (13,6 MB PGN, 2543 Abschnitte): Parsen 0,25–0,36 s, Graph bauen 1,5–2,4 s, Reihen
  < 0,1 s, ohne Gegnerpartien 7571 Explorer-Stellungen — der Explorer dominiert, danach der Graph (nächster Schritt
  wäre ein Repertoire-Cache wie beim Lochfinder).
- Eigene Startstellung (0.717.0, Screenshot 08.10.: „Prep: Stoettner" bestand aus 50 Chessable-Übungen aus Modellpartien):
  Eine Linie mit `[FEN]` zählt nur, wenn er ihre Startstellung in einer Partie erreicht hat (`OpponentTrainingLines.StartReached`:
  Grundstellung oder `Stats[start].Reached > 0`); sonst bleibt sie „nie erreicht"/`source = none` — OHNE Schätzung, denn der
  Explorer kennt ab der FEN die Züge, aber nicht die Wahrscheinlichkeit, dort je hinzukommen (kurze Übungen mit 1–2
  Gegnerzügen schlugen sonst jede echte Eröffnungslinie). `NeedsExplorer` fragt für solche Linien nichts ab. Linienliste
  der Repertoire-Seite nummeriert die Zugvorschau ab dem Start-FEN („12. Bc4 …" statt „1. Bc4 …", `startNumbering`).
- Gefolgt, aber ohne Quelle danach (0.718.1): `source = none` mit `ownMoves > 0` (er folgt der Linie, so weit seine Partien
  reichen, danach kein Explorer) ist eine eigene Stufe ZWISCHEN „mit Quelle" und `deviates` (Auffüllregel darin) — vorher
  stand die Linie, der er folgt, hinter der, der er widerspricht. Ganz hinten bleiben nur `none` ohne eigenen Treffer.

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
| GET/PUT | `/api/admin/groups/{id}/roles` | `roles.manage` | Rollen einer Gruppe `{ roleIds }` (0.589.0) — jedes Mitglied hat sie, live. Die admin-Rolle geht nie an eine Gruppe (still verworfen), „Everyone" nimmt keine (400) |

Admin-Oberfläche „Gruppen“: Mitglieder hinzufügen über ein Suchfeld mit Liste statt Dropdown (0.591.0, Wunsch „die Dropdown ist dafür nicht geeignet“) — ohne Suche die neuesten Konten zuerst (aus den vorab geladenen 500), ab zwei Zeichen die Server-Suche (`GET /api/admin/users?search=`, auch jenseits der 500), je Zeile ein „Hinzufügen“-Knopf; Mitglieder stehen nicht in der Liste.

**Rechte gelten LIVE, nicht ab dem nächsten Anmelden** (0.589.0, Wunsch 2026-09-28: „das ist doch scheiße — sollte
immer wieder neue Infos holen"). Vorher standen die Rechte als `perm`-Claims im Token (bis 30 Tage gültig): eine neue
Rolle wirkte erst nach dem nächsten Anmelden, eine entzogene galt so lange weiter. Jetzt:
* **Server**: `Services/PermissionResolver.cs` löst eigene Rollen (`UserRoles`) + Rollen der Gruppen (`GroupRoles` über
  `UserGroups`) auf, 60 s gespeichert. `PermissionAuthorizationHandler` (`[HasPermission]`) und `LeagueClubController`
  (Verwalter) fragen ihn statt des Claims; die Admin-Rolle des Tokens erfüllt weiter alles. **Jede Änderung** an Rollen,
  Rollen-Rechten, Gruppenrollen, Mitgliedschaften und dem Admin-Flag ruft `PermissionResolver.InvalidateAll()` — wer eine
  neue Schreibstelle dafür baut, ruft es mit, sonst gilt die Änderung bis zu einer Minute später. **Der Admin-ENTZUG**
  (`AdminService.ToggleAdminAsync`) rotiert deshalb zusätzlich den Security-Stamp (die Token-Rolle gälte sonst bis zu
  90 Tage weiter; alle Sitzungen des Kontos enden sofort) und nimmt die System-Rolle „admin" weg, die der `RoleSeeder`
  beim Start nur ANLEGT; das Hochstufen hängt sie an.
* **Oberfläche**: `GET /api/auth/permissions` → `{ isAdmin, permissions }` (live). `AuthService.has` liest diesen Stand
  (Signal, sonst die Claims des Tokens als Rückfall); `core/permission-refresher.service.ts` holt ihn vor dem ersten
  Seitenaufbau (`provideAppInitializer` in RookHub, Turnierseite, LeagueHub, höchstens 3 s gewartet), bei jeder
  An-/Abmeldung, beim Zurückkehren in den Tab (höchstens alle 2 min, seit Codereview F1-019 — vorher ≥ 30 s) und alle
  2 min, solange der Tab sichtbar ist.
* Die `perm`-Claims beim Anmelden bleiben (jetzt inkl. Gruppenrollen) — nur noch Startwert für ältere Oberflächen.

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
| PUT | `/api/endless/progress/anonymous` | Anon+RL | Anonymer Progress speichern. Eigener Deckel `activeGameState` ≤ 64 K Zeichen (`SaveAnonymousProgressDto.MaxActiveGameStateLength`; Konto: 1 Mio.). Gesamtdeckel `MaxAnonymousProgressRowsTotal` = 10 000 anonyme Zeilen: darüber keine NEUE Session-Id (400 `{ reason: "anonymousStorageFull" }` + Warnung `EndlessAnonymousStorageFull`), bestehende speichern weiter |
| POST | `/api/endless/sessions` | Auth | Session aufzeichnen. `puzzles` ≤ 500 (`RecordEndlessSessionDto.MaxPuzzles`), `lichessId` ≤ 20; je Puzzle ein `EndlessPuzzleAttempt`-Log + Summen-Event `EndlessSessionCompleted` |
| POST | `/api/endless/sessions/anonymous` | Anon+RL | Anonyme Session aufzeichnen (gleiche Deckel; geloggt NUR das Summen-Event, keine Einzel-Puzzle-Events — sonst ~1000 erfundene ES-Dokumente je Aufruf). Gesamtdeckel `MaxAnonymousSessionRowsTotal` = 50 000 anonyme Läufe (der Trim auf 50 gilt nur je Session-Id), darüber 400 `anonymousStorageFull` |
| POST | `/api/endless/sessions/bulk` | Auth | Bulk-Import (localStorage-Migration) |
| POST | `/api/endless/sessions/bulk/anonymous` | Anon+RL | Bulk-Import anonym (gleicher Gesamtdeckel; ein Paket, das nicht mehr passt, wird ganz abgewiesen) |
| POST | `/api/endless/claim-session` | Auth | Anonyme Daten auf User übertragen |

### Kurse (auth, gruppen-/admin-gated)
„Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet. Fortschritt pro Buch (gelöste Puzzles / gesamt), geteilt über beide Modi; der Modus bestimmt nur die Reihenfolge. Alles user-bezogen in der DB. **Sichtbarkeit**: Admins sehen alle Bücher; Nicht-Admins nur Bücher, die einer ihrer Gruppen via `BookGroupAccess` freigegeben sind. Zugriff wird je Buch in jedem Endpoint erzwungen (kein Zugriff → 404).

**`?lang=`** (0.547.0) an `/{bookId}/puzzles`, `/{bookId}/public`, `/{bookId}/next`, `/{bookId}/chapters` und
`GET /api/courses/{bookId:int}` liefert die Kurs-Uebersetzung, wo es eine aktuelle gibt (Kapitel/Titel als Label) —
Regeln unter „Anmerkungen in mehreren Sprachen" → KURSE. Ohne `lang` alles wie bisher.

Der `mode`-Parameter bei `/next` akzeptiert `sequential` (Buchreihenfolge, `after` = überspringen) oder `random` (zufällig, `exclude` vermeidet Wiederholung); `completed` wenn alle gelöst. **Random-Pool: jedes Puzzle nur EINMAL pro Durchgang** — neben den gelösten (CoursePuzzleResults) werden auch die seit dem letzten Reset GESCHEITERTEN ausgeschlossen (CourseAttempt mit `AttemptedAt >= CourseProgress.ResetAt`; `ResetAt==null` ⇒ alle bisherigen Versuche zählen). Erst `POST /reset` (rückt `ResetAt` vor + leert die gelöste Menge) bringt sie zurück. Im Solver-„abgeschlossen"-Panel gibt es dafür im Random-Modus einen „Von vorn"-Knopf. Sequential bleibt unverändert (nur gelöste raus).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses` | Auth | Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle) |
| GET | `/api/courses/access` | Auth | `{ hasAccess }` — Basis für die Menü-Sichtbarkeit (Admin: true wenn Bücher existieren) |
| POST | `/api/courses` (Alt-Route `/api/courses/upload`) | Auth | „Neuen Kurs erstellen“: legt einen persönlichen Kurs an (eigenes Buch, nur für den Besitzer sichtbar). Multipart mit `name` und OPTIONALEM `file` (.pgn, max. 10 MB) — **ohne Datei entsteht ein LEERER Kurs**, der danach über die Detailseite Kapitel für Kapitel gefüllt wird (`ImportVersion` steht sofort auf der aktuellen Pipeline-Version, damit ein handgepflegtes Buch nicht im „Aktualisieren“-Banner hängt); ohne Datei ist `name` Pflicht (400), weil es keinen Dateinamen zum Ableiten gibt. MIT Datei gilt die alte Regel: Puzzle-PGN im Chessable-Stil, sonst 400 und kein Buch. Linien mit mehr als 1000 Halbzügen zählen als ungültig (`PgnImportService.MaxMainlinePlies`, geprüft VOR dem Nachspielen — Codereview N3-003) |
| GET | `/api/courses/{bookId}/chapters` | Auth | Kapitel des Buchs in Lesereihenfolge inkl. Fortschritt je Kapitel (`index`/`name`/`puzzleCount`/`solvedCount`/`progressPercent`); `name=null` = Sammel-„ohne Kapitel" |
| GET | `/api/courses/{bookId}/next?mode=&after=&exclude=&chapterIndex=` | Auth | Nächstes ungelöstes Puzzle (siehe `mode` oben); mit `chapterIndex` auf das Kapitel beschränkt (Pool + Fortschritt). **Kalkulationsbücher → 404** |
| POST | `/api/courses/{bookId}/results` | Auth | Lösungsversuch aufzeichnen (idempotent); validiert Puzzle↔Buch |
| GET | `/api/courses/{bookId}/puzzles` | Auth | Alle Puzzles eines (zugänglichen) Buchs am Stück — für Offline-Speichern. **Kalkulationsbücher → 404** (der Voll-Export enthielte `Moves`, also die Lösung; „öffentlich" heißt Kurs-Zugriff für JEDEN angemeldeten Nutzer). Frontend blendet Offline-Speichern/Durchsehen/Flashcards bei diesen Büchern aus |
| GET | `/api/courses/by-slug/{slug}` | **AllowAnonymous** | Kurz-Alias (`Book.PublicSlug`, nur öffentliche Bücher) → `{ bookId, isCalculation }`; 404 bei unbekanntem Alias. `isCalculation` entscheidet, ob `/{slug}` in den Kalkulations-Modus oder in den Solver springt (ein Kalkulationsbuch hat nur Info-Linien → der Solver meldete sofort „abgeschlossen") |
| GET | `/api/courses/by-slug/{slug}/{chapter}` | **AllowAnonymous** | `/{slug}/{kapitel}` → `{ bookId, isCalculation, chapter, chapterIndex }`. Der Kapitel-Teil der URL IST der Kapitelname (getrimmt, ohne Groß-/Kleinschreibungs-Unterschied; gesucht über ALLE Linien inkl. `IsInfoOnly`, sonst fände ein Kalkulationsbuch gar kein Kapitel); zurück kommt die Schreibweise aus dem Buch. `chapterIndex` = SOLVER-Index (`ChapterOrder`, nur Quiz-Linien) für `courses/:bookId/chapter/:index/:mode` — `null` bei Kalkulationsbüchern (dort filtert der Modus über den NAMEN) und bei reinen Info-Kapiteln. 404 bei unbekanntem Alias ODER Kapitel |
| GET | `/api/courses/stats` | Auth | Aggregierte Kurs-Puzzle-Statistik des Users (TotalAttempts/Solved/Accuracy/Streaks; **ohne Elo** — Kurs-Puzzles haben kein User-Elo). Quelle: `CourseAttempt`. Literal-Route vor `{bookId}` |
| GET | `/api/courses/history?page=&pageSize=` | Auth | Paginierte Kurs-Versuchs-History (neueste zuerst) inkl. Buch-Puzzle-Infos (LineId/Title/BookRating/Difficulty). Literal-Route vor `{bookId}` |
| GET | `/api/courses/stats/breakdown` | Auth | Aufschlüsselung der Kurs-Versuche nach Tag/Thema (aus `BookPuzzle.Tags`), Rating-Band (aus `BookPuzzle.BookRating`) und Aktivität (`PuzzleBreakdownDto`). Literal-Route vor `{bookId}` |
| POST | `/api/courses/{bookId}/reset` | Auth | Fortschritt des Kurses zurücksetzen |
| POST | `/api/courses/{bookId}/convert-to-repertoire` | Auth | „Kurs → Repertoire umwandeln": legt aus dem Kurs-PGN (`CourseRepertoireConversionService.ConvertCourseToRepertoireAsync` → `RepertoireService.CreateFromPgnAsync`, `UseForExtension=false`) ein neues Repertoire an; ein EIGENER Kurs wird dabei entfernt (Verschieben), ein geteilter bleibt. Zugriff wie andere Kurs-Endpoints (kein Zugriff → 404). **Kalkulationsbücher → 404** |
| GET | `/api/courses/reprocess/status` | Auth | Aufbereitungs-Status der verwaltbaren Kurse (Admin: alle; sonst eigene): `{ currentVersion, total, stale, reprocessableLocally, fromCache, refetchable, needsReimport }` (`fromCache` ⊆ `reprocessableLocally`) — Basis fürs „Aktualisieren (N)"-Banner. Literal-Route vor `{bookId}` |
| POST | `/api/courses/reprocess` | Auth | Bereitet alle veralteten verwaltbaren Kurse neu auf: lokal in-place aus `Book.Source.SourcePgn` (Fortschritt/IDs bleiben), Chessable-Kurse mit `[ChessableOid]` vorher mit frischen Zugtexten aus dem Linien-Cache (`StaleAction.Cache`), Chessable-Altbestand ohne Quelle wird als Re-Fetch-Job eingereiht; sonst übersprungen. `?localOnly=true` („Aus Cache") lässt nur den Re-Fetch aus. Läuft im Hintergrund (`ReprocessLauncher`), antwortet sofort 202 `{ started }`; Ergebnis im Log (`reprocessed, updatedLines, rebuiltFromCache, cacheLinesReplaced, enqueued, skipped, failed`) |
| POST | `/api/courses/{bookId}/share` | Auth | „Kurs mit ausgewählten Personen teilen" (Batch) `{ recipientUserIds[] }` — nur der Besitzer eines persönlichen Kurses; Empfänger müssen befreundet sein (Admin an alle). Antwort `{ shared, skipped[] }` (übersprungen mit Grund `self`/`not_found`/`not_friends`/`duplicate`); legt je neuem Empfänger die Notification `course_shared` an. 403 wenn nicht Besitzer |
| GET | `/api/courses/{bookId}/shares` | Auth | Mit welchen Nutzern ist dieser eigene Kurs geteilt (für den Teilen-Dialog); 403 wenn nicht Besitzer |
| DELETE | `/api/courses/{bookId}/share/{recipientId}` | Auth | Freigabe des eigenen Kurses für einen Empfänger zurücknehmen (idempotent); 403 wenn nicht Besitzer |
| POST | `/api/courses/{bookId}/link` | Auth | Kurs mit einem anderen (zugänglichen) Kurs verknüpfen (Buch↔Workbook) `{ linkedBookId }` — persönlich, symmetrisch, je Buch max. 1 Partner (ersetzt bestehende). 400 self-link, 404 unzugänglich |
| GET | `/api/courses/{bookId}/link` | Auth | Aktuell verknüpfter Partner-Kurs `{ linkedBookId, linkedDisplayName }` (leer wenn keiner) — für den Schnellwechsel im Solver. Literal-Route |
| DELETE | `/api/courses/{bookId}/link` | Auth | Verknüpfung dieses Kurses lösen (beide Richtungen, idempotent) |
| GET | `/api/courses/{bookId:int}/flashcards` | Auth | PERSISTENT als Flashcard markierte Linien des Users in diesem Kurs `{ lineIds }` (kein Zugriff → 404) |
| POST/DELETE | `/api/courses/{bookId:int}/flashcards/{lineId:int}` | Auth | Flashcard-Markierung setzen/entfernen (idempotent) → `{ marked }`; 404 wenn kein Kurs-Zugriff oder Linie nicht im Buch. Logik in `FlashcardMarkService`; Frontend: Checkboxen im Durchsehen + „Markierte (n)"-Knopf bzw. ⋮-Menü der Detailseite → `/courses/:bookId/flashcards?marked=1` |
| GET | `/api/courses/{bookId:int}/translations` | **AllowAnonymous** | Kurs-Übersetzungen (0.548.0, `CourseTranslationController`): `{ sourceLanguage, languages[{ language, linesTranslated, linesTotal }], jobs[{ id, bookId, language, status, linesTotal, linesDone, linesFailed, automatic, requestedByMe, queuePosition, createdAt, startedAt, finishedAt, lastError }], quietUntil, myOpenJob?{ jobId, bookId, bookName, language, status }, available, canRequest }` — `status` klein (`queued/running/done/failed/cancelled`), `jobs` = offene (laufender zuerst, dann nach Platz), dahinter die 5 jüngsten erledigten; `queuePosition` nur bei `queued` (1 = der nächste). Lesbar wie der Kurs, anonym nur ein öffentlicher (sonst 404). Bestimmt beim ersten Abruf die Quellsprache |
| POST | `/api/courses/{bookId:int}/translations` | Auth | Übersetzung anfordern `{ language }` → **202** mit dem Auftrag (`jobs[]`-Form), **200** mit einem schon offenen für (Kurs, Sprache). 400 `{ reason, message }` mit `unsupported-language`/`same-language`/`nothing-to-translate`, **409** `user-limit` (+ `openJob`), **503** `not-configured`, 404 ohne Kurs-Zugang. In der Sperrzeit angenommen (wartet). Regeln unter „Anmerkungen in mehreren Sprachen" → KURSE |
| DELETE | `/api/courses/{bookId:int}/translations/{jobId:int}` | Auth | Auftrag zurückziehen: den eigenen WARTENDEN (Admin: jeden offenen, ein laufender bricht sofort ab) → 204; 403 fremder, 409 `not-waiting`, 404 unbekannt/kein Zugang |
| PUT | `/api/courses/{bookId:int}/comment-language` | Besitzer/Admin | Quellsprache der Kommentare korrigieren `{ language }` (nur die Form geprüft, `und` = nicht bestimmbar) → `{ sourceLanguage }`; 403 nicht Besitzer, 400 `invalid-language`. Vorhandene Übersetzungen bleiben gültig |
| GET | `/api/admin/course-translations` | `books.manage` | Warteschlange (laufender zuerst, dann in Dienst-Reihenfolge, mit `bookName`/`requestedByUsername`) + 50 jüngste erledigte, `quietUntil`, `available`, `autoLanguages` — nur Endpoint, keine Oberfläche |

### Kurs-Detailseite + Inhaltspflege (auth)
`/courses/:bookId` (Frontend) zeigt Metadaten, eigenen Fortschritt und die **Kapitel-Verwaltung**.
Logik in `Services/CourseAuthoringService.cs`. Zwei Rechte-Ebenen: **lesen** = Kurs-Zugriff
(`CourseAccess`, kein Zugriff → 404); **Inhalte ändern** = Besitzer des persönlichen Buchs ODER Admin
(sonst 403); den **eigenen Fortschritt** (Kapitel-Reset) darf jeder mit Lese-Zugriff.

Kapitel werden hier über den **Namen** adressiert, nicht über einen Index (der verschiebt sich beim
Anlegen) — und die Verwaltungssicht listet **ALLE** Kapitel, auch rein aus Stellungs-/Info-Linien
bestehende (die Solver-Kapitelliste `GET /chapters` bleibt unverändert: nur Quiz-Linien, Index-Kontrakt
mit `?chapterIndex=`). `CourseManageChapterDto.SolverIndex` verbindet beides (`null` = im Solver nicht
startbar). **Manuell angelegte Linien sind `IsInfoOnly=true`** (keine Lösung ⇒ nie abgefragt, nicht in
Daily-/Random-Pools) — genau das sind die Stellungen des Kalkulations-Modus; Einfügen setzt zusätzlich
`Book.ImportVersion = ImportPipeline.CurrentVersion` (handgepflegte Bücher haben kein Quell-PGN und
sollen nicht im „Aktualisieren"-Banner hängen).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses/{bookId:int}` | Auth | Detailbild: Metadaten, Fortschritt, `canManage`, Kapitel-Verwaltungssicht (`LineCount`/`QuizCount`/`SolverIndex`/`FirstLineId`). Terminierte, für den Betrachter noch gesperrte Wochen einer Kalkulations-Serie fehlen in Kapitelliste UND Zählern (`Services/CalcVisibility.cs`; Besitzer/Admin sehen alles — Codereview A7-002) |
| GET | `/api/courses/{bookId:int}/lines?chapter=` | Auth | Linien EINES Kapitels (leer = „ohne Kapitel") — mit `MoveCount`, aber **ohne Zugfolge**. Gesperrte Woche → leere Liste wie ein unbekanntes Kapitel (kein Orakel) |
| GET | `/api/courses/{bookId:int}/chapter-pgn?chapter=` | Auth | EIN Kapitel als PGN (leer = „ohne Kapitel"): je Linie das Spiel mit derselben `Round` verbatim aus `Book.Source.SourcePgn` (`PgnParser.SplitGameBlocks`), sonst aus der gespeicherten Linie rekonstruiert (`CoursePgnExporter`). **Kalkulationsbücher → 404** (die Züge wären die Lösung) |
| GET | `/api/courses/{bookId:int}/lines/{lineId:int}/pgn` | Auth | EINE Linie als PGN, gleiche Regeln wie beim Kapitel. Alle drei Kurs-Downloads (auch `/{bookId}/pgn`) entfernen die internen Marker `[%alt]`/`[%info]` (`PgnParser.StripInternalMarkers`); „Kurs → Repertoire" und der Repertoire-Endpunkt `/api/repertoires/{id}/pgn` behalten sie (Viewer/Trainer brauchen `[%alt]`), dort entfernt das Frontend sie erst beim Speichern (`shared/pgn-export.util.ts`) |
| POST | `/api/courses/{bookId:int}/lines` | Besitzer/Admin | Stellungen als Text einfügen `{ chapter?, text }` → `{ added, chapter, issues[], totalLines }`. Parser = `Services/FenListParser.cs` (eine FEN je Zeile, führende Nummer „1:"/„2." wird ignoriert, Kommentar nach `\|` oder in `{…}`; **keine** Legalitätsprüfung, nur Struktur). Bereits im Buch vorhandene FENs → `issues` mit Grund `duplicate`; max. 500 Zeilen (`too_many`) |
| DELETE | `/api/courses/{bookId:int}/lines/{lineId:int}` | Besitzer/Admin | Einzelne Linie löschen (räumt Restrict-Abhängige ab: CoursePuzzleResults/CourseAttempts/CourseInfoViews/BookPuzzleAttempts/DailyPuzzles/CalculationTrees) |
| PUT | `/api/courses/{bookId:int}/calculation` | Besitzer/Admin | Kalkulations-Modus des Kurses ein-/ausschalten `{ isCalculation }` → `{ isCalculation }`. Ändert KEINE Linien (Analysebäume/Lösungen bleiben), nur Einstieg + Fortschritts-Zählung. Bewusst hier statt im Admin-Bücher-Tab: wer die Stellungen einfügt, entscheidet auch, wie sie serviert werden |
| PUT | `/api/courses/{bookId:int}/chapters/rename` | Besitzer/Admin | Kapitel umbenennen `{ chapter, newName }` (leerer Name = „ohne Kapitel"); 400 wenn Zielname existiert. Eine Ausgabe der Kalkulations-Serie wird in derselben Speicherung mit umbenannt; Ziel „ohne Kapitel" bzw. ein Ziel mit schon vorhandener Ausgabe → 400, bevor etwas geändert wird (Codereview A7-003) |
| POST | `/api/courses/{bookId:int}/chapters/delete` | Besitzer/Admin | Ganzes Kapitel = alle seine Linien löschen `{ chapter }` — löscht die Ausgabe der Kalkulations-Serie mit (A7-003) |
| POST | `/api/courses/{bookId:int}/chapters/reset` | Auth | **Einzel-Kapitel-Reset des EIGENEN Fortschritts** `{ chapter }` — leert CoursePuzzleResults/CourseInfoViews dieses Kapitels; die `CourseAttempts` bleiben als Zeit-Log stehen, statt dessen merkt sich `CourseLineResets` je Linie den Reset-Zeitpunkt (0.672.3). Pool, Statistik und ✓/✗ zählen nur Versuche nach BEIDEN Zeitpunkten (`CourseService.AttemptsSinceReset`) — vorher fielen die zurückgesetzten Linien weiter aus dem Pool und der Kurs meldete „abgeschlossen". Buchweites `CourseProgress.ResetAt` bleibt (ist buchweit, löscht beim Kurs-Reset die `CourseLineResets` des Buchs), eigene `CalculationTrees` bleiben ebenfalls (Nutzerarbeit) |

### Aufgabenblätter (auth) — Stellungen sammeln, ordnen, drucken

Ein **Aufgabenblatt** ist eine benannte, sortierte Sammlung von Stellungen, die man ausdruckt (6/4/2
Diagramme je A4-Seite, Platz für die Lösung). Jeder Nutzer hat genau EIN Blatt mit `IsClipboard` — die
**Zwischenablage**: dort landet standardmäßig alles, was man „an ein Aufgabenblatt schickt" (ganzer Kurs,
Kapitel, markierte Linien, eine einzelne Stellung aus dem Durchsehen, das zuletzt gelöste Puzzle). Aus der
Ablage wird per Namen ein festes Blatt — die Stellungen WANDERN mit, die Ablage ist danach wieder leer.

Die Stellung wird beim Senden **ausgeschrieben** (FEN + Ausrichtung), nicht verlinkt: ein Neuimport des
Kurses ändert ein fertiges Blatt nicht mehr. `Source`/`SourceId`/`BookId` sind reiner Herkunftsvermerk
(kein FK). Gerechnet wird die AUFGABEN-Stellung im Frontend (`features/worksheets/worksheet-items.util.ts`
über `buildFlashcard`/`taskItemFromPuzzle`) — dieselbe Rechnung wie bei den Karteikarten, damit Blatt und
Karte nie auseinanderlaufen. Gedruckt wird ohne Lösung und ohne Linientitel (der verriete die Aufgabe);
Überschrift und Begleittext schreibt der Ersteller je Aufgabe selbst dazu.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/worksheets` | Übersicht: Zwischenablage zuerst, dann die benannten Blätter (zuletzt geändert zuerst), je mit `itemCount` |
| GET | `/api/worksheets/clipboard` | Zwischenablage samt Stellungen (wird beim ersten Zugriff angelegt) |
| GET | `/api/worksheets/{id:int}` | Ein Blatt samt Stellungen (fremdes Blatt → 404) |
| POST | `/api/worksheets` | Leeres benanntes Blatt `{ name, perPage? }` |
| POST | `/api/worksheets/clipboard/save` | Ablage unter Namen sichern `{ name, perPage? }` — Stellungen wandern mit, Ablage bleibt leer zurück; leere Ablage → 400 |
| PUT | `/api/worksheets/{id:int}` | Umbenennen / Dichte / **Themen** `{ name?, perPage?, themes? }` (die Zwischenablage behält ihren leeren Namen; `themes: null` lässt sie unberührt, `[]` löscht sie — max. 12 à 40 Zeichen, dedupliziert ohne Rücksicht auf Groß-/Kleinschreibung) |
| DELETE | `/api/worksheets/{id:int}` | Blatt löschen; die **Zwischenablage wird nur geleert** |
| DELETE | `/api/worksheets/{id:int}/items` | Alle Stellungen entfernen, Blatt bleibt |
| POST | `/api/worksheets/items` | „An Aufgabenblatt senden" `{ worksheetId?, items[] }` — `worksheetId` leer/0 = Zwischenablage. Antwort `{ worksheetId, name, isClipboard, added, skipped, total, full }`. Schon vorhandene Stellungen (gleiche FEN + Ausrichtung) werden übersprungen, unbrauchbare FENs ebenso; Deckel `WorksheetService.MaxItemsPerSheet` = 240 → `full` |
| PUT | `/api/worksheets/{id:int}/items/{itemId:int}` | Überschrift/Begleittext/Ausrichtung `{ heading?, text?, orientation? }` (nur gesetzte Felder wirken) |
| DELETE | `/api/worksheets/{id:int}/items/{itemId:int}` | Eine Aufgabe vom Blatt nehmen |
| PUT | `/api/worksheets/{id:int}/order` | Reihenfolge `{ itemIds[] }` — unbekannte IDs werden ignoriert, nicht genannte Aufgaben hängen sich hinten an (eine halbe Liste vom Client darf keine Stellung verschwinden lassen) |
| POST | `/api/worksheets/{id:int}/share` | Öffentlichen Link einschalten (**idempotent** — ein vorhandenes Token bleibt, sonst wären gedruckte QR-Codes tot) → `{ shareToken }`. Die Zwischenablage lässt sich NICHT teilen (404): ihr Inhalt wechselt ständig |
| DELETE | `/api/worksheets/{id:int}/share` | Link abschalten = Widerruf; ein erneutes Teilen erzeugt ein **neues** Token, gedruckte QR-Codes laufen danach ins Leere |
| GET | `/api/worksheets/shared/{token}` | **`[AllowAnonymous]`** — das geteilte Blatt hinter dem Link: Name + Aufgaben (FEN, Ausrichtung, Überschrift, Begleittext, **Lösung**), keine Besitzer-Daten |

**Themen**: ein Blatt trägt bis zu 12 frei wählbare Themen (`Worksheet.Themes`, CSV) — wonach man ein
altes Blatt wiederfindet. Vorgeschlagen werden sie aus den AUFGABEN: beim Senden wandern die Themen des
Quell-Puzzles als `WorksheetItem.SourceThemes` mit (aus `BookPuzzles.Tags` bzw. `Puzzles.Themes`, roh wie
überall in der App — für diese Keys gibt es keine Übersetzung), der Editor zählt sie und bietet die
häufigsten zum Anklicken an; eigene Wörter („U12 Mittwoch") sind erlaubt. Die Themen wirken NUR in der
Übersicht (Anzeige + Filterzeile) — nicht im Druck und nicht hinter dem geteilten Link. Einzelne Aufgaben
tragen bewusst KEINE eigenen Tags (Überschrift + Begleittext reichen je Stellung).

**Teilen + durchspielen**: ein Blatt kann einen öffentlichen Link bekommen (`/w/{token}`, ohne Anmeldung
— wie `/g/` und `/l/`), der auf dem Ausdruck in der **Fußzeile jeder Seite als QR-Code** steht (ein
einzelnes Blatt auf dem Tisch soll allein funktionieren). Hinter dem Link wird das Blatt GELÖST: dafür
wandert beim Senden die Lösung der Quelllinie als `WorksheetItem.SolutionMoves` (UCI ab der
Aufgabenstellung, Gegnerzüge eingeschlossen) mit; auf dem Papier steht sie nie. Aufgaben ohne Lösung
(FEN von Hand, Stellung aus einer Kommentar-Variante) bleiben dort ein **Rechenbrett** — Züge frei,
nichts wird bewertet. Die Prüf-Logik steht als eigene Klasse `WorksheetTask` in
`worksheet-solve.component.ts` (ohne Angular/HTTP, damit die Regel einzeln testbar bleibt). **Achtung**:
wer den Link hat, sieht damit auch die Lösungen des Kurses — das ist die bewusste Entscheidung des
Teilenden, und Abschalten ist der Widerruf.

Frontend: `/worksheets` (Übersicht), `/worksheets/:id` (Bearbeiten: ziehen, Überschrift/Begleittext,
FEN von Hand, Teilen-Schalter mit Link + QR), `/worksheets/:id/print?print=1` (Druckansicht, öffnet den
Druckdialog), `/w/:token` (öffentliche Löseseite). Gesendet wird über
`<app-send-to-worksheet>` (Menüzeile mit Ziel-Untermenü bzw. `[asButton]` in Werkzeugleisten) — eingebaut im
Kurs-⋮, im Kapitel-⋮, in der Steuerleiste des Durchsehens und im ⋮ der Puzzle-Aktionszeile (neben
„♥ Letztes Puzzle"). Die PDF macht der Browser; gezeichnet wird mit `FlashcardBoardComponent` als
Inline-SVG (chessground-Figuren sind CSS-Hintergründe und werden nicht gedruckt).

### Kalkulations-Modus (auth) — Stellungen ohne Lösung
Ein Buch mit `Book.IsCalculation` (Schalter auf der KURS-Detailseite, Besitzer/Admin — `PUT
/api/courses/{bookId}/calculation`; **nicht** im Admin-Bücher-Tab) ist ein **Kalkulationsbuch**: seine Linien
werden nicht abgefragt, sondern als reine Stellungen (FEN + optionaler Kommentar) zum Durchrechnen serviert.
Die Kursübersicht bietet dafür statt sequenziell/zufällig den Kalkulations-Modus an
(`/courses/:bookId/calc`); Fortschritt = Stellungen mit eigenem Analysebaum (`PuzzleCount`/`SolvedCount` in
`CourseListItemDto` zählen bei diesen Büchern ALLE Linien bzw. die bearbeiteten). `calcPoints`/`calcMaxPoints`
im selben DTO (und in `CourseDetailDto`) = Punktestand des Kurses aus der Selbstbewertung, IMMER als
„x / y" (Maximum = 4 × alle Stellungen) — nur bei Kalkulationsbüchern gefüllt, sonst beide `null`.

**Es gibt keine Lösung — und sie verlässt den Server nicht.** Die Endpoints liefern `BookPuzzle.Moves`
bewusst NICHT aus; bei einer normalen Puzzle-Linie höchstens den Vorlauf bis zum Trainingsstart
(`CalcPositionDto.SetupMoves` = Halbzüge `0..StartPly`), nie die Züge ab dem Schlüsselzug. Der Baum selbst
ist für den Server **opak** (JSON-String, nur auf Größe + Gültigkeit geprüft) — Struktur/Semantik liegen im
Frontend (`features/courses/calc/calc-tree.util.ts`), damit Formatänderungen keine Migration brauchen.
Zugriff je Buch über `Services/CourseAccess.cs` (dieselbe Regel wie die Kurs-Endpoints, aus
`CourseService.CanAccessAsync` herausgezogen); kein Zugriff → 404. Der Modus ist nicht auf Bücher mit dem
Flag beschränkt — es steuert nur den Einstieg in der Übersicht.

**Kein SOLVER-Pfad liefert ein Kalkulationsbuch aus** (`CourseAccess.IsCalculationBookAsync`, seit der
Freigabe öffentlicher Kalkulations-Kurse). Die Solver-/Kurs-Wege reichen die Linien über
`BookPuzzleService.MapToDto` samt `Moves` durch — in einem Kalkulationsbuch ist das die Lösung. Sie
antworten dort deshalb wie auf ein nicht vorhandenes Buch (404): `GET /api/courses/{bookId}/public`
(anonym), `GET /api/courses/{bookId}/puzzles` (Offline-Export), `GET /api/book-puzzles/{id}/next` und
`{id}/random`, seit dem Codereview 2026-09-29 (A7-001) auch `GET /api/courses/{bookId}/pgn`,
`POST /api/courses/{bookId}/convert-to-repertoire`, `GET /api/courses/{bookId}/next` und
`GET /api/book-puzzles/random?bookId=`; die Favoriten (A9-001): `POST /api/favorites` lehnt Linien aus
Kalkulationsbüchern mit 404 ab, `GET /api/favorites` liefert bei solchen Linien `moves` leer. Alle PGN-Exporte
(Buch/Kapitel/Linie) liegen im `CoursePgnExportService` hinter EINER Pforte (`EnsureExportAllowedAsync`, A7-010). **Warum das nötig ist**: ein öffentlicher Kalkulations-Kurs BRAUCHT `Book.IsPublic` für
seine Kurz-URL `/{slug}` — und genau dieses Flag ist auch das einzige Tor dieser Nachbar-Endpoints;
ohne die Sperre erzwänge das Freischalten die Preisgabe der Lösung. Wer die Zugfolgen wieder über die
Kurs-Pfade braucht, schaltet den Kalkulations-Modus aus (Besitzer/Admin, `PUT
/api/courses/{bookId}/calculation`). Unberührt bleibt `GET /api/book-puzzles/{id}` (Einzel-Puzzle per Id) —
bewusst ungegatet für Teilen-Links/Tagespuzzle/OG-Vorschau/Bot-Lookup (siehe `BookAccess`).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/calculations/books/{bookId}` | Auth | Buchkopf + leichte Stellungsliste (`id`/`round`/`title`/`chapter`/`hasTree` + `chosenSan`/`chosenUci`/`secondsSpent`/`grade`/`points`), Reihenfolge Round→Id — ohne FEN/Kommentar/Züge. Dazu **serverseitig** gerechnete Kapitelsummen (`chapters[]`: `positionCount`/`treeCount`/`chosenCount`/`ratedCount`/`points`/`maxPoints`/`secondsSum`) + Buchsummen `points`/`maxPoints`/`secondsSum` |
| GET | `/api/calculations/books/{bookId}/public` | **AllowAnonymous** | Kopf + VOLLSTÄNDIGE Stellungen (`id`/`round`/`title`/`chapter`/`fen`/`setupMoves`/`comment`) eines ÖFFENTLICH freigegebenen Buchs — die EINZIGE (lesende) Öffnung des Modus, gegatet auf **`Book.IsPublic`** (dieselbe Bedingung wie die Slug-Auflösung, damit Einstieg und Inhalt zusammenpassen); nicht freigegeben → 404. Bewusst **nicht** `BookAccess.PubliclyExposed`: die Pool-Flags (`ForDaily`/`ForRandom`/`ForBlind`) öffnen einzelne Puzzles/Zufallsziehungen, nicht den strukturierten Kurs — ein persönlicher Import im Tagespuzzle-Pool ginge sonst anonym als vollständiger Kurs heraus. Bewusst ein eigener Endpoint mit eigenem DTO statt `books/{bookId}` zu öffnen: hier gibt es überhaupt keine Nutzer-Felder (Baum/Zeit/Stufe/Festlegung), also auch keinen „kein Nutzer"-Sonderfall in einem Pfad, der sonst Nutzerdaten liefert. Anonyme Arbeit bleibt LOKAL im Browser |
| GET | `/api/calculations/positions/{bookPuzzleId}` | Auth | Eine Stellung (FEN, `setupMoves`, Kommentar) + eigener Analysebaum (`treeJson`, `treeUpdatedAt`) + die drei Trainings-Werte |
| PUT | `/api/calculations/positions/{bookPuzzleId}` | Auth | Baum speichern (Upsert) `{ treeJson, addSeconds?, secondsToken?, grade?, clearGrade?, chosenSan?, chosenUci?, clearChoice? }` — 400 bei leerem/ungültigem JSON, > `CalculationService.MaxTreeJsonLength` (256 KB), `grade` außerhalb 0–4 oder `secondsToken` > 64 Zeichen; Antwort = `CalcPositionStateDto` (`bookPuzzleId`/`updatedAt`/`hasTree`/`chosenSan`/`chosenUci`/`secondsSpent`/`grade`/`points`) |
| PATCH | `/api/calculations/positions/{bookPuzzleId}` | Auth | **Nur** die drei Trainings-Werte, ohne den Baum erneut zu schicken (festlegen/Zeit/bewerten) — gleiches Feld-Set wie oben ohne `treeJson`, gleiche Antwort. Legt die Zeile bei Bedarf mit LEEREM Baum an (zählt dann nirgends als „bearbeitet"); ein PATCH ohne Wirkung legt gar nichts an |
| DELETE | `/api/calculations/positions/{bookPuzzleId}` | Auth | Eigenen Baum verwerfen (idempotent) — Zeit/Festlegung/Bewertung bleiben stehen, die Zeile wird nur bei komplett leeren Werten entfernt |
| GET | `/api/calc-editions/{bookId}` | AllowAnonymous | Kalkulations-Serie: bereits FREIGEGEBENE Ausgaben eines Buchs inkl. Video (keine Entwürfe) — für die Betrachter-Serienseite |
| GET | `/api/calc-editions/{bookId}/manage` | Besitzer/Admin | ALLE Ausgaben inkl. Entwürfe (Verwaltung); 403 sonst |
| PUT | `/api/calc-editions/{bookId}` | Besitzer/Admin | Ausgabe anlegen/ändern (Upsert je Kapitel) `{ chapter, title?, videoUrl?, publishAt, testerPreviewAt? }`; 400 ab 120 Ausgaben je Buch (`MaxEditionsPerBook`, Codereview A7-004) |
| DELETE | `/api/calc-editions/{bookId}/{editionId}` | Besitzer/Admin | Ausgabe löschen |
| GET | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Kalkulations-Serie Phase 2: Verteiler-Mitglieder inkl. Tester-Häkchen (`{ userId, username, isTester, createdAt }`) |
| PUT | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Mitglied hinzufügen/ändern per Benutzername `{ username, isTester }`. NEU nur Freunde des Besitzers (Admin: jeder); 404 gleich für unbekannt und „kein Freund" (kein Benutzernamen-Orakel); 400 bei Nicht-Kalkulationsbuch oder mehr als 200 Mitgliedern (`MaxMembersPerBook`). Bestehende Mitglieder bleiben änderbar (Codereview A7-004) |
| DELETE | `/api/calc-editions/{bookId}/members/{userId}` | Besitzer/Admin ODER das Mitglied selbst | Mitglied aus dem Verteiler entfernen bzw. sich selbst austragen |
| GET | `/api/calc-editions/{bookId}/views` | Besitzer/Admin | Kalkulations-Serie Phase 3: „Gesehen"-Übersicht — welches Verteiler-Mitglied welche Ausgabe wann geöffnet hat (`{ editionId, chapter, userId, username, viewedAt }`) |

**Serien-Freigabe-Benachrichtigung (Phase 3b):** `CalcSeriesAnnounceScheduler` (HostedService, Standard alle 5 min, Config `CalcSeries:AnnounceIntervalSeconds` 60..3600) ruft `CalcSeriesAnnounceService.RunOnceAsync`: fällige Ausgaben (nur solche, deren Kapitel noch Stellungen hat — eine Ausgabe ohne Stellungen bleibt unmarkiert und wird erst angekündigt, wenn das Kapitel wieder Stellungen hat; Codereview A7-003; Verteiler nur lebende Konten, A9-004) → In-App-Benachrichtigung `calc_series_edition_released` (Daten `book`/`chapter`, Link `/courses/{bookId}`) an den Verteiler. Tester werden zum früheren `TesterPreviewAt` informiert, alle übrigen Mitglieder zur öffentlichen `PublishAt`. Idempotent über `CalcEdition.TesterAnnouncedAt`/`PublishAnnouncedAt`; die öffentliche Runde schließt die Tester-Runden-Empfänger über die GESPEICHERTE Liste `CalcEdition.TesterAnnouncedUserIds` (CSV) aus — NICHT über das veränderliche `IsTester`-Flag (sonst würde ein spät hinzugefügter Tester verloren gehen bzw. ein ent-Tester-tes Mitglied doppelt benachrichtigt). **Kein Mail-Kanal** (es gibt kein Mail-Opt-out-Modell — bewusst nur In-App).

**Rechnen → festlegen → prüfen → bewerten**: je Stellung hält `CalculationTrees` drei eigene SPALTEN
(nicht im opaken `TreeJson` vergraben — dort wären sie für Auswertungen für immer unerreichbar):
`ChosenSan`/`ChosenUci` (die EINE Festlegung auf einen ersten Zug — derselbe Zug erneut = Toggle
zurück, ein anderer verschiebt sie), `SecondsSpent` (der Client schickt **Deltas**, der Server
ADDIERT; Deckel `MaxSecondsPerFlush` = 1 h je Übertragung, `MaxSecondsSpent` gesamt) und `Grade`
(Selbstbewertung als benannte STUFE). `null` vs. löschen unterscheiden die Schalter
`clearGrade`/`clearChoice` — ein fehlendes Feld heißt immer „unverändert". Die Bewertung ist
**reine Selbsteinschätzung**: der Server liefert weiterhin keine Lösung aus.

**Zeit ist der einzige ADDIERENDE Wert — und braucht deshalb eine Idempotenz-Marke.** Stufe und
Festlegung SETZEN (Wiederholung schadet nicht), `addSeconds` addiert: kam eine Anfrage an und ging
nur die ANTWORT verloren (Timeout/502), würde der Wiederholversuch die Zeit still ein zweites Mal
buchen. Der Client vergibt darum je gemessenem Delta eine Marke (`secondsToken`, ≤64 Zeichen,
Inhalt für den Server opak) und **wiederholt mit DERSELBEN Marke**; die Zeile merkt sich
`SecondsToken` + `SecondsTokenApplied` und rechnet nur an, was unter dieser Marke noch nicht
verbucht war (identischer Retry ⇒ 0 s; ein beim Wiedereinreihen um neue Messungen gewachsener Patch
behält seine Marke ⇒ nur die Differenz). Stufe/Festlegung laufen dabei weiter durch. Ohne
`secondsToken` gilt das alte Verhalten (bedingungslos addieren). Frontend-Gegenstück:
`newSecondsToken()` + `mergeReviewPatch` in `calc-review.util.ts` (die Marke des ÄLTEREN Patches
gewinnt beim Zusammenlegen — genau der kann schon beim Server sein).

**Bewertung = AUSWAHL, keine freie Zahl** (`Models/CalculationGrade.cs`): fünf benannte Stufen
`notSolved` (0) · `someIdeas` (1) · `moveNoMainLine` (2) · `moveNoSideLines` (3) · `solved` (4) —
„Hauptfolge nicht gesehen" wiegt bewusst schwerer als „Nebenfolgen nicht gesehen". Eine benannte
Stufe ist reproduzierbar, „7 von 10" bedeutet nächste Woche etwas anderes. **Gespeichert wird die
STUFE**, nicht die Punktzahl; die Punkte entstehen ausschließlich in `CalculationGrades.PointsFor`
(heute linear 0..4) und werden zusätzlich mit ausgeliefert — eine spätere Neugewichtung passiert nur
dort und schreibt die Vergangenheit nicht um. `null` = noch nicht bewertet und ausdrücklich etwas
anderes als Stufe 0 („nicht gelöst"). Eine Stufe außerhalb 0–4 ist ein **Client-Fehler → 400** und
wird NICHT still auf 0 geklemmt. Kapitel-/Kurssummen nennen IMMER auch ihr Maximum
(`points` + `maxPoints` = 4 × Stellungen), weil eine nackte Summe ohne die Zahl der Stellungen
nicht lesbar ist („14 / 24" statt „14").

Buch↔Gruppe-Freigabe verwaltet der Admin:
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/books/{id}/groups` | Admin | Gruppen-Ids mit Kurs-Zugriff auf das Buch |
| PUT | `/api/admin/books/{id}/groups` | Admin | Vollständige Gruppen-Freigabe setzen (ersetzt; ungültige Ids ignoriert) |

### Wochenpost (öffentlich lesbar, durchspielbar mit Login, Admin verwaltet)
Bildet die wöchentlichen schach-bot-Posts auf RookHub ab: ein PGN + Termin (Datum + Uhrzeit; `WeeklyPost.ScheduledAt` ist UTC — das Frontend schickt ISO mit `Z` und zeigt Ortszeit, Termin-Helfer in `weekly.service.ts`, Codereview F5-009). PGN-Validierung via `RepertoireService.LooksLikePgn`. Puzzles werden on-the-fly aus dem PGN geparst (`PgnImportService.ParsePgn`) — Progress ist index-basiert.

**Per-User-Fortschritt**: idempotenter erster Versuch je `(WeeklyPostId, UserId, PuzzleIndex)`. „Erledigt" = **alle Puzzles gespielt** (gelöst egal). Aufgeben und Reset nach mindestens einem Zug zählen als ✗. Nach jedem **neuen** Versuch fire-and-forget Webhook (`SchachBotWebhookService.NotifyWeeklyAsync`, HMAC-signiert) an den Bot → Discord-Embed mit Live-Bestenliste.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/weekly-posts` | AllowAnonymous | Liste (ohne PGN), nach Termin absteigend |
| GET | `/api/weekly-posts/progress` | Authorize | Batch-Fortschritt für die Übersicht (`List<WeeklyPostProgressDto>`, nur Posts mit Versuchen) — literal-Route MUSS vor `{id}` stehen |
| GET | `/api/weekly-posts/{id}` | AllowAnonymous | Detail inkl. PGN |
| GET | `/api/weekly-posts/{id}/puzzles` | AllowAnonymous | Puzzle-Sequenz zum Durchspielen |
| POST | `/api/weekly-posts/{id}/attempt` | Authorize | Versuch erfassen `{ puzzleIndex, solved, timeSeconds }` (idempotent je Index) |
| GET | `/api/weekly-posts/{id}/progress` | Authorize | Eigener Fortschritt `{ total, playedCount, solvedCount, totalSeconds, playedIndices[], completed }` |
| GET | `/api/weekly-posts/{id}/results` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | Bestenliste (alle Spieler mit ≥1 Versuch): `playedCount`, `solvedCount`, `totalSeconds`, `completed`; Sortierung erledigt→gelöst→Name |
| POST | `/api/admin/weekly-posts` | Admin | Upload (multipart: file + scheduledAt + optional title) |
| PUT | `/api/admin/weekly-posts/{id}` | Admin | Termin/Titel ändern |
| DELETE | `/api/admin/weekly-posts/{id}` | Admin | Löschen |

### Bot-Stats (Bot-intern, HMAC-signiert)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/bot/player-progress/{discordId}` | AllowAnonymous + HMAC | Heutiger Trainingsziel-Fortschritt + Puzzle-Stats + jüngster Wochenpost-Status für eine verknüpfte Discord-ID. Signaturheader `X-Bot-Signature: sha256=…` mit `SchachBot:StatsSecret` (== Bot-`ROOKHUB_STATS_SECRET`); 401 bei falscher Signatur, 404 `{ reason: "not-linked" }` bei nicht verknüpfter Discord-ID, 503 `{ reason: "not-configured" }` wenn `SchachBot:StatsSecret` leer oder Platzhalter ist (vorher ebenfalls 404 — der Bot hielt dann JEDEN Abonnenten für unverknüpft; leeres Secret meldet der Start einmal als Warnung) |
| POST | `/api/bot/heartbeat` | AllowAnonymous + HMAC + RL | Signiertes Lebenszeichen des Schach-Bots (S5-008, `BotHeartbeatController`): `X-Bot-Timestamp` + `X-Bot-Signature: sha256=<hex(HMAC_SHA256(SchachBot:StatsSecret, "<ts>./api/bot/heartbeat"))>`, ±300 s. Gültig → 204 und die Zeile `Heartbeat: schach-bot healthy` (Kopf `HeartbeatService.LogTemplatePrefix`, Feld `HeartbeatService = schach-bot` → `labels.HeartbeatService`, danach zählt der log-watcher); ohne/falsche/alte Signatur → 401 und KEINE Zeile; Secret leer/Platzhalter → 503 `{ reason: "not-configured" }`. `kind=heartbeat_bot` über `/api/client-log` ist nur noch der anonyme Altpfad (fälschbar) |

### Externe Engine (auth) — eigener Broker ODER Lichess-External-Engine-Protokoll als CLIENT
Das Analysebrett kann statt der Browser-WASM-Engine eine **externe Engine** rechnen lassen: Stockfish auf
dem eigenen Rechner (offizieller Lichess-Provider, `lichess-org/external-engine`) oder eine gemietete
Cloud-Engine (stockfishcloud.com tritt selbst als Provider auf; Chessify lässt sich über sein
UCI-Tunnel-Binary vom Provider wrappen). **Zwei Wege** (seit 0.537.0): der Provider meldet sich DIREKT bei RookHub an (eigener Broker, empfohlen —
Abschnitt „Eigener Engine-Broker“ unten), oder RookHub spricht die offene Lichess-API als Client (bleibt für
Cloud-Engines, die sich nur bei Lichess registrieren können): der User hinterlegt einen Lichess-API-Token (Scope
`engine:read`, AES-verschlüsselt in `LichessEngineCredentials`), RookHub listet damit die auf DIESEM
Lichess-Konto registrierten External Engines und reicht Analyse-Anfragen an den Broker
(`engine.lichess.ovh`) durch — der ndjson-Stream geht 1:1 an den Browser.

**Warum als Server-Proxy und nicht direkt aus dem Browser**: das `clientSecret` einer Engine ist ein
Dauer-Geheimnis (wer es hat, kann fremde Rechenzeit verbrauchen) und bleibt deshalb serverseitig
(`LichessEngineService`, MemoryCache je `userId:engineId`, TTL 10 min). Nebeneffekt: die CSP
(`connect-src 'self'`) bleibt unangetastet und die eigene Engine ist auch vom Handy aus nutzbar.
Logik in `Services/LichessEngineService.cs`; URLs konfigurierbar (`Lichess:ApiUrl`/`Lichess:BrokerUrl`) —
zugleich die Vorbereitung auf den RookHub-EIGENEN Broker (seit 0.537.0, gleiche Endpoints, siehe unten).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/engine/credentials` | Status + maskierter Token (`{ hasCredentials, maskedToken }`) |
| POST | `/api/engine/credentials` | Lichess-Token setzen/überschreiben `{ token }` (max. 200 Zeichen) |
| DELETE | `/api/engine/credentials` | Token löschen |
| GET | `/api/engine/external` | Engines BEIDER Quellen — direkt angemeldete (`rhe_…`, `source: "rookhub"`, `online` = Provider hat in den letzten 30 s gepollt) zuerst, dann die des Lichess-Kontos (`eei_…`, `source: "lichess"`, `online: null`) — **ohne `clientSecret`** (`{ hasCredentials, tokenInvalid, lichessUnreachable, engines[] }`). Immer 200: `tokenInvalid` sagt, WARUM die Lichess-Liste leer ist (Lichess wies den Token ab), `lichessUnreachable`, dass Lichess nicht antwortete (die direkten Engines stehen trotzdem da) |
| POST | `/api/engine/external/{id}/analyse` | Analyse anfordern → **`application/x-ndjson`-Stream** (bei `rhe_…` vom eigenen Broker erzeugt, bei `eei_…` von Lichess durchgereicht — dieselben Zeilen). Body = `EngineAnalyseRequest` (`sessionId`, `initialFen`, `moves[]`, `multiPv`, GENAU EINES von `depth`/`movetime`/`nodes`, optional `threads`/`hash`); Threads/Hash werden serverseitig auf die von Lichess gemeldeten Engine-Maxima geklemmt, `variant` ist fest `chess`. Abbruch = Verbindung schließen (wandert über den Broker zum Provider) |

| PUT | `/api/engine/background` | Hintergrund-Engines für Analyseaufträge setzen `{ engineIds: [] }` (leere Liste = entfernen; jede muss registriert sein → sonst 404, höchstens 32 seit 0.669.2, davor 16). `GET /api/engine/external` liefert sie als `backgroundEngineIds` mit — der Live-Picker blendet sie aus. **MEHRERE sind der Sinn** (0.460.0): der Worker rechnet je ENGINE genau einen Auftrag, es laufen also so viele Aufträge nebeneinander, wie hier stehen. Mit einer einzigen ist die Warteschlange strikt seriell — auf Dev blockierte EIN zäher Auftrag (Tiefe 22, 5 Linien, 31 min) alle 49 wartenden. Ein neuer Auftrag geht auf die Engine mit der KÜRZESTEN Schlange (`AnalysisJobService.PickBackgroundEngineAsync`), nicht reihum: reihum trifft daneben, sobald eine Engine an einer zähen Stellung hängt |

### Eigener Engine-Broker (0.537.0) — der Provider spricht direkt mit RookHub

**Was**: RookHub bietet die PROVIDER-Seite des Lichess-External-Engine-Protokolls selbst an
(`Controllers/ExternalEngineController.cs`, `Controllers/TokenController.cs`, `Services/EngineBroker/`). Der
offizielle Provider (`example-provider.py`, Pin `d0eeb242`) läuft UNVERÄNDERT — nur `--lichess`/`--broker` zeigen
auf RookHub (`ROOKHUB_URL`), und der Token ist ein RookHub-API-Token mit Scope `engine` (`ROOKHUB_API_TOKEN`,
im Profil unter „API-Tokens“ anzulegen). Plan, Protokoll und Begründung: `docs/eigener-engine-broker.md`.

**Warum**: jede Suche lief über lichess.org/engine.lichess.ovh. Dreizehn Engines einer Maschine liefen in 429 und
IP-Sperren (2026-09-11), der Lichess-Broker antwortete 503, sobald gerade kein Provider pollte, und ohne
Lichess-Konto ging gar nichts. Direkt gibt es keine fremde Drossel, keinen Lichess-Token und keinen Hop übers Internet.

**Aufbau**:
* **Registrierung** `ExternalEngineRegistrations`: Kennung `rhe_` + 12 Zeichen, `ClientSecret` (32 Byte base64url,
  verlässt den Server nie Richtung Browser), `ProviderSelector` = sha256(`"providerSecret:" + secret`) hex — das
  Geheimnis des Providers selbst wird NIE gespeichert. Identität = der NAME je Konto: der Provider fragt `GET`, dann
  `PUT` auf den gleichnamigen Eintrag bzw. `POST`; ein `POST` mit exakt vorhandenem Namen aktualisiert ebenfalls
  (zwei Provider, die gleichzeitig starten). Ein Name, der sich nur in Groß/Klein unterscheidet → 409; höchstens 32
  je Konto. Anlegen/Ändern NUR mit Engine-Token (der Provider registriert, der Browser nicht → 403), Löschen auch
  aus dem Profil.
* **`EngineHub`** (Singleton, Arbeitsspeicher): Schlange je Selector, Übergabe direkt an einen wartenden Poll.
  `POST /api/external-engine/work` wartet `AcquireWaitSeconds` (10) → 200 `{ id, work, engine }` oder 204. Der
  Upload `POST /api/external-engine/work/{id}` wird ZEILENWEISE gelesen (`EngineUploadPump`), über `UciLineParser` +
  `EmitBuilder` (Port von lila-engine `emit.rs`, Commit 60ea115c) in genau die ndjson-Zeilen verwandelt, die der
  Lichess-Broker schickt, und sofort an den Anfragenden gereicht. `{"keepalive":true}` geht unverändert durch.
* **`EngineRegistry`** löst eine Kennung auf (lokal/Lichess), **`IEngineBroker`** (`EngineBrokerRouter` →
  `LocalEngineBroker` bzw. `LichessEngineBroker`) liefert beiden Aufrufern denselben Strom. `EngineController`
  (live) und `AnalysisJobWorker` (Hintergrund, damit auch `GameAnalysisService`) kennen nur noch diese zwei.
* Nimmt kein Provider einen Auftrag binnen `ProviderTimeoutSeconds` (15) an, ist das ein **503** wie beim
  Lichess-Broker (der Worker wechselt dann die Engine); ebenso eine Schlange über `MaxQueuedPerEngine` (64).
  „online“ = gepollt in den letzten `OnlineWindowSeconds` (30) (`EngineSelectorDirectory`; `LastSeenAt` schreibt
  `EngineBrokerMaintenanceService` alle 60 s, dazu alle 10 min eine Statistikzeile). Schalter
  `Engine:LocalBroker:Enabled` (Vorgabe an; aus = `/api/external-engine/*` 404).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/external-engine` | JWT oder Token `engine` | Registrierte Engines im Lichess-Format; `clientSecret` NUR für den Engine-Token |
| POST | `/api/external-engine` | Token `engine` | Registrieren `{ name, maxThreads, maxHash, variants, providerSecret, providerData? }` (gleicher Name = Aktualisierung) |
| PUT | `/api/external-engine/{id}` | Token `engine` | Registrierung ändern (neues `providerSecret` = neuer Selector) |
| DELETE | `/api/external-engine/{id}` | JWT oder Token `engine` | Engine entfernen (auch aus der Hintergrund-Liste); ein laufender Provider meldet sie beim nächsten Start neu an |
| POST | `/api/external-engine/work` | **anonym** (Selector ist der Nachweis) | Long-Poll `{ providerSecret }` → 200/204. Unbekannter Selector = 204 nach der Wartezeit, je Client-Adresse höchstens 30 je Minute (`UnknownSelectorThrottle`), darüber sofort 429 ohne Halten; Rumpf ≤ 4 KB (`[RequestSizeLimit]`, sonst 413) |
| POST | `/api/external-engine/work/{id}` | **anonym** (Auftragskennung) | Chunked-Upload der UCI-Ausgabe bis `bestmove`; 404 = Auftrag weg/abgelaufen |
| POST | `/api/token/test` | **anonym** + RL | Lichess-kompatible Token-Prüfung (Rumpf `text/plain`, Tokens mit Komma, max. 20) → `{ token: { userId, scopes, expires } \| null }`; Scopes `engine:read,engine:write` NUR für Scope `engine`. Der Provider fragt hier vor dem Start (`preflight.py`) |

**Abweichungen von lila-engine — alle bewusst, alle mit Test:**
* `EmitBuilder` ist ein TREUER Port. `tools/emit-reference/run.sh` baut das Original (Rust, im Container) und lässt es
  gegen dieselben Fälle laufen; `EmitBuilderVectorTests` enthält dessen Ausgaben LITERAL. Wer am Emit dreht, lässt das
  Skript laufen und übernimmt die neuen Ausgaben nicht ungeprüft.
* Eine unlesbare `info`-Zeile, eine Zeile über 16 KiB und eine unbekannte JSON-Steuerzeile werden ÜBERSPRUNGEN (lila:
  400), ein Upload-Ende ohne `bestmove` beendet den Strom regulär (lila: 400 — genau diese 400 kostete bis d0eeb242
  fünf Sekunden je Suche). Warnungen je Upload auf drei gedeckelt. `bestmove (none)` erscheint nicht als Zug.
* Der `WorkSanitizer` weist VOR der Schlange ab, was keine legale Standardstellung ist (Gegner im Schach, Bauer auf der
  Grundreihe, Rochaderechte ohne König/Turm, Chess960-Rochaderechte, falsches e.p.-Feld) → **400**. Stockfish 19
  BEENDET sich bei so einer Stellung samt Provider (siehe Dockerfile); eine krumme Stellung kostete sonst den ganzen
  Engine-Pool einen Neustart. Der Worker setzt so einen Auftrag bei einer `rhe_`-Engine sofort auf `Failed`
  („Stellung abgewiesen: …“) statt ihn zwei Minuten zurückzustellen — Warten ändert daran nichts.

**Fallen, an denen es STILL scheitert** (alle mit Test festgenagelt):
1. **Kestrels Mindest-Datenrate**: ein Upload schweigt zwischen zwei tiefen Iterationen minutenlang (nur das Keepalive
   alle 15 s); Kestrel bricht Rümpfe unter 240 B/s ab („Reading the request body timed out due to data arriving too
   slowly“). `Submit` setzt `IHttpMinRequestBodyDataRateFeature.MinDataRate = null` und `[DisableRequestSizeLimit]` —
   `EngineBrokerTests` (Integration, echter Kestrel) fällt ohne beides um.
2. **Rate-Limiter**: 13 Provider sind 78 Polls je Minute plus Uploads, der globale Deckel 100/min je IP →
   `[DisableRateLimiting]` an Poll UND Upload (schaltet auch den globalen Limiter ab — per Test belegt: ohne es 429).
   Registrierung und `token/test` bleiben limitiert.
3. **Puffernde Proxys**: der Frontend-nginx hat `location ^~ /api/external-engine/` mit `proxy_request_buffering off`
   (sonst sammelt nginx den Chunked-Upload bis zum ENDE der Suche und die erste Zeile käme mit der letzten),
   `proxy_buffering off`, `client_max_body_size 0` und 3600-s-Timeouts (`DeploymentConfigTests`); nur der Poll
   `= /api/external-engine/work` hat eine eigene exakte location mit `client_max_body_size 8k`. **Der Nginx Proxy
   Manager davor puffert Anfragen per Vorgabe genauso** — Dev und Prod brauchen je eine Custom Location
   `/api/external-engine/` mit denselben Direktiven, sonst läuft der direkte Weg nur in Zeitlupe. Deploy-Schritt
   außerhalb des Repos, nur auf Zuruf (TODO.md). **Und die Registrierung heißt `/api/external-engine` OHNE
   Schrägstrich**: für genau diese URI antwortet nginx bei einer Präfix-location mit Schrägstrich und `proxy_pass`
   SELBST mit 301 auf „…/“ (an den Container-Port). Die exakte `location = /api/external-engine` nimmt ihr die URI
   weg (`DeploymentConfigTests`). Gefunden erst mit dem echten Provider — der Kestrel-Integrationstest geht an nginx
   vorbei.
4. **Die frühe Antwort**: ist der Anfragende weg (Browser zu, Worker bricht ab), antwortet der Broker dem Upload
   SOFORT 200 — so stoppt der Provider die Engine und holt den nächsten Auftrag. Kestrel meldet den dabei
   abgebrochenen Rumpf als `BadHttpRequestException` („Unexpected end of request content“), nicht als Abbruch: ob der
   Anfragende oder der Provider weg ist, entscheidet deshalb der Zustand des Auftrags, nicht der Ausnahmetyp.
5. **Unbekannter Selector = 204 nach der Wartezeit**, nicht 401/404: ein veralteter Provider pollte sonst im
   Sekundentakt Fehler (log-watcher `api_scan`), und ein fremder Selector verrät nichts. Polls loggen auf Debug,
   `/api/external-engine/work` zählt als Systemaufruf (`SystemCallClassifier`). **Das Halten ist je Client-Adresse
   gedeckelt** (Codereview 2026-09-29, A4-005, `UnknownSelectorThrottle`, 30 je Minute × `RateLimitScale`): darüber
   sofort 429 ohne Halten — der Provider wartet nach einer 4xx-Antwort selbst (Backoff bis 10 s), nach einem 204 dagegen
   nicht, deshalb bleibt das Halten innerhalb des Fensters. Bekannte Selectors laufen ungedrosselt. Der Poll-Rumpf ist
   auf 4 KB gedeckelt (`ExternalEngineController.MaxAcquireBodyBytes`), vorher band MVC bis zu 30 MB in einen String.
6. **Scope-Zaun je Scope** (`PatScopeFenceMiddleware.AllowedPrefixesByScope`): ein Token `engine` erreicht NUR
   `/api/external-engine/*`, einer `extension` nur `/api/extension/*`. Ein neuer Scope braucht dort einen Eintrag,
   sonst erreicht er gar nichts.
7. **Die Selector-Menge liegt im Speicher** (`EngineSelectorDirectory`, alle 60 s bzw. bei Registrierung neu geladen):
   ein Poll kostet keine Datenbankabfrage. Ein Neustart der API verliert die Schlangen — laufende Uploads enden, der
   Browser setzt fort (Abriss-Regel des Live-Pfads), der Worker pausiert und startet neu.
8. `LichessEngineCredentials` trägt die Hintergrund-Liste auch OHNE Lichess-Token (`EncryptedToken` leer):
   `DELETE /api/engine/credentials` löscht dann nur den Token, die Zeile mit `rhe_`-Hintergrund-Engines bleibt.

**Vertragstest mit dem echten Provider**: `engine-provider/test/rookhub-broker.e2e.sh` (kein CI-Test — baut den
E2E-Stack und das Provider-Image, läuft rund zehn Minuten). Provider gegen `http://host.docker.internal:18099`
(Frontend-nginx-Hop), lichess.org/engine.lichess.ovh im Container auf 127.0.0.1 gesperrt; misst erste Zeile,
`pvs`, `bestmove`, zweiten Auftrag direkt danach und drei Minuten Last mit 12 Hintergrund-Engines; prüft die Logs
auf 503/ProviderTimeout, abgerissene Uploads und Lichess-Aufrufe. **Gemessen 2026-09-26** (Provider auf
`--cpus 6`, Live 2 Threads, Hintergrund je 1 Thread/64 MB): Live-Analyse Tiefe 18 × 3 Linien erste Zeile nach
0,55 s (kalte Engine), der zweite Auftrag direkt danach nach 0,05 s; drei Minuten Last mit 12 Hintergrund-Engines
(Tiefe 20, 2 Linien): 206 Aufträge angelegt, 182 fertig, 0 gescheitert, 11–18 je Engine; 22 Live-Proben daneben
erste Zeile im Mittel 0,11 s, höchstens 0,18 s; **0 × 503**, 0 abgerissene Uploads, keine Lichess-Erwähnung.

**Rollout** (nur auf Zuruf): Merge → NPM-Custom-Location auf Dev → Provider mit `ROOKHUB_URL` gegen Dev, Messung
wiederholen → Tag → NPM-Custom-Location auf Prod → in den Provider-Stacks einen ZWEITEN Dienst für den direkten Weg
DAZU (`.env` mit `ROOKHUB_URL`/`ROOKHUB_API_TOKEN`, 1 Live + Hintergrund-Engines) und den bestehenden Lichess-Dienst
auf die eine Live-Engine zurückfahren (0.538.0: beide Wege = zwei Container) und die Hintergrund-Liste im Profil auf
die `rhe_`-Engines. Die Lichess-Registrierungen bleiben liegen und stören nicht; der Lichess-Token bleibt für
Cloud-Engines und für das Analysebrett von lichess.org.

### Hintergrund-Analyseaufträge (auth) — „diese Stellung rechnen, sobald die Hintergrund-Engine frei ist"
`AnalysisJobs`: eine Stellung mit Zieltiefe + Linienzahl, abgearbeitet vom `AnalysisJobWorker` (Hosted
Service, Singleton) über denselben Broker-Pfad wie live — **je ENGINE ein laufender Auftrag** (`_running`
ist nach `EngineId` verschlüsselt: ein Stockfish-Prozess kann nur EINE Suche; Aufträge auf verschiedenen
Engines laufen deshalb parallel), ohne den 10-Minuten-Deckel des Live-Proxys. **Vorrang der Live-Analyse**:
der `EngineActivityTracker` (Singleton; zählt Live-Streams je Nutzer FÜR DEN DECKEL und je Engine FÜR DEN
VORRANG, ersetzt das frühere statische `ActiveStreams`) feuert `LiveStarted(engineId)` beim Übergang 0→1
auf einer Engine → der Worker bricht genau den Auftrag auf DIESER Engine ab (Status `Paused`; der Provider
stoppt Stockfish, die Hashtabelle bleibt warm) und setzt erst nach `AnalysisJobs:IdleGraceSeconds` (20 s)
Ruhe dort fort; andere Engines bleiben unberührt. **Laufender Stand**: `CurrentDepth`/`CurrentNps` werden
bei JEDER empfangenen Zeile fortgeschrieben — auch bei den flacheren, die das Ergebnis nicht ersetzen.
Ohne das stand die Anzeige nach einer Fortsetzung minutenlang still (die Engine rechnet erst von Tiefe 1
wieder hoch, und ohne Persist lief nicht einmal die Zeit weiter). Für die SEKÜNDLICHE Anzeige kommt
`Services/AnalysisJobLive.cs` (Singleton) dazu: derselbe Stand ohne Datenbank, `Seconds` wächst aus der
Startzeit des Laufs (läuft also auch, während die Engine schweigt), abrufbar über `GET
/api/analysis-jobs/live`. Bewusst NICHT über häufigeres Persistieren gelöst — das hieße, für eine reine
Anzeige jede Sekunde eine Zeile zu schreiben. **Fortsetzungs-Regel**: der Broker liefert nach
jedem Neustart die flachen Iterationen erneut — übernommen wird nur eine Zeile mit `depth ≥ ReachedDepth`
(`AnalysisJobStream.ShouldPersist`); dieselbe Regel lässt bei „mehr Linien" das alte Ergebnis stehen, bis
die neue Suche es überholt. **Sticky hash**: `PickNextAsync` bevorzugt den zuletzt gelaufenen Auftrag
(typisch „weiter bis Tiefe 50" direkt nach Abschluss), sonst FIFO; `NextAttemptAt` = Backoff nach
Broker-Fehlern. Beim API-Start werden `Running`-Aufträge auf `Paused` gesetzt. Anpassungs-Regeln im
`AnalysisJobService.UpdateAsync`: Tiefe ↑ über das Erreichte → fertiger Auftrag zurück in die Queue;
Tiefe ↓ auf/unter das Erreichte → `Done`; Linien ↓ → gespeicherte `pvs` kürzen, kein Neustart; Linien ↑ →
laufende Suche abbrechen (`IAnalysisJobControl.Interrupt`), zurück in die Queue, Ergebnis bleibt Anzeige.
Ergebnis = letzte Broker-Zeile als opakes JSON (`ResultJson`), das Frontend mappt es wie den Live-Stream; die
Bewertung der Hauptvariante steht zusätzlich als `EvalText` in der Zeile, damit Listen die (großen) Roh-Zeilen
nicht laden müssen. **Eine gescheiterte Stellung ist nicht verloren** (0.460.0): ein gescheiterter Auftrag heißt fast
immer „die Engine war gerade nicht zu gebrauchen" und nicht „diese Stellung geht nicht" — eine
Stellung der Partie hat immer einen legalen Zug, Matt oder Patt kann sie gar nicht sein. Früher
schloss `IngestFinishedAsync` sie sofort mit LEERER Kandidatenliste ab; eine tote Engine löschte
damit stillschweigend Stellungen aus der Partie, die nie wieder gerechnet wurden (am 2026-09-10 an
25 Stück passiert, alle von Hand zurückgesetzt). Jetzt zählt `GameAnalysisPosition.FailedAttempts`
mit und die Stellung wird erneut eingereiht; erst nach `GameAnalysisDefaults.MaxPositionAttempts`
(3) ist Schluss, damit eine wirklich unlösbare Stellung nicht ewig im Kreis läuft.

**Grenzen und Terminalzustände (0.383.0, aus dem Review)**: `MultiPv` ist auf **1..5**
gedeckelt — das Protokoll-Maximum von `work.multiPv`, im Worker ein zweites Mal geklemmt (ein größerer Wert
wird vom Broker abgewiesen und der Auftrag liefe endlos in die Wiederholung). Der Worker bricht **nicht mehr
selbst** ab, wenn die Hauptvariante die Zieltiefe erreicht: die Engine bekommt `depth` als Limit und beendet den
Stream selbst — sonst blieben die Linien 2..K eine Iteration flacher (jede `pv` traegt ihre eigene Tiefe). Ein KURZER Lauf
ohne Tiefenfortschritt (< `AnalysisJobs:FruitlessMinRuntimeSeconds`, 60 s) erhoeht `FruitlessAttempts`; ab
`MaxFruitlessAttempts` (3) gilt der Auftrag als `Failed` (Matt-/Pattstellungen liefen sonst ewig im 30-s-Takt).
Ein LANGER Lauf ohne Fortschritt zaehlt bewusst NICHT: das ist eine gekappte Verbindung, kein Sackgassen-Beweis.
**Live aufgetreten**: der Wachhund des offiziellen Providers setzt seinen „zuletzt benutzt"-Stempel erst am
Stream-ENDE und terminiert jede Suche, die laenger als `--keep-alive` (Vorgabe 300 s) dauert — ab Tiefe 29 mit
5 Linien jede Iteration. Der Auftrag kam nie tiefer und galt nach drei Runden als gescheitert; Abhilfe beidseitig:
`KEEP_ALIVE` im Provider hoch (siehe `engine-provider/README.md`) UND diese Laufzeit-Unterscheidung hier. (Das betraf den bis
0.478.10 gepinnten Provider; der heutige erneuert den Stempel waehrend der Suche — die Unterscheidung bleibt fuer fremde
Provider.) Bleibt die erste Datenzeile binnen
`AnalysisJobs:FirstLineTimeoutSeconds` (300) aus, wird pausiert statt den Slot des Users unbegrenzt zu halten.
**Ein 503/504 des Brokers wechselt die ENGINE, statt zu warten** (0.475.6). Diese Antwort heisst
beim Broker: fuer diese Engine ist gerade kein Provider verbunden — eine Aussage ueber die Engine
und nicht ueber den Auftrag. Vorher stellte `BackoffAsync` den Auftrag zwei Minuten zurueck und
klopfte dann an dieselbe Tuer; am 2026-09-12 auf Prod pendelten damit 24 von 32 Auftraegen gegen
die zwoelf Engines der zweiten Maschine (309 solcher Antworten in 25 Minuten Log), waehrend die
vier der ersten die ganze Arbeit trugen. Gewechselt wird REIHUM (`NextEngineAfter`, dieselbe Regel
wie beim Stillstand) mit `AnalysisJobs:EngineSwitchBackoffSeconds` (5..600, Vorgabe 15) statt der
120 s. Die kurze Frist ist unbedenklich, weil der Worker je ENGINE nur einen Auftrag rechnet: es
sind hoechstens so viele Versuche gleichzeitig unterwegs wie Engines hinterlegt sind, nicht so
viele wie Auftraege offen sind — bei einem Totalausfall des Brokers also rund ein Abruf je
Sekunde. Steht die Engine nicht in der Hintergrund-Liste (von Hand gewaehlt), bleibt sie und es
gilt weiter der lange Backoff.

**Eine WIEDERHOLTE Bewertungszeile ist ein Lebenszeichen, kein Fortschritt** (0.475.1). Das
Lebenszeichen des RookHub-Providers bis 0.478.10 war die erneut gesendete letzte `info`-Zeile —
fuer den Worker war sie damit von echter Arbeit nicht zu unterscheiden. Der Waechter der ersten Zeile
war nach ihr entschaerft, einen zweiten gab es nicht: eine Engine, die NACH der ersten Zeile stehen
blieb, hielt ihren Auftrag unbegrenzt auf `Running`. Am 2026-09-12 auf Prod: Auftrag 14240 stand VIER
STUNDEN auf Tiefe 11, die wiederholte Zeile bis auf das Byte dieselbe (`time` 27 ms, `nodes` 24006,
Tempo eingefroren auf 889 111 Knoten/s), Rechenzeit lief weiter mit. Weil die Pumpe je Nutzer nur EINE
Partie fuettert (`IsOwnersTurnAsync`), standen dahinter 434 Partien und elf freie Engines — seit 01:51
keine einzige gerechnete Stellung.

`StreamTally` zaehlt eine zeichengleiche Wiederholung deshalb als Lebenszeichen (eine rechnende Engine
KANN sich nicht wiederholen, `time` und `nodes` wandern mit jeder Zeile), und der Waechter laeuft
weiter: er wird bei jeder FRISCHEN Zeile neu gestellt, mit `AnalysisJobs:StallTimeoutSeconds`
(300..86400, Vorgabe 1800). Die Frist ist bewusst weit — sie faengt nicht die langsame, sondern die
haengende Engine; eine tiefe Iteration mit fuenf Linien darf Minuten dauern. Faellt sie, wandert der
Auftrag REIHUM auf die naechste hinterlegte Hintergrund-Engine (`AnalysisJobWorker.NextEngineAfter`):
ueber die kuerzeste Schlange gewaenne ausgerechnet die haengende, sie hat ja gerade nichts zu tun. Eine
Engine, die NICHT in der Hintergrund-Liste steht, wurde von Hand gewaehlt und bleibt. Ein Stillstand
zaehlt als Fehlversuch — anders als ein abgerissener Stream ist er eine Aussage ueber die Engine; der
Zaehler faellt bei jedem Lauf mit Tiefenfortschritt auf 0 zurueck, eine bloss langsame tiefe Suche kann
also nicht daran scheitern.

**Seit 0.478.11 kommt das Lebenszeichen als EIGENE Zeile** (`{"keepalive":true}`, vom offiziellen
Provider alle 15 s geschickt, vom Broker durchgereicht). `StreamTally.IsKeepalive` zaehlt es als
Lebenszeichen; es traegt keine Tiefe und stellt den Waechter deshalb nicht neu. Die
Wiederholungs-Regel bleibt stehen: auf fremden Rechnern laeuft der alte Provider weiter.

Unerwartete Ausnahmen setzen den Auftrag in einem EIGENEN Scope auf `Paused` zurueck (sonst stuende er bis zum
naechsten API-Start auf `Running` und wuerde nie wieder aufgegriffen). `TryCancel` faengt `ObjectDisposedException`
— zwischen Dictionary-Griff und `Cancel()` kann der Lauf selbst geendet haben, und der Wurf lief bis in den
Request-Thread des Live-Streams (dessen Zaehler waere fuer immer stehen geblieben). `EngineActivityTracker.Begin`
kapselt den `LiveStarted`-Aufruf entsprechend. `MaxJobsPerUser` (200) trimmt die aeltesten fertigen Auftraege.
**Frontend (0.380.0)**: Hintergrund-Engine in der Profil-Engine-Karte wählen (`PUT /api/engine/background`);
das Analysebrett filtert sie aus Live- und Vergleichs-Picker (`backgroundEngineId` aus `GET /api/engine/external`)
und bietet ein Uhr-Symbol „Im Hintergrund analysieren" (`AnalysisJobDialogComponent`: Tiefe/Linien vorbelegt aus
den Live-Einstellungen, legt den Auftrag direkt an) sowie den Sprung zur Seite `/analysis/jobs`
(`AnalysisJobsComponent`: Liste, 10-s-Poll solange Aufträge offen, aufklappen = Brett + gespeicherte Linien
ohne laufende Engine, Tiefe/Linien nachträglich ändern). Die Abbildung Broker-Zeile → Anzeige teilt sich der
Live-Pfad mit der Auftragsseite über `features/analysis/engine-lines.util.ts` (`mapBrokerLine`, `uciLineToSan`,
`toDisplayLines`, `formatElapsed`).
**Verbindung zu „Gemerkte Stellungen" (0.381.0)**: `AnalysisJobService.CreateAsync` legt je Stellung EINMAL eine
`RememberedPosition` an (Match über die ersten 4 FEN-Felder; ein Chessable-Eintrag bleibt unangetastet;
`SourceUrl = "/analysis/jobs"`, `CourseName = Titel`), und `RememberedPositionService.ListAsync` hängt jeder
gemerkten Stellung den jüngsten passenden Auftrag als `Analysis` an (Status, Tiefen, Linien, `EvalText` der
Hauptvariante via `AnalysisJobService.EvalTextOf`). Die Remembered-Seite rendert interne `sourceUrl`s (führender
`/`) als `routerLink`, zeigt die Analyse-Info als Chip-Zeile und bietet ohne Auftrag das Uhr-Symbol (gleicher Dialog);
sie frischt sich alle 10 s auf, SOLANGE ein Auftrag offen ist (`hasOpenJob`) — sonst ruht der Poll ganz.
**„Brett + aktueller Stand" (0.599.0, Wunsch 2026-09-29: „zeige Brett und aktuellen Stand der Analyse, und lass Job
weiterlaufen")**: auf der Remembered-Seite öffnen der Name „Analyse-Auftrag" und die Analyse-Zeile
`AnalysisJobViewDialogComponent` (statt auf `/analysis/jobs` zu springen) — Brett mit Pfeil je Linie (bester Zug grün),
Linien, erreichte/laufende Tiefe, Tempo, Zeit; fertig die endgültigen Linien. Er fragt `GET /api/analysis-jobs/{id}`
alle 5 s und, solange der Auftrag rechnet, `/live` im Sekundentakt; fertig/gescheitert ruht beides. **Bewusst ohne
Engine**: „Im Analysebrett öffnen" mit der Auftrags-Engine würde den Auftrag über den Live-Vorrang pausieren.
**„Im Analysebrett öffnen" (0.384.0)** hängt `engine`/`depth`/`lines` des Auftrags an die URL: das Brett wählt genau
diese Engine (auch die sonst ausgeblendete Hintergrund-Engine, einmalig und NICHT als Dauerwahl gespeichert) und
setzt die Suche fort, statt bei Tiefe 0 zu beginnen — der Provider hat die Stellung noch im Hash. Zahlen im
Engine-Kontext laufen einheitlich über `formatKiloNps`/`formatKiloNodes` (kN, Tausendertrennung, keine
Nachkommastellen) — vorher sprang die Einheit je nach Tempo zwischen N/s, kN/s und MN/s.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/analysis-jobs/live` | NUR der laufende Stand der gerade rechnenden Aufträge (`{ id, depth, nps, seconds }`) — aus dem Arbeitsspeicher, ohne DB; die Auftragsliste holt ihn im Sekundentakt (Literal-Route vor `{id:int}`) |
| GET | `/api/analysis-jobs/{id}` | EIN eigener Auftrag (gleiche Form wie in der Liste; 404 fremd/unbekannt) — für „Brett + aktueller Stand" auf den gemerkten Stellungen (0.599.0) |
| GET | `/api/analysis-jobs` | Eigene Aufträge (neueste zuerst) inkl. Status (`queued/running/paused/done/failed`), `reachedDepth`, `resultJson`, `secondsSpent`, `lastError`, `houseEngine` (rechnet auf fremder Rechenzeit — Tiefe/Linien/Engine nur für Admins änderbar) |
| POST | `/api/analysis-jobs` | Anlegen `{ fen, targetDepth (1–60), multiPv (1–10), engineId?, title? }` — `engineId` fehlend = Hintergrund-Engine aus dem Profil (keine → 400); ein GENANNTES `engineId` muss eine Engine des Engine-Besitzers sein (Hintergrund-Liste, eigene `rhe_`-Registrierung oder Lichess-Engine des Tokens), sonst 400 — erst danach verdrängt der Auftrag einen Hintergrund-Lauf auf dieser Engine (Codereview 2026-09-29, A4-003; ist Lichess gerade nicht erreichbar, wird angelegt, aber nichts verdrängt); FEN muss legal sein; max. 50 offene je User |
| POST | `/api/analysis-jobs/batch` | Mehrfachauswahl `{ fens[] (1–200), targetDepth, multiPv, engineId? }` → `{ created[], skipped[{fen, reason}] }` mit `invalid` (keine legale FEN) / `duplicate` (nicht gescheiterter Auftrag zur Stellung existiert — auch innerhalb des Batches) / `limit` (Deckel offener Aufträge); nie 4xx wegen einzelner Stellungen — ein genanntes `engineId`, das nicht dem Nutzer gehört, ist dagegen ein 400 für den ganzen Batch (A4-003) |
| PUT | `/api/analysis-jobs/{id}` | Anpassen `{ targetDepth?, multiPv?, title?, engineId? }` nach den Regeln oben; ein Engine-Wechsel bricht den Lauf ab und reiht neu ein (Ergebnis bleibt, neue Engine startet mit kaltem Hash bei `ReachedDepth`); 404 wenn nicht eigener. **Auftrag auf der Haus-Engine** (`EngineOwnerUserId` gesetzt: Punktepartie, „Partie analysieren“, Vertiefung): Tiefe/Linien/Engine ändert nur ein Admin, sonst 400 — derselbe Riegel wie `POST /api/game-analyses` (Codereview 2026-09-29, A4-001); Titel, Neustart, Löschen bleiben erlaubt, unveränderte Werte zählen nicht als Änderung. Ein Engine-Wechsel nur auf eine Engine des Engine-Besitzers (sonst 400, A4-003) |
| POST | `/api/game-analyses/{id}/restart` | Die ganze Partie neu anstossen: alles Ungerechnete kommt frisch in die Warteschlange, aufgegebene Stellungen (`CandidatesJson = "[]"`) kommen zurueck. Die alten Auftraege werden GELOESCHT und neu angelegt — erst dadurch waehlt `PickBackgroundEngineAsync` erneut. Der Ausweg aus „Auftrag haengt an einer Engine, die aus ist": ein Auftrag wechselt von sich aus nie die Engine (am 2026-09-11 hingen fuenf Partien an ihren letzten Halbzuegen fest, waehrend fuenf freie Engines danebenstanden). Eine gepinnte `EngineId` bleibt |
| POST | `/api/analysis-jobs/{id}/restart` | Wieder einreihen (Fehlversuchs-Zähler + Backoff gelöscht, laufender Lauf abgebrochen); das ERGEBNIS bleibt — die Suche setzt bei `ReachedDepth` an. Ein Auftrag mit erreichtem Ziel bleibt `Done` |
| DELETE | `/api/analysis-jobs/{id}` | Löschen (laufende Suche wird abgebrochen) |

### Analyse-Verlauf + Sterne (0.603.0, auth)
Wunsch 2026-09-29: „merk dir eine History der letzten 20 Analysen von jedem User — diese sollen auch irgendwo ausgewählt
werden können" und „innerhalb einer Analyse will ich Stellungen mit einem Stern markieren, damit ich schnell zu diesen
springen kann". `Services/AnalysisHistoryService.cs`, Tabelle `AnalysisHistoryEntries`.
* Eine Analyse = Ausgangsstellung + Zugfolge (UCI, serverseitig nachgespielt — eine, die nicht geht, ist ein 400) + der
  Halbzug, an dem man stand + Sterne (Halbzüge, 0 = Ausgangsstellung) + Titel aus den PGN-Kopfdaten. Höchstens
  `MaxPerUser` 20 je Nutzer, die zuletzt angefassten bleiben; Konto löschen räumt ab.
* **Die Oberfläche führt die Kennung ihrer Analyse** (`AnalysisComponent.historyId`) und schickt sie bei jedem Speichern mit
  — sonst entstünde je Zug ein Eintrag. Ohne Kennung nimmt der Server einen Eintrag mit GLEICHER Stellung und GLEICHEN Zügen
  wieder (dieselbe Partie zweimal aus „Meine Partien" geöffnet). Eine neue Analyse beginnt bei Zurücksetzen, FEN laden,
  Stellung aufbauen, PGN laden und beim Öffnen eines Verlauf-Eintrags (dann mit dessen Kennung); der bisherige Stand geht
  dabei noch raus (`flushHistory`), eine verspätete Antwort gehört über `historySession` nicht mehr zur neuen.
* Gespeichert wird gedrosselt (`HISTORY_SAVE_MS` 1,5 s nach der letzten Änderung, beim Verlassen sofort), nur angemeldet und
  nicht die leere Grundstellung; nie zwei Anfragen gleichzeitig (die zweite hätte noch keine Kennung).
* Sterne: Knopf neben den Zugpfeilen bzw. Taste S, Sprungliste über der Zugliste („12...Nf6"), ★ in der Zugliste. Seit 0.604.0
  hängt ein Stern am KNOTEN des Zugbaums (auch in Varianten) und geht nur mit „ab hier löschen" weg. Abgemeldet gehen Sterne
  nur für die Sitzung.
* „Verlauf" neben der Überschrift → `analysis-history-dialog.component.ts`; `/analysis?history=<id>` öffnet einen Eintrag direkt.
* **Zugbaum** (0.604.0, Wunsch 2026-09-29 mit Screenshot des Lichess-Analysebretts: „so hätt ichs bei uns auch gern in der
  Analyse, inkl. der Variationen + Hauptlinie + Rechtsklick Variante hochstufen/löschen"). Reine Regeln in
  `features/analysis/analysis-tree.ts` (ohne Angular): `children[0]` ist IMMER die Fortsetzung, weitere Kinder sind Varianten;
  ein anderer Zug als die Fortsetzung wird eine Variante, derselbe Zug geht in die vorhandene (`addMove`). Das Brett hält
  `root` + `line` (= `lineThrough(aktueller Knoten)`: Weg dorthin und Fortsetzung) + `ply` — Pfeiltasten und die bisherigen
  Specs laufen unverändert auf `line`. Anzeige `analysis-move-tree.component.ts`: Hauptlinie als Tabelle Nr | Weiß | Schwarz mit
  der zuletzt gesehenen Bewertung je Zug (`node.evalText`, gesetzt, sobald die Leiste sie übernimmt — ab
  `EVAL_SETTLE_DEPTH`, Matt, Suchende), Varianten als Block, der die Tabelle unterbricht („1 | e4 | …", Block, „1 | … | c5"),
  Untervarianten in Klammern. Rechtsklick bzw. langer Druck (500 ms, eigene Uhr — iOS schickt kein `contextmenu`; Android
  schickt eins, die 800-ms-Sperre verhindert das doppelte Öffnen) öffnet ein `mat-menu` an einem fest positionierten Anker:
  Stern, Variante hochstufen (`promote`: EINE Stufe, an der tiefsten Verzweigung, an der der Weg nicht die Fortsetzung ist), zur
  Hauptvariante machen (`makeMainline`), ab hier löschen (`removeNode`; stand man darin, geht es beim Elternknoten weiter).
  `treeVersion` zählt jede Änderung am Baum, die Tabelle baut nur dann neu auf (nicht, wenn nur der aktuelle Zug wandert).
  PGN laden liest seit 0.604.0 Varianten mit (`parsePgnTree`: RAV, nur die erste Partie, `[FEN]`, Wörter ohne Zug wie „e.p."
  fallen weg, ein ungültiger Zug beendet nur seine Variante).
* **Gespeichert wird der Baum FLACH** (`AnalysisTreeDto`: `n[]` mit `p` = Index des Elternknotens davor, -1 = Wurzel, `u` UCI,
  `s` Stern, `e` Bewertung; `s` an der Wurzel = Ausgangsstellung markiert; `current` = Index des Knotens, an dem man stand).
  Verschachtelt bräuchte jeder Halbzug zwei JSON-Ebenen — System.Text.Json liest höchstens 64. `AnalysisHistoryService.CheckTree`
  spielt jeden Zug in der Stellung seines Elternknotens nach (Rochade → e1g1) und weist ab: Elternverweis nach vorn, derselbe
  Zug zweimal unter einem Knoten, mehr als `MaxNodes` 1500 Züge, 600 Halbzüge Tiefe, 60 Sterne. Unbrauchbare Bewertungen fallen
  still weg. `Moves` bleibt die HAUPTLINIE (Vorschau, Wiederfinden derselben Partie). Die Liste trägt den Baum nicht mit — das
  Brett holt einen gewählten Eintrag über `GET {id}`. Einträge von 0.603.0 (ohne `TreeJson`) kommen als Zugfolge zurück.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/analysis-history` | Die letzten 20 (zuletzt angefasste zuerst) `{ id, startFen, moves[] (Hauptlinie), ply, title, preview, moveCount, nodeCount, starCount, tree: null, current, createdAt, updatedAt }` |
| GET | `/api/analysis-history/{id}` | Ein eigener Eintrag MIT `tree` (404 fremd/unbekannt) |
| POST | `/api/analysis-history` | Speichern `{ id?, startFen, title?, tree: { s?, n: [{ p, u, s?, e? }] }, current }` → der Eintrag samt Kennung und Baum; 400 bei unlesbarer Stellung oder einem Baum, der nicht aufgeht |
| DELETE | `/api/analysis-history/{id}` | Eintrag löschen |

### Maia-Sparring im Analysebrett (0.632.0)

Wunsch: „Maia-3 in unterschiedlicher Stärke für Sparring von Stellungen, Knopf im Analyse-Modus". Maia-3 (CSSLab,
Universität Toronto, GPL-3.0) ist ein neuronales Netz, das den Zug eines MENSCHEN einer bestimmten Stärke vorhersagt —
kein Rechner. Karte „Sparring gegen Maia" zwischen Engine-Karte und Zugliste; kein Backend, kein Endpunkt, keine
Migration. Code: `features/analysis/maia/` (siehe `src/frontend/CLAUDE.md`). Entscheidungen und warum:
1. **Läuft im Browser** (ONNX-Modell + `onnxruntime-web` WASM, eigener Worker) — kein Dienst, kein Container.
2. **Modell `maia3_simplified.onnx` nicht im Repo** (45 MB): `src/frontend/app/maia-model/fetch.sh` holt es beim Docker-Build
   (nur `APP_PROJECT=app`, VOR `COPY app/ .` → Layer-Cache) bzw. lokal (`sh maia-model/fetch.sh`) und prüft sha256.
3. **`onnxruntime-web` exakt gepinnt** und NIE in TypeScript importiert: drei Dateien aus `dist/` gehen als Assets nach
   `/assets/ort/`, der Worker lädt sie per `importScripts` (Muster wie Stockfish).
4. **Klassischer Worker als statische Datei** (`maia-worker.js` → `/assets/maia/`), kein gebündelter TS-Worker.
5. **Zwischenspeicher = Cache API mit eigenem Code** (Fortschritt!), nicht der ngsw: der puffert ohne Fortschritt. Laufzeit +
   Worker stehen in der `lazy`-Gruppe `maia` (`ngsw-config.json`), das `.onnx` in KEINER Gruppe. Ohne sicheren Kontext (Dev
   über HTTP) gibt es keine Cache API — die Rückfrage sagt dann „wird bei jedem Besuch neu geladen" (`canStore`).
6. **Stärke = Elo als roher Wert** ans Modell (`elo_self` = `elo_oppo`), 600–2600 in 200er-Schritten, Vorgabe 1600, je Gerät
   (`rookhub_analysis_maia_elo`).
7. **Gewürfelt, nicht der wahrscheinlichste Zug** (Temperatur 1, Nucleus `topP` 0,95) — dieselbe Stellung soll verschiedene
   Antworten bekommen.
8. **Engine während des Sparrings AUS** (sonst Bewertung + Pfeile), Schalter gesperrt; danach kommt der vorige Zustand. Ausnahme:
   die zwei Maia-Schalter lassen sie STILL mitlaufen (unten, „Schlechte Züge melden + Bewertungsleiste“).

**Zwei Fallen, live nachgestellt:** (1) `nginx:alpine` kennt `.mjs` nicht und liefert `application/octet-stream` — ORT scheitert
mit „Failed to fetch dynamically imported module"; `nginx.conf` hat dafür `location ~* \.mjs$` (ohne `add_header`, das ersetzte
die CSP). (2) Eine fehlende Datei unter `/assets/` beantwortet der SPA-Fallback mit **200 + index.html** — ob das Modell da ist,
erkennt `MaiaModelStore` deshalb nur an der GRÖSSE, nie am Status, und legt eine falsche Größe nie in den Cache.

**Pin ändern:** COMMIT/SHA256/BYTES in `maia-model/fetch.sh` UND `version` (erste acht Zeichen der sha256) + `bytes` in
`maia/maia-model.ts`, dazu der Commit in `public/CHESS-ASSETS.md`. `DeploymentConfigTests.Maia_ModelPin_AndDelivery_StayConsistent`
hält das zusammen (dazu Dockerfile-Bedingung, `.mjs`-Regel, ngsw, angular.json nur bei `app`, exakter ORT-Pin).

**Sparring-Regeln** (`analysis.component.ts`): Start (die Karte meldet `start` erst mit fertigem Modell) nur in einer Stellung mit
legalen Zügen — der Nutzer spielt die Seite am Zug, Brett dreht sich, `engineOn` wird gemerkt, aber nicht in localStorage
geschrieben. Auf einen EIGENEN Zug antwortet Maia (Mindest-Bedenkzeit `maiaDelayMs` 500), auch auf einen per Explorer/Repertoire-Karte
gespielten (`playRepertoireMoves`); ein Zug für Maias Seite löst nichts aus. Jede Navigation (`goTo`/`goToNode`), der Editor, „Seite wechseln" und das Ende lassen eine laufende Antwort verfallen
(`maiaEpoch`) — Maia zieht nach einer Navigation NIE von selbst, dafür gibt es „Maia zieht". „Seite wechseln" und „Nochmal ab der
Ausgangsstellung" fordern sofort einen Zug, wenn dann Maia am Zug ist. Beenden, jeder neue Baum (Zurücksetzen, FEN/PGN, Stellung
aufbauen, Verlauf-Eintrag) und das Löschen der Ausgangsstellung beenden das Sparring und stellen `engineOn` wieder her; ein Fehler
von Maia → Snackbar, das Sparring bleibt, und ist die Sitzung dabei weg (Status nicht `ready`), baut `prepare()` sie im Hintergrund
aus dem Cache neu auf. Maias Züge laufen über `goToNode` und landen damit im Zugbaum und im Analyse-Verlauf.
Verlassen der Seite → `release()` (gibt ~150 MB frei; das Modell kommt beim nächsten Mal in ~1 s aus dem Cache).

**Partie analysieren nach dem Sparring (0.644.0):** derselbe Weg wie in „Meine Partien" — die Sparring-Partie wird über
`POST /api/games/import` eine gewöhnliche Partie (Quelle `pgn`) und läuft dann durch `AnalyzeGameService.submit`; bei Erfolg
geht es auf `/games/{id}` (Kurve, Zug-Klassen, Fehler-Training), bei einer Absage bleibt man, ohne Id/bei Fehler Snackbar
`analysis.maia.saveFailed`. Die Partie ist die Linie vom Start bis zum zuletzt GESPIELTEN Zug (`sparring.tip`, gesetzt von
eigenem Zug, Maias Zug und Explorer/Repertoire-Zug im Teilbaum des Starts) — nicht `lineThrough(start)`, denn mitten in einer
geladenen Partie stehen dort deren Züge. Beim Ende bleibt sie als `lastSparring` stehen (ab zwei Halbzügen), verfällt mit jedem
neuen Baum und wenn ihre Züge gelöscht werden. Knopf nur angemeldet; während eines Sparrings nur, wenn die Stellung zu Ende
ist (der Klick beendet es dann wie „Beenden"). Die Engine-Auskunft (`status()`) kommt höchstens einmal, sobald der Knopf zum
ersten Mal sichtbar würde. Das PGN baut `maia/sparring-pgn.ts` (chess.js, „Maia 1600" als Name, keine Elo-Kopfzeilen, im
Zugtext die kompakte Form „4... Bc5", damit es auch gegen eine API ohne die Parser-Reparatur geht). **`ownerSide` geht mit**:
für Quelle `pgn` rät `DetermineOwnerSide` die Seite über den Plattform-Namen, und der trifft hier nie — ohne Seite schrieben
die Fehler-Erklärungen neutral, das Fehler-Training nähme die Seite mit den meisten Fehlern, und das Brett drehte nicht.

**Schlechte Züge melden + Bewertungsleiste (0.645.0):** zwei Schalter in der Maia-Karte, je Gerät gemerkt
(`rookhub_analysis_maia_warn`, `rookhub_analysis_maia_evalbar`, Vorgabe aus), jederzeit umschaltbar — auch mitten im Sparring,
wirkt sofort (`refresh()`). Regeln, die nicht kippen dürfen:
* **Stille Engine** (`sparringEngineQuiet` = Sparring UND einer der Schalter): nur die HAUPT-Engine rechnet (auch nach Tiefen-/
  Linienwechsel, `restartSearches`), nie die Vergleichs-Engine; Linien, Pfeile und Kandidaten bleiben LEER — sie verrieten den
  besten Zug. Leiste und `node.evalText` nur mit „Bewertungsleiste anlassen"; ohne sie ist die Leiste neutral. Die Vorlage
  bleibt: der Engine-Schalter ist gesperrt, „Engine pausiert während des Sparrings" stimmt für das, was man SIEHT.
* **Tiefen-Spur** (`evalTrack`): je Stellung die Bewertung der besten Linie je Tiefe (Weiß-Sicht), bei `refresh()` für die neue
  Stellung geleert. Ein EIGENER Zug am Brett (`onMove`, nicht Explorer/Repertoire, nie einer für Maias Seite) nimmt die Spur der
  Stellung davor mit (`pendingBefore`).
* **Vergleich bei GLEICHER Tiefe** (`maia/sparring-check.ts`, rein): Nachher-Wert = die Stellung nach dem Zug bei
  `min(MAIA_CHECK_DEPTH 14, Tiefe)` oder früher, wenn die Suche endet (Matt); Vorher-Wert = die größte Tiefe ≤ der des
  Nachher-Werts (`matchingBefore`) — eine flache gegen eine tiefe Bewertung meldete sonst Schwankungen der Suche als Fehler.
  Warnung ab `MAIA_BAD_MOVE_PAWNS` 0,2 Bauern Verlust aus Sicht des Ziehenden (in Centibauern gerechnet, Matt = ±(1000 − n)
  Bauern). Spätestens nach `maiaCheckTimeoutMs` (4 s) mit dem tiefsten Wert ab `EVAL_SETTLE_DEPTH`, sonst kein Urteil.
* **Maias Antwort wartet auf das Urteil** (dritte Zusage im `Promise.all` von `requestMaiaMove`): so steht die Warnung schon da,
  wenn ihr Zug aufs Brett kommt, und ihr Zug würgt die Suche der Stellung nach dem eigenen Zug nicht ab. Maias eigene Züge
  werden nie geprüft. Navigation, „Seite wechseln", Beenden und Schalter-aus lösen eine offene Prüfung ohne Urteil auf.
* **Die Warnung** („12.Nf3 war nicht gut: +0.40 → −0.30") ersetzt bzw. löscht jede neue Prüfung; sie geht bei Beenden/Ende,
  „Nochmal", „Seite wechseln", neuem Baum und „Analysieren" weg, NICHT bei Navigation. **„Analysieren"** beendet das Sparring
  wie „Beenden" (die Partie bleibt für „Partie analysieren"), schaltet die Engine EIN — nur für diese Sitzung, nicht in
  localStorage — und springt auf die Stellung VOR dem Zug; der Zug steht dort als Fortsetzung, die Linien zeigen, was besser war.

### Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?"
Wunsch 2026-09-29: „soll diese durchrechnen und schaun, warum Zug 1 besser ist als Zug 2 — vor allem im Vergleich: was
sind bei Zug 2 die besten Züge, und warum gehen die bei Zug 1 nicht (so gut)". `Services/MoveComparisonService.cs`,
Tabellen `MoveComparisons` + `MoveComparisonLines`, Pumpe `MoveComparisonPumpService` (5 s, `MoveComparison:PumpIntervalSeconds`).
* **Zwei Durchgänge über die Hintergrund-Aufträge** (NICHT die Live-Engine — rechnet auch, wenn die Seite zu ist):
  (1) je Kandidat (2–4) die Stellung DANACH mit `CandidateLines` (3) Linien = die besten Antworten des Gegners; daraus
  der beste Kandidat (`BestUci`, Sicht der Seite am Zug, Matt ± 100 000). (2) Je SCHWÄCHEREM Kandidaten dessen
  `RepliesTested` (3) beste Antworten, gespielt nach dem BESTEN: geht die Antwort dort nicht → `Illegal` (ohne Auftrag,
  selbst eine Auskunft), sonst ein Auftrag mit 1 Linie = die eigene Erwiderung und die Bewertung. Matt/Patt auf dem Brett
  braucht keinen Auftrag (`Terminal`).
* **Engine wie die Punktepartie** (`Services/EngineOwnerResolver.cs`, von `GameAnalysisService` mitbenutzt): eigene
  Hintergrund-Engine, sonst die Haus-Engine; auf der Haus-Engine höchstens `HouseMaxDepth` 24 (eigene bis 30, Vorgabe 22).
  Die Aufträge sind normale (nicht `Background`) — sie verdrängen Vertiefung/Meisterpartien. `remember: false`, und
  `AnalysisJobService.ListAsync` blendet Aufträge eines Vergleichs aus. **Fertige Aufträge werden eingesammelt**: Ergebnis
  in die Zeile (`ResultJson`, `ReachedDepth`), Auftrag GELÖSCHT — sonst stünden sie in der Liste und fielen irgendwann dem
  Trimmer (`MaxJobsPerUser`) zum Opfer. Ein verschwundener Auftrag macht die Zeile `Failed`, nicht den Vergleich.
* **Begründung** je schwächerem Kandidaten nur mit Modell auf eigener Hardware (`IsLocal`), im Hintergrund
  (`MoveComparisonExplainJobs` gegen Doppelstarts, ein Neustart lässt die Pumpe neu anstoßen). Fakten wie bei „Warum war das
  ein Fehler?" (`ExplanationFacts`: `FirstMoveAttacks`, `LineEvents` — die Bilanz der Antwort-Linie ab der Stellung VOR dem
  schwächeren Zug samt diesem, die der eigenen Erwiderung ab der Stellung nach dem besten Zug samt der Antwort), Stufen in
  Worten (`LevelName`), „fast gleich" unter 0,3 Bauern; Leser = die Seite am Zug. Genannte Züge nur aus den Linien
  (`MentionsOnly`, eine Nachfrage), gespeichert in englischer SAN, `PieceLetters` beim Lesen. Ohne Modell endet der
  Vergleich ohne Text; ein unbrauchbarer Text lässt ihn trotzdem `Done` werden (sonst hinge er auf „wird erklärt").
* Deckel: `MaxOpenPerUser` 3 laufende, `MaxKeptPerUser` 50 gespeicherte (älteste fertige gehen beim Anlegen).
* Frontend: ⋮-Menü der Stellung (`position-menu.component.ts`, angemeldet immer — ob eine Engine bereitsteht, sagt der
  Dialog) → `move-compare-dialog.component.ts` (Engine-Vorschläge des Analysebretts vorausgewählt, `[candidates]`, dazu
  jeder legale Zug) → Seite `/analysis/compare/:id` (`move-comparison.component.ts`, fragt alle 3 s nach, solange es
  läuft; Brett aus Sicht der Seite am Zug, Klick auf Zug/Antwort/Erwiderung stellt die Stellung auf).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/move-comparisons/status` | `{ engineAvailable, ownEngine, explanations, maxCandidates, defaultDepth, maxDepth, openComparisons, maxOpen }` (Literal vor `{id}`) |
| GET | `/api/move-comparisons` | Die letzten 20 eigenen (`moves` als SAN, `bestSan`) |
| GET | `/api/move-comparisons/{id}` | Stand: `status` (`candidates`/`replies`/`explaining`/`done`/`failed`), je Kandidat `evalText` (WEISS-Sicht), `replies` (beste Antworten, Linie als SAN), `tests` (dieselben nach dem besten Zug: `state` `pending`/`done`/`failed`/`illegal`, `line` = eigene Erwiderung), `explanation`; beste zuerst, solange gerechnet wird Reihenfolge der Auswahl. Laufende Zeilen zeigen den Zwischenstand ihres Auftrags |
| POST | `/api/move-comparisons` | `{ fen, moves[] (UCI, Rochade auch König-schlägt-Turm), depth?, lang?, title? }`; 400 `reason` ∈ invalid-fen/game-over/invalid-move/too-few-moves/too-many-moves/too-many-open/no-engine/too-many-jobs |
| DELETE | `/api/move-comparisons/{id}` | Löschen samt offener Aufträge |

**Vergleichsmodus (0.375.0)**: Der Waagen-Knopf startet eine ZWEITE Engine auf derselben
Stellung (`compareEngine`), beide Linienlisten stehen untereinander. Möglich ohne Umbau, weil
`AnalysisEngineService` weder Konstruktor noch `inject()` hat — er lässt sich schlicht per `new`
ein zweites Mal instanziieren (eigener Worker, eigener Zustand, eigene Generationszählung).
Drei Regeln, die dabei nicht kippen dürfen: (1) **nie dieselbe Engine beidseitig** — der Schutz
sitzt zentral in `startCompare`/`ensureDistinctCompareEngine` und wird auch beim Wechsel der
HAUPT-Engine und beim Wiederherstellen aus localStorage durchlaufen (sonst zwei WASM-Kerne bzw.
zwei Ströme auf dieselbe externe Engine); (2) **das Etikett muss die Wahrheit sagen** — fällt
eine Seite auf WASM zurück, nennt `compareEngineName`/`mainEngineName` „Browser" statt des
gewählten Namens; (3) **kein Block ohne Engine dahinter** — das Template hängt an
`compareRunning` (Schalter AN *und* Instanz vorhanden), sonst stünde nach einem Neuladen ohne
Engine-Liste ein ewiges „Berechne…" ohne erreichbaren Ausschalter.

**Frontend**: `features/analysis/external-engine.service.ts` (ndjson über den normalen `HttpClient` mit
`reportProgress` — `partialText` wächst, nur NEUE vollständige Zeilen werden geparst; so greifen die
Interceptors inkl. Auth). Der `AnalysisEngineService` bekam einen zweiten Analyse-Pfad
(`setRemoteEngine`/`analyzeRemote`): dieselbe `analysis$`-Schnittstelle, dieselbe `AnalysisLine`-Form,
also unverändertes Brett/Eval-Bar. **Die Bewertung kommt remote bereits aus Weiß-Sicht** (Spez.) — im
Gegensatz zum WASM-Pfad (`parseInfo`) darf `mapRemoteLine` das Vorzeichen deshalb NICHT drehen.
**Rochaden liefert der Broker als König-schlägt-Turm** (`e1h1`, lila-engine `emit.rs` nutzt
`CastlingMode::Chess960`) — chess.js kennt in der Standardvariante nur `e1g1` und bräche die Variante
dort ab. `castling-uci.util.ts` schreibt das um, aber NUR wenn auf dem Startfeld wirklich ein König
steht (`e1h1` ist ein legaler Turmzug, wenn der König woanders steht) — deshalb wird die Linie
mitgespielt statt Strings zu ersetzen. **Zwei Analyse-Pfade, EIN Zustand**: `searchGen` (beim Absetzen
des `go` festgehalten) hält Worker-Zeilen einer überholten Suche aus `state$` — der frühere Vergleich
`gen` gegen `gen` im selben Aufruf war wirkungslos, sodass Nachzügler des WASM-Kerns die Remote-Anzeige
überschrieben und sein spätes `bestmove` die laufende Suche für beendet erklärte. Beim Umschalten wird
der lokale Kern zusätzlich gestoppt (`stopLocalSearch`), und `analyze()` prüft die Auswahl NACH dem
`await init()` erneut (die Engine-Liste kann während des WASM-Handshakes eintreffen).
Scheitert die Remote-Suche VOR der ersten Datenzeile (Provider offline; auch: gar keine Antwort binnen
12 s), fällt der Service für den Rest der Sitzung still auf WASM zurück und die Seite sagt es
(`analysis.remoteFallback`); ein Abriss MITTEN im Stream gilt dagegen als beendete Suche (Ergebnis
bleibt stehen). Der angeforderte **Hash** ist auf `AnalysisEngineService.MaxRemoteHashMb` (4096 MB) gedeckelt — die von Lichess
gemeldete Registrierungs-Grenze (bis 1 TiB) ist keine Aussage darüber, was der Provider-Rechner je Analyse
allozieren soll; die Hintergrund-Aufträge nehmen dagegen das volle gemeldete Maximum (eigener Prozess, lange
Läufe). Token-Verwaltung: Profil-Karte `features/profile/engine-card.component.ts`.
**nginx**: eigene `location /api/engine/` mit `proxy_buffering off` + langen Timeouts — sonst sammelt
der Proxy die info-Zeilen bzw. kappt eine tiefe Suche nach 60 s. **Das genügt nicht allein**: nginx
VERBRAUCHT den `X-Accel-Buffering: no`-Header der API und reicht ihn NICHT weiter, ein davor
stehender Reverse-Proxy (hier Nginx Proxy Manager) sieht ihn also nie und puffert weiter. Deshalb
setzt der Frontend-nginx den Header für `/api/engine/` per map + server-weitem `add_header` SELBST.
**In Prod aufgetreten (0.373.0)**: kurze Suchen kamen am Stück an, lange (Tiefe 22 × 3 Linien > 12 s)
gar nicht — der Browser sah null Bytes, brach ab und meldete „Externe Engine nicht erreichbar",
während Provider und Broker fehlerfrei rechneten. Im NPM-Zugriffslog erkennbar an `499` mit
`Length 0` bei langen und `200` mit ~15 KB bei kurzen Suchen.
**Lebenszeichen + Fortsetzung (0.377.0)**: Der API-Proxy kopiert den Broker-Stream nicht mehr nackt
(`Services/NdjsonHeartbeatPump.cs`): schweigt der Broker 20 s, geht eine Leerzeile raus. Grund: bei
MultiPV 5 liegen ab Tiefe ~27 Minuten zwischen zwei Zeilen, und NPM (Default `proxy_read_timeout`
60 s) kappte den Stream, den der Browser dann als „fertig" wertete (Prod-Log: Streams mit exakt ~61 s,
Anzeige bleibt bei Tiefe 27 stehen). Der Client-Parser ignoriert Leerzeilen und Steuerzeilen ohne `pvs`
(das `{"keepalive":true}` des Providers — durchgelassen stünde die Anzeige alle 15 s bei Tiefe 0 ohne
Linien). Reißt ein Stream trotzdem
vor der Zieltiefe ab (Fehler ODER ≥ 5 s Funkstille vor dem Ende — ein Ende direkt nach der letzten
Zeile ist die Engine selbst, z. B. einzüge Stellungen), setzt `AnalysisEngineService.startRemoteStream`
bis zu dreimal ab der erreichten Tiefe fort (flache Wiederholungszeilen aus der warmen Hashtabelle werden
verschluckt, die Linien bleiben stehen) und meldet es über `remoteInterrupted$`
(`analysis.remoteCutResuming`/`remoteCutFinal`); ohne Tiefenfortschritt zwischen zwei Abrissen gilt der
letzte Stand als Ergebnis — bewusst KEIN Fallback auf WASM, das würde tiefe Linien durch flache ersetzen.
Die Karte zeigt zusätzlich „rechnet seit m:ss an Tiefe N" ab 5 s ohne neue Zeile (`showThinking`).

**Gegenstelle beim Nutzer**: `engine-provider/` ist ein fertiges Docker-Setup für den Rechner des
Users (Anleitung dort in der `README.md`). Es startet den OFFIZIELLEN Lichess-Provider — beim Bauen
auf einen Commit gepinnt + per Prüfsumme verifiziert statt ins Repo kopiert (eindeutige Herkunft,
Update = Zeilenwechsel im Dockerfile). Eigener Anteil: `entrypoint.sh` (Aufruf aus `.env`-Variablen)
und `preflight.py` (prüft den Token via `POST /api/token/test` VOR dem Start). **Seit 0.537.0 zeigt `ROOKHUB_URL`
beide Basen (`--lichess`, `--broker`) auf RookHub** und `ROOKHUB_API_TOKEN` ist der Token dafür (eigener
Broker, siehe oben; seit 0.538.0 hat er mit `ROOKHUB_URL` VORRANG vor einem daneben stehenden `LICHESS_API_TOKEN`,
und ohne `ROOKHUB_URL` ist er ein Abbruch mit Klartext — bei Lichess gilt er nicht); ohne `ROOKHUB_URL` läuft alles
wie bisher über Lichess. **EIN Container bedient EINEN Broker; beide Wege gleichzeitig sind ZWEI Container** (0.538.0,
Wunsch 2026-09-26 „lichess & eigener Broker", bewusst nicht ein Container, der beides kann): `compose.yml` hat den
zweiten Dienst `engine-provider-lichess` am Profil `lichess` mit eigener `.env.lichess` (Vorlage
`.env.lichess.example`, OHNE `ROOKHUB_URL`); `docker compose --profile lichess up -d` bzw. dauerhaft
`COMPOSE_PROFILES=lichess` in der `.env`. Compose braucht die `.env.lichess` nur bei aktivem Profil (geprüft). Über
Lichess gehört nur die Live-Engine, die Hintergrund-Engines in den direkten Container (IP-Drosselung). In RookHub
stehen Engines beider Quellen ohnehin in EINER Liste (`GET /api/engine/external`, `EngineRegistry`), die
Hintergrund-Liste darf `rhe_` und `eei_` mischen. **Das Lebenszeichen patcht das
Image seit 0.478.11 nicht mehr hinein** (bis dahin `patch_provider.py`): der gepinnte Stand (`d0eeb242`, 2026-09-06) erfüllt die zwei Regeln
des Brokers, an denen RookHub hängt, und `test/provider.test.py` prüft sie gegen einen nachgebauten Broker.

1. **Jede Suche endet mit `bestmove`.** Der Broker verlangt das seit lila-engine `0e1223b` (2026-09-06) und
   antwortet sonst `400 uci protocol error: expected bestmove before end of stream`. Der vorher gepinnte
   Provider (`a6ef15a8`) schickte nie eins, bekam die 400 bei JEDER Suche, schlief danach 5 s und holte erst
   dann den nächsten Auftrag. Gemeldet als „es dauert relativ lang, bis die Cloud-Engine anspringt": ein Zug
   eine Sekunde nach einer beendeten Suche wartete 4,1 s auf die erste Zeile (gegen den echten Broker
   gemessen, 2026-09-15). Die 400 stand schon vorher im Provider-Log, galt aber als Folge gekappter
   Verbindungen — so blieb die Verzögerung neun Tage unbemerkt.
2. **Lebenszeichen alle 15 s** (`{"keepalive":true}`). Der Provider reicht nur `info`-Zeilen MIT `score`
   weiter, zwischen zwei tiefen MultiPV-Iterationen vergehen Minuten, und der Broker kappt nach 60 s Stille —
   bei uns sichtbar als `HttpIOException: The response ended prematurely`; Aufträge kamen nie über Tiefe 29
   hinaus, 23 hingen auf Dev bei Tiefe 20/22. Bis 0.478.10 lieferte das ein eigener Eingriff
   (`patch_provider.py`: Wiederholung der letzten `info`-Zeile — eine Leerzeile verwirft der Broker). Dieselbe
   Klasse Fehler wie der `NdjsonHeartbeatPump` auf der Strecke API→Browser, nur einen Hop weiter vorne. Das
   Keepalive kommt beim Empfänger als eigene ndjson-Zeile an: der Worker zählt es als Lebenszeichen
   (`StreamTally.IsKeepalive`), der Browser verwirft es im Parser (`ExternalEngineService.analyse`).

**Ein Eingriff bleibt (0.535.1, `patch_force_close.py`)**: der asynchrone Provider lädt Suchen über eine
aiohttp-Sitzung mit Verbindungs-Pool hoch; eine inzwischen von der Gegenseite geschlossene Pool-Verbindung
fällt erst beim nächsten Upload auf — Auftrag schon abgeholt, Engine hat `go`, Upload stirbt im ersten Byte,
der Anfragende bekommt nach 15 s einen 503 (upstream Issue #45, per A/B belegt). `TCPConnector(force_close=True)`
für die Upload-Sitzung (nicht für den Poll) behebt es; `test/provider.test.py` verlangt drei Uploads auf drei
Verbindungen, die CI wendet den Patch vor dem Test an (`DeploymentConfigTests` hält die Zeile fest). Weg damit,
sobald upstream es behebt.

**Wer den Pin anhebt, lässt `test/provider.test.py` laufen** (die CI tut es): die Regeln des Brokers ändern
sich, ohne dass ein laufender Provider davon erfährt. Gegen den alten Stand scheitert der Test mit genau den
gemeldeten vier bis fünf Sekunden. **Reihenfolge beim Ausrollen**: erst das Frontend mit dem Parser-Filter,
dann den Provider — ein älteres Frontend reicht jedes Keepalive als leeres Ergebnis an die Anzeige weiter.

Zwei Fallen, die dort
bewusst adressiert sind: der Provider-Token braucht `engine:read` **und `engine:write`** (er
REGISTRIERT die Engine; RookHub selbst genügt `engine:read`; ein RookHub-Token mit Scope `engine` meldet beides) — ohne Vorabprüfung endete das in einem
401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt; und die Registrierung
wird über den **Namen** identifiziert (gleicher Name = Aktualisierung, zwei Rechner brauchen zwei
Namen, sonst überschreiben sie sich). **`ENGINE_COUNT` (0.378.0)**: ein Container kann mehrere
Provider = mehrere registrierte Engines fahren (`ENGINE_<i>_NAME/_MAX_THREADS/_MAX_HASH` je Engine,
sonst `ENGINE_NAME <i>`) — gedacht als „Server Live" + „Server Hintergrund" für die Hintergrund-
Analyseaufträge; beide dürfen alle Kerne haben, weil RookHub den Hintergrund pausiert, sobald Live
rechnet. Stirbt ein Provider, endet der Container mit dessen Code (restart zieht alle neu). Der
Entrypoint ist deshalb bash (`wait -n`); `ENTRYPOINT_DRY_RUN=1` zeigt nur die Aufrufe —
`engine-provider/test/entrypoint.test.sh` prüft damit den Argument-Aufbau. **Die Provider starten
GESTAFFELT** (`PROVIDER_START_DELAY`, Vorgabe 3 s, seit 0.534.1): jeder registriert sich beim Start bei
lichess.org, und 13 Registrierungen im selben Augenblick hielt der DDoS-Schutz von Lichess für einen
Angriff (2026-09-11 auf der zweiten Maschine: 429, dann IP-Sperre, null Engines — jeder Neustart
wiederholte es). `test/supervisor.test.sh` misst den Abstand der echten Starts.

### Knotenziel + „nur auf Anforderung"-Engines (2026-10-05, Phase 1 der Lc0-Zweitprüfung, UNGETAGGT)
- `AnalysisJob.TargetNodes` (long?, Migration `AnalysisJobTargetNodes`), `CreateAnalysisJobRequest.TargetNodes` 1 000..50 000 000.
  Gesetzt: Worker schickt `nodes` statt `depth` (genau EIN Limit), setzt `ReachedDepth` je Lauf auf 0, bricht selbst ab und gilt
  als fertig bei Ziel (`AnalysisJobStream.NodeGoalMet`, 5 % Spielraum wenn die Engine selbst endet). `TargetDepth` zählt dann nicht;
  `UpdateAsync`/`RestartAsync` fassen fertige Knotenaufträge nicht an.
- Config `AnalysisJobs:ExplicitOnlyEngineIds` (Liste von Engine-Ids; leer = wie bisher, NICHT in Prod gesetzt): solche Engines fallen
  aus der automatischen Wahl (`PickBackgroundEngineAsync`), aus dem Failover (`NextEngineAfter`, weder hinein noch heraus) und aus der
  Platzzahl der Meisterpartien-Analyse; mit ausdrücklicher `EngineId` rechnen sie. Helfer `Services/ExplicitOnlyEngines.cs`.
- Offen: `EngineOwnerResolver` zählt sie noch als Hintergrund-Engine (nur Lc0 eingetragen → Besitzer gilt als „hat Engine", automatische Wahl wirft).
- **Konvergenz-Stufen einer Knotenanalyse** (2026-10-05, UNGETAGGT): bei Aufträgen mit `TargetNodes` hält der Worker aus denselben Stream-Zeilen
  (keine Mehrrechnung) je Schwelle `AnalysisJobs:SnapshotStepNodes` (Vorgabe 10 000, 0 = aus) den Stand fest — `NodeStepRecorder`
  (`Services/NodeSteps.cs`, rein): Schwelle T bekommt die LETZTE Zeile mit `nodes ≤ T`, aber nur, wenn sie höchstens eine Schrittweite alt ist
  (sonst fehlt die Stufe, nichts wird erfunden); am Ende steht die letzte Zeile zusätzlich unter dem Knotenziel selbst (mit der tatsächlich
  erreichten Knotenzahl). Eine Wiederaufnahme ersetzt eine Stufe nur mit MEHR Knoten. Gespeichert als JSON `AnalysisJobs.NodeStepsJson`
  (`[{"t","n","m","cp"|"mate"}]`, Bewertung aus Sicht der Seite am Zug, mit dem Persist-Intervall gebündelt) und beim Ingest nach
  `GameAnalysisPositions.NodeStepsJson` mitgenommen, bevor der Auftrag gelöscht wird. Auswertung `Convergence.Evaluate` (rein): Bezug je Stellung
  = ihre letzte Stufe, Stellungen mit nur einer Stufe fehlen; je Schwelle Anteil gleicher Zug, Median und 90. Perzentil des Abstands in cp (Matt ±1000)
  und in Gewinnchance-Prozentpunkten, Zugwechsel gegenüber der Vorstufe, Matt-gegen-kein-Matt getrennt gezählt. `GET /api/game-analyses/{id}/convergence`
  (nur eigene Analyse, sonst 404); Tabelle „Konvergenz" in `game-analysis-detail.component.ts` (nur bei Knotenanalysen). **Dichte der Stufen hängt an der Engine**:
  der Broker (`EmitBuilder`) gibt je VOLLSTÄNDIGEM MultiPV-Satz einer `info`-Folge eine Zeile weiter — wie oft Lc0 so einen Satz meldet, steht nirgends im Code.
- **Taktik-Ernte, Zweitprüfung (Phase 2, Plan `TACTIC_LC0_PLAN_2026-10-05.md`)**: Config `TacticHarvest:SecondEngineId` (leer = aus, alles wie
  vorher), `TacticHarvest:SecondEngineNodes` (Standard 50 000), MultiPV 3, Aufträge mit `EngineId` + `TargetNodes`. Läuft NACH der
  Erstprüfung (Status `Done`, `SecondAgrees == null`): Stufe 0 Aufgabenstellung (bester Zug gleich + `IsUnique`, `TacticHarvest.Agree`),
  Stufe 1 Stellung vor dem Fehler (`Detect` mit den Zweitwerten), ab Stufe 2 spätere Löserzüge (uneinig → Lösung endet davor, bei
  Matt-Aufgaben `Disputed`). Uneinig → `TacticCandidateStatus.Disputed` + `RejectReason` (`lc0Move`/`lc0NoBlunder`/`lc0Line`/`lc0Failed`/`lc0Fen`).
  `PublishAsync` nimmt bei eingeschalteter Zweitprüfung nur `SecondAgrees == true`. Steht die Engine nicht in der Hintergrund-Liste des
  Besitzers, wartet die Zweitprüfung (Warnung im Log). Zähler: `SELECT Status, COUNT(*) FROM TacticCandidates GROUP BY Status`.

### Endstellung in Partie-Analysen (0.689.0)
Wunsch 2026-10-06 („die letzte Stellung hat keine Linien"): `GameAnalysisService.CreateAsync` legt nach den Zugzeilen eine
weitere `GameAnalysisPosition` mit `Ply = PlyCount`, der Stellung NACH dem letzten Zug und LEEREM `GameMoveUci`/`GameMoveSan`
an — nur wenn dort noch gezogen werden kann (`FinalPositionOf`, kein Matt/Patt). Bestand nicht nachgezogen (Entscheidung des
Users). Pumpe, Vertiefung und Fertig-Erkennung behandeln sie wie jede Zeile. **Leser, die je Zeile einen Partiezug brauchen,
lassen sie weg** — `p.GameMoveUci != ""` (Punktepartie: alle Abfragen in `GuessSessionService`; Erklärungen, Roast,
Nacherzählung, Taktik-Ernte, Detailliste `GetAsync`, „gleiche Partie") bzw. `Ply < PlyCount` (Zähler `Analyzed` in
`GameEvalsStore`, Restdauer). `GameEvals.FinalOfPlies`: die Bewertung nach dem letzten Zug kommt aus der Endstellung, sonst wie
bisher aus dem gespielten Kandidaten — Kurve, Genauigkeit (Server `GameAccuracy` und Client `evals.final`) nutzen sie. Die
Computer-Linien der Partieseite zeigen sie am letzten Zug von selbst (`computerLinesAt` sucht `ply = currentIndex + 1`).
Neue Abfrage auf `GameAnalysisPositions`, die je Zeile einen Zug erwartet → die Bedingung mitnehmen.

### „Tiefe Analyse" einer Stellung (0.686.0) — `DeepAnalysisService`, `POST /api/deep-analysis`
Wunsch 2026-10-06: Vereinsmitglieder (`league.view`, live; sonst 403) rechnen über das ⋮-Menü der Partieseite die
Stellung auf dem Brett tiefer: `{ fen }` → `{ stockfish, lc0, stockfishDepth: 40, lc0Nodes: 500000 }` = zwei gewöhnliche
Aufträge DES NUTZERS (`AnalysisJobService.CreateAsync`, `remember: false`, MultiPv 3): Stockfish auf der Engine aus
`EngineOwnerResolver` (eigene, sonst Haus-Engine), Lc0 auf der Registrierung namens `ClubSecondEngine:EngineName` (fehlt sie:
`lc0: null`). Normale Aufträge → verdrängen die Stapelarbeit ihrer Engine. Erkannt am Titel (`DeepAnalysisService.StockfishTitle`
/`Lc0Title`): dieselbe Stellung = dieselben Aufträge (auch fertige), eine andere löscht die noch offenen der vorigen — eine
tiefe Analyse je Nutzer. `/api/analysis-jobs/live` trägt dafür `nodes`. Oberfläche: `features/games/deep-analysis-dialog.component.ts`
(Regeln rein in `deep-analysis.util.ts`): je Engine ein Balken (Tiefe bzw. Knoten), Linien = die HINTERLEGTEN der Partie-Analyse
(`GameReviewComponent.storedChange`), bis der Auftrag weiter ist (`deepAhead`), dann seine; der Menüpunkt erscheint nur, wo die
Seite `[deep]` an `app-position-menu` reicht (Partieseite) und das Konto `league.view` hat.
**Unter der Partie (0.690.0):** `GET /api/deep-analysis` = die eigenen tiefen Aufträge (ohne Recht leer). Der Rückblick
(`GameReviewComponent.deepJobs`, mit `withAlternatives`) holt sie beim Start, alle 5 s solange einer offen ist, sonst alle 30 s;
passt einer zur Stellung auf dem Brett (`deepJobFor`, erste vier FEN-Felder, Lc0 am Knotenziel) und ist weiter als das
Hinterlegte (`deepAhead`), ersetzen seine Linien den Stockfish- bzw. Lc0-Block, mit Etikett „Tiefe Analyse · Tiefe n".
**Live-Analyse mit Lc0 (0.693.0):** `AnalysisEngineService` schickt für eine Lc0-Engine (`isLc0Engine`) `nodes: Lc0LiveNodes` (500 000) statt der Tiefe — Lc0s Tiefe ist die mittlere Baumtiefe, `go depth 12` war nach ~6k Knoten fertig; ein Stream-Ende gilt dann nie als Abriss.
**Ganze Partie auf Lc0 (0.692.0):** `POST /api/deep-analysis/game { pgn }` (ebenfalls `league.view`) → eigene Analyse
(`Origin.Manual`) auf der Lc0-Registrierung mit `ClubSecondEngine:TargetNodes`; dasselbe PGN noch einmal = dieselbe Analyse.
Partieseite: ⋮ → „Mit Lc0 analysieren", solange der Rückblick keine Lc0-Alternative hat (`GameReviewComponent.hasLc0`), danach
`refreshAlternatives()`.

### Vereinspartien zusätzlich auf Lc0 (0.685.0) — `ClubSecondEngineScheduler`
Wunsch 2026-10-06: „alle Ligapartien, die neu dazukommen, automatisch mit 100k rechnen, und einmalig alle alten nachrechnen".
Jede `LeagueClubGame` bekommt eine ZWEITE Analyse: `Origin = Club` MIT `EngineId` (Registrierung namens
`ClubSecondEngine:EngineName`, Vorgabe „RookHub Spark Lc0" — per Name, die `rhe_`-Kennung wechselt beim Neuanmelden; leer
= aus) und `TargetNodes` (`ClubSecondEngine:TargetNodes`, Vorgabe 100 000 — gemessen 06.10. an 2 Partien: Lc0 stoppt per
Smart Pruning im Median bei ~88k, ab 50k bewegt sich die Bewertung nur um Hundertstel), MultiPv 3, höchstens
`ClubSecondEngine:MaxOpen` (1) offen, neueste Partie zuerst, minütlicher Takt. **Die Stockfish-Analyse bleibt die der
Partie**: jeder Leser der Club-Analyse fragt `EngineId == null` (LeagueClubService.ClubAnalyses/FillAnalysis,
SavedGameService.ClubAnalysisForMovesAsync, LeagueAnalysisQueue, MasterAnalysisScheduler.NextClubGameAsync + Zählung offener
Stellungen, Taktik-Ernte) — wer eine neue Club-Abfrage baut, nimmt die Bedingung mit. Sichtbar wird sie über
`POST /api/game-analyses/same-game` und `GET /api/game-analyses/{id}/evals`: Nutzer mit `league.view` sehen dort zusätzlich
Club-Analysen mit `EngineId` (`clubReader`). `GameEvalPlyDto.Nodes` = erreichte Knoten (letzte Stufe aus `NodeStepsJson`),
die Partieseite zeigt sie in den Ansichten Lc0/Beide.

### Meisterpartien im Hintergrund analysieren (2026-09-28) — `MasterAnalysisScheduler`
Wunsch: „zu den gleichen Zeiten wie die Übersetzung auch Analyse der Meisterpartien — auf allen 16 Direktengines, aber
wenn ein anderer Auftrag reinkommt, hat der Vorrang". Drei Bausteine:
- **Takt** (`Services/MasterAnalysisScheduler.cs`, Hosted Service): außerhalb der Sperrzeiten der Spark (`QuietHours`,
  dieselbe Angabe `TextLlm:QuietHours` wie Übersetzung und Texte — Vorgabe Mo–Do 08–17, Fr 08–14 gesperrt) legt er
  Bibliothekspartien ohne Analyse als `GameAnalysisOrigin.Library` an — erst die kommentierten, dann der Rest, je nach
  Id; eine neue erst, wenn die laufenden zusammen weniger offene Stellungen haben als der Besitzer Hintergrund-Engines
  (derselbe „Schwanz" wie 0.543.0). Besitzer = Haus-Engine-Besitzer (Admin mit „als Haus-Engine teilen"), abweichend
  `MasterAnalysis:OwnerUserId`; abschalten mit `MasterAnalysis:Enabled=false`. Tiefe/Linien wie eine angeforderte
  Bibliothekspartie (`GuessTargetDepth`, 5 Linien, `CreateLibraryBatchAsync`). In der Sperrzeit reiht die laufende
  Partie keine Stellungen ein (`EnqueueNextAsync`); was schon eingereiht ist, läuft zu Ende.
- **Vorrang**: jeder Auftrag einer `Library`-Analyse ist `Background`. Neu seit diesem Stand: ein normaler Auftrag
  VERDRÄNGT einen laufenden Hintergrund-Auftrag auf seiner Engine (`IAnalysisJobControl.PreemptBackground`, aufgerufen in
  `AnalysisJobService.CreateAsync`) — wie der Live-Vorrang auf `Paused`, ohne Fehlversuch; vorher wartete er, bis die
  Vertiefung/Meisterpartie auf dieser Engine fertig war. Gilt auch für die Vertiefung eigener Partien.
- **Sichtbarkeit**: `Library`-Analysen gehören dem Besitzer, sind aber für ALLE lesbar (`GetPlayableHeadAsync`,
  Punktepartie starten, Bibliothek: `InPool`/`RequestAsync` nimmt sie statt neu zu rechnen) — und bewusst NICHT
  `IsPublic`: `ListPublicAsync` (Punktepartie-Bestand) ist ungeblättert und läge sonst unter >100 000 Partien begraben.
  Nicht in `ListAsync` des Besitzers, nicht in seiner Auftragsliste (`/api/analysis-jobs`), nicht in seiner
  Partien-Reihenfolge (`IsOwnersTurnAsync`, `OpenFirstPassPliesAsync`) — sonst hielte eine Meisterpartie seine eigene auf.
- **Speicher**: ~500 B je Stellung + ~15 KB je Analyse (PGN-Kopie) → bei einigen tausend Partien je Woche grob
  +0,4 GB/Woche, beim ganzen Bestand (130 544 Partien, 94 881 kommentiert) ~10 GB. Plattenstand im Blick behalten.
- **Vereinspartien zuerst** (0.588.0, Wunsch „wirf die Partien aus dem Vereinsverzeichnis auch immer in die Analyse"):
  jede Partie der LeagueHub-Vereins-Datenbank (`LeagueClubGame`) ohne Analyse kommt VOR der nächsten Meisterpartie dran —
  `GameAnalysisOrigin.Club = 4`, verknüpft über `GameAnalysis.LeagueClubGameId` (Index, kein FK), angelegt mit
  `CreateClubBatchAsync` (Titel aus dem PGN-Kopf). Dieselben Zeiten, Engines und derselbe Vorrang; beide Etiketten
  zählen gemeinsam in „offene Stellungen < Engines" und stehen in jeder Ausschluss-Abfrage ausgeschrieben
  (`Origin != Library && Origin != Club`; für C#-Seiten `GameAnalysisOrigins.IsBatch`). Kein Zeiger wie bei den
  Meisterpartien: je Takt die kleinste Id ohne Analyse (es sind wenige, neue kommen jederzeit dazu). **Anders als eine
  Meisterpartie NICHT für alle lesbar** (`GetPlayableHeadAsync`/Guess-Start lassen `Club` weg) — die Vereins-Datenbank
  sieht nur, wer in LeagueHub `league.view` hat. **Die Analyse trägt die Namen der Partie**: `LeagueClubService`
  bekommt `GameAnalysisService` (optional, Tests ohne) — `DeleteAsync` löscht vorher die Analyse samt offenen Aufträgen
  (`DeleteForClubGameAsync`), `UpdateAsync` zieht Namen/Ergebnis/Veranstaltung/Titel/PGN nach (`SyncClubGameAsync`,
  die Züge ändert eine Korrektur nie) — sonst bliebe ein zu „Schwaz" korrigierter Name in der Analyse stehen.
- **Für alle im Verein** (0.593.0, Wunsch „die Partien sollen allen aus dem Verein zur Verfügung stehen"): die Analyse
  liest jeder mit `league.view` — der Zugang hängt an der PARTIE, nicht am Besitzer der Analyse. `GET
  /api/league/club/games` trägt je Zeile `analysis` (Stand + Genauigkeit wie in „Meine Partien"), `GET
  …/games/{id}` dieselbe Zeile mit frischem Stand, `GET …/games/{id}/evals` die Bewertungen (ohne Buchzüge). Gelesen wird über
  `Services/GameEvalsStore.cs` — DIESELBEN zwei Wege wie bei den gespeicherten Partien (`SavedGameService` nutzt ihn
  seither auch): `ReadAsync` (Kurve/Zug-Klassen) und `StatesAsync` (Listen-Stand samt Genauigkeits-Nachtrag). Welche
  Analyse zu einer Vereinspartie gehört: die jüngste mit `Origin = Club`. LeagueHub: Spalte „Analyse" (Genauigkeit
  Weiß · Schwarz bzw. Fortschritt) und „Nachspielen" (klappt `lh-game-replay` mit `evalsUrl` auf — darunter RookHubs
  `GameReviewComponent` mit `[withExplanations]="false"`: „Warum war das ein Fehler?" gibt es für Vereinspartien nicht,
  jede Nachfrage wäre ein 404). Über Teilen-Links bleibt die Vereins-Datenbank unlesbar.
- **„Analyse" = RookHubs Partieseite** (0.653.0, Wunsch 2026-10-04: „sollte das Spiel wie aus Meine Games aufmachen, mit
  unten den vorberechneten Werten"): der Knopf springt per Einmal-Code auf `/club-games/{id}` — `SharedGameComponent` mit
  `data.mode = 'club'`: Partie aus `GET /api/league/club/games/{id}`, Kurve aus `…/evals`, kein „Partie analysieren", keine
  Erklärungen, Jahr statt Datum. Das Recht prüft nur der Server (`league.view`, sonst „konnte nicht geladen werden").
- **Eine kopierte Vereinspartie wird NICHT ein zweites Mal gerechnet** (0.653.0, Wunsch: „wenn jemand das Game lokal kopiert,
  soll es nur einmal analysiert werden"): `SavedGameService.ClubAnalysisForMovesAsync` findet die nicht gescheiterte
  Club-Analyse einer Vereinspartie mit GENAU denselben Zügen ab der Grundstellung (`LeagueClubService.HashOf` über die SAN,
  dieselbe Schreibweise wie beim Upload). Der PGN-Import (`POST /api/games/import`, also auch „Zu meinen Partien" aus
  LeagueHub) verknüpft sie gleich beim Anlegen, „Partie analysieren" nimmt sie als dritte Stufe vor dem Neu-Einwerfen.
  Preis: die Kopie bekommt die Vereins-Analyse (Tiefe 20, keine Vertiefung auf 30). Die umgekehrte Richtung (eine schon
  analysierte eigene Partie wird in die Vereins-Datenbank hochgeladen) rechnet weiterhin neu.
- **Liga-Partien danach** (0.665.0, Wunsch „analysier im Hintergrund auch alle Partien für LeagueHub — zumindest von
  Spielern, die aktuell in der Liga mitspielen; erst die neuesten (2 Jahre zurück), dann bevorzugt die Gegner von Schwaz
  nächste Runde, dann der Rest, und erst dann wieder die Meisterpartien"): `Services/League/LeagueAnalysisQueue.cs`
  (Singleton, im Takt NACH den Vereinspartien, VOR den Meisterpartien). Quelle sind die Profile
  (`LeaguePlayerProfile.Pgn`) aller Spieler mit FIDE-ID auf einer Meldeliste der laufenden Saison, nur Partien der letzten
  `Years` (2) Jahre ab der Grundstellung; zuerst die Meldeliste des Gegners jeder Schwazer Mannschaft in ihrer kleinsten
  Runde ohne Ergebnis, dann alle übrigen, je neueste zuerst. Dieselbe Partie in zwei Profilen zählt einmal. Die Liste lebt
  im Speicher und wird stündlich neu gebaut. Angelegt mit `GameAnalysisOrigin.League = 5` (`CreateLeagueBatchAsync`,
  `IsBatch`, dieselben Ausschlüsse wie Library/Club — nicht für alle lesbar, in keiner Liste) und dem Zug-Schlüssel
  `GameAnalysis.MovesHash` (= `LeagueClubService.HashOf` der kanonischen SAN, Index). Übersprungen wird, was schon eine
  nicht gescheiterte Analyse mit diesem Schlüssel oder eine Club-Analyse derselben Züge hat; `ClubAnalysisForMovesAsync`
  nimmt Liga-Analysen über `MovesHash` mit (Kopie nach „Meine Partien", „Analyse" auf der Spielerkarte).

### Punktepartie (`/guess`) — eine Meisterpartie Zug fuer Zug erraten

Der Nutzer uebernimmt EINE Seite einer analysierten Partie und raet ab einem bestimmten Halbzug
jeden Zug; gewertet wird gegen den TATSAECHLICHEN Partiezug (`GuessScoring`), die Engine urteilt nur
ueber die Alternativen — die Kandidatenlisten stehen fertig in der `GameAnalysis`, hier laeuft keine
Engine mehr.

**Die eiserne Regel: die Fortsetzung verlaesst den Server nicht.** Ausgeliefert wird immer nur die
aktuelle Stellung; der Partiezug kommt erst als ANTWORT auf den Rateversuch. Genau deshalb liegt der
Fortschritt in einer SITZUNG am Server — auch ohne Anmeldung. Ein Client, der selbst mitzaehlt,
muesste dem Server sagen, bei welchem Halbzug er steht, und koennte damit jeden Zug der Partie
einzeln abfragen.

**Ohne Anmeldung** (seit 0.459.0) laeuft alles ueber `…/anonymous` mit der Sitzungskennung des
Browsers (`GuessSession.UserId` ist NULLBAR, daneben `AnonymousSessionId` — dasselbe Muster wie bei
den anonymen Puzzle-Versuchen). Spielbar ist dort ausschliesslich der **kuratierte Bestand**
(`GameAnalysis.IsPublic`): an einer fremden privaten Analyse kommt niemand vorbei, weil
`StartAsync` anonym gar nicht erst nach einem Besitzer fragt. Die Kennung kommt aus
`core/anon-session.ts` und muss `ValidationConstants.SessionIdPattern` erfuellen (Hex, 32–36) — die
Mindestlaenge ist die einzige Schranke zwischen zwei anonymen Durchlaeufen.

**Der erste relevante Zug** (0.467.0, `GuessStartPly`): wo das Raten anfaengt, war bis dahin eine
KONSTANTE (`DefaultSkipPlies` = 8, im Code als „grober Platzhalter" vermerkt) — bei einem scharfen
Gambit mitten im Gefecht, bei einer geschlossenen Eroeffnung noch reines Buchwissen. Jetzt
entscheidet der FRUEHERE von zwei Hinweisen: (1) die Eroeffnung verlaesst das Buch — der erste
Halbzug, dessen Zugfolge in weniger als `RareBelowGames` (20) Partien des Rohbestands vorkommt
(`LibraryGame.OpeningLine`, Praefix-Suche auf dem Index, BINAER gesucht: fuenf Abfragen statt
dreissig); (2) der Kommentator faengt an zu reden (`LibraryGameReader` liefert den ersten
kommentierten Halbzug gleich mit). Gedeckelt auf `Earliest` (6) bis `Latest` (40) und nie hinter
das Partieende. **Die beiden Hinweise werden VERSCHIEDEN umgerechnet** (0.468.0): beim
Eroeffnungs-Hinweis wird der erste Zug ausserhalb des Buchs GERATEN (minus eins), beim
Kommentar-Hinweis wird der kommentierte Zug noch VORGESPIELT und der Zug danach geraten. Sonst
gehoerte der Kommentar zu dem Zug, der gerade gesucht ist, und duerfte nicht gezeigt werden — der
Hinweis, der den Einstieg bestimmt hat, waere ausgerechnet der einzige unsichtbare. Das Brett zeigt
ihn beim Aufgehen der Sitzung (`showOpeningNote`). **Die Untergrenze ist noetig**: Sammlungen setzen den ersten Kommentar oft an den
ERSTEN Zug, und dort steht dann eine Quellenangabe statt einer Erklaerung. Ohne Bestand (frische
Installation, `MinLibrarySize` 1000) greift der erste Hinweis nicht und die alte Vorgabe traegt
weiter. Das Ergebnis haengt an der PARTIE und wird in `GameAnalysis.SuggestedStartPly` gemerkt.

**Ein Partiezug, den die Engine nicht unter ihren besten fuehrt, wird jetzt GESPIELT** (0.467.0).
Vorher uebersprang `AdvanceToPlayableAsync` die Stellung wortlos — und das traf ausgerechnet die
interessanten Zuege: ein Opfer, das die Engine erst zwei Zuege spaeter versteht, ist genau der Zug,
den man raten moechte. Die Bewertung liegt laengst da: die Stellung NACH dem Partiezug ist selbst
gerechnet, und ihre beste Bewertung ist die des GEGNERS — mit umgekehrtem Vorzeichen die des
gespielten Zuges (`GuessSessionService.GameMoveEvalAsync`, durchgereicht als
`GuessScoring.Evaluate(..., gameEvalPawns)`). Uebersprungen wird nur noch, was sich so auch nicht
bewerten laesst: der letzte Zug der Partie (keine Folgestellung) und eine aufgegebene Folgestellung.
Der Deckel „ungelistete Zuege sind hoechstens gleichwertig" gilt dabei NICHT fuer den Partiezug
selbst — sonst braechte ausgerechnet der gesuchte Zug statt der vollen Wertung ein „gleichwertig".

**Die Seite waehlt man im Bestand NICHT** (`CreateGuessSessionRequest.GuessWhite` weglassen): man
uebernimmt die des GEWINNERS, das ist der Sinn der Uebung. `GuessSessionService.WinnerSideAsync`
nimmt dafuer das ERGEBNIS, sonst die BEWERTUNG der letzten gerechneten Stellung (ab 1,5 Bauern
Unterschied) — und faellt sonst auf Weiss zurueck. Der Umweg ueber die Bewertung ist kein Sonderfall:
die zehn Meisterpartien aus Capablancas *Chess Fundamentals* tragen in der Kopfzeile nur `*`, weil
das Buch die Ergebnisse allein im Fliesstext nennt. Bei den EIGENEN Analysen bleibt die Wahl.

**Ein Deckel je Besitzer** (`MaxSessionsPerOwner` = 50, raeumt nur BEENDETE Durchlaeufe weg): `POST`
legt auch ohne Anmeldung Zeilen an, und ohne Deckel waechst die Tabelle mit allem, was der
Rate-Limiter durchlaesst.

**Eigene Partien einwerfen** (0.463.0, nur angemeldet): auf der Seite steht ein PGN-Feld, das eine
`GameAnalysis` mit `Origin = Guess` anlegt — und zwar OHNE Tiefen- und Linien-Regler.
`GameAnalysisDefaults.GuessTargetDepth` (20) setzt der Server; die Anfrage
(`CreateGuessGameRequest`) nimmt die Tiefe gar nicht erst entgegen, das Verbergen im Formular ist
also kein Vorhang vor einem offenen Feld. Zwei Gruende: erstens rechnet meist NICHT die eigene
Maschine (siehe Haus-Engine), und ein Regler waere Selbstbedienung an fremder Rechenzeit — Tiefe 40
kostet grob das Zehnfache von 30. Zweitens braucht die Punktepartie die Tiefe nicht: gewertet wird
gegen den TATSAECHLICH gespielten Zug, die Engine liefert nur die Rangfolge der Alternativen. Wer
die Tiefe wirklich will, reiht die Partie weiter ueber `/analysis/games` von Hand ein — dort aendert
sich nichts.

**Die Haus-Engine.** Analyseauftraege laufen ueber Token und External-Engine-Registrierung des
AUFTRAGGEBERS; ein normal registrierter Nutzer hat keine, sein PGN prallte an „No background engine
configured" ab. Ein Admin kann seine Hintergrund-Engines deshalb freigeben
(`LichessEngineCredential.ShareAsHouseEngine`, `PUT /api/engine/house`, Haekchen in der
Engine-Karte des Profils). `GameAnalysisService.ResolveGuessEngineOwnerAsync` nimmt dann zuerst die
EIGENE Engine des Einwerfers und faellt sonst auf die Haus-Engine zurueck; die Freigabe gilt nur,
solange das Konto Admin ist. Der Auftrag BLEIBT dabei beim Einwerfer — nur `EngineOwnerUserId`
(neu an `GameAnalysis` UND `AnalysisJob`, `null` = Besitzer) sagt dem Worker, wessen Token und
Engine er benutzt. Den Auftrag stattdessen dem Haus-Konto zu geben waere einfacher gewesen und
falsch: Deckel (`MaxOpenJobsPerUser` = 50), Trimmer, Auftragsliste und Sekundenanzeige haengen alle
am Besitzer — alle Einwerfer teilten sich dann fuenfzig Plaetze, einer koennte alle aussperren, und
in der Auftragsliste des Admins staenden fremde Partien. `PickBackgroundEngineAsync` zaehlt die
kuerzeste Schlange entsprechend nach ENGINE-BESITZER (`EngineOwnerUserId ?? UserId`).

**Eine Partie nach der anderen** (0.466.0): die Pumpe fuettert je NUTZER immer nur EINE Partie
weiter — die aelteste mit ungerechneten Stellungen (`GameAnalysisService.IsOwnersTurnAsync`).
Vorher fuetterte sie jede unfertige Partie bis zum Block-Limit; bei fuenfzig offenen Auftraegen je
Nutzer liefen damit vier bis fuenf nebeneinander und alle wurden gleich langsam fertig. Das ist die
schlechteste Aufteilung: dieselbe Engine-Zeit, aber man wartet auf JEDE Partie das Fuenffache,
statt nach einem Fuenftel die erste spielen zu koennen — und eine halb gerechnete Partie ist fuer
die Punktepartie nichts wert. Gemessen wird an den STELLUNGEN, nicht am Status; gescheiterte
Partien blockieren nicht; laufende Auftraege einer anderen Partie werden nicht abgebrochen, sie
laufen aus. Je Nutzer und nicht global, damit sich zwei Leute nicht gegenseitig ausbremsen.

**Der Schwanz einer Partie (0.543.0)**: die Regel „eine nach der anderen" hat eine Ausnahme — haben
alle aelteren unfertigen Partien des Nutzers ZUSAMMEN weniger offene Stellungen, als Engines da sind
(`GameAnalysisService.EngineSlotsAsync`: die Hintergrund-Engines des Engine-Besitzers, bei fest
gewaehlter Engine 1), bekommt die naechste Partie schon Auftraege. Sonst stuenden am Ende jeder Partie
Engines still: gemessen am 2026-09-26 auf Prod mit 16 Engines endete jede Partie mit 20–30 s, in denen
15 Engines nichts taten — bei Partien von drei Minuten ein Fuenftel der Zeit. Die aeltere Partie wird
trotzdem ZUERST fertig, ihre Auftraege stehen vorn (FIFO nach `CreatedAt` in `PickNextForEngineAsync`).
Dieselbe Schwelle gilt fuer die Vertiefung (`IsOwnersRefineTurnAsync`): sie wartet, solange der erste
Durchgang mindestens so viele offene Stellungen hat wie Engines — und seit 0.647.0 auch unter den Vertiefungen
selbst (die naechste beginnt am Schwanz der aelteren, siehe „Zwei Durchgänge"). Zweite Haelfte derselben Messung: der
Worker holte den naechsten Auftrag fuer eine frei gewordene Engine erst beim naechsten Tick (5 s) —
seit 0.543.0 weckt ein beendeter Lauf die Schleife (`WakeSignal`), und `GET
/api/game-analyses/throughput` traegt die SPITZE der letzten 24 h (`MaxRunningEngines24h`,
`MaxNodesPerSecond24h`, Stundenkoerbe in `AnalysisJobLive`, nur Arbeitsspeicher): am Schwanz einer
Partie rechnet oft nur eine Engine, und „engines: 1 · 1 164 kN/s" allein sah wie ein Ausfall aus.

**Welche Einstellung wirklich schneller ist, wurde am 2026-09-13 auf Prod AUSGEMESSEN** — drei
Fenster zu je zwanzig Minuten, und das Ergebnis widerlegt die naheliegende Annahme:

| Engines | Block | Tiefe je Engine | Stellungen je Stunde |
|---|---|---|---|
| 16 | 32 | 2 | 957 |
| **5** | **32** | **6–7** | **1596** |
| 5 | 96 | 19 | 1104 |
| 16 | 96 | 6 | 786 |

Die letzte Zeile hat dieselbe Schlangentiefe wie die zweite und liefert die HAELFTE — nicht die
Tiefe entscheidet also, sondern die ZAHL der Engines, und zwar gegenlaeufig. Belegt ist davon der
Anteil, der auf die 503-Wechsel geht: im 16-Engine-Fenster 156 Wechsel gegen null in beiden
5-Engine-Fenstern, und jeder legt einen Auftrag `EngineSwitchBackoffSeconds` (15 s) schlafen.
Warum ein GROESSERER Block bei gleicher Engine-Zahl schadet, ist offen. Vorbehalt: jedes Fenster
umfasst sechs bis sieben verschiedene Partien, und die rechnen unterschiedlich schnell — die
Reihenfolge ist konsistent, die Zahlen sind es nicht auf zehn Prozent.

**Wer hier dreht, misst nach.** Diese drei wirken zusammen und nicht einzeln:
`MaxOpenJobsPerGame`, die Laenge der Engine-Liste im Profil und `MaxOpenJobsPerUser`.

**Der 503 des Brokers ist KEINE Ausfallmeldung** — er heisst „fuer diese Engine ist gerade kein
Provider frei“ und trifft auch Engines, die nachweislich rechnen (am selben Tag direkt
angesprochen: alle antworten 200). Aus einer Haeufung auf einer Maschine auf deren Ausfall zu
schliessen war falsch: beim Reihum-Wechsel landet ein pendelnder Auftrag zwoelf von sechzehn Malen
dort, sie klopft also dreimal so oft an.

**Wer nicht pollt, bekommt keine Arbeit** (0.711.0, gemeldet 2026-10-07 an /games/61): `EngineAvailability.UsableAsync`
lässt direkt angemeldete Engines (`rhe_`) weg, deren `LastSeenAt` älter als `OfflineAfter` (3 min) oder leer ist — gilt
für neue Aufträge (`PickBackgroundEngineAsync`), Slots/Stapel (`GameAnalysisService`, `MasterAnalysisScheduler`) UND den
Wechsel nach einem 503 (`SwitchEngineAsync` → `NextEngineAfter(…, usable)`: reihum zur nächsten LAUFENDEN). Vorher lagen
die Aufträge einer Partie auf den 16 Engines eines seit einem Tag ausgeschalteten PCs (leerste Schlange gewinnt) und
wanderten nach jedem 503 mit 15 s Pause zur nächsten ausgeschalteten. Lichess-Engines (`eei_`) kennen keinen Poll und
bleiben; ist gar keine Engine brauchbar, gilt wie bisher die ganze Liste (der Auftrag wartet).

**Wie viele Engines wirklich rechnen, entscheidet `MaxOpenJobsPerGame`** (96 seit 0.475.9, davor 32,
davor 12) —
nicht die Zahl der hinterlegten Hintergrund-Engines. Der Worker nimmt je Engine EINE Suche; jede Engine
ueber die Zahl der offenen Auftraege hinaus steht still. Am 2026-09-12 auf Prod nachgemessen: sechzehn
Engines hinterlegt, zwoelf Auftraege auf zwoelf Engines, vier dauerhaft untaetig. Der Ueberhang ueber die
Engine-Zahl ist Absicht: laeuft eine Suche aus, nimmt die Engine sofort den naechsten Auftrag statt bis zu
einen Pump-Durchgang (20 s) zu warten. Der Deckel bleibt unter `MaxOpenJobsPerUser` (50), damit daneben
von Hand eingereiht werden kann — jetzt mit 18 statt 38 freien Plaetzen.

**Der Deckel des Einwurfs** (`MaxOpenGuessGamesPerUser` = 5) zaehlt `Origin = Guess` UND (seit 0.512.0)
`Origin = SavedGame` zusammen — beide rechnen auf fremder Rechenzeit, getrennte Deckel waeren doppelt so viele
Plaetze — und nur Partien, die noch rechnen (`Pending`/`Running`); gescheiterte sperren niemanden aus. Von Hand
ueber `/analysis/games` eingereihte Partien bleiben ungezaehlt: dort rechnet die eigene Maschine. Die Pumpe
(„eine Partie nach der anderen") ist je Nutzer und unterscheidet die Urspruenge nicht.

### Rohbestand (`LibraryGames`) — der Vorrat, aus dem die Punktepartie ausgewaehlt wird

Eine PGN-Sammlung landet NICHT direkt als `GameAnalysis`. Eine Analyse ist ein Versprechen an die
Engine — je Halbzug ein Auftrag, bei Tiefe 20 grob zwanzig Sekunden je Stellung, also rund eine
halbe Stunde je Partie. Bei den 130 679 kommentierten Meisterpartien aus dem ChessBase-Magazin
waeren das Jahre Rechenzeit; die AUSWAHL ist damit die eigentliche Arbeit, und `LibraryGames` ist
der Ort, an dem die Partien liegen, waehrend sie getroffen wird.

Die Zahlenspalten sind der Sinn der Tabelle: sie erlauben das Sortieren, ohne 338 MB PGN erneut zu
lesen. Sie werden nach und nach befuellt, und ein noch nicht befuelltes Feld ist `null` und nicht 0
— „nicht gezaehlt" und „keine Kommentare" sind verschiedene Aussagen.

| Spalte | Was drinsteht |
|---|---|
| `SourceFile` / `SourceTitle` / `SourceRef` / `ExternalGameId` | Herkunft: Datei, Ausgabe (`CBM 104 Extra`), Quelle (`ChessBase`), deren Partie-Kennung |
| `MovesHash` | SHA-256 ueber die normalisierte Zugfolge — Bewertungszeichen und Zugnummern raus, sonst faende der Abgleich keine einzige Dublette |
| `DuplicateOfId` / `Status` | Dieselbe Partie von zwei Leuten kommentiert: behalten wird die mit den MEISTEN kommentierten Halbzuegen, die andere zeigt darauf. Geloescht wird nichts — die zweite Meinung kann die bessere sein |
| Kopfdaten | `White`/`Black`/`WhiteElo`/`BlackElo`/`Result`/`Event`/`Site`/`Round`/`PlayedOn`/`Eco`/`StartFen`/`PlyCount` |
| `Annotator` | Wer kommentiert hat (im Bestand traegt JEDE Partie einen) |
| `CommentCount` / `CommentedPlies` / `CommentChars` | Wie viele Kommentare, wie viele HALBZUEGE einen tragen, wie viel Text. Die mittlere Zahl entscheidet: die Punktepartie haelt an kommentierten Zuegen an, drei lange Absaetze am Schluss machen sie stumm |
| `NagCount` / `VariationCount` | Symbol-Bewertungen und Nebenvarianten — Zeichen ernsthafter Arbeit, aber der Spielende sieht sie nie |
| `Languages` | CSV von ISO-Kuerzeln (`de`, `en,de`), `und` = nicht bestimmbar. NIE `null` lassen, sobald geprueft — sonst holt der naechste Durchgang dieselben Zeilen wieder |
| `OpeningLine` | Die ersten 30 Halbzuege normalisiert („e4 e5 Nf3 …"), indiziert. Damit ist der Bestand eine EROEFFNUNGSSTATISTIK: „wie viele Partien spielen dieselben ersten k Zuege" ist eine Praefix-Suche statt einer Volltextsuche ueber 338 MB |
| `FirstCommentedPly` | Der erste kommentierte Halbzug — zusammen mit `OpeningLine` die Grundlage von `GuessStartPly` |
| `SearchText` | Spieler, Turnier und Kommentator kleingeschrieben in einer Spalte, mit VOLLTEXT-Index — das Feld, ueber das die Bestandssuche laeuft |
| `Score` | Eignungsnote 0–100 (`GuessSuitability`), eigene Spalte statt Formel in der Abfrage: die Gewichtung wird sich aendern |
| `Pgn` | Die Partie selbst (LONGTEXT). Bewusst hier und nicht als Verweis auf Datei und Byte-Position — die Quelldatei ist ein Fund im Ablage-Ordner und keine Zusage |

`LibraryGameReader` liest eine Partie in EINEM Textdurchgang und fasst dabei kein Brett an: das
Nachspielen (`GamePlies.Parse`) ist bei 130 000 Partien der Unterschied zwischen Minuten und
Stunden. Der Preis ist, dass dieser Durchgang die Zuege NICHT prueft — das tut erst die Uebernahme
in eine `GameAnalysis`, und dort gehoert es auch hin. Im Rohbestand zu liegen heisst „eingelesen",
nicht „gut".

**Stand 2026-09-10 (nur DEV, Prod bekommt die leere Tabelle mit dem naechsten Tag):** 130 572
Partien aus `kommentierteMeistergames.pgn` (338 MB, ChessBase-Magazin-Export). 72 Eintraege der
Datei trugen keinen einzigen Zug (Theorie-Fragmente mit `[White "?"]`) und fielen weg, 28 Zeilen
sind Dubletten. 94 898 Partien tragen mindestens einen kommentierten Halbzug. Sprache: 32 594
englisch, 11 031 deutsch, 8 676 franzoesisch, 1 509 gemischt en/de, der Rest verteilt; 58 370 mal
`und` — davon haben aber 54 161 weniger als 400 Zeichen Text, es ist also meist wirklich nichts da
(Quellenangaben, Seitenzahlen, blosse Symbole). Bei den Partien MIT ordentlich Text trifft die
Erkennung in neun von zehn Faellen.

Die Spitze der Note sieht aus, wie sie soll: Aronian–Ding mit 57 von 59 kommentierten Halbzuegen,
Giri ueber seinen eigenen Sieg gegen Carlsen, Tal–Timman von Kuljasevic. **Bekannte Unschaerfe:**
`CommentChars` zaehlt auch ChessBase-Markup im Kommentar mit (`[%cal …]`, `[%csl …]` — 54 841
Partien enthalten welches). Das sind echte Anmerkungen, nur eben Pfeile statt Prosa; auf die
Reihenfolge an der Spitze wirkt es sich nicht sichtbar aus.

### Partie anfordern (`/api/library-games`) — der Rohbestand als Nachschlagewerk

Bis 0.469.0 sah nur das Wartungswerkzeug in `LibraryGames`. Damit war die Auswahl aus 130 000
Partien die Aufgabe genau einer Person mit Datenbankzugang. Der Knopf „Partie anfordern" auf der
Punktepartie-Seite oeffnet den Bestand: suchen, und einzelne Partien in die Warteschlange stellen.

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/library-games?q=&language=&minCommentedPlies=&page=&pageSize=&line=` | **AllowAnonymous** + RL | Eine Seite der Bestandssuche, nach Eignungsnote sortiert; je Zeile `inPool`/`requested`/`gameAnalysisId`. **Ohne die Zuege** |
| POST | `/api/library-games/{id}/request` | Auth | Diese Partie rechnen lassen — derselbe Weg wie ein eingeworfenes PGN (feste Tiefe 20, Haus-Engine, Deckel 5). Liegt sie schon spielbar da, kommt die vorhandene Analyse zurueck (`alreadyPlayable`), es wird NICHTS doppelt gerechnet. 400 mit `reason` ∈ `not-found`/`too-many-open`/`no-engine`/`invalid-pgn` |

**Suchen darf jeder, anfordern nur angemeldet** (0.475.5). Die Suche liefert Kopfdaten — Spieler,
Turnier, Jahr, Kommentator, Kommentardichte — und ausdruecklich KEINE Zuege und keine Anmerkungen;
das ist derselbe Zuschnitt, den `/api/guess-tree` ohnehin anonym ausliefert, und ohne ihn fuehrte der
Stellungsfilter einen Besucher ohne Konto in eine leere Liste. `MarkKnownAsync` bekommt anonym die
UserId 0: die gibt es nicht, also bleibt „schon angefordert“ ueberall falsch und nur „liegt im Bestand“ traegt. Das ANFORDERN bleibt angemeldet — es verbraucht Rechenzeit,
die jemandem gehoert — und der Dialog sagt den Grund an der Stelle, wo der Knopf waere.

**Gesucht wird ueber einen VOLLTEXT-Index** (`LibraryGame.SearchText` = Spieler, Turnier und
Kommentator kleingeschrieben in EINER Spalte). Am echten Bestand gemessen (2026-09-11, 130 572
Zeilen):

| Weg | Dauer |
|---|---|
| `LIKE '%Capablanca%'` ueber eine der vier Einzelspalten | 4,5 s |
| dasselbe ueber eine schmale indizierte Spalte | 0,9 s |
| … und mit `ORDER BY Score DESC LIMIT 25` | **50 s** (der Optimierer nimmt den Score-Index und sucht sich zeilenweise durch) |
| Volltext-Index, Seite samt Sortierung | **4 ms** (Zaehlen 63 ms) |

Der Preis ist, dass WORTANFAENGE gesucht werden: „Capa" findet „Capablanca", „blanca" nicht — fuer
Namen ist das die Suche, die Leute ohnehin tippen. Der Ausdruck wird in
`LibraryGameService.BooleanTerm` gebaut (jedes Wort Pflicht, das letzte mit Stern); die Operatoren
der Boolean-Syntax werden WEGGEWORFEN, sonst waere ein Bindestrich in einem Doppelnamen eine Suche,
die das Gegenteil meint. Woerter unter `MinQueryLength` (3) filtern gar nicht, weil MariaDBs
Volltext-Index sie nicht aufnimmt. **Der Index steht als SQL in der Migration** — EF kann diese
Indexart nicht ausdruecken; `Down()` raeumt ihn wieder weg.

**Die Zuordnung ist VIELE Analysen zu EINER Bibliothekspartie** (`GameAnalysis.LibraryGameId`, kein
Fremdschluessel — der Rohbestand ist ein Arbeitsvorrat, der auch neu eingelesen werden kann): fordern
zwei Leute dieselbe Partie an, bekommt jeder seine eigene. Der umgekehrte Verweis
(`LibraryGame.GameAnalysisId`) koennte immer nur einen halten und bleibt der Kuratierungs-Vermerk.

**Die angeforderte Partie ist PRIVAT** (wie ein eingeworfenes PGN); oeffentlich macht sie erst ein
Admin ueber `PUT /api/game-analyses/{id}/public`. Das ist bewusst so: die Kommentare stammen aus
einer gekauften Sammlung, und „jeder Nutzer kann etwas in den oeffentlichen Bestand schieben" waere
eine andere Entscheidung als „ich rechne mir meine Partie durch".

**Befuellt wird mit `tools/LibraryImport`** (Wartungswerkzeug, kein Teil des API-Images; das
Docker-Image baut nur aus `src/api/RookHub.Api`). Vier Schritte, jeder fuer sich wiederholbar:
`import <datei>` · `dedupe` · `openings` · `languages` · `score`, dazu `stats`, `comments` und `resplit`
(zweisprachige Partien, die als einsprachig vermerkt sind — siehe „Anmerkungen in mehreren Sprachen").

**`queue [anzahl] --user <id>`** ist der Massen-Weg zu dem, was auf der Seite der Knopf „Partie
anfordern" je Partie tut: die besten Partien des Bestands als `GameAnalysis` mit `Origin = Guess`
und fester Tiefe einreihen. Die Auswahl trifft `Services/LibraryPicks.cs` — Note, dann
kommentierte Halbzuege, dann Textmenge, und hoechstens `--per-annotator` (2) Partien je
Kommentator: an der Spitze stehen tausende Partien mit Note 100, und nach der Zeilennummer
sortiert bekaeme man den Bestand alphabetisch nach Kommentator. Der Deckel von fuenf offenen
Partien gilt hier bewusst NICHT (er ist eine Fairness-Regel zwischen Nutzern an der Oberflaeche);
die Reihenfolge bleibt trotzdem gewahrt, weil die Pumpe je Nutzer immer nur EINE Partie
weiterfuettert. Angelegt werden nur die Zeilen — die AUFTRAEGE macht die laufende API beim
naechsten Pump-Durchgang. `--dry-run` zeigt die Auswahl, ohne etwas einzureihen; `--game <id>` reiht genau EINE benannte
Bibliothekspartie ein (der Weg fuer „rechne mir diese hier", ohne die Rangfolge). Verbindung ueber
`ConnectionStrings__DefaultConnection`. Es startet KEINE API-Instanz, sondern oeffnet nur einen
DbContext — eine zweite `RookHub.Api` gegen dieselbe Datenbank streitet sich mit dem
Auftrags-Worker um die Engines.

Die Liste der eigenen Partien auf der Punktepartie-Seite zeigt seither AUCH die noch rechnenden (mit
Fortschrittsbalken, „Spielen" bis zur ersten gerechneten Stellung gesperrt) und frischt sich alle
10 s auf, solange eine offen ist. Vorher standen dort nur Partien mit mindestens einer gerechneten
Stellung — die gerade eingeworfene waere fuer Minuten spurlos verschwunden und ein zweites Mal
eingeworfen worden.

### „Frag die Kommentare" — semantische Suche im Rohbestand (0.536.0)

Dritte Suchart im Dialog „Partie anfordern" (Reiter „Namen & Turniere" | „Frag die Kommentare"): eine Idee, ein Plan
oder ein Motiv in Worten („Läuferopfer auf h7", „Minoritätsangriff") → Partien, deren ANMERKUNGEN davon sprechen, in jeder
Sprache (das Embedding-Modell ist mehrsprachig), je Partie der passendste Auszug.

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/library-games/semantic?q=&take=` | **AllowAnonymous** + RL | `{ available, indexed, items[{ game (wie die Namenssuche, mit inPool/requested), matches[{ fromPly, text, score }] }] }`. Ohne `q` nur `available`/`indexed` — der Dialog zeigt den Reiter nur mit Modell UND eingebettetem Bestand |

* **Stücke statt Partien** (`CommentChunks`, Tabelle `CommentEmbeddings`): Kopfzeile (Spieler, Turnier, Jahr, Kommentator)
  plus aufeinanderfolgende kommentierte Halbzüge („17. Bxh7+: …", Figurenschrift aufgelöst) bis ~900 Zeichen. Der Text
  des Stücks ist zugleich der Auszug der Trefferliste (ohne Kopfzeile).
* **Vektor in MariaDB** (≥ 11.7, Prod/Dev 11.8): Spalte `VECTOR(512)` (EF-seitig `byte[]`, float32 little endian — so nimmt
  MariaDB den Parameter an), Kosinus-Index per SQL in der Migration. Gesucht wird mit rohem SQL
  `ORDER BY VEC_DISTANCE_COSINE(`Vector`, @q) LIMIT 200` — genau diese Form benutzt den Index; InMemory rechnet in C#
  (`VectorMath`). **`VECTOR` ist ein Schlüsselwort — die Spalte steht in SQL immer in Backticks** (ohne: Syntaxfehler im
  `CREATE VECTOR INDEX`). Die CI-MariaDB (`mariadb:11`) muss dafür ≥ 11.7 sein; `CommentSearchSqlTests` prüft Speichern
  und Suche gegen echtes MariaDB.
* **Embedding-Modell** (`OpenAiTextEmbedder`, `Embedding:BaseUrl`/`ApiKey`/`Model`, Compose `EMBEDDING_*`): OpenAI-kompatibles
  `POST /embeddings` — gedacht ist ein Pooling-Modell auf dem DGX Spark, z. B. `Qwen/Qwen3-Embedding-0.6B`. Angefordert werden
  512 Werte (`dimensions`, Matryoshka); mehr wird gekürzt und neu normiert, weniger ist ein Fehler. Suchfragen bekommen die
  Qwen3-Anweisung vorangestellt (`QueryInstruction`), Dokumente nicht. **Auf dem Spark läuft `qwen3-embedding-4b`** (2560
  Werte, seit 2026-09-26) — OHNE Matryoshka-Schalter, vLLM antwortet auf `dimensions` mit 400. Der Client fragt dann einmal
  ohne nach und kürzt selbst (0.539.1; bei Qwen3-Embedding dasselbe wie serverseitig). `EMBEDDING_MODEL` gehört GESETZT:
  `/v1/models` des Spark-Proxys zeigt auf den Chat-Server und nennt das Embedding-Modell gar nicht (ohne Einstellung nimmt der
  Client das erste mit „embed" im Namen, sonst das erste).
* **Befüllen**: `tools/LibraryImport embed [--limit n] [--batch 32]` (Env `Embedding__BaseUrl` …): noch nicht eingebettete
  kommentierte Partien, beste Note zuerst, je Partie ganz oder gar nicht; wiederholbar. Die Auswahl
  (`CommentSearchService.PendingGameIdsAsync`) läuft EINMAL je Lauf mit 10-min-Timeout und wird dann in Portionen zu 500
  abgearbeitet (`EmbedGamesAsync`, überspringt schon eingebettete) — auf Prod dauerte sie bei ~10 000 eingebetteten
  Partien 37 s, und als sie je Portion lief, riss der 30-s-Standard-Timeout den Lauf ab (2026-09-26, 0.550.2).
  `--batch 256` = 256 Texte je Anfrage an den Spark (~18 Stücke/s gemessen; mit 32 rund ein Zehntel). Dev: ~130 000 Partien, ~800 000
  kommentierte Halbzüge, ~95 Mio. Zeichen → geschätzt 150–250 000 Stücke, ~0,5 GB Vektoren.

### Stellungsfilter (`/api/guess-tree`) — der Eroeffnungsbaum der Punktepartie

Die Bestandssuche daneben beantwortet „ich weiss, wie die Partie heisst". Die andere Frage, die
man vor dem Ueben stellt, ist „was gibt es zu MEINER Eroeffnung?" — und das ist keine Textsuche.
Der Knopf „Nach Stellung filtern" oeffnet ein Brett in der Grundstellung und daneben den Baum:
je Zug, wie viele Partien ihn spielen; ein Klick geht hinein, die Liste darunter zeigt die Partien
zu dieser Stellung.

**Zwei Quellen, EIN Baum** (`onlyPlayable`): die freigegebenen `GameAnalysis`-Partien (sofort
spielbar) oder der ganze Rohbestand (`LibraryGame`, 130 000 Partien — von dort wird angefordert).
Der Umschalter ist der Punkt: ohne ihn fuehrt der Baum entweder in eine fast leere Auswahl oder
auf Partien, die man nicht spielen kann.

**Beide Seiten fuehren dieselbe normalisierte Zeile** (`OpeningLine`, die ersten 30 Halbzuege,
„e4 e5 Nf3"), und deshalb ist es ein Baum mit zwei Zaehlungen und nicht zwei Baeume. Bewertungs-
und Schachzeichen fallen beim Normalisieren weg: „Nf3+" und „Nf3" sind derselbe Zug, und ein Baum,
der sie trennt, hat zwei Aeste fuer eine Stellung. `GameAnalysis.OpeningLine` ist seit 0.474.0
eine eigene indizierte Spalte — der Baum fragt bei JEDEM Klick, und aus den Stellungszeilen
gerechnet waere das ein Selbst-Verbund ueber zehn Halbzuege. Nachgetragen wird sie mit
`tools/LibraryImport analysis-openings` (aus den Stellungszeilen, nicht aus dem PGN: dort steht
der Zug schon geprueft da).

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/guess-tree?line=&onlyPlayable=` | **AllowAnonymous** + RL | Die Fortsetzungen nach `line` mit Partienzahl, haeufigste zuerst (hoechstens `MaxMoves` = 40), dazu `total` |

**Gezaehlt wird in der DATENBANK, und die Zahlen sind exakt** (0.475.2). Die erste Fassung holte
bis zu 20 000 Zeilen und zaehlte sie clientseitig. Der Deckel galt auf JEDER Ebene — nicht nur in
der Grundstellung, wie der Kommentar dort behauptete — und er war keine Stichprobe, sondern die
ersten 20 000 Zeilen nach Id, also nach Importreihenfolge. Am echten Bestand log damit alles bis zu
der Tiefe, ab der die Treffermenge unter den Deckel faellt: die Grundstellung meldete 20 000 statt
130 572 Partien, und die 9761 Partien, die dort bei `e4` standen, wurden nach dem Klick auf `e4`
wieder zu 20 000. Eine Zahl, die sich unter der Hand aendert, ist schlimmer als keine.

Jetzt zaehlt ein `GROUP BY` ueber `SUBSTRING_INDEX(SUBSTRING(OpeningLine, n), ' ', 1)`, die
Gesamtzahl ein `COUNT(*)`. Bewusst rohes SQL (`SqlQueryRaw`): `SUBSTRING_INDEX` uebersetzt kein
Anbieter einheitlich, und die InMemory-Datenbank der Tests kennt es nicht — dort laeuft der
clientseitige Weg weiter (`IsRelational()`-Weiche wie bei `LibraryGameService`). Der Praefix geht
als PARAMETER hinein.

**Dafuer braucht es einen ABDECKENDEN Index** (`IX_LibraryGames_Status_OpeningLine`,
`IX_GameAnalyses_IsPublic_OpeningLine`, Migration `OpeningTreeIndexes`). Mit dem Praefix-Index
allein waehlt MariaDB bei `LIKE 'e4 %'` — das ist die halbe Tabelle — den vollen Tabellenscan, und
der laeuft ueber die LONGTEXT-Spalte mit den PGNs. Gemessen auf Dev (130 572 Partien):

| Abfrage | ohne den Index | mit |
|---|---|---|
| Grundstellung | 15,8 s | 0,13 s |
| nach `1.e4` | 22,4 s | 0,25 s |

**Eine `OpeningLine` bekommt nur, wer in der GRUNDSTELLUNG anfaengt** (`LibraryGameReader.StartsFromInitialPosition`,
angewandt in `LibraryGameReader` und `GameAnalysisService.CreateAsync`). Eine Eroeffnungszeile
beschreibt einen Weg aus der Grundstellung; bei einer Vorgabepartie, einer Studie oder Chess960
stand der erste Zug der Partie damit als Fortsetzung an der WURZEL des Baums, wo es ihn gar nicht
gibt. Auf Prod war das ein `Kc6` (Partie 462, Stiller–Welz, Potsdam 1995) — ein Klick darauf konnte
nur mit „dieser Zug geht nicht" antworten. Solche Partien fallen aus dem Baum und bleiben ueber die
Namenssuche erreichbar; `openings` und `analysis-openings` RAEUMEN die Zeilen des Altbestands
entsprechend weg, bevor sie nachtragen.

**Ein Brett in einem DIALOG misst sich zu klein** (0.475.3, gilt app-weit). Material blendet einen
Dialog mit `transform: scale()` ein, und Chessground misst mit `getBoundingClientRect()` — das
liefert die SKALIERTE Groesse. Ein 300-px-Kasten misst waehrend der Einblendung 240, und diese 240
schreibt Chessground als feste Pixelzahl in sein inneres `cg-container`. Danach korrigiert es
nichts mehr: der ResizeObserver sieht die LAYOUT-Groesse des Wrappers, und die hat sich nie
geaendert. Sichtbar war das als Vollbild-Knopf sechzig Pixel neben dem Brett und einer gleich
grossen Luecke darunter. `ChessBoardComponent.fitToHost` misst deshalb nach (rAF, 200 ms, 500 ms),
vergleicht gegen das INNERE Element und duldet dabei Chessgrounds Rasterung auf ein Vielfaches von
acht (in einem 300-px-Kasten steht es richtigerweise auf 296) — ohne diese Schranke zeichnete jeder
Anlauf das Brett neu.

**Der Dialog laedt ohne Umbau nach** (`position-filter-dialog.component.ts`): der vorige Stand
bleibt stehen und wird nur abgeblendet, oben laeuft ein duenner Balken. Vorher setzte jeder Klick
beide Listen auf einen Spinner — der Dialog fiel auf halbe Hoehe zusammen und das Brett sprang
dabei in der Groesse, bei JEDEM Zug. Eine Antwort, deren `line`/`onlyPlayable` nicht mehr zum
angesehenen Stand passt, wird verworfen (wer schnell klickt, hat zwei Abfragen unterwegs). Die
Partieliste darunter nennt die Anzahl und blaettert (25 je Seite); auf schmalen Geraeten bekommt
das Brett 42 % der Breite statt aller — mit 300 px Brett blieben auf einem 400-px-Geraet genau zwei
Zuege sichtbar.

**Baum und Liste muessen DIESELBE Menge zaehlen** (0.475.7/.8). Der Stellungsfilter schickt
`byPosition=true` mit, die Namenssuche nicht. An diesem Schalter haengt, wer mitgezaehlt wird: eine Partie ohne `OpeningLine` (eigene
Ausgangsstellung) erreicht keine Stellung des Baums und darf in seiner Liste nicht auftauchen —
in der Namenssuche schon, denn dort ist sie zu finden und anzufordern. Und die Statusregel ist
in `GuessOpeningTree` dieselbe wie in `LibraryGameService` (weder `Rejected` noch `Duplicate`).
Ohne beides stand auf Prod „130 028 Partien erreichen diese Stellung“ ueber einer Liste mit
der Ueberschrift „Partien (130 544)“ — die 516 Partien ohne Zeile.

Bewusst ein eigener Schalter und nicht die Frage, ob `line` null ist: die Modellbindung von
ASP.NET macht aus einem leeren `?line=` ein `null`, und die Grundstellung ist genau dieser Fall.
Der erste Anlauf (0.475.7) unterschied daran und wirkte deshalb ueberall AUSSER an der Wurzel —
auf Prod nachgemessen: in der Spanischen stimmten die Zahlen, in der Grundstellung nicht.

Die beiden Listen nehmen denselben Filter entgegen: `GET /api/game-analyses/public?line=` und
`GET /api/library-games?line=`. Gesucht wird ueber ein PRAEFIX (`LIKE 'e4 e5 Nf3%'`) — der
Platzhalter steht hinten, also trifft es den Index.

**Das Brett fuehrt der Dialog selbst mit** (chess.js): der Baum liefert nur Zugnamen. Ein Zug, den
die Stellung nicht hergibt — eine Partie mit abweichender Ausgangsstellung kann so einen liefern —
verschiebt die Linie deshalb nicht, sondern sagt es.

### Anmerkungen in mehreren Sprachen (`CommentSets`)

Die Sammlungen liefern ihre Anmerkungen oft ZWEISPRACHIG — und das PGN kann das nicht ausdruecken:
gemessen am 2026-09-11 ueber alle 130 572 Zeilen des Rohbestands traegt **keine einzige** einen
`[%lang`-Marker. ChessBase haengt die Sprachen beim Export schlicht aneinander, erst der englische
Absatz, direkt dahinter der deutsche, in EINEM `{}`-Block.

**Abgelegt wird getrennt vom PGN** (`CommentSets` + `CommentTexts`), und das ist keine
Bequemlichkeit: das PGN ist die QUELLE und traegt Herkunft und Ausgabe einer gekauften Sammlung
(`SourceTitle`, `SourceVersion`). Wer eine Uebersetzung hineinschreibt, kann Quelle und Zutat nie
wieder auseinanderhalten, und ein erneutes Einlesen der Datei wuerde sie verwerfen.

**Ein SATZ je Sprache, nicht Zeilen mit Sprachspalte**: die Herkunft (aus der Quelle gelesen?
maschinell uebersetzt? mit welchem Modell?) gehoert EINMAL je Sprache hin, ein Uebersetzungslauf
schreibt einen Satz am Stueck, und „Sprache umschalten" ist eine Abfrage statt eines Filters ueber
Zeilen verschiedener Herkunft. **Der Anker ist die BIBLIOTHEKSZEILE**, wo es eine gibt — fordern
zwei Leute dieselbe Partie an, entstehen zwei Analysen, und eine Uebersetzung je Analyse waere
dieselbe Arbeit zweimal bezahlt; eine selbst eingeworfene Partie haengt an der Analyse.

**Die FIGURENZEICHEN werden aufgeloest** (`Services/Figurines.cs`). Im PGN stehen die Figuren
INNERHALB der Kommentare nicht als Buchstaben, sondern als Codepunkte der ChessBase-Figurenschrift
(U+E024 bis U+E029, privater Unicode-Bereich). Ohne diese Schrift sind sie unsichtbar, und der
Kommentar liest sich als „I can't win the pawn due to the h7+ trick" — gemeint ist `Bh7+`. Am
Bestand gemessen (2026-09-11): **45 von 101** Partien einer Stichprobe tragen solche Zeichen, und
der Nachtrag hat 737 von 6091 gespeicherten Zeilen angefasst. Die Zuordnung ist am Text BELEGT
(Koenig „I would prefer …b1", Dame „...…a5", Turm „…dg1!?", Laeufer „…h7+ trick", Springer
„...…c6", Bauer „Black's … structure") und haengt an der SPRACHE des Satzes: derselbe Springer
heisst englisch N und deutsch S. Aufgeloest wird deshalb beim Ablegen, wo die Sprache feststeht;
`comments --figurines` holt den vorhandenen Bestand nach. Der Bauer bekommt als einziger ein WORT
statt eines Buchstabens — in der Notation traegt er keinen.

**Getrennt wird satzweise** (`Services/CommentSplit.cs`), mit zwei Signalen: den Funktionswoertern
je Sprache (laengere Listen als bei `CommentLanguage` — dort wird eine ganze PARTIE eingeordnet,
hier ein einzelner SATZ) und den **Figurenbuchstaben**, `Be3/Ng4` gegen `Le3/Sg4`. Gezaehlt werden
nur Buchstaben, die in genau EINER der beiden Sprachen vorkommen (bei `fr/nl` sagt ein `D` nichts,
ein `C` und ein `P` schon).

Drei Entscheidungen, die dabei tragen:
* **Ein Block hat nicht zwei Haelften, sondern beliebig viele Abschnitte.** Sobald der Kommentator
  eine Nebenvariante einschiebt (die beim Einlesen an den Kommentar angehaengt wird), steht dort
  en-de-en-de. Zugeordnet wird deshalb satzweise mit einem Wechselaufschlag (ein gewoehnliches
  Viterbi ueber zwei Zustaende). Gemessen an 875 langen Bloecken: **55 % getrennt mit einem
  einzigen Schnitt, 92 % satzweise.**
* **Die Satzgrenze ist grosszuegig** (Doppelpunkt zaehlt, der naechste Buchstabe darf klein sein) —
  aber ein `.` nach einer ZIFFER ist keine: „8. Ng4" ist eine Zugnummer. Ein Zug am Satzende
  („…well met by Ng4.") wird dagegen erkannt, weil ein Zug auf einem FELD endet.
  Zu fein zu trennen kostet nichts (die Zuordnung legt Nachbarn derselben Sprache wieder zusammen),
  ein verpasstes Satzende kettet zwei Sprachen dagegen fuer immer aneinander.
* **Im Zweifel wird nicht geschnitten.** Reichen die Belege nicht oder widersprechen sie sich,
  bleibt der Block ganz und zaehlt zur ersten Sprache. Ein halbierter Satz ist schlimmer als ein
  zweisprachiger Block.

**Zweisprachig, aber als einsprachig vermerkt** (0.560.3, `CommentSetService.ResplitLibraryAsync`,
`tools/LibraryImport resplit [--dry-run] [--limit n] [--game id]`). Getrennt wird nur, wenn `Languages` ZWEI
Sprachen nennt — und `CommentLanguage.Detect` nimmt die zweite erst ab 60 % der Treffer der ersten, gezaehlt in
den ersten 4000 Zeichen. Eine ChessBase-Partie, deren Kommentar vorn fast nur Englisch traegt (Zugnummern,
Varianten) und den deutschen Teil erst spaeter, stand damit als `en` da: EIN gemischter Quell-Satz, und die
deutsche Uebersetzung warf die deutsche Haelfte zu Recht weg — und scheiterte an der Laengenpruefung („zu wenig
Text", am 2026-09-27 an Partie 108403 nachgestellt: 46 % deutsche Treffer vorn, 67 % ueber den ganzen Text).
Gemessen an 3 495 englischen Quell-Saetzen auf Prod: einsprachig Englisch liegt fast immer unter 5 %, zweisprachig
zwischen 10 und 100 %. `resplit` zaehlt deshalb ueber den GANZEN Kommentar (`CommentLanguage.Rank`) und nimmt die
zweite Sprache schon ab 20 % (`ResplitMinSecondShare`), verlangt aber, dass die Satz-Trennung sie auch TRAEGT: mindestens
zwei Halbzuege und mindestens halb so viele wie die erste (`ResplitMinCoverage`). Ohne diese zweite Schranke bekaeme
ein englischer Kommentar mit EINEM deutschen Zitat einen winzigen deutschen Quell-Satz — und der sperrte die Partie fuer
die deutsche Uebersetzung. Umgebaut wird nur ohne von Hand gepflegte Fassung (`Human` → `Conflict`): `Languages` auf
„erste,zweite", die Quell-Saetze neu, die maschinellen Saetze in BEIDEN Sprachen weg (sie entstanden aus dem Gemisch).
Die Schwelle von `Detect` selbst bleibt — sie entscheidet beim Einlesen, und dort ist ein falsches „zweisprachig"
teurer als ein verpasstes.

**Gebaut wird beim Einreihen, nicht auf Vorrat** (`CommentSetService.EnsureSourceAsync`, gerufen
aus `GameAnalysisService.CreateAsync` und aus `tools/LibraryImport queue`): der Rohbestand hat
94 898 kommentierte Partien, sie alle zu zerlegen waeren Millionen Zeilen fuer Partien, die nie
jemand spielt. `tools/LibraryImport comments` holt den Altbestand nach; `--probe n` misst die
Trennquote an echten Partien, ohne etwas zu schreiben.

**Ausgeliefert wird ueber `?lang=`** an den Sitzungs-Endpunkten (`GET /api/guess-sessions/{id}`,
`POST` beim Starten und beim Raten). Ohne Satz in der gewuenschten Sprache kommt die Quelle; fehlt
ein EINZELNER Halbzug darin, tritt die Quelle nur fuer diesen ein und die Zeile nennt ihre Sprache
(`GuessHistoryMoveDto.CommentLanguage`) — eine Luecke waere die schlechtere Antwort. Das
Sitzungs-DTO fuehrt `CommentLanguages` (was es gibt) und `CommentLanguage` (was gerade kommt). Der
Altbestand ohne Saetze bekommt seine Kommentare weiterhin direkt aus dem PGN.

Das Brett zeigt die Wahl als Kuerzel neben der Blaetterleiste (nur, wenn es mehr als eine Sprache
gibt); die Wahl merkt sich das Geraet, und beim ersten Mal gilt die Sprache der Oberflaeche.

**Uebersetzen** (`Services/CommentTranslationService.cs`, `tools/LibraryImport translate --to de`)
legt einen weiteren Satz an — `Origin = Machine`, dazu `TranslatedFrom` und das Modell. Vier
Regeln, die dabei nicht kippen duerfen:
* **Die PARTIE ist die Einheit, nicht der Kommentar.** Uebersetzt wird in moeglichst wenigen
  Fuhren (`ChunkChars` 8000), weil Figurennamen und Eroeffnungsbegriffe sonst innerhalb derselben
  Partie wechseln — „Springer" hier, „Pferd" zwei Zuege spaeter.
* **Die Figurenbuchstaben stehen im Auftrag** (de: K D T L S, fr: R D T F C …). Ohne diese Angabe
  wird aus den Zuegen Buchstabensalat.
* **Uebersetzt wird aus der QUELLE**, nie aus einer Uebersetzung, und die Quelle wird nie
  ueberschrieben — auch `--force` ersetzt nur eine MASCHINELLE Fassung.
* **Ein Fehlschlag schreibt gar nichts.** Ein halb uebersetzter Satz waere der schlechtere Zustand:
  er sieht vollstaendig aus.
* **Die LAENGE wird geprueft** (`MinLengthShare` = 0,7). Deutsch ist eher laenger als Englisch;
  liegt das Ergebnis deutlich darunter, fehlt Text. Am 2026-09-11 an echten Partien erlebt: ein
  sparsameres Modell lieferte 18 bis 53 % der Quelllaenge — Saetze mitten im Absatz abgeschnitten,
  waehrend Struktur und Zuege stimmten. Keine der uebrigen Pruefungen schlug an, weil in der Prosa
  keine Zuege stehen. Die Grenze liegt bei 70 % und nicht hoeher, weil manche Quell-Saetze selbst
  zweisprachig sind und die Uebersetzung die doppelte Haelfte zu Recht wegwirft (gemessen 53 %).
* **Kein offenes Zitat** (0.551.1, `CommentTranslator.EndsInsideQuote`, gilt fuer Partien UND Kurse). Das Modell
  oeffnet ein deutsches Zitat mit „ und schliesst es mit dem GERADEN `"` — das beendet in der JSON-Antwort den
  String, die Grammatik laesst danach nur noch das Schliessen zu: der Text endet mitten im Zitat, alle Eintraege
  dahinter fehlen. Am 2026-09-27 auf Prod gemessen: jede fuenfte Kurs-Linie verworfen (fehlende Eintraege), und
  308 von 57 079 Bibliothekstexten mitten im Zitat abgeschnitten GESPEICHERT — bei Partien fing das nur die
  Laengenpruefung, und die nur, wenn viel fehlte. Deterministisch, keine Laune (Linie 54177 dreimal an derselben
  Stelle). Zwei Riegel: der Auftrag verlangt Anfuehrungszeichen, die das Modell NICHT mit dem geraden `"` verwechselt
  (seit 0.555.1 `QuoteNote`: Deutsch »…«, Englisch ‘…’, sonst «…» — mit „…“ im Auftrag schloss Qwen trotzdem gerade:
  an vier Prod-Linien 0 von 4 Versuchen glatt, mit »…« 8 von 8), und
  ein Ergebnis, das in einem offenen „…/«…/“… endet, das die Vorlage NICHT offen hat, ist ein Fehlschlag (die Vorlage
  darf offen sein: Chessable trennt Saetze auch mitten im Zitat auf zwei Zuege auf). Weil das Modell streut (an 54177
  ging einer von zwei Versuchen glatt), bekommt eine Fuhre mit abgeschnittenem Zitat oder fehlenden Eintraegen EINEN
  zweiten Versuch (`MaxAttemptsPerChunk` = 2); ein abgebrochener Aufruf, zu wenig Text und die falsche Sprache nicht.

* **Deutsch duzt den Leser** (0.565.1, `CommentTranslator.AddressNote`, Wunsch 2026-09-27): ohne Vorgabe siezte Qwen fast
  immer („Beachten Sie …", „Finden Sie den Weg") — an acht Prod-Kurslinien 22-mal „Sie" gegen einmal „du"; mit der Zeile
  „Address the reader informally with du …" 0 gegen 18, auch in Aufforderungen. Andere Zielsprachen bekommen (noch)
  keine Vorgabe. Schon gespeicherte Uebersetzungen aendert das nicht — Stand 27.09.: 200 von 3 073
  Bibliotheks-Saetzen und 234 von 1 486 Kurs-Saetzen siezen (Suche nach „Sie/Ihnen/Ihr…" mitten im Satz).

Braucht `Anthropic:TextApiKey` (derselbe Schluessel wie die Puzzle-Tipps — NICHT der Konto-Schluessel
`Anthropic:ApiKey`, der gehoert allein dem Formular-Einlesen). Ohne Schluessel passiert
nichts — der Rest des Stacks laeuft unveraendert. **Das Modell ist ein eigener Schalter**
(`Anthropic:TranslationModel`, Vorgabe `claude-sonnet-5`) und nicht dasselbe wie bei den Tipps:
ein Tipp ist ein Dreizeiler, der jede Sorgfalt wert ist, eine Uebersetzung ist Mengenarbeit —
rund 8000 Zeichen je Partie, und der Bestand hat 94 898 kommentierte.

**Beides geht auch OHNE Analyse** (`comments --library n`, `translate --library n`): der Text
haengt nicht an der Engine, und eine angeforderte Partie steht damit sofort in beiden Sprachen da
statt erst nach einer halben Stunde Rechnen.

**Den GANZEN Bestand uebersetzen** (0.539.0): `translate --to de --library 200000 --parallel 8`
(Env `TextLlm__BaseUrl`/`TextLlm__ApiKey` = Spark, `ConnectionStrings__DefaultConnection`). Die Auswahl
ist `CommentTranslationService.LibraryCandidatesAsync` — kommentiert, weder aussortiert noch Dublette,
ohne Satz in der Zielsprache, die BESTEN zuerst (Note, kommentierte Halbzuege, Textmenge): auf ~95 000
Partien dauert es Tage, und was die Punktepartie zuerst zeigt, soll zuerst fertig sein. Die Quell-Saetze
legt der Lauf je Partie selbst an (`EnsureSourceForLibraryAsync`) — ein vorgeschaltetes `comments
--library` ueber den ganzen Bestand ist nicht noetig. `--parallel p` = p Partien gleichzeitig mit je
eigenem DbContext (vLLM buendelt gleichzeitige Anfragen; eine einzelne nutzt nur einen Bruchteil des
Durchsatzes). Warnungen des Uebersetzers und des Modell-Clients gehen auf die Konsole: „abgebrochen",
„verworfen: n % der Quelllaenge" und „am Token-Deckel abgeschnitten" sind sonst von „nichts zu tun" nicht
zu unterscheiden. Wiederholbar — was die Zielsprache hat, faellt aus der Auswahl.

**KURSE** (Plan „Kurs-Kommentare mehrsprachig", TODO.md — Stufe A 0.547.0 Uebersetzen + Ausliefern, Stufe B 0.548.0
Auftraege + Hintergrunddienst, Stufe C 0.549.0 Oberflaeche — Sprachwahl, Uebersetzungs-Kasten, Labels, siehe
`src/frontend/CLAUDE.md`): je Kurs-LINIE und Sprache ein `CommentSet` mit `BookPuzzleId` (dritter Anker neben
Bibliothekszeile und Analyse, eindeutig `(BookPuzzleId, Language)`, Cascade). Was dabei anders ist als bei Partien:
* **Es gibt KEINEN Quell-Satz.** Die Quelle bleibt die Linie (`Comment`, `MoveComments`, `Title`, `Chapter`) —
  genau diese Felder ueberschreiben Aufbereitung (in-place per oid/LineId) und naechtliches Aktualisieren; ein
  zweiter Quell-Satz muesste staendig nachgezogen werden. Kurs-Saetze sind `Origin = Machine`,
  `TranslatedFrom` = `Book.CommentLanguage` (Quellsprache, beim ersten Lauf aus einer Stichprobe der ersten 200
  kommentierten Linien bestimmt, nicht bestimmbar = `und`; eine spaetere Korrektur macht nichts ungueltig).
* **Stellen** (`Services/CourseTextSlots.cs`, NUR dort die Zahlen): `≥ -1` wie `MoveComments`, `-2` =
  `Comment`, `-3` = `Title`, `-4` = `Chapter`. Ein Satz traegt alles, was die Linie an Text hat.
* **Fingerabdruck** `CommentText.SourceHash` (`Services/CourseTextHash.cs`: 16 Hex-Zeichen SHA-256 ueber den
  normalisierten Text, `\r\n`→`\n`, getrimmt; NIRGENDS selbst hashen) — Pflicht bei Kurs-Saetzen, `null` bei
  Partien. Er entscheidet (1) VERALTET: passt er nicht mehr zum Text der Linie, liefert der Localizer das
  Original und der naechste Lauf uebersetzt nur diesen Text neu; (2) WIEDERVERWENDEN: derselbe Text in einem
  anderen Kurs-Satz gleicher Sprache (Doppel-Import, `_firstkey`-Kopie, gleicher Kapitelname) wird KOPIERT.
* **Uebersetzt wird in `Services/CourseTranslationService.cs`** ueber denselben Kern wie Partien
  (`Services/CommentTranslator.cs`: Fuhren, Auftrag, `PieceLetters`, Laengen- und Sprachpruefung — der Partie-Weg
  ist woertlich unveraendert, `CommentTranslationServiceTests` unangetastet). Je Linie: nur fehlende/veraltete
  Stellen ans Modell, gleiche Texte (Kommentar = Einleitung, bei 51 549 von 87 035 Prod-Linien) nur EINMAL,
  Stellen mit verschwundener Vorlage fliegen raus; das Modell muss JEDEN Eintrag beantworten, sonst schreibt die
  Linie NICHTS. Laenge und Sprache pruefen bei Kursen nur die Prosa-Stellen (`≥ -2`), die Laenge erst ab 150
  Zeichen; der Auftrag nennt die Regeln fuer Partie-Zitate/Namen/Ueberschriften; Quelle `und` → „from the language
  it is written in", Figurenbuchstaben bleiben.
* **Ein Kurs-Lauf** (`TranslateCourseAsync`): Quellsprache sichern (Ziel = Quelle → nichts), offene Arbeit in C#
  bestimmen (Fingerabdruck-Vergleich je Linie), alle dabei fehlenden KAPITELNAMEN in EINER Fuhre (Einheitlichkeit;
  jede Linie traegt ihr Kapitel danach an `-4`; Kapitelnamen bekommen KEINE Laengen- und Sprachpruefung — kurz und
  voller Namen, eine deutsche Liste mit englischen Eroeffnungsnamen laese sich als englisch), dann die Linien in
  Kursreihenfolge, `CourseTranslation:Parallel` (Vorgabe 4) gleichzeitig mit je eigenem Scope/DbContext, in **zwei
  Phasen**, damit jeder verschiedene Text je Lauf genau EINMAL ans Modell geht: Linien mit gemeinsamem Zuganfang
  tragen dieselben Kommentare, liegen nebeneinander und landen gleichzeitig im Parallel-Fenster, die Wiederverwendung
  greift aber erst nach dem Speichern (Prod, Buch 36: 1,90 Mio. Zeichen, davon 0,25 Mio. verschieden — ohne Phasen
  gingen 0,36 Mio. ans Modell). Phase 1: jeder offene Fingerabdruck GEHOERT der ersten Linie, in der er offen ist,
  jede Linie uebersetzt nur ihre eigenen (`ownedHashes`); Phase 2: alle noch offenen Linien normal, praktisch nur
  Wiederverwendung — ans Modell nur noch, wo die Besitzer-Linie scheiterte. Eine Linie zaehlt, sobald sie fertig ist
  (am Ende `LinesDone + LinesFailed = LinesTotal`), Zwischenstand alle 10 Linien an einen Rueckruf. Abbruch per
  Token: die laufenden Linien schreiben nichts, der naechste Lauf ueberspringt das Fertige.
* **Von Hand**: `tools/LibraryImport translate --to de --course <bookId> [--parallel p]` (Env wie beim
  Bibliothekslauf: `TextLlm__*`, `ConnectionStrings__DefaultConnection`; Strg+C bricht sauber ab) — ohne Auftrag,
  an der Warteschlange vorbei.
* **Auftraege** (0.548.0, `Services/CourseTranslationJobService.cs`, Tabelle `CourseTranslationJobs`, Endpunkte unter
  „Kurse"): anfordern darf jeder mit Kurs-Zugang, in jede der 25 Oberflaechensprachen
  (`Services/CourseTranslationLanguages.cs` — Spiegel von `SUPPORTED_LANGS`, beide Seiten mit LITERALER Liste im
  Test), **hoechstens EIN offener angeforderter Auftrag je Nutzer** (wartend oder laufend), Admin unbegrenzt. Gibt es
  fuer (Kurs, Sprache) schon einen offenen, kommt DER zurueck (200, egal wer ihn anlegte, zaehlt nicht gegen das
  Limit). Beides erzwingt der Dienst, nicht die DB. Reihenfolge der Pruefungen: Zugang (404) → Sprache
  (`unsupported-language`) → Modell (`not-configured`, 503) → Quellsprache (`same-language`, dafuer wird sie hier
  bestimmt) → vorhandener Auftrag → offene Arbeit (`nothing-to-translate`, ueber `OpenWorkAsync`, `LinesTotal` =
  offene Linien) → Limit (`user-limit`, 409, mit dem offenen Auftrag). **In der Sperrzeit angenommen, kein 503.**
  Zurueckziehen: der Nutzer seinen WARTENDEN, der Admin jeden offenen (`Cancelled`). **Konto loeschen** setzt die
  offenen Auftraege des Nutzers auf `Cancelled` („account deleted") — `RequestedByUserId` hat bewusst keinen FK.
* **Hintergrunddienst** (`Services/CourseTranslationWorker.cs`, EIN Auftrag gleichzeitig — die Parallelitaet steckt
  im Lauf): kein Text-Modell → schlafen (10 min); Sperrzeit (`QuietHours`) → schlafen bis `EndOf`, hoechstens 10 min
  am Stueck; sonst `ClaimNextAsync` — **angeforderte vor der Automatik, je Gruppe die aeltesten** (`InQueueOrder`, EINE
  Stelle fuer Dienst und Platzanzeige). Ohne Arbeit wartet er auf einen Weckruf (`CourseTranslationSignal`: Anfordern,
  Nachziehen), hoechstens 5 min. Beim Start `Running` → `Queued`. **Abbruch mitten im Lauf** ueber einen eigenen Token:
  Sperrzeit beginnt (alle 30 s geprueft) oder ein NEU angeforderter Auftrag kommt, waehrend AUTOMATIK laeuft
  (`PreemptAutomatic` — ein grosser Kurs rechnet sonst einen halben Tag, und „Vorrang" hiesse nur „danach") → zurueck
  auf `Queued`; Admin zieht zurueck → bleibt `Cancelled`; Dienst stoppt → bleibt `Running`, kommt beim Start zurueck.
  Der Fortschritt landet je Zwischenstand (zu Beginn, dann alle 10 Linien) in einem EIGENEN Kontext am Auftrag —
  nur die Zaehler; steht er dabei nicht mehr auf `Running` (zurueckgezogen, Konto geloescht), bricht der Lauf ab.
  Ergebnis: `Done` (auch mit einzelnen gescheiterten Linien, `LastError` nennt sie), `Failed` (keine einzige Linie
  ging durch), `Cancelled` + `same-language` (Quellsprache inzwischen = Ziel).
* **Automatik** (`CourseTranslation:AutoLanguages`, Compose `COURSE_TRANSLATION_AUTO_LANGUAGES`, Vorgabe LEER = aus; nur
  Prod bekommt `de,en`, sonst uebersetzte Dev dieselben Kurse ein zweites Mal auf der Spark): wartet nichts, legt der
  Dienst EINEN Auftrag fuer den naechsten Kurs an, dem die Sprache fehlt — zuletzt benutzte Kurse zuerst (juengster
  `CourseAttempt`), ueber die Sprachen hinweg (der frischeste Kurs bekommt de UND en vor dem naechsten), Kurse in der
  Quellsprache nicht. Vorauswahl in SQL („Linie mit Text ohne Satz in der Sprache"), den Fingerabdruck prueft der
  Lauf. **Sperrfrist `AutoCooldown` (7 Tage)** nach einem fertigen Auftrag der Sprache — sonst holte eine Linie, die nie
  einen Satz bekommt, die Automatik in eine Schleife. **Modell nicht erreichbar (0.624.1):** scheitert eine Linie,
  während `IsUnreachable` wahr ist, bricht `TranslateCourseAsync` den Lauf ab (`CourseTranslationRunStatus.Unreachable`),
  der Auftrag geht mit `LastError = "model unreachable"` und OHNE gescheiterte Linien zurück auf `Queued`, und der Dienst
  wartet `UnreachablePoll` (5 min) statt sofort den nächsten zu nehmen. Anlass 30.09.2026: zehn Minuten „Connection
  refused" — Auftrag 53 verbuchte 252 von 264 Linien als gescheitert, galt als fertig, und die Sperrfrist hielt den Kurs
  sieben Tage halb übersetzt.
* **Nachziehen** (`EnqueueRefreshAsync`/`NotifyCourseChangedAsync`): aendert sich ein Kurs mit Uebersetzungen — der
  Import-Kern `PgnImportService.ImportIntoBookAsync` (Aktualisieren, Neu-Aufbereiten, Cache-Weg, angehaengte Linien,
  auch nur geaenderte Etiketten), `CourseAuthoringService` (Kapitel umbenennen, Linien einfuegen) —, bekommt jede
  Sprache mit Saetzen einen Automatik-Auftrag (erledigt nur Veraltetes). Ein WARTENDER genuegt, ein LAUFENDER nicht
  (er hat seine Arbeit vorher bestimmt). Ein Fehler dabei laesst die Aenderung selbst nicht scheitern.
* **Vorrang vor dem Bibliothekslauf** liegt AUSSERHALB der App: `.jobs/spark-uebersetzung.sh` haelt an, solange ein
  angeforderter Auftrag offen ist (`Status IN (0,1) AND RequestedByUserId IS NOT NULL`).
* **Ausliefern ueber `?lang=`** (`Services/CourseCommentLocalizer.cs`, in den Controllern NACH dem Dienst): ersetzt
  `Comment` und `MoveComments[ply]` NUR, wo der Fingerabdruck zum Original IM DTO passt. **`Title`/`Chapter` werden
  NIE ersetzt** — der Kapitelname ist im Frontend ein SCHLUESSEL (`?chapter=`, Kapitel-PGN, Umbenennen,
  Gruppieren); die Uebersetzung kommt als `TitleLabel`/`ChapterLabel` (bzw. `Label` an den Kapitellisten), Anzeige
  `label ?? original`. Dazu `CommentLanguage` (was tatsaechlich kommt), `CommentLanguages` (was es fuer die Linie
  gibt, **Quelle zuerst**) und `CommentMachine`. **`lang` fehlt oder ist kein Kuerzel → DTO exakt wie vorher**
  (keine Abfrage — alte Clients, Offline-Kopien); `lang` = Quellsprache → Original. Endpunkte mit `lang` (per grep
  erhoben): `GET /api/courses/{id}/puzzles`, `/{id}/public`, `/{id}/next`, `/{id}/chapters`, `GET /api/courses/{id}`
  (Detail, Kapitel-Labels), `GET /api/book-puzzles/{id}`, `/{id}/next`, `/{id}/random`,
  `GET /api/calculations/books/{id}`, `/books/{id}/public`, `/positions/{id}`. **Bewusst Original**: Bearbeiten
  (`/{id}/lines`), alle PGN-Downloads, Tagespuzzle, Zufallspools (`/api/book-puzzles/random`), Wochenpost, Tipps,
  Kurs→Repertoire. `/{id}/flashcards` liefert nur Linien-Ids (nichts zu uebersetzen).
* **Loeschen**: jeder Pfad, der `BookPuzzles` loescht (`CourseAuthoringService.RemoveLinesAsync` = Linie/Kapitel,
  `BookAdminService.DeleteBookAsync` = Buch/eigener Kurs/Kurs→Repertoire/Konto, der Rueckbau in
  `CourseService.UploadPersonalCourseAsync`), raeumt die Kurs-Saetze AUSDRUECKLICH mit ab
  (`CourseTranslationCleanup`), das Buch zusaetzlich seine `CourseTranslationJobs`. Die TEXTE uebernimmt in MariaDB
  der Fremdschluessel (sie zu laden hiesse, jede Uebersetzung zu lesen und einzeln zu loeschen), unter InMemory
  werden sie ausdruecklich geloescht.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/guess-sessions` | Auth | Eigene Durchlaeufe (max. 100, neueste zuerst) |
| POST | `/api/guess-sessions` | Auth | Starten `{ gameAnalysisId, guessWhite?, startPly? }` — `guessWhite` weglassen = Seite des Gewinners |
| GET | `/api/guess-sessions/{id}` | Auth | Zustand: Kopf, Punkte, Verlauf bis hierhin, aktuelle Stellung — **ohne** den zu ratenden Zug |
| POST | `/api/guess-sessions/{id}/guess` | Auth | Raten `{ uci?, addSeconds? }`; leeres `uci` = passen (0 Punkte, keine Strafe). HIER kommt der Partiezug zum ersten Mal mit |
| GET | `/api/guess-sessions/{id}/review` | Auth | Rueckblick: je Halbzug Partiezug, eigener Zug, Stufe, Engine-Bestzug |
| DELETE | `/api/guess-sessions/{id}` | Auth | Durchlauf loeschen |
| GET/POST/DELETE | `/api/guess-sessions/anonymous[/{id}[/guess\|review]]` | **AllowAnonymous** + RL | Dieselben sechs Operationen ohne Konto; Kennung als `?sessionId=` (GET/DELETE) bzw. im Rumpf (POST). Ungueltige Kennung → 400 |
| GET | `/api/game-analyses/public` | **AllowAnonymous** + RL | Kuratierter Bestand: freigegebene UND spielbare Partien (mind. eine gerechnete Stellung), mit `annotated` fuer den Filter „alle / nur kommentierte". Kopfdaten und Fortschritt, **nicht** die Zugliste |
| PUT | `/api/game-analyses/{id}/public` | Auth | Partie in den Bestand aufnehmen/herausnehmen `{ isPublic }` — Besitzer der Analyse oder Admin |
| POST | `/api/game-analyses/guess` | Auth | Eigene Partie einwerfen `{ pgn, title? }` — KEINE Tiefe/Linien/Engine im Rumpf (Server setzt Tiefe 20). 400 mit `reason` ∈ `too-many-open` / `no-engine` / `invalid-pgn`; die Seite formuliert den Satz, der Server kennt die Sprache nicht |
| GET | `/api/game-analyses/throughput` | Auth | Tempo und Restdauer der eigenen Analysen AUS DER HISTORIE (`GameAnalysisPosition.AnalyzedAt`): Stellungen je Minute, gemessene Spanne, Rest, hochgerechnete Restdauer. Vorher zaehlte der Browser selbst mit — eine Minute offene Seite, bevor ueberhaupt etwas dastand, und beim naechsten Aufruf wieder bei null. Seit 0.521.2 nach `Services/AnalysisPace.cs`: die juengsten ≤ 200 Ergebnisse der letzten 24 h, davon nur der juengste ZUSAMMENHAENGENDE Lauf (eine Luecke > 15 min = Pause, zaehlt nicht), gemessen bis jetzt, solange der Lauf lebt, sonst bis zum letzten Ergebnis. Vorher: letzte Stunde ab dem ersten Zeitstempel darin — eine Pause davor zaehlte als Rechenzeit („0,56 Stellungen/min · noch ca. 1 h 44 min" fuer 58 offene Stellungen). Dieselbe Regel rechnet die Restdauer einer Partie (`GameEvals.EtaMinutes`). Literal-Route vor `{id:int}` |
| GET | `/api/game-analyses/guess/status` | Auth | Steht eine Engine bereit (eigene oder Haus) und wie viele der fuenf Plaetze sind frei — gefragt, BEVOR jemand ein PGN hineinkopiert |
| PUT | `/api/engine/house` | Admin | Eigene Hintergrund-Engines als Haus-Engine freigeben `{ share }` (403 ohne Admin, 400 ohne Token bzw. ohne hinterlegte Hintergrund-Engine) |

`annotated` wird IN SQL ermittelt (`Pgn LIKE '%{%'`): jede geschweifte Klammer in einem PGN ist ein
Kommentar, und so bleibt das LONGTEXT-Feld ausserhalb der Antwort. Menue-Key `guess`, Stufe **All**;
die Route traegt entsprechend keinen `authGuard` mehr.

### Partieformular einlesen (auth, 0.529.0)

Foto eines handgeschriebenen Partieformulars → legale Partie in „Meine Partien" (Quelle `scoresheet`), das Foto
bleibt daneben liegen. Menü-Key `scoresheet` (Stufe `Registered`), Frontend `/games/scoresheet` (Upload) und
`/games/:id/edit` (Korrekturseite, für JEDE eigene Partie). Braucht `Anthropic:ApiKey` — und der gehört seit 2026-09-25
ALLEIN diesem Feature (Tipps/Übersetzung laufen über `Anthropic:TextApiKey`); ohne Schlüssel antwortet der Upload 503 `notConfigured` und die Seite sagt es.

**Datenschutzerklärung** (Codereview 2026-09-29, A6-008): der Abschnitt „KI-Dienste" nennt Anthropic als Empfänger der
Formular-Fotos und den Sprachmodell-Server (Spark) als Empfänger der Texte (nur als Kategorie). Rechtstext bleibt
ENTWURF: Betreiber namentlich nennen und AV-Rolle klären entscheidet der Betreiber. LeagueHub-Einlesungen haben
keinen Aufräumlauf — das Foto geht nur beim Übernehmen oder Verwerfen.

**Drei Schichten, jede für sich testbar:**
* **Lesen** (`ClaudeScoresheetVisionClient`, Modell `Anthropic:ScoresheetModel`, Vorgabe seit 0.533.2 `claude-opus-5-5`
  im Modus „nur abschreiben" — siehe **Vorgabe** unten; mit `Scoresheet:Thinking=true` adaptives Nachdenken; gestreamt, structured output nach `ScoresheetPrompt.Schema`): je Halbzug, was DASTEHT (`written`, in der
  Sprache des Formulars), die Lesart des Modells als englische SAN, bis zu drei Ersatz-Lesarten und eine Sicherheit.
  Das Modell soll zuerst das GANZE Formular lesen und die Partie im Kopf mitspielen; Korrekturen (Streichungen,
  Pfeile, übersprungene Zeilen) löst es selbst und nummeriert neu. Bild vorher aufrecht (EXIF) und auf 2000 px
  (`ScoresheetImage`).
* **Auflösen** (`ScoresheetResolver`, rein, ohne Modell): macht daraus die LEGALE Partie, die das ganze Formular am
  besten erklärt — Strahlsuche über 32 Stellungen statt einer Entscheidung je Zug. Kosten je Halbzug: Lesart des
  Modells 0, Eintrag in seiner Sprache 0,2 (`ScoresheetNotation`: 18 Sprachen, Groß/klein egal, Langschrift,
  Figurinen, jede Lesart ist nur ein KANDIDAT — die Stellung entscheidet), Ersatz-Lesart 1, fehlender Zusatz
  („Sd2" für Sbd2) +0,3, Lesefehler 2,5 je Zeichen, Joker 8. **Der Joker braucht eine Bestätigung** durch den
  nächsten Eintrag (sonst „löst" die Suche jeden Unsinn auf), höchstens 1 + einer je 15 Einträge, und bei einer
  Sackgasse geht die Suche bis zu drei Einträge zurück. **Unsichere Stellen** bekommen die drei wahrscheinlichsten
  Lesarten, jede bis zu 60 Einträge weitergespielt; gemessen wird die GLATTE Reichweite (bis zum ersten Lesefehler
  oder Joker — mit genug Reparaturen käme fast jede falsche Lesart bis zum Ende). **Verschobene Formulare**
  (0.530.0): passt an einem Eintrag nichts glatt, darf ein Zug EINGESCHOBEN werden, der auf dem Formular fehlt
  (Kosten 5, bestätigt nur, wenn der Eintrag danach glatt passt; vorsortiert nach dem übernächsten Eintrag, höchstens
  6 je Zustand), oder ein Eintrag ÜBERSPRUNGEN — aber nur ein DOPPELT notierter (sonst wäre Überspringen der
  billigste Weg, Unleserliches loszuwerden, und die Lesung ließe still echte Züge weg). Solche Wege bekommen drei
  Einträge lang reservierte Plätze im Strahl, sonst verdrängen sie die vielen billigen Lesefehler-Varianten. Weitere
  Regeln: ein KLEINER Buchstabe a–h vorn ist eher Bauer als Figur (+0,3), in Handschrift verwechselbare Zeichen (6/8,
  1/7, a/d …) machen einen Lesefehler um 0,5 billiger, ein gestrichenes/dazugedichtetes Zeichen zählt 1,2 statt 1;
  Gleichstände werden fest nach Stellung/Zug entschieden (die Zugreihenfolge von Gera.Chess ist kein Vertrag).
  **Eintrag vor Deutung** (0.531.0): der Formular-Eintrag kostet 0, die SAN-Deutung des Modells 0,1 — sind beide legal und
  verschieden, gewinnt was DASTEHT (das Modell „korrigiert" sonst richtige Einträge, deren Legalität es falsch einschätzt;
  ist der Eintrag illegal, trägt die Deutung). **Markiert** wird zusätzlich: Sicherheit „low", eine vom Modell genannte
  Alternative, die ein ANDERER legaler Zug ist (auch bei „high"), und ein Widerspruch Eintrag/Deutung zwischen gleich
  direkten Lesarten. „medium" allein markiert NICHT — das Modell vergibt es freigiebig (am Testsatz 13 % aller Züge).
  **Kurzschrift beim Schlagen** (0.555.0, gemeldet 2026-09-27): „LxS", „SxB", „DxD", „exd" — schlagende Figur (bzw.
  Linie des Bauern) und geschlagene Figur (bzw. Ziellinie) OHNE Zielfeld, die alte Schreibweise und die von Anfängern
  und Kindern. `ScoresheetNotation.ShortCaptures` liest sie (Figurenbuchstaben wie sonst, der geschlagene Bauer heißt
  deutsch/skandinavisch „B", sonst „P"; gemischte Schrift mit englischem „B" für den Läufer läuft über die Fremdsprache
  mit), `ScoresheetResolver.Score` prüft sie gegen das Brett VOR dem Zug (`Squares`/`CaptureOf`: welche Figur zieht,
  welche steht auf dem Zielfeld, en passant) — Kosten 0,2 (Eintrag) bzw. 0,3 (Deutung des Modells), ein zweiter
  passender Schlagzug bleibt als ebenbürtige Lesart markiert. Anlass: Opus 5.5 „nur abschreiben" kopiert „BxB" wörtlich,
  der Auflöser machte daraus „b3" (Lesefehler-Weg) → falsche Partie ab Zug 6 mit 17 Markierungen. Gemessen per
  `--replay` (kostenlos): das gemeldete Formular 29–31 → 50/50 (drei gespeicherte Lesungen, 2–4 statt 17–22 unsicher),
  10er-Testsatz mit der Referenz-Lesung 550 → 579/591 (Beleg 10: 59 → 88/92), Opus-5-mit-Nachdenken- und
  Opus-5.5-effort-low-Lesungen unverändert.
  Unsicher bleibt eine Stelle nur,
  wenn der Zug zurechtgebogen ist, das Modell zweifelte oder eine andere Lesart gleich weit trägt, ohne teurer zu
  sein; eine Zugumstellung (17. Sbd4/Sfd4 Sxd4 18. Sxd4) gilt als ebenbürtig. Anlass-Partie als Test:
  `ScoresheetResolverTests` rekonstruiert alle 66 Halbzüge allein aus den deutschen Einträgen.
* **Ablauf** (`ScoresheetScanService` + `ScoresheetScanWorker`): der Upload legt einen `ScoresheetScan` an
  (Foto in der DB, `LONGBLOB`, über 12 MB auf 3000 px verkleinert), der Worker liest im Hintergrund — DB-gestützt,
  eigener Weckruf (`ScoresheetScanSignal`), NICHT die Arbeitsspeicher-Queue (Watchtower-Neustart, Chessable-Import).
  Beim Start kommen `Running` zurück auf `Pending`, nach `MaxAttempts` (3) ist Schluss. Geht die Lesung irgendwo
  nicht auf, fragt der Dienst NACH (`ScoresheetPrompt.Repair`: akzeptierte Züge, FEN, legale Züge, „oft ist ein
  FRÜHERER Eintrag falsch"), höchstens drei Durchgänge; behalten wird die beste Lesung (weitester Weg, dann die
  wenigsten Unsicherheiten). Im PGN stehen `{sheet: Qxd4}` an zurechtgebogenen Zügen und
  `{sheet, not resolved: …}` am letzten Zug.
* **Festgedacht → ohne Nachdenken abschreiben** (0.532.1, `ScoresheetReadMode`): am 25.09. auf Prod dachte Claude an
  einem vollen, verbesserten 60-Zug-Formular (Kufstein) 14 Minuten und 64 000 Tokens nach und schrieb kein Zeichen
  Antwort — 1,63 $, gescheitert mit `truncated`. Seither: ein Aufruf MIT Nachdenken hat höchstens
  `ScoresheetReader.FullCallMaxTokens` (40 000; am Testsatz brauchte die längste Lesung 23 142), und wird er
  abgeschnitten, liest der nächste Durchgang denselben Auftrag OHNE Nachdenken (`ThinkingConfigDisabled` +
  `ScoresheetPrompt.TranscribeSystem`, Deckel `TranscribeCallMaxTokens` 32 000) — und bleibt dabei, auch für die
  Nachfragen. Der Laufzeit-Deckel des Workers (`ScoresheetScanWorker.MaxRuntime`) steht auf 40 statt 15 Minuten: die
  15 reichten gerade für EINEN Aufruf. Reißt er trotzdem, ist die Einlesung `timeout` (Glocke), und der laufende Aufruf
  wird mit seinem ungünstigsten Fall verbucht (Reserve-Eingabe + sein Deckel) — die API meldet dann keine Tokens mehr.
  Nur das HERUNTERFAHREN lässt eine Einlesung auf `Running` (`ProcessAsync(…, shutdown)`), sie kommt beim Start zurück.
  Beim Gegenlesen fiel ein zweiter Fehler auf: `AddBranches` hörte nach `MaxBranchPoints` (40) GANZ auf — bei einer
  langen Lesung mit fast nur „medium" war das Budget nach 28 Zügen verbraucht, und jeder spätere zurechtgebogene Zug
  stand da wie ein sicherer (Kufstein: 42 repariert, 14 markiert). Jetzt wird über das Budget hinaus weiter MARKIERT
  (zurechtgebogen oder „low"), nur ohne Lesarten. Am 10er-Testsatz unverändert (578/591, dieselben Markierungen).
* **Ohne Nachdenken von Anfang an** (0.533.0, `Scoresheet:Thinking=false`, seit 0.533.2 die VORGABE): der erste Durchgang liest
  schon im Modus `Transcribe` (Deckel `TranscribeCallMaxTokens`), die Nachfragen ebenso. Gedacht für ein kleineres
  Modell (`Anthropic:ScoresheetModel` = Haiku — dessen adaptives Nachdenken ist nicht geprüft, daher nur so) oder wenn
  das Nachdenken sein Geld nicht wert ist. Wer das Modell wechselt, stellt die Preise der Kostenbremse mit um.
  Im Testwerkzeug seit 0.533.2 ebenfalls Vorgabe (`--thinking` schaltet es ein), `--usd-per-mtok ein,aus` (Vorgabe nach
  Modell: `claude-haiku-*` 1,5; `claude-sonnet-*` 2,10; `claude-opus-5-5` 4,20; sonst 5,25).
  **Gemessen 25.09.** (10er-Testsatz, 591 Halbzüge): Opus 5 MIT Nachdenken 578 (97,8 %, ~0,37 $ und 1–4½ min je
  Formular, 6 falsch ohne Marke); Opus 5 OHNE Nachdenken 563 (95,3 %, 0,13 $ und ~37 s je Formular, 10 falsch ohne
  Marke); **Haiku 4.5 ohne Nachdenken 208 (35,2 %, 0,025 $, 160 falsch ohne Marke) — unbrauchbar**: am Kufstein-Formular
  erfand Haiku eine völlig andere, LEGALE Partie (2…Dd6 3…c5 statt 2…Sf6 3…Sxd5), die kaum Reparaturen braucht und
  deshalb sicher aussieht. Eine glatte Auflösung ist also KEIN Beleg für eine richtige Lesung.
* **Denkaufwand** (0.533.2, `Anthropic:ScoresheetEffort` = low|medium|high|xhigh|max, leer = Vorgabe des Modells;
  Testwerkzeug `--effort`). `ClaudeScoresheetVisionClient.PlanThinking` ist die EINE Regel je Modell und Modus: „nur
  abschreiben" schaltet das Nachdenken ab — außer bei Modellen, die das nicht erlauben (`claude-opus-5-5`, Fable/Mythos:
  Nachdenken immer an), dort `effort: low`; Opus 5 verbietet Abschalten zusammen mit `xhigh`/`max`, der effort fällt dann
  weg. Ohne diese Regel liefe der Rückfall unter Opus 5.5 in einen 400. Der OpenAI-kompatible Leser schickt beim
  Abschreiben `chat_template_kwargs: {enable_thinking: false}` und `reasoning_effort: low` mit (Qwen3/Qwen3.5 denken
  sonst über die Chat-Vorlage; gpt-oss hört nur auf den zweiten Schalter, 0.597.2).
  Er STREAMT (`stream: true`, `include_usage`): vor dem Spark kappt ein Reverse-Proxy (openresty) jede Anfrage nach
  90 s ohne Antwort mit 504, eine Formular-Lesung dauert dort Minuten.
* **Vorgabe = Opus 5.5 „nur abschreiben"** (0.533.2, Wunsch des Nutzers nach dem Modellvergleich vom 25.09.2026):
  `Anthropic:ScoresheetModel` = `claude-opus-5-5`, `Scoresheet:Thinking` aus (→ `effort: low` + `TranscribeSystem`),
  Preise der Kostenbremse 4 $ / 20 $. Gemessen am 10er-Testsatz: 550/591 (93,1 %), 7 falsch ohne Marke, 0,08 $ und
  ~25 s je Formular (Ausreißer Formular 10: 59/92); am Kufstein-Formular die einzige schlüssige Lesung (120 Halbzüge,
  10 Reparaturen; Opus 5 ohne Nachdenken 52, mit Nachdenken festgedacht). Weitere Zeilen des Vergleichs: Opus 5.5
  `effort: low` mit vollem Auftrag 95,6 % (am Kufstein-Formular nach 13 Zügen aufgegeben), Sonnet 5 93,4 % ohne /
  94,5 % mit Nachdenken (8 Formulare, so teuer wie Opus 5), Qwen3.5-122B auf dem Spark rund 70 % (verrutscht bei
  unordentlichen Formularen). Diese Konfiguration ist die REFERENZ für künftige Vergleiche; zurück zu Opus 5 mit
  Nachdenken: `Anthropic:ScoresheetModel=claude-opus-5`, `Scoresheet:Thinking=true`, Preise 5 / 25.

**Lesen von außen statt über den Schlüssel** (0.687.0, Wunsch 2026-10-06: „der Watcher soll das bisherige Verarbeiten via
Key ersetzen — Key ganz abschalten"; `Scoresheet:Reader`, in `appsettings.json` auf `external`, Rückfall ohne Angabe =
`model` wie bisher — so laufen die Unit-Tests). Mit `external` liest der `ScoresheetScanWorker` nichts mehr: jede
Einlesung (RookHub UND LeagueHub, mit Konto oder über einen Teilen-Link) bleibt `pending`, bis der Watcher auf dem Server
(`~/claude/formulare-bot/watch.sh`, Claude in einer eingeschränkten Sitzung) sie über `/api/admin/scoresheets` liest und
die Lesung zurückgibt. `ProcessReadingAsync` nimmt sie in der Form der Modell-Antwort (Kästen in Pixeln des aufrechten,
auf 2000 px verkleinerten Fotos) und geht danach denselben Weg wie eine gelesene (`FinishAsync`: Auflösung,
Engine-Prüfung, Liga → offen zum Prüfen, eigene → Partie + Glocke); `Model = "claude-manual"`. Der Schlüssel ist
dabei nicht nötig (`Available` gilt auch ohne), und Tageszahl/Anzahl offener gelten weiter, die Kostenbremse nicht
(keine Kosten). Die Seite „etwa 1–2 Sekunden pro Zug" stimmt in diesem Modus nicht — der Watcher schaut alle 5 min.

**Kostenbremse** (`ScoresheetBudget`, gewünscht 2026-09-25: „nicht dass einer mein Konto leerräumt"): gerechnet in
GELD, nicht in Einlesungen — eine Einlesung mit zwei Nachfragen kostet das Dreifache. Jeder Aufruf verbucht SOFORT
die Tokens, die die API meldet (`InputTokens`/`OutputTokens`/`CostMicroUsd` an der Einlesung; Nachdenken zählt als
Ausgabe, abgebrochene und abgeschnittene Aufrufe zählen mit). Drei Budgets, alle als Konfiguration mit Vorgabe:
`Scoresheet:UserDailyUsd` (2), `Scoresheet:UserMonthlyUsd` (10, über 30 Tage), `Scoresheet:GlobalDailyUsd` (15, alle
Nutzer zusammen — schützt das Konto auch bei vielen Nutzern), dazu `Scoresheet:AnonDailyUsd` (3, ALLE Einlesungen
ohne Konto zusammen — zehrt zusätzlich vom Gesamtbudget, der Rest bleibt den Konten; Sperrgrund bleibt
`globalBudget`, Codereview A6-003); Preise `Scoresheet:InputUsdPerMTok` (4) /
`Scoresheet:OutputUsdPerMTok` (20) = Claude Opus 5.5 (bis 0.533.1: 5 / 25 = Opus 5) — wer das Modell wechselt, stellt sie mit um. **Der
Antwort-Deckel (max_tokens) kommt aus dem verbleibenden Budget** (`ScoresheetBudget.Allowance`, seit 0.531.0): so viel
Ausgabe, wie nach 12 000 Eingabe-Tokens Reserve noch bezahlbar ist, höchstens 64 000 (je Aufruf mit Nachdenken seit
0.532.1 höchstens 40 000, siehe oben); unter 16 000 startet der Aufruf
nicht — so wird kein Budget überzogen, und lange Partien bekommen trotzdem Platz (ein fester Deckel von 24 000 schnitt
am Testsatz die 92-Halbzug-Partie nach 0,63 $ ab). Der Stoppgrund im Stream wird über Gleichheit mit der API-Zeichenkette
geprüft (`reason == "max_tokens"`), NICHT über `ToString()` — das meldete ein abgeschnittenes Ende als „failed". Geprüft beim Upload (Absage 400 `userDailyBudget`/`userMonthlyBudget`/
`globalBudget`) und vor JEDEM Aufruf im Worker: vor der ersten Lesung scheitert die Einlesung mit dem Grund, vor
einer Nachfrage entfallen nur die Nachfragen (die Lesung bis dahin bleibt). Admins: keine Nutzerbudgets und keine
Tageszahl, das Gesamtbudget gilt auch für sie. Dazu weiter `Scoresheet:DailyLimit` — seit 0.568.1 **EINE Einlesung je
24 h** (Wunsch 2026-09-27, vorher 20; rollendes Fenster) — und 3 offene je Nutzer. Mit einer am Tag hängt viel an der
Zählregel (`ScoresheetScanService.CountingSince`): eine GESCHEITERTE Einlesung ohne Kosten zählt nicht (sonst sperrte
ein Ausfall auf unserer Seite 24 h), eine bezahlte schon. **Partie löschen ist kein zweiter Versuch**: die Einlesung
bleibt als Zeile stehen, nur Foto, Dateiname, Modell-Antwort und Stand gehen mit der Partie
(`DetachWithoutLoading`; vorher ging die Zeile samt Verbrauch — löschen + neu hochladen umging Tageszahl UND Budget).
Solche Zeilen stehen nicht in `GET /api/scoresheets`. Das UPDATE läuft im selben SaveChanges VOR dem DELETE der Partie
(sonst nähme der Cascade-Fremdschlüssel die Zeile mit) — `ScoresheetScanSqlTests` prüft das gegen MariaDB. Die
Upload-Seite zeigt den Verbrauch in Prozent (`budgetUsedPercent`, das knappere Budget) und bei erreichter Tageszahl,
ab wann die nächste geht (`nextAllowedAt`).

**Engine-Prüfung der Lesung** (0.646.0, `Services/ScoresheetPlausibility.cs`, gemeldet 2026-10-03 an Gruber–Schöler,
LeagueHub-Vereinspartie 153: „viele Zickzack in der Bewertung — meist ein Zeichen, dass ein Zug nicht richtig erkannt wurde").
Das Modell hatte „Tc1" als „Te1" und „Ke4" als „Kc4" gelesen — beides legal, also nahm der Auflöser es, und ab dort ließ
Schwarz zehn Züge lang `fxe1=D+` liegen. Der Auflöser kennt nur Schrift und Legalität; nach dem Lesen (`ProcessAsync`, BEIDE
Wege — „Meine Partien" und Liga) bewertet deshalb Stockfish die Partie (`IScoresheetEngine`, Singleton
`StockfishScoresheetEngine`: EIN Prozess je Prüfung, `go depth 10`, ~7 ms je Stellung). Regeln, die nicht kippen dürfen:
* **Zickzack** = ein Halbzug verliert ≥ `SwingPct` (15) Prozentpunkte GEWINNCHANCE (Lichess-Formel `GameAccuracy.WinPercent`,
  nicht Centibauern — ein verpasstes Matt in entschiedener Stellung zählt sonst), und der nächste ebenso. Gesucht wird an den
  `Window` (4) Halbzügen bis einschließlich des ersten Patzers (bei „Kc4" kam er zwei Halbzüge später).
* **Ersatz-Lesarten nur um EIN leicht verwechselbares Zeichen** (`MaxExtraCost` 2,0 über der gewählten — c/e, 1/7, 5/6 aus
  `ScoresheetNotation.IsConfusable`; ein beliebiges Zeichen 2,5 nur, wenn das Modell den Eintrag selbst „low" las). Gemessen am
  10er-Testsatz (perfekte Lesung und Abschrift, 20 Durchläufe): mit 2,5 standen 13 richtige Züge als unsicher da (echte
  Amateur-Patzer, die eine beliebige Ein-Zeichen-Lesart „glättet"), mit 2,0 drei. Der Rest wird je Lesart über ein Fenster neu
  gelesen (`ScoresheetResolver.Resolve(…, writtenTo:)`, neu: liest nur bis zu einem Eintrag) und neu bewertet.
* **Zwei Stufen**: ERSETZT (`ScoresheetPly.Check = "replaced"`, unsicher, alte Lesung als zweite Option) nur, wenn das Zickzack
  ≥ `PersistentPlies` (4) Halbzüge anhält, genau EINE Stelle es am besten beseitigt (Gleichstand an mehreren Halbzügen = die
  Engine kann nicht unterscheiden — „Ra6" statt „Ra5+" glättet „Kc4" genauso wie „Ke4"), es ≥ `MinReplaceGain` (2) Zickzacks
  beseitigt UND die volle Neuauflösung nicht mehr zurechtgebogene Züge und nicht mehr Unaufgelöstes hat (am Testsatz, Beleg 04,
  glättete „Lf3+" statt „Lxf5" das Zickzack und bog dafür elf Züge zurecht). Sonst nur ANGEBOTEN (`Check = "suggested"`: der
  gelesene Zug bleibt, unsicher, die Lesart ohne Zickzack als zweite Option; bei Gleichstand bis zu drei Stellen).
* **Messung** (Prüfwerkzeug im Scratchpad, nicht im Repo): Gruber nachgestellt (Soll-Partie + „Te1"/„Kc4" eingestreut) → Te1
  ersetzt, Ke4 angeboten, 3 s; 10er-Testsatz → 0 Ersetzungen, 3 Markierungen an richtigen Zügen; 80 zufällig eingestreute
  Ein-Zeichen-Fehllesungen → 0 Schäden, 3 richtig angeboten — die meisten Fehllesungen fängt schon der Auflöser ab, und von den
  übrigen sind die meisten „leise" (kein Bewertungssprung): die sieht keine Engine.
* **f/g verwechselbar** (0.647.1, `ScoresheetNotation.Confusable`): in Einlesung 11 derselben Partie las das Modell „Dg5 Dxg5
  hg5" als „Df5 Df5 hf5" — mit dem Paar findet schon der Auflöser 113/115 Halbzüge (Test `Resolve_GsReadAsFs_FindsTheQueenTrade`
  mit den echten Einträgen); am Testsatz unverändert.
* **Ausfallsicher**: keine Engine, Fehler, `Budget` (90 s) überschritten → die Lesung bleibt, wie sie ist (Warnung im Log).
  Schalter `Scoresheet:Plausibility=false`; Pfad `Scoresheet:EnginePath` (sonst `StockfishPath.Resolve`: `/usr/games/stockfish`
  aus dem Debian-Paket im API-Image — NICHT im PATH —, sonst `stockfish`), Tiefe `Scoresheet:EngineDepth`. Dieselbe Regel
  gilt seit 0.647.1 für den Tipp-Generator (`StockfishAnalyzer`, `Stockfish:Path`) — bis dahin fand er im Container keine Engine.
* **Oberfläche**: Chip „von der Engine korrigiert" / „Engine zweifelt" mit Erklärung als Tooltip (`games.edit.engine*`, RookHub
  `game-edit.component.ts`; LeagueHub `club-scan-page.component.ts`), `check` reist in `ScoresheetPly`/`EditPly` mit. Das
  Neu-Aufbereiten auf der Korrekturseite (`…/scoresheet/resolve`) prüft NICHT erneut — es läuft im Request.

**Testwerkzeug** `tools/ScoresheetBench` (Konsole, wie `tools/LibraryImport` kein Teil des Images, OHNE Datenbank
und ohne zweite API-Instanz — es benutzt nur `ScoresheetReader` + Auflöser): misst einen Ordner mit `NN.png|jpg`
(Formular), `NN.pgn` (Soll), `NN.formular.txt` (Abschrift) und `belege.json`. `--resolver-only` nimmt die Abschrift
als Eingabe (kostenlos, misst nur den Auflöser); ohne den Schalter liest das Modell die Bilder
(`ANTHROPIC_API_KEY`, harter Deckel `--max-usd`). Ausgabe: `report.md`, `results.json`, je Beleg `NN.plies.tsv`
(Abschrift, Modell, Soll, Ergebnis, Art, Lesarten, Kosten des Soll-Wegs) und `NN.answer.json`. Stand 25.09.2026 am
10er-Testsatz (HCS USA + Brasilien, portugiesisch): nur Auflöser 583/591 Halbzüge richtig, jeder falsche markiert.
Mit Modell (Claude Opus 5, 25.09.): 0,18–0,56 $ und 1–4½ min je Formular, 3,69 $ für alle zehn; auf den neun vollständig
gelesenen 486/499 richtig. `--replay <ordner>` wertet gespeicherte Antworten neu aus, ohne Kosten — so werden
Auflöser-Änderungen an echten Modell-Lesungen gemessen. Verglichen wird der ZUG (von–nach), nicht die Schreibweise.
**Andere Leser** (0.532.0, bisher NUR im Testwerkzeug, in der API nicht verdrahtet): `--provider openai` liest über eine
OpenAI-kompatible Schnittstelle (vLLM auf eigener Hardware, gedacht für Qwen3-VL auf dem DGX Spark;
`OpenAiScoresheetVisionClient`: Bild als Daten-URL, `response_format: json_schema` mit demselben Schema — lehnt der
Server das ab, einmal ohne und dann dabei bleiben —, Denkblöcke und Code-Zäune fallen weg, `finish_reason: length` =
`truncated`; Vorgabe-Auftrag `ScoresheetPrompt.TranscribeSystem` = NUR abschreiben, `--prompt full` = der
Claude-Auftrag). `--provider dots` nimmt dots.ocr (`DotsOcrScoresheetVisionClient`): ein Dokument-Leser, der IMMER
denselben Layout-Auftrag bekommt — wörtlich `prompt_layout_all_en` aus dem dots.ocr-Quelltext, auf diesen Wortlaut ist
das Modell trainiert, ein Test hält ihn fest — und Tabellen als HTML liefert; `DotsOcrLayout` macht daraus
Formular-Einträge (Nummern-Spalte = Zahlen, die Zeile für Zeile um eins steigen — so fallen Zeit-Spalten heraus;
jede Nummern-Spalte beginnt einen Block, darin Weiß und Schwarz; fehlende Nummer aus dem Versatz; ohne Nummern
paarweise; ohne Tabelle Zeilen „12. Sf3 Sc6"), ohne SAN-Deutung, und liest mit EINEM Durchgang
(`ScoresheetReader.ReadAsync(maxRounds: 1)` — eine Nachfrage ergäbe dieselbe Lesung). Aufruf:
`--endpoint <…/v1> --key-env SPARK_API_KEY [--model …] [--max-tokens 16384]`; der Schlüssel kommt nur aus der
genannten Umgebungsvariable, `--list-models` prüft die Verbindung. Eigene Hardware kostet 0 $ — der Deckel ist dort das
Kontextfenster des Servers, nicht das Budget. Abgelegt wird zusätzlich die Rohantwort (`NN.raw.txt` bzw.
`NN.dots.txt`).

**Quer fotografiert** (0.672.6, `Services/ScoresheetOrientation.cs`, gemeldet 2026-10-05 an LeagueHub-Formular 24): aus den
Kästen der ersten Lesung — auf einem aufrechten Formular steigen die Zugnummern nach UNTEN und Schwarz steht RECHTS neben
Weiß (Mediane der Schritte n→n+1 und Weiß→Schwarz; mindestens 6 bzw. 4 Paare, beide Richtungen eindeutig, sonst 0) — folgt
je Seite die Drehung 0/90/180/270 im Uhrzeigersinn. Ist eine nötig, dreht `ScoresheetImage.Rotate` die gespeicherten Fotos
aufrecht (volle Größe, JPEG), und die Einlesung liest NOCH EINMAL (zweiter Aufruf, gleiche Kostenbremse). Übernommen wird die
zweite Lesung samt gedrehter Fotos nur, wenn sie aufgeht; sonst bleiben erste Lesung und Fotos. Kein eigener Modell-Aufruf
und keine Schemaänderung — die Referenz-Lesungen bleiben wörtlich.

**Mehrere Seiten** (0.600.0, Wunsch 2026-09-29: „2. Bild für 2. Seite von Partieformular (+ 3. Seite)"): eine lange Partie
geht über mehrere Blätter — bis `ScoresheetScanService.MaxPages` (3) Fotos in Seitenreihenfolge, hochgeladen als mehrere
Teile `file` in EINER Anfrage, gespeichert als Seite 1 = `ScoresheetScan.Photo` (bestehende Einlesungen bleiben, wie sie
waren) und Seite 2+ = `ScoresheetScanPages`, dazu `ScoresheetScan.PageCount`. Es bleibt EINE Einlesung (Tageszahl, Budget).
Gelesen wird in EINEM Aufruf mit allen Fotos (`IScoresheetVisionClient.ReadPagesAsync`, `ScoresheetReader.ReadPagesAsync`;
Claude und der OpenAI-kompatible Leser setzen „Page n:" vor jedes Foto, dots.ocr und die Test-Attrappen können nur eine
Seite — Vorgabe der Schnittstelle, Absage `multiPage`), damit das Modell sieht, wo Seite 2 weitermacht (auch mit neu
beginnender Nummerierung, ein Zugpaar kann über die Seiten gehen). **Mit EINER Seite bleiben Auftrag und Schema wörtlich
wie vorher** (die Referenz-Lesungen hängen daran, Test): erst ab zwei Seiten nennt `ScoresheetPrompt.FirstRead`/`Repair`
Seitenzahl und Maße je Seite, und `Schema(multiPage: true)` verlangt je Zug `page`. Die gespeicherte Antwort trägt dann
`pageSizes` (`ScoresheetTranscription.WithImageSize`), jeder Kasten wird in den Pixeln SEINER Seite umgerechnet
(`NormalizedBoxes`), `EntryPages()` klemmt die Seite auf 1..n. `GET …/scoresheet` liefert `pageCount` + `pages` (je Eintrag),
`GET /api/games/{id}/photo?page=n` die Seite, jede Antwort mit `X-Page-Count` (damit blättert der Foto-Dialog aus Liste,
Partieseite und Korrekturseite ohne weiteren Abruf). Korrekturseite: Seitenwahl über dem Foto, der gewählte Zug blättert
mit (`SheetEditSession.currentPage`), der Ausschnitt kommt aus der Seite des Eintrags (Maße je Seite werden beim Laden
gemessen, `setPageSize`). Löschen: Partie löschen räumt die Seiten ohne Laden ab (`RemovePagesWithoutLoading`, die
Einlesung bleibt fürs Kontingent), das Konto ebenso. **Frontend-nginx**: eigene `location ^~ /api/scoresheets` mit 64 MB
(die generische /api/-Regel deckelt auf 15 MB, drei Handyfotos kämen nicht durch; `DeploymentConfigTests` hält sie über dem
`[RequestSizeLimit]` des Uploads). **LeagueHub seit 0.690.1** (Wunsch 2026-10-06): `POST …/club/scans` und
`POST /api/league/s/{token}/club/scans` nehmen ebenfalls bis zu drei Teile `file` (`ClubUpload.ReadPagesAsync`, 64 MB, die
verschachtelte nginx-Location dafür auf 64M), `GET …/scans/{id|key}/photo?page=n`; die Seite bietet nach dem ersten Foto
„+ Seite 2/3“, die Prüfseite blättert zwischen den Seiten (folgt dem gewählten Zug).

**Glocke** (0.531.0): fertig gelesen → `scoresheet_read` (Daten white/black/moves/uncertain/unresolved, Link auf
`/games/{id}/edit`), gescheitert → `scoresheet_failed` (Daten reason, die Glocke übersetzt den Code über
`scoresheet.error.*`). Ein Fehler beim Benachrichtigen lässt die Einlesung nicht scheitern.

**Meine Seite** (0.531.0, `SavedGame.OwnerSide` white/black/null): selbst festgelegt schlägt `DetermineOwnerSide` die
Zuordnung über den Plattform-Namen — und weil Partieseite, Teilen-Link UND Link-Vorschaubild (`OgMetaService`) alle
über diese eine Regel gehen, dreht eine Festlegung überall. Beim Einlesen: Formularfeld `side` (white/black/auto,
`ScoresheetScan.OwnerSide`); `auto` sucht Nachname, Anzeigename, Vorname, Benutzername als GANZES Wort (ohne
Groß/klein, ohne Akzente) in den gelesenen Spielernamen — nur wenn genau EINE Seite passt
(`ScoresheetScanService.GuessOwnerSide`). Korrekturseite: `PUT /api/games/{id}` mit `ownerSide` (leer = zurücknehmen,
weglassen = unverändert), für jede Partie.

**Ausschnitt je Eintrag** (0.550.0, Wunsch 2026-09-27): das Modell liefert zu jedem Eintrag `box` = [x0, y0, x1, y1] in
PIXELN des Bildes, das es bekam (Schema-Pflichtfeld, in BEIDEN Aufträgen erklärt; ~15 Ausgabe-Tokens je Halbzug mehr,
~+0,02–0,04 $ je Formular). **Die Größe steht im Auftrag** (`ScoresheetPrompt.FirstRead`/`Repair` mit `photoSize`,
„The photo is 1500 × 2000 pixels"), und die Einlesung hängt `imageWidth`/`imageHeight` an die gespeicherte Antwort
(`ScoresheetTranscription.WithImageSize`). Warum Pixel (0.551.3): um „0..1000" gebeten, antwortete Opus 5.5 im echten
Lauf trotzdem in Pixeln des 1500×2000-Bildes (y bis 1790) — die Ausschnitte saßen alle falsch; im Einzeltest vorher hatte
es sich an 0..1000 gehalten. Die Einheit hängt also nicht verlässlich an der Bitte, die Pixel mit genannter Größe schon.
`GET /api/games/{id}/scoresheet` reicht die Kästen als `boxes` in 0..1000 durch (Index wie `written`) —
`ScoresheetTranscription.NormalizedBoxes` rechnet Pixel → Promille, verwirft Kästen, die über 3 % aus dem Bild ragen,
sortiert vertauschte Ecken, verwirft alles ohne vier Werte mit Fläche (`null`). Einlesungen von 0.550.0 ohne Bildmaße:
nur übernommen, wenn KEIN Wert über 1000 liegt, sonst alle `null`. Die Korrekturseite zeigt unter dem Foto den Eintrag des gewählten Halbzugs (`ply.w`; am Ende
der Liste den ersten unaufgelösten, `unresolvedFrom`) als Ausschnitt mit Umfeld — seitlich 60 % der Kastenbreite, oben/
unten 130 % der Höhe (`cropView` in `game-edit.util.ts`) —, rot gerahmt, wenn der Zug unsicher ist. Ausgeschnitten wird
per CSS aus dem schon geladenen Foto (Seitenverhältnis aus `naturalWidth/Height`), keine Bildbearbeitung am Server. Das
gespeicherte Foto ist meist das ORIGINAL mit EXIF-Drehung, das Modell sah es gedreht (`ScoresheetImage.Prepare`); der
Browser zeigt es ebenfalls gedreht, deshalb passen die Koordinaten ohne Umrechnung. Ältere Einlesungen und dots.ocr
haben keine Kästen → kein Ausschnitt.

**Korrekturseite**: das Brett zeigt die Stellung VOR dem gewählten Halbzug, ein Zug am Brett ersetzt ihn (oder fügt
ein), `Zug löschen` streicht ihn. Bei einer eingelesenen Partie wird danach der REST neu aufbereitet
(`POST …/scoresheet/resolve`, ohne Modell-Aufruf): Präfix = die festen Züge, `writtenFrom` = welcher Formular-Eintrag
zum nächsten Halbzug gehört (ersetzen: der nächste, einfügen: derselbe, löschen: der übernächste —
`resolveRequest` in `game-edit.util.ts`). **Ein Halbzug OHNE Eintrag (`w = null`, eingefügt) verbraucht keinen**
(0.558.1, `nextEntryAt`): ersetzen/löschen geht beim nächsten OFFENEN Eintrag weiter (nach dem letzten Halbzug mit
Eintrag), und ein ersetzter eingefügter Zug bleibt ohne Eintrag. Vorher zählte jeder Halbzug dazwischen mit — an
Prod-Partie 27 (36…Bd5 vergessen, Rb4 eingefügt) fiel beim Nachspielen von Rb4 der Eintrag „Kd7" weg und der Rest
verrutschte. Gespeichert wird über `PUT /api/games/{id}`: Züge werden nachgespielt
(illegal → 400, nichts geschrieben), nicht bearbeitete Header (Elo, Bedenkzeit, FEN) bleiben, und **ändern sich die
Züge, fallen `GameAnalysisId`, der Stand des Fehler-Trainings und die Nacherzählung weg** — sie gehörten zu einer
anderen Partie (`SavedGameService.OnMovesChangedAsync`, gilt seit Codereview N8-002 auch für den längeren Re-Save aus der
Erweiterung, `POST /api/extension/games`). Der
Stand je Halbzug (bestätigt, Lesarten) geht als `scoresheetPlies` mit und liegt als `ResolutionJson` an der Einlesung.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/scoresheets/status` | `{ available, dailyLimit, usedToday, nextAllowedAt, budgetUsedPercent, blocked, unlimited, languages[{ code, name, pieces }] }` — `nextAllowedAt` (UTC) nur bei erreichter Tageszahl |
| GET | `/api/scoresheets?take=` | Die letzten Einlesungen (ohne Foto) |
| POST | `/api/scoresheets` | Multipart `file` (bis zu 3 Teile = Seiten in Reihenfolge, 0.600.0) + `language` (Code oder `auto`) + `side` (white/black/auto) → 202 mit der Einlesung (`pageCount`); 400 `reason` ∈ `noFile`/`unsupportedImage`/`tooLarge`/`tooManyPages`/`dailyLimit`/`tooManyOpen`/`invalidLanguage`/`userDailyBudget`/`userMonthlyBudget`/`globalBudget`, 503 `notConfigured` |
| GET | `/api/scoresheets/{id}` | Stand: `pending`/`running`/`done`/`failed` (+ `error`), `savedGameId`, Zähler |
| PUT | `/api/games/{id}` | Partie korrigieren `{ moves[{ san, comment? }], white, black, result, event, site, round, date, ownerSide?, scoresheetPlies? }` (`GameCorrectionController`, eigene Klasse unter derselben Route wie `GamesController`) |
| GET | `/api/games/{id}/photo?download=&page=` | Das Formular-Foto, Seite `page` (ab 1; 404 ohne) — Header `X-Page-Count` = Zahl der Seiten |
| GET | `/api/games/{id}/scoresheet` | Formular-Einträge + Stand je Halbzug für die Korrekturseite; `pageCount` + `pages` (Seite je Eintrag) |
| POST | `/api/games/{id}/scoresheet/resolve` | Rest neu aufbereiten `{ prefix[], writtenFrom }` → `{ plies, unresolved, unresolvedFrom }` |
| GET | `/api/admin/scoresheets/pending` | messages.admin: wartende Einlesungen für den Leser von außen (0.687.0) `[{ id, purpose (own/league), userId, anonymous, pageCount, notationLanguage, ownerSide, createdAt }]`, älteste zuerst |
| GET | `/api/admin/scoresheets/{id}/photo?page=` | messages.admin: Foto einer Seite |
| POST | `/api/admin/scoresheets/{id}/reading` | messages.admin: Lesung übernehmen `{ transcription }` → die Einlesung; 400 `reason` ∈ notPending/invalidTranscription, 404 |
| POST | `/api/admin/scoresheets/{id}/fail` | messages.admin: als gescheitert schließen `{ reason }` (unreadable/noMoves/failed, Glocke wie sonst) → 204 |

### Partie rekonstruieren (auth)

Eine am Brett gespielte Partie aus Bruchstücken wieder zusammensetzen. Der Ausgangspunkt ist die
Erinnerung: die ersten Züge weiß man meist vollständig, danach nur noch einzelne Stellungen und
kurze Zugfolgen. Aufgezeichnet wird deshalb eine GEORDNETE Liste von Teilen
(`GameReconstructionPart`) — Zugfolge (SAN) oder Stellung (FEN) —, nicht eine fertige Partie.

**Ein PGN kann das nicht.** Eine Stellung ohne den Weg dorthin lässt sich darin nicht ausdrücken,
und genau diese Teile sind hier die Eingabe. Erst wenn die Lücken geschlossen sind, entsteht eine
Partie; die SUCHE danach ist ein späterer Schritt, aufgezeichnet wird zuerst.

**Die Grundannahme ist LÜCKE, nicht Anschluss** (`GameReconstructionPart.ContinuesPrevious`,
Vorgabe `false`): Bruchstücke stammen von verschiedenen Stellen der Partie — hingen sie aneinander,
wären sie EIN Teil. Nur das erste Teil hängt automatisch an der Grundstellung. Die Oberfläche setzt
den Haken beim Anlegen genau dann vor, wenn das neue Zug-Teil auf eine STELLUNG folgt („in dieser
Stellung ging es so weiter") — nach einer Zugfolge nicht.

**Geprüft wird serverseitig** (`Services/ReconstructionChain.cs`, reine Funktion über Gera.Chess,
ohne DB und Engine): je Teil `Anchored` (ist die Stellung davor bekannt?), `Valid`, `StartFen`,
`EndFen`, `PlyCount`, `StartPly` und der erste nicht spielbare Zug. Zwei Regeln hängen daran:

* **Ein Bruchstück ohne Anschluss ist NICHT falsch.** Ohne die Stellung davor lässt sich eine
  Zugfolge weder bestätigen noch widerlegen — und diese Stellung ist ja das Gesuchte. Solche Teile
  tragen `Anchored = false` und sonst keinen Vorwurf.
* **Ein behaupteter Anschluss, der nicht passt, wird gemeldet** (`Mismatch`): sagt ein Stellungs-Teil
  „direkt danach", stimmt aber nicht mit der Stellung nach dem vorigen Teil überein, ist das ein
  Widerspruch in den Erinnerungen — genau die Auskunft, die beim Rekonstruieren weiterhilft.

`StartPly` gibt es nur, solange die Kette lückenlos an der Grundstellung hängt: eine Zugnummer nach
einer Lücke wäre geraten. `KnownPlies`/`PrefixSan` sind derselbe Gedanke für die ganze Partie — was
schon sicher steht.

Jede schreibende Operation antwortet mit der GANZEN Rekonstruktion: ein eingefügtes Teil ändert
Gültigkeit und Nummern aller folgenden, ein DTO nur des geänderten Teils wäre danach mehrfach falsch.
Deckel: `MaxPerUser` 50, `MaxParts` 200, Zugtext 4000 Zeichen.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/reconstructions` | Eigene Rekonstruktionen (zuletzt geänderte zuerst) mit Teilezahl, gesicherten Halbzügen und Lücken |
| POST | `/api/reconstructions` | Anlegen `{ title, white?, black?, event?, playedOn?, result?, note? }`; 400 `reason: too-many` am Deckel |
| GET | `/api/reconstructions/{id}` | Detail: alle Teile samt Auswertung + `prefixSan` |
| PUT | `/api/reconstructions/{id}` | Kopfdaten ändern |
| DELETE | `/api/reconstructions/{id}` | Löschen (mit allen Teilen) |
| POST | `/api/reconstructions/{id}/parts` | Teil anhängen `{ kind (0 = Züge, 1 = Stellung), moves?, fen?, fromPly?, continuesPrevious?, note? }`; 400 `reason` ∈ `no-moves`/`invalid-fen`/`too-many-parts` |
| PUT | `/api/reconstructions/{id}/parts/{partId}` | Teil ändern (gleicher Rumpf) |
| DELETE | `/api/reconstructions/{id}/parts/{partId}` | Teil löschen (Reihenfolge wird geschlossen) |
| PUT | `/api/reconstructions/{id}/parts/order` | Reihenfolge setzen `{ partIds: [] }` — fehlende Ids bleiben hinten, damit eine unvollständige Liste nichts verschwinden lässt (Literal-Route VOR `{partId}`) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/propose` | **Lücke schließen**: sucht die Wege und setzt sie als VORSCHLÄGE (`Generated`) vor das Teil `{ maxPlies? }` → `{ reason, nodes, budgetExhausted, inserted, detail }`. Ersetzt die Vorschläge derselben Lücke. Rate-Limit `reconstruction-gap` (6/min je Konto, gemeinsam mit `gap`); 429 `reason: busy`, wenn gerade alle Suchplätze belegt sind — die alten Vorschläge bleiben dann |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/discard` | Vorschläge dieser Lücke verwerfen (idempotent) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/waypoint` | Eine Stellung aus einem Vorschlag übernehmen `{ fen, certain? }` — sie kommt als eigenes Teil davor, die Vorschläge fallen weg, die Lücke zerfällt in zwei |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap` | **Lücke schließen**: sucht die Züge von der Stellung am Ende des vorigen Teils bis zu diesem Teil `{ maxPlies? }` → `{ fromFen, toFen, maxPlies, nodes, budgetExhausted, reason, solutions[] }`. 200 auch ohne Weg — „es gibt keinen Weg" ist eine Auskunft, kein Fehler des Aufrufers; nur 429 bei Rate-Limit (`reconstruction-gap`) oder `reason: busy` (alle Suchplätze belegt) |
| POST | `/api/reconstructions/{id}/parts/{partId}/gap/apply` | Einen gefundenen Weg übernehmen `{ moves }` — die Züge kommen als eigenes Teil VOR `partId`, beide schließen danach nahtlos an. 400 `reason` ∈ `does-not-fit`/`no-gap`/`no-previous`/`no-anchor`/`no-moves`/`target-not-a-position` |
| POST | `/api/reconstructions/{id}/share` | **Ganze Partie teilen**: öffentlichen Link einschalten (idempotent) → `{ shareToken }` |
| DELETE | `/api/reconstructions/{id}/share` | Link abschalten — ein späteres Teilen erzeugt ein ANDERES Token |
| GET | `/api/reconstructions/shared/{token}` | **Ohne Anmeldung**: die ganze Partie hinter dem Link (Kopfdaten, alle aufgezeichneten Teile in Reihenfolge, `knownPlies`/`gaps`/`prefixSan`) |

**„Ganze Partie teilen"** (`/r/{token}`, Knopf auf der Detailseite): geteilt wird die
REKONSTRUKTION, nicht eine Kopie — wer den Link öffnet, sieht den Stand von jetzt, samt der
Lücken, die noch offen sind. Genau darum verschickt man ihn („so weit habe ich die Partie,
erkennst du den Rest wieder?"). Drei Entscheidungen hängen daran:

* **Kein PGN.** Die öffentliche Seite zeigt dieselben Teile wie der Besitzer — Züge, erinnerte
  Stellungen, Lücken dazwischen — und nicht eine Zugliste, die die Lücken verschweigen müsste.
  Der Betrachter blättert mit ← → oder per Klick durch die Stellungen (`buildRows` in
  `shared-reconstruction.component.ts` spielt die Züge mit chess.js nach; eine Lücke ist eine
  Zeile ohne Stellung und damit kein Halt beim Blättern).
* **Vorschläge der Lückensuche bleiben draußen** (`Generated`): sie sind Arbeitsstand des
  Besitzers, keine Aussage über die Partie.
* **Ein vergebenes Token bleibt** (Teilen ist idempotent), damit ein schon verschickter Link
  gültig bleibt; das Abschalten ist der Widerruf und erzeugt beim nächsten Mal ein neues.

**Die Lücke schließen ist eine SUCHE, kein Raten** (`Services/GapSolver.cs`). Gesucht wird das
klassische Beweispartie-Problem: welche Halbzüge führen von der Stellung am Ende des vorigen Teils
zu der erinnerten Stellung? Der Baum wächst mit ~30 Zügen je Halbzug, deshalb vier Dinge:
iterative Vertiefung (die KÜRZESTE Erklärung zuerst — ist eine Tiefe fündig, hört die Suche auf),
eine zulässige untere Schranke (`MinPlies`), Zugsortierung (`Ordered`: zuerst die Züge, die eine
Figur auf ihr Zielfeld stellen) und ein Gedächtnis für ausgeschöpfte Sackgassen.
`MaxSearchPlies` = 12, Vorgabe 4, Knoten-Budget 500 000, Zeitbudget 4 s (beide meldet die Antwort
als `budgetExhausted` samt `deepestSearched` — „so weit kam ich" ist eine andere Aussage als
„es gibt keinen Weg").

**Die Suche ist reine CPU im Request-Thread — deshalb gedeckelt** (Codereview 2026-09-29, N5-001; vorher
3 Mio. Knoten/12 s und nur der globale 100/min-Deckel je Adresse, ein frei registriertes Konto hielt so rund
hundert Suchen gleichzeitig am Laufen): Rate-Limit `reconstruction-gap` (6/min je Konto) auf `gap` und
`gap/propose`; höchstens `GapSearchGate.DefaultSlots` = 3 Suchen gleichzeitig im ganzen Prozess, sonst SOFORT
429 `reason: busy` (kein Warten, keine Schlange im Thread-Pool); Abbruch der Anfrage oder Herunterfahren
beendet die Suche wie ein erschöpftes Budget; das Sackgassen-Gedächtnis hört bei `MaxDeadEntries` = 200 000
auf zu wachsen. Gemessen (~20 000 Knoten/s): sechs Halbzüge ~1 s, Spanisch über acht 2,2 s; eine
Sizilianisch-Stellung nach acht Halbzügen lief selbst mit 30 s ins Budget — die Antwort sagt dann `budget`.

**Die Schranke ist die halbe Miete** (`MinPlies`): je Seite das MAXIMUM aus Schlagfällen
(verschwundene Figuren der Gegenseite), Umwandlungen und **Verschiebung** — für jede Figur der
ZIELstellung die billigste passende Ausgangsfigur (Springer per Distanztabelle, Läufer 1 oder 2,
Turm/Dame 1 oder 2, König Chebyshev, Bauern nur vorwärts, seitwärts nur per Schlag, Doppelschritt
aus der Grundreihe als EIN Zug, Umwandlung über den Bauernmarsch), dazu die feste Parität. Die
Rochade bewegt zwei Figuren in einem Zug und wird deshalb mit bis zu 3 abgezogen. Vorher stand
dort „veränderte Felder ÷ 4" — bei einer Stellung acht Halbzüge später sagte das „mindestens
zwei", also praktisch nichts, und die Suche erstickte ab sechs Halbzügen im Budget. **Wer hier
etwas ändert, prüft die ZULÄSSIGKEIT**: `MinPlies_NeverAsksForMoreMovesThanWereActuallyPlayed`
spielt zufällige Zugfolgen und besteht darauf, dass die Schranke nie mehr verlangt, als gespielt
wurde — eine zu große Schranke verwirft echte Lösungen lautlos (so gefunden: der Doppelschritt des
Bauern zählte als zwei Züge).

Vier Regeln, die dabei nicht kippen dürfen:

* **„Nicht gefunden" ist nicht „gibt es nicht".** Bricht die Suche am Budget ab, sagt die Antwort
  das (`budgetExhausted`), und die Oberfläche schreibt einen anderen Satz. Ein Ergebnis, das beides
  gleich behandelt, verleitet dazu, eine richtige Erinnerung zu verwerfen.
* **Das en-passant-Feld zählt nur, wenn BEIDE Seiten eines nennen** (`GapSolver.Matches`). Der
  Stellungs-Editor schreibt dort immer „-" (er kennt den Zug davor nicht), die Zug-Erzeugung setzt
  nach jedem Doppelschritt eines. Verlangte man Gleichheit, fiele die häufigste Erinnerung durch
  („und dann ging der Bauer nach e5" — jeder Weg, dessen letzter Halbzug ein Doppelschritt ist).
* **Das Ziel ist immer ein STELLUNGS-Teil.** Eine Zugfolge nach einer Lücke hat selbst keine
  bekannte Ausgangsstellung — und genau die wäre das Ziel der Suche.
* **Beim Übernehmen wird NOCH EINMAL geprüft** (spielbar ab der Stellung davor, endet auf der
  Zielstellung): die Züge kommen aus einer Antwort, aber ankommen tut ein Request. Passt es nicht,
  entsteht gar kein Teil. Das eingesetzte Teil trägt `Certain = false` — mehrere Wege enden in
  derselben Stellung, welcher gespielt wurde, weiß nur der Mensch.

**Gefundene Wege stehen als VORSCHLÄGE in der Liste** (`GameReconstructionPart.Generated`, seit
0.493.0): „Lücke schließen" sucht und setzt die Wege dorthin, wo die Lücke ist —
`POST …/parts/{partId}/gap/propose` (ersetzt die Vorschläge derselben Lücke),
`…/gap/discard` wirft sie weg, `…/gap/waypoint` macht eine Stellung DARAUS zu einem eigenen Teil
(„die stimmt" bzw. ihre korrigierte Fassung) und teilt die Lücke damit in zwei kleinere.
`ReconstructionChain.Analyze` ÜBERSPRINGT Vorschläge: sie stehen in der Liste, aber die Lücke bleibt
offen und die „gesicherten Halbzüge" wachsen nicht — sonst behauptete die Rekonstruktion etwas,
das nur geraten ist. Ein Teil, das ein Mensch bearbeitet, verliert die Marke (`UpdatePartAsync`);
`AddPartAsync` kann mit `InsertBeforePartId` an beliebiger Stelle einsetzen. Die Oberfläche blendet
Vorschläge auf Wunsch aus, öffnet den ersten gleich zum Durchklicken und bietet dort „Stellung
stimmt" / „Stellung korrigieren" / „ganze Linie übernehmen".

**Sicher oder unsicher** (`GameReconstructionPart.Certain`, Vorgabe `true`): je Bruchstück ein
Haken. Wer etwas aufschreibt, meint es zunächst — die Auskunft, auf die es ankommt, ist das
Gegenteil („hier bin ich mir nicht sicher"), und die steht am TEIL statt in einer Notiz, weil eine
unsichere Stellung der erste Kandidat für einen zweiten Blick ist, wenn die Lücke daneben nicht
aufgeht. Im Request ist das Feld NULLBAR: ein Client, der die Frage nicht kennt, darf nicht für den
Nutzer „unsicher" behaupten (und die Migration trägt für den Bestand `true` nach).

**Wer am Zug ist, sagt notfalls der Mensch** (`GameReconstructionPart.BlackToMove`): bei einer
Stellung steht die Seite in der FEN, bei einem anschließenden Teil in der Stellung davor — bei
einem FREIEN Bruchstück gibt es keine Quelle dafür, und ohne die Angabe ließe sich „und dann schlug
ER auf f7" gar nicht eingeben (das Brett stünde auf Weiß). Der Umschalter im Editor löst ein Teil
deshalb vom vorigen, wenn die gewählte Seite der Stellung davor widerspricht. Beim ERSTEN Teil
heißt der Haken: das ist nicht die Eröffnung — `ReconstructionChain` verankert es dann nicht an der
Grundstellung, sondern behandelt es wie jedes andere freie Bruchstück.

**Aufgezeichnet wird im FLUSS** (`reconstruct-detail.component.ts`): eine leere Rekonstruktion
öffnet gleich den Zug-Editor, „Stellung eingeben" speichert das Getippte und führt in den
Stellungs-Editor, und eine übernommene Stellung öffnet wieder den Zug-Editor (mit Anschluss an sie).
Ein LEERER Zug-Editor wechselt dabei nur die Art und legt nichts an — das ist der Weg „direkt zur
nächsten Stellung". So entsteht Züge → Stellung → Züge → Stellung, ohne nach jedem Teil zurück ins
Menü zu gehen. Der Sicher-Haken lässt sich an jedem gespeicherten Teil mit einem Klick umlegen
(`toggleCertain`), weil einem beim Aufschreiben erst später auffällt, dass ein früheres Bruchstück
wackelt.

Menü-Key `reconstruct` (Stufe `Registered`), Frontend `/reconstruct` (Liste) und
`/reconstruct/:id` (Arbeitsplatz: Teile links, Brett rechts). Die Züge werden im Browser
mitgespielt (chess.js), damit man beim Tippen und Klicken sofort sieht, wo man steht — die
verbindliche Auskunft kommt trotzdem vom Server.

### Client-Diagnostik (offen)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| POST | `/api/client-log` | AllowAnonymous + RL | Client-seitiges Diagnose-Event `{ kind, detail?, url? }` (v. a. Browser-Engine-Crash/Hänger) — wird strukturiert mit Marker „ClientLog" geloggt (→ ES/Kibana), nichts in der DB. `heartbeat*`-Kinds auf Information, sonst Warning. `kind=heartbeat_bot` ist nur noch der Altpfad des Bots ohne Secret — der signierte Heartbeat läuft über `POST /api/bot/heartbeat`. Frontend: `ClientLogService` (gedrosselt), Engine-Services melden via `reportEngineEvent`-Hook |

### Bestenlisten (auth)
Ranglisten über vier Kategorien je Periode (`weekly`/`monthly`/`alltime`, UTC-Grenzen). `weekly`/`monthly` sind **rollierende Fenster** = die letzten **7** bzw. **31** Tage (taggenau inkl. heute, `WindowStart` = `today.AddDays(-6)`/`-30`), NICHT Kalenderwoche/-monat. Nur eingeloggte Nutzer (Menü-Key `leaderboards`, Stufe `Registered`); anonyme Versuche (`UserId == null`) zählen nicht. Logik in `LeaderboardService` (rein lesend, keine neue Tabelle). Kategorien: **Puzzles** = einzigartige gelöste Standard-Puzzles (distinct `PuzzleAttempts.PuzzleId` mit `Solved`, im Fenster), **DailyPuzzles** = einzigartige gelöste Tagespuzzles (gelöste `BookPuzzleAttempts`, deren `BookPuzzleId` in `DailyPuzzles` vorkommt, distinct), **EndlessRuns** = abgeschlossene `EndlessSessions` (je Lauf), **CourseLines** = gelöste Kurs-Linien (`CoursePuzzleResults`, idempotent = einzigartig). Sortierung Count desc → Name asc; jeder Eintrag trägt seinen echten 1-basierten `rank` + ein `isMe`-Flag. Geliefert wird je Kategorie nur **Top-`top`** (1–500, Default **5**) **PLUS das Fenster ±`around`** (0–25, Default **2**) um den eigenen Platz — die Liste kann also eine Lücke zwischen Top-Block und eigenem Fenster haben. Frontend: `/leaderboards` (Perioden-Umschalter + 4 Karten; eigene Zeile hervorgehoben, „⋯"-Trenner bei Lücke).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/leaderboards?period=&top=&around=` | Auth | Alle vier Bestenlisten für die Periode (`{ period, puzzles[], dailyPuzzles[], endlessRuns[], courseLines[] }`, je Eintrag `{ name, discordId?, discordUsername?, count, rank, isMe }`). Je Kategorie nur Top-`top` (Default 5) + Fenster ±`around` (Default 2) um den eigenen Platz |

### Trainingsziele (auth)
Tagesziele Puzzles/Buch-Kurs/**Chessable** (in Minuten) + wöchentliches Spielen-Ziel (Anzahl Rapid-/Classical-Partien pro ISO-Woche) + Wochenziel (volle Tage); effektives Ziel = persönlicher Override > zuletzt aktualisierte Gruppen-Vorlage > keins. Tracker aggregiert je UTC-Tag die verbrachte Zeit (Pro-Einzelpuzzle-Clamp 1800 s, Chessable-Häppchen-Clamp 3600 s) für Puzzles/Buch/Chessable + die Partienzahl für Spielen und markiert Tage none/partial/full (**Tagesstatus aus Puzzles + Buch + Chessable** — Spielen ist ein Wochenziel). Kategorien-Quellen: Puzzles = PuzzleAttempt + EndlessSession + BookPuzzleAttempt + **CourseAttempt aus Büchern der Art Puzzle**; Buch/Kurs = **CourseAttempt aus Büchern der Art Study** (`Book.Kind` steuert das Routing; **jeder** Kurs-Versuch zählt, nicht nur die Erstlösung); **Chessable = ChessableActivity** (aktive Trainingszeit, von der RepCheck-Extension via `POST /api/extension/training-activity` gemeldet). Logik in `TrainingGoalService`; Admin-Vorlage je Gruppe siehe Gruppen-Tabelle.

**Manuelle Offline-Aktivitäten** (selbst gemeldet, korrigierbar): `ManualActivities` (`/api/training-goals/manual` GET/POST/PUT/DELETE) speist **dieselben bestehenden Kategorien** — kein neues Ziel-Feld. Mapping je `ManualActivityKind`: **OtbGame** → Spielen (+Amount Partien/Tag, Cap 50), **OfflinePuzzle** → Puzzles (Amount Min), **OfflineStudy** + **Coaching** → Buch/Kurs (Amount Min); Minuten-Arten via `PerSessionCapSeconds` (4 h) gedeckelt. Tage mit ≥1 manuellem Eintrag liefern `TrackerDayDto.HasManual=true` (Tracker-Marker „manuell").

Spielen-Tracking: `PlayTimeService` (typed HttpClient) holt Lichess exakt (createdAt/lastMoveAt) + chess.com Best-Effort (PGN-Header UTCDate/UTCTime↔EndDate/EndTime) öffentlich ohne Login; `PlayTimeSyncService` (BackgroundService, `PlayTime:IntervalHours`=6) + manueller `/sync-play`-Button. Gezählt: Lichess `speed` rapid+classical, chess.com `time_class` rapid (keine eigene classical-Live-Klasse); Bullet/Blitz/Korrespondenz zählen nicht.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/training-goals` | Auth | Effektives Ziel (`source` personal/group/none, ggf. `groupName`) |
| PUT | `/api/training-goals` | Auth | Persönlichen Override setzen (PuzzleMinutes/BookMinutes 0–600, PlayGames 0–200 Partien/Woche, WeeklyDaysTarget 0–7) |
| DELETE | `/api/training-goals` | Auth | Override entfernen → Rückfall auf Gruppen-Vorlage |
| GET | `/api/training-goals/today` | Auth | Heutiger Fortschritt Puzzles/Buch (Tag) + Spielen-Partien (Woche) + Tagesstatus + Wochenstand (X/Y Tage) |
| GET | `/api/training-goals/tracker?weeks=27` | Auth | Tagesreihe (nur Tage mit Aktivität) für die Tracker-Heatmap; je Tag auch PlayGames (informativ) |
| GET | `/api/training-goals/daily-series` | Auth | Vollständige Tagesreihe (ganze Historie, **ungedeckelt** durch das 53-Wochen-Fenster), je Tag bySource+byTheme — Basis für die client-seitig umschaltbare Perioden-Aufschlüsselung (Tag/Woche/Monat/Jahr/Gesamt mit Durchschalten) |
| POST | `/api/training-goals/sync-play` | Auth | Gespielte Rapid-/Classical-Partien (Lichess/chess.com) des eigenen Users sofort synchronisieren. Je Plattform höchstens alle 5 min (`PlayTimeService.ManualSyncCooldown`; sonst `synced: false` + `retryAfterSeconds`), dazu Rate-Limit `sync-play` (3/min je Konto) gegen parallele Anfragen; der Hintergrund-Sync ist ausgenommen (Codereview F5-002) |
| GET | `/api/training-goals/manual?take=200` | Auth | Eigene manuell eingetragene Offline-Aktivitäten (neueste zuerst) |
| POST | `/api/training-goals/manual` | Auth | Manuelle Offline-Aktivität anlegen `{ date (yyyy-MM-dd, nicht Zukunft), kind, amount, note? }` — `kind` ∈ OtbGame/OfflinePuzzle/OfflineStudy/Coaching; `amount` = Partienzahl (OtbGame, 1–50) bzw. Minuten (sonst, 1–600), serverseitig geklemmt. 400 bei ungültigem/Zukunfts-Datum |
| PUT | `/api/training-goals/manual/{id}` | Auth | Eigene manuelle Aktivität ändern (404 wenn nicht vorhanden/nicht eigene) |
| DELETE | `/api/training-goals/manual/{id}` | Auth | Eigene manuelle Aktivität löschen (404 wenn nicht vorhanden/nicht eigene) |

## Datenbank-Schema (eigene DB `rookhub`, nicht geteilt mit Crawler)

| Tabelle | Zweck | Wichtige Felder / Constraints |
|---------|-------|-------------------------------|
| AppUsers | Auth | Username (unique), Email (unique, **nullable**), PasswordHash, CreatedAt |
| UserProfiles | Schach-Identität | UserId (1:1 zu AppUser), FideId, ChessResultsId, ChessComUsername, LichessUsername, DisplayName, DiscordId (unique, nullable) + DiscordUsername |
| Friendships | Freundesliste | RequesterId, AddresseeId (unique pair), Status (Pending/Accepted/Declined) |
| PuzzleChallenges | Puzzle an Freund(e) schicken | FromUserId, ToUserId (beide Restrict-FK auf AppUser), **Source (Enum Standard/Book)** + PuzzleId (polymorph, **kein FK** — je nach Source `Puzzles.Id` oder `BookPuzzles.Id`), Status (Pending/Solved/Failed), CreatedAt, ResolvedAt?, TimeSpentSeconds?; Index (ToUserId, Status) + (FromUserId) + (Source, PuzzleId) |
| RevengeNotifications | Revanche an gescheitertem Puzzle | AvengerUserId, TargetUserId, PuzzleId (alle Restrict), Solved, CreatedAt, SeenAt?; Index (TargetUserId, SeenAt) |
| TournamentDirectoryEntries | Turnierverzeichnis aus der chess-results-Suche UND dem FIDE-Kalender | **PublicId (unique, ≤24 — die IDENTITAET: chess-results-Nummer bzw. `f<FIDE-Nummer>`; Routen-Parameter, Ausblend-/Melde-Schluessel, Teilen-Link)**, **ChessResultsId (≤20, NULLBAR — fehlt bei Turnieren, die dort nicht ausgeschrieben sind; alles, was chess-results braucht, ist darauf abgefragt)**, Name, Federation, State, StartDate/EndDate, **StartsOnWeekend** (vorberechnet — `DateOnly.DayOfWeek` uebersetzt der MySQL-Provider nicht), LocationText, TimeControlText, Speed, Rounds, PlayerCount, **Lat/Lon/GeoSource/GeoPlaceName**, FirstSeenAt/LastSeenAt/**MissedSweeps**/RemovedAt, **ChangeHash** (nur Termin+Ort), **Kind** (Einzel/Mannschaft — AUS DER QUELLE, siehe unten), **IsLeague** + **AgeGroups** (Flags-Bitfeld U8…U20/YouthUnspecified/Senior) + **Gender** (Open/Female/Male), beide abgeleitet vom `TournamentClassifier`; Index (EndDate), (Federation, EndDate), (Lat, Lon) |
| TournamentSearchProfiles | Gespeicherte Umkreise je Nutzer | UserId (Cascade), Name, PlaceQuery, Lat/Lon, RadiusKm, Federations/Speeds (CSV), WeekendOnly, MinPlayers, NotifyNew; unique (UserId, Name) |
| TournamentDirectorySweeps | Buchfuehrung je Foederation | Federation (PK), LastSweptAt (**nur bei Erfolg**), LastAttemptedAt, LastRowCount, LastError, ConsecutiveFailures |
| TournamentDirectoryVenues | ALLE Spielorte eines Turniers — bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit", „Schwaz/Jenbach/Kufstein"; auf dem Dev-Stand 270 Eintraege). Die Koordinaten am Eintrag bleiben der HAUPT-Spielort (der erste); die Tabelle traegt nur Turniere mit MEHR als einem. Umkreissuche und Karte fragen sie mit: ein Turnier gilt als in der Naehe, wenn EINER seiner Orte in der Box liegt, und die angezeigte Entfernung ist die zum naechsten | TournamentDirectoryEntryId (Cascade), Ordinal (0 = Hauptort), Name (≤200), SourceText? (≤300, der Textabschnitt — Nachvollziehbarkeit), Lat/Lon, GeoSource; Index (Lat, Lon) + (EntryId, Ordinal) |
| TournamentDirectoryRounds | Die einzelnen SPIELTERMINE eines Turniers. Start und Ende sagen bei einer Liga nicht, wann gespielt wird — elf Runden von September bis April liegen Wochen auseinander, und der Kalender zeigte die Liga deshalb an rund 200 Tagen ohne Schach. Gefuellt vom `TournamentRoundPlanService` (chess-results `art=14`), nur fuer Eintraege ueber 8 Tage mit mehr als einer Runde. Leer = nicht bekannt, dann gilt der ganze Zeitraum | TournamentDirectoryEntryId (Cascade), Number (Rundennummer), Date, TimeText? (≤40, Rohtext „14:00 Uhr" — fuer den Kalender zaehlt der Tag); Index (Date) + **UNIQUE (EntryId, Number)** |
| TournamentDirectorySources | Auf WELCHER Seite ein Turnier gefunden wurde und unter welcher Nummer dort — **n-zu-n**, dasselbe Turnier steht auf mehreren. Ohne Herkunftsvermerk ist spaeter nicht zu sagen, woher eine Angabe kommt, und genau das entscheidet bei Widerspruch. Die Identitaet des Eintrags liegt seit 0.428.0 in `TournamentDirectoryEntry.PublicId` — genau weil mit dem FIDE-Kalender eine zweite Quelle wirklich Turniere liefert und die chess-results-Nummer dort fehlt | TournamentDirectoryEntryId (Cascade), Kind (Unknown/ChessResults/Fide/Manual), ExternalId (≤60), Url? (≤500), FirstSeenAt, LastSeenAt; **UNIQUE (Kind, ExternalId)** + Index (EntryId) |
| TournamentDirectoryIgnores | „Dieses Turnier will ich nicht sehen" je Nutzer. Gemerkt wird die IDENTITAET (`PublicId`) und nicht die Eintrags-Id (ein Eintrag kann verschwinden und wiederkommen, die Entscheidung soll gelten) — deshalb auch kein FK aufs Turnier, wie bei `TournamentSubscription` | UserId (Cascade), PublicId (≤24), CreatedAt; **UNIQUE (UserId, PublicId)**. Wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) |
| PlayerTournamentResults | Zwischenspeicher des Turnierverlaufs: die Teilnahme EINES Spielers an EINEM Turnier samt Ergebnis. Der Schluessel ist der **SPIELER, nicht das Konto** — die Historie ist fuer jeden dieselbe, ein Freund benutzt denselben Speicher. Die Kartenwerte werden genau einmal geholt (ein abgeschlossenes Turnier aendert sich nie wieder) | PlayerKey (≤40, `fide:…`/`cr:…`/`name:…`), ChessResultsId (≤20), Snr, TournamentName (≤500), EndDate?, Rank?/Rounds?/PlayerCount? (aus der Trefferliste), Points? (5,2)/PerformanceRating?/RatingChange? (6,2)/RatingInternational? (aus der Spielerkarte), **GamesPlayed?** (gespielte Partien von der KARTE — nicht die Rundenzahl), **CardVersion** (Fassung des Kartenabrufs; aeltere werden einmal nachgeholt, sonst bekaeme der Bestand ein spaeter ergaenztes Feld nie), CardFetchedAt? (gesetzt AUCH bei leerem Ergebnis — sonst wird dieselbe Seite jedes Mal erneut geholt; bei einem NETZfehler dagegen nicht), UpdatedAt; **UNIQUE (PlayerKey, ChessResultsId)** + Index (PlayerKey, EndDate) |
| TournamentTimeControls | Die BEDENKZEIT eines Turniers — je TURNIER, nicht je Teilnahme: zwei Konten im selben Open teilen sie sich, und sie kostet einen eigenen Seitenabruf (chess-results fuehrt sie nur in der Turnierdetail-Ansicht, die Spielersuche liefert sie nicht). Gespeichert wird Rohtext UND abgeleitete Klasse, damit eine geaenderte Einordnungsregel ohne neuen Abruf auf den Bestand wirkt | ChessResultsId (PK, ≤20), TimeControlText? (≤300, „90 min + 30 sec / Zug"), Speed (`TournamentSpeed`, via `TournamentSpeedClassifier`), FetchedAt (gesetzt AUCH ohne gefundene Bedenkzeit — sonst wird dieselbe Seite bei jedem Durchgang erneut geholt; ein NETZfehler legt nichts an), **Version** (Fassung des Abrufs; aeltere werden EINMAL nachgeholt, sonst friert ein Parser-Fehler als „nennt keine Bedenkzeit" ein) |
| TrackedPlayers | Ein Spieler, dessen Verlauf ein Nutzer mitverfolgt, ohne dass dieser Spieler ein KONTO haette (das eigene Kind, ein Vereinskamerad, der naechste Gegner). Gespeichert ist eine SUCHE, kein Personendatensatz — genau die Felder, aus denen `TournamentHistoryService.IdentityOf` den Spielerschluessel baut. Der Verlauf selbst liegt weiter an `PlayerTournamentResult.PlayerKey` und wird geteilt | UserId (Cascade), PlayerKey (≤40, wie `PlayerTournamentResult.PlayerKey` — derselbe Spieler ueber Name und ueber FIDE-Nummer gefunden ist EIN Eintrag), DisplayName (≤200), LastName (≤100), FirstName? (≤100), FideId? (≤20), ChessResultsId? (≤20), CreatedAt; **UNIQUE (UserId, PlayerKey)**. Deckel 20 je Konto (`TournamentHistoryController.MaxTracked`); wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) |
| PlayerHistorySyncs | Wann die Trefferliste EINES Spielers zuletzt geholt wurde (TTL 12 h). Nach einem Fehlschlag bleibt der Zeitstempel ALT, damit der naechste Aufruf es wieder versucht statt zwoelf Stunden zu warten | PlayerKey (PK, ≤40), LastFetchedAt, LastError? (≤500) |
| UserViewStates | Anzeige-Zustand EINER Seite fuer EINEN Nutzer (heute die Filterleiste des Turnierkalenders). Fuer den Server **OPAK** — nur JSON-Gueltigkeit, Objekt-Form und Groesse werden geprueft; er wird nie abgefragt. `ViewKey` kommt aus `ViewStateService.AllowedKeys`, sonst waere das ein freier Speicher je Nutzer | UserId (Cascade), ViewKey (≤64), Json (**text**, ≤8192 Zeichen — `varchar(8192)` zaehlte in utf8mb4 mit 32 KB gegen das 64-KB-Zeilenlimit), UpdatedAt; **UNIQUE (UserId, ViewKey)**. Wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) |
| GeoPlaces | GeoNames-Ortslexikon (CC BY 4.0) | Country (ISO2), PostalCode?, Name, NameNormalized, Lat/Lon, Kind (PostalCode/City/Region), Population; Index (Country, PostalCode), (Country, NameNormalized) |
| Repertoires | PGN-Sammlungen | UserId, Name, Description, Kind (Enum None/Opening/Middlegame/Endgame), IsPublic, CreatedAt, UpdatedAt, **ImportVersion (Pipeline-Version; < CurrentVersion ⇒ veraltet/reprozessierbar — live ausgewertet, also meist nur Versions-Mark; Chessable-Repertoires mit oids bekommen dabei die Zugtexte aus dem Linien-Cache)** |
| RepertoireFiles | Einzelne PGNs | RepertoireId, FileName, PgnContent (LONGTEXT), FileSize |
| TournamentSubscriptions | Turnier-Abo | UserId + CrawlerTournamentId (unique pair), TournamentName, EventDate (`DateOnly?`, Turniertermin — steuert Refresh-Crawl + Bot-Turnier-Einordnung) |
| TournamentFavorites | Markierte Turniere | UserId + CrawlerTournamentId |
| TournamentUserSettings | Per-Turnier-User-Einstellungen | UserId + TournamentId, Highlights/Notes/Pinning |
| TournamentMonitors | Runden-Monitor | TournamentId, RoundsCount, LastSeenRound, AutoSubscribed; `RoundMonitorService` checkt periodisch |
| Puzzles + PuzzleAttempts | Standard-Puzzle-Pool + Versuche | klassische Lichess-Puzzles + Pro-User-Versuche (UserId Cascade) |
| KidsLevelProgresses | KidHub-Fortschritt eines angemeldeten Kindes je Stufe (0.563.0) | UserId (Cascade), Level, Stars (0–3, beste), RunIndex/RunMistakes (laufender Durchgang), RunAt (Zeit laut Gerät — der jüngere gewinnt), UpdatedAt; **UNIQUE (UserId, Level)** |
| KidsCourseProgresses | KidHub: „Von vorn" je Kinderkurs | UserId (Cascade), BookId (Cascade), ResetAt? (Linien davor zählen nicht), UpdatedAt; **UNIQUE (UserId, BookId)** |
| KidsCourseLines | KidHub: gelöste Linien eines Kinderkurses | UserId (Cascade), BookId (Cascade), BookPuzzleId (**kein FK**), SolvedAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId) |
| KidsPuzzles | Kinder-Leiter (KidHub): als „besonders einfach" markierte Lichess-Puzzles mit Platz in der Stufenfolge | **PuzzleId (PK, FK → Puzzles, Cascade)**, Level (1-basiert), Position (0-basiert, leichteste zuerst), Theme (≤20: mate1/promote/capture/fork/skewer/mate2/pin/discovered), PieceCount, SolverMoves (1/2), CurriculumVersion (≠ Code ⇒ Seeder baut neu); Index (Level, Position). Gefüllt von `KidsPuzzleSeeder`/`POST /api/admin/kids/rebuild` |
| Tags + PuzzleTags | Normalisierte Puzzle-Themen für schnellen Themen-Filter | Tag.Name (unique); PuzzleTag composite PK (PuzzleId, TagId) + denormalisiertes Rating, Index **(TagId, Rating)** → indexgestützter Themen-Filter statt LIKE-Scan. Import pflegt automatisch; **einmaliger Backfill bestehender Puzzles via `POST /api/admin/puzzles/backfill-tags`** (Hintergrund-Job). Bis Backfill: Fallback auf LIKE |
| BookPuzzles | Buch-Puzzles | LineId (unique), BookFileName (indexed), Round, Fen, Moves, Title, Chapter, Comment, **MoveComments (LONGTEXT, JSON `{plyIndex:text}`; Pro-Zug-Kommentare der Hauptlinie, Schlüssel = 0-basierter Halbzug NACH dem Zug, -1 = Einleitung; beim Durchspielen/Review angezeigt; der Kurs-Import faltet seit Pipeline 19 JEDE Hauptlinien-Variante mit ihren Zugnummern in den Kommentar ihres Zugs, `features/puzzles/comment-variation.util.ts` macht die Züge dort klickbar und verankert sie über die Zugnummer — NUR dort: geht ein Zug an seiner Nummer nicht (z. B. mehrdeutig, zwei Springer nach e4), bleibt er Text, statt in einer anderen Stellung der Partie zu landen)**, Difficulty, BookRating, Tags, **HintsJson (LONGTEXT, JSON `{lang:[h1,h2,h3]}`; vorberechnete gestufte Tipps de/en/hr, per LLM erzeugt) + HintsVersion (int, 0=keine; entkoppelt von Book.ImportVersion) + HintsFlagged (bool; Admin-Review-Flag „dumme Tipps", per Solver-Button)**, **Retired (indexed; ausgemustert → nicht mehr in Daily/Random/Blind-Pools)**, **Source (≤16, nullable; null = vollwertig/getGame, "review" = aus getReview vorbelegter Lücken-Füller — zählt als vollwertig gecacht (Overlay-✓, kein getGame-Re-Fetch; getReview≡getGame für die Linie) und wird, falls getGame doch mal für den oid importiert wird, per oid IN-PLACE ersetzt)** |
| SharedPuzzleAttempts | „Track solves" geteilter Einzel-Puzzles (opt-in per Teilen-Link `?track=1`) — Erstversuch je Besucher | BookPuzzleId (indexed), **IdentityKey** (`u:{userId}` eingeloggt / `s:{sessionId}` anonym), Solved (true nur saubere Erstlösung; Fehlzug/Aufgeben/Reset = false), **HintsUsed (höchste angesehene Tipp-Stufe 0–3 beim Erstversuch)**, CreatedAt; **UNIQUE (BookPuzzleId, IdentityKey)** = nur 1. Versuch zählt. Kein harter FK (Index genügt) |
| BookPuzzleAttempts | Buch-/Tagespuzzle-Versuche | BookPuzzleId (Restrict) + UserId (Cascade, nullable für Anon) + AnonymousSessionId, Solved, TimeSeconds, AttemptedAt, **HintsUsed (höchste angesehene Tipp-Stufe 0–3)**; Index (BookPuzzleId, AttemptedAt) + (BookPuzzleId, UserId) + **UNIQUE (BookPuzzleId, AnonymousSessionId)** (eine anonyme Lösung je Session; auth. Versuche = NULL-Session → mehrfach erlaubt) |
| Books | Buch-Metadaten | FileName (unique), Title, Author, **Kind** (Enum Puzzle/Study, Default Puzzle; steuert das Trainingsziel-Routing der Kurszeit), **IsCalculation (bool, Default false; „Kalkulationsbuch" = Stellungen ohne Lösung → Kurs öffnet den Kalkulations-Modus statt des Solvers; geschaltet auf der Kurs-Detailseite von Besitzer/Admin, nicht im Admin-Tab)**, **SourcePgn (LONGTEXT, nullable; Roh-PGN als Reprocessing-Quelle, null bei Altbestand/JSON-Import; seit 0.508.3 per Tabellensplitting als eigene Entität `BookSource` gemappt, NICHT als Property von `Book`)**, **ImportVersion (Pipeline-Version; < CurrentVersion ⇒ veraltet → Reprocess-Knopf)**, **CommentLanguage? (≤8; Quellsprache der Kommentare fuer die Kurs-Uebersetzung, `und` = nicht bestimmbar, `null` = nie gefragt)**, **ForKids (bool, Default false; Kurs auf der Kinderseite KidHub, nur Admin, öffnet die Aufgaben dort ohne Anmeldung)**, **KidsTitles? (≤1000, JSON je KidHub-Sprache; eigene Titel dort)** |
| CalculationTrees | Selbst eingeklickter Analysebaum EINES Users zu EINER Stellung eines Kalkulationsbuchs (Kalkulations-Modus; es gibt keine Lösung, der Nutzer legt seine Varianten für beide Seiten selbst an) | UserId (Cascade) + BookId (denormalisiert für die „bearbeitet"-Zähler, Cascade) + BookPuzzleId (**Restrict**, wie CoursePuzzleResult — vermeidet doppelte Cascade-Pfade), **TreeJson (LONGTEXT; für den Server OPAK, nur JSON-Gültigkeit + Maximalgröße geprüft; LEER erlaubt = Zeile trägt nur Trainings-Werte, „hat Baum" ist überall `TreeJson != ''`, nicht „Zeile existiert")**, **ChosenSan (20)/ChosenUci (10) = die eine Festlegung, SecondsSpent (int, Default 0, aufsummiert), SecondsToken (64, nullable) + SecondsTokenApplied (int, Default 0) = Idempotenz-Marke des zuletzt verbuchten Zeit-Deltas samt darunter angerechneter Sekunden (Retry darf die addierte Zeit nicht doppelt buchen), Grade (int?, 0–4 = benannte Stufe `CalculationGrade`, `null` = unbewertet ≠ Stufe 0 „nicht gelöst"; Punkte sind eine Ableitung via `CalculationGrades.PointsFor` und werden NICHT gespeichert)**, CreatedAt, UpdatedAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId) |
| CalcEditions | Kalkulations-SERIE (Phase 1, eigener Bereich à la Wochenpost): terminiert EIN Wochen-Kapitel eines Kalkulationsbuchs (Video + Freigabe). Kapitel OHNE Ausgabe = ungegatet (Übergang); Gating zentral in `CalcVisibility.HiddenChaptersAsync` (`Services/CalcVisibility.cs`), genutzt vom `CalculationService` UND von der Kurs-Detailseite (`CourseAuthoringService`) (Wochen mit Ausgabe versteckt bis `PublishAt`, für Tester ab `TesterPreviewAt` — Phase 2; Owner/Admin sehen Entwürfe). Verwaltung nur Besitzer/Admin | BookId (Cascade von Book), Chapter (≤300, = Wochen-Kapitelname), Title? (≤300), VideoUrl? (≤500), PublishAt (DateTime), TesterPreviewAt? (DateTime, früher), CreatedAt, UpdatedAt, PublishAnnouncedAt?/TesterAnnouncedAt? (Ankündigungs-Marker, Phase 3b), TesterAnnouncedUserIds? (CSV der Tester-Runden-Empfänger); **UNIQUE (BookId, Chapter)** |
| CalcSeriesMembers | Kalkulations-SERIE (Phase 2): privater VERTEILER eines Serien-Buchs. Mitgliedschaft ist ein zusätzlicher Zugriffspfad in `CourseAccess.CanAccessAsync` — sobald das Buch nicht mehr `IsPublic` ist, sehen nur noch Mitglieder (+ Owner/Admin/Share/Gruppe) den Kurs. `IsTester` gibt einem Mitglied Frühzugang (Wochen ab `TesterPreviewAt`). Verwaltung nur Besitzer/Admin, neu eintragen nur Freunde des Besitzers, Selbst-Austragen erlaubt (A7-004) | BookId (Cascade von Book), UserId, IsTester (bool), CreatedAt; **UNIQUE (BookId, UserId)** |
| CalcEditionViews | Kalkulations-SERIE (Phase 3): „Gesehen"-Vermerk — ein Verteiler-MITGLIED hat eine Stellung einer terminierten Woche geöffnet. Erfassung automatisch in `CalculationService.GetPositionAsync` (nur Mitglieder; Owner/Admin/öffentliche Betrachter zählen nicht), einmalig je Ausgabe+Nutzer. Übersicht nur Besitzer/Admin | CalcEditionId (Cascade von CalcEdition), UserId, ViewedAt; **UNIQUE (CalcEditionId, UserId)** |
| DailyPuzzles | Persistierte Tagespuzzle-Zuordnung je UTC-Datum | Date (PK, DATE), BookPuzzleId (Restrict), CreatedAt; vom `DailyPuzzleScheduler` (00:00 UTC) gesetzt oder on-demand bei `/daily/{date}` (nur heute/gestern); Admin-Regenerate ändert nur `BookPuzzleId` (Datum bleibt) |
| Groups | Benutzergruppen | Name (unique), Description, CreatedAt |
| UserGroups | User<->Gruppe (n:m) | Composite PK (UserId, GroupId), Cascade von AppUser + Group |
| EndlessProgresses | Endless Config+Highscore | UserId (unique, nullable), AnonymousSessionId, StartElo, Themes, FasttrackThreshold1/2, StockfishDepth, Highscore, ActiveGameState (LONGTEXT) |
| EndlessSessions | Abgeschlossene Endless Sessions | UserId (nullable), AnonymousSessionId, Timestamp, TotalSolved, MaxRating, DurationSeconds, ConfigJson (TEXT), MistakeAtRatings |
| CourseLineResets | Kapitel-Reset je Linie und User (0.672.3): Versuche davor zählen im Kurs nicht mehr, das Zeit-Log `CourseAttempts` bleibt | UserId (Cascade) + BookId (Cascade) + BookPuzzleId (**Restrict**, abgeräumt über `BookPuzzleDependents`), ResetAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId). Der buchweite Reset löscht die Zeilen des Buchs |
| CourseProgresses | Per-Kurs-Zustand (Buch) | UserId + BookId (unique pair), LastMode ("sequential"/"random"), CreatedAt, UpdatedAt |
| CoursePuzzleResults | Gelöste Buch-Puzzles im Kurs (idempotente „gelöst"-Menge für Fortschritt) | UserId + BookPuzzleId (unique pair), BookId (denormalisiert, indexed mit UserId), SolvedAt, TimeSeconds (nur Erstlösung; **nicht mehr Aggregations-Quelle**) |
| CourseAttempts | Append-only Zeit-Log JEDES Kurs-Versuchs (gelöst/fehlgeschlagen/Wiederholung) für die akkumulierte Kurs-/Studienzeit im Trainingsziele-Tracker | UserId (Cascade) + BookId (denormalisiert für Kind-Join, Cascade) + BookPuzzleId (Restrict), Solved, TimeSeconds, AttemptedAt, **HintsUsed (höchste angesehene Tipp-Stufe 0–3)**; Index (UserId, AttemptedAt) |
| BookGroupAccesses | Welche Gruppe darf welches Buch als Kurs sehen | Composite PK (BookId, GroupId), Cascade von Book + Group, Index GroupId |
| CourseShares | Persönlichen Kurs (Book.OwnerUserId) person-zu-person mit ausgewählten Nutzern teilen (Empfänger sieht/löst mit eigenem Fortschritt, kann nicht verwalten) | BookId (Cascade von Book), OwnerId + RecipientId (beide Restrict-FK auf AppUser, analog Friendship — vermeidet doppelte Cascade-Pfade), SharedAt; **UNIQUE (BookId, RecipientId)** + Index (RecipientId). Nur mit Freunden teilbar (Admins an alle); DeleteBook räumt Freigaben explizit ab |
| CourseLinks | Persönliche Kurs-Verknüpfung (Buch↔Workbook) für den Schnellwechsel — SYMMETRISCH in 2 Zeilen (A→B, B→A) | UserId (Cascade), BookId (Cascade von Book), LinkedBookId (**kein FK** → vermeidet 2. Cascade-Pfad von Book; Gegenzeile + DeleteBook-Cleanup halten Konsistenz), CreatedAt; **UNIQUE (UserId, BookId)** = je Buch max. 1 Partner. Beide Kurse müssen zugänglich sein; DeleteBook räumt beide Richtungen ab |
| RepertoireShares | Persönliches Repertoire person-zu-person teilen (Empfänger sieht/öffnet/downloadet/trainiert mit eigenem SR-Fortschritt, kann nicht bearbeiten/löschen/weiterteilen) | RepertoireId (Cascade von Repertoire), OwnerId + RecipientId (beide Restrict-FK auf AppUser, analog CourseShare), SharedAt; **UNIQUE (RepertoireId, RecipientId)** + Index (RecipientId). Nur mit Freunden teilbar (Admins an alle); RepertoireService.DeleteAsync räumt Freigaben explizit ab. Training-Zugriff via `RepertoireTrainingService.CanTrainAsync` (Besitzer ODER Empfänger); Repertoire-SR-Intervall-Override bleibt owner-only |
| WeeklyPosts | Wochenpost (terminiertes PGN) | Title, FileName, PgnContent (LONGTEXT), FileSize, **PuzzleCount (beim Upload gecachte Puzzle-Anzahl; 0=Alt → Lazy-Backfill)**, ScheduledAt (indexed), CreatedAt, UpdatedAt |
| WeeklyPostAttempts | Per-User-Fortschritt Wochenpost | WeeklyPostId + UserId + PuzzleIndex (unique triple), Solved, TimeSeconds, AttemptedAt; beide FKs Cascade |
| GroupTrainingGoals | Coach-Vorlage Trainingsziel je Gruppe | GroupId (unique, Cascade von Group), PuzzleMinutes, BookMinutes, ChessableMinutes, PlayGames (Partien/Woche), WeeklyDaysTarget, CreatedAt, UpdatedAt |
| UserTrainingGoals | Persönlicher Trainingsziel-Override | UserId (unique, Cascade), PuzzleMinutes, BookMinutes, ChessableMinutes, PlayGames (Partien/Woche), WeeklyDaysTarget, CreatedAt, UpdatedAt |
| ChessableProblemMoves | „Schwierige Züge" je Chessable-Linie und User (aus den von RepCheck mitgeschnittenen getList/getGame-Antworten; Upsert bei Training + Kurs-Holen) | UserId (Cascade), Bid (≤12), Oid (≤32), NHard? (Chessables Zähler aus getList), ProblemMovesJson? (LONGTEXT, `thisUser` roh/opak; "{}" = zuletzt fehlerfrei), LastReviewedAt?, UpdatedAt; **UNIQUE (UserId, Bid, Oid)** |
| ChessableReviewLines | Rohes getReview-JSON EINER trainierten Chessable-Linie je User (zweite Linien-Quelle neben getGame; Lücken-Füller für den Kurs-Aufbau, `MergeIntoCourseAsync`) | UserId (Cascade), Bid (≤12), Oid (≤32), Json (LONGTEXT, opak, erst beim Kurs-Aufbau geparst), ChapterTitle? (≤300), UpdatedAt; **UNIQUE (UserId, Bid, Oid)** |
| AnonymousChessableReviewLines | Wie ChessableReviewLines, aber für Nutzer OHNE RookHub-Token: identifiziert über die **Chessable-uid** statt einen Account (KEIN FK). Wird beim Verknüpfen des Bearers in `ChessableReviewLines` übernommen (`ClaimAnonForUidAsync`); Retention 90 Tage | ChessableUid (≤32), Bid (≤12), Oid (≤32), Json (LONGTEXT), ChapterTitle? (≤300), CreatedAt, UpdatedAt; **UNIQUE (ChessableUid, Bid, Oid)** + Index (ChessableUid) |
| ChessableSessionMoves | Append-only Roh-Log der SITZUNGS-Ergebnisse trainierter Chessable-Linien (aus dem von RepCheck mitgeschnittenen saveProgress-REQUEST): je Halbzug u. a. falsch gespielte Züge (wrong[]), Overstudy/Alternative, Level, Punkte. Eine Zeile je Linie UND Durchlauf (bewusst kein Upsert — Historie für spätere Auswertung); Trim auf 200k Zeilen je User | UserId (Cascade), Bid (≤12), Oid (≤32), MovesJson (LONGTEXT, opak, ≤64 KB), CreatedAt; Index (UserId, Bid, Oid) |
| ChessableActivities | Append-only Zeit-Log aktiver Chessable-Trainingszeit (von RepCheck-Extension gemeldet) für die Kategorie „Chessable" im Trainingsziele-Tracker | UserId (Cascade), TimeSeconds, MovesTrained, **LinesTrained (abgeschlossene Varianten, seit RepCheck v1.34; 0 bei Altbestand)**, CourseKind?, CourseId?, CourseName? (Modus-Label-Müll wird beim Schreiben verworfen/über die Kurs-ID geheilt), AttemptedAt; Index (UserId, AttemptedAt) |
| ManualActivities | Manuell (selbst) eingetragene Offline-Trainingsaktivität — speist bestehende Tracker-Kategorien, editier-/löschbar | UserId (Cascade), Date (DateOnly), Kind (Enum OtbGame/OfflinePuzzle/OfflineStudy/Coaching), Amount (Partien bzw. Minuten), Note? (≤200), CreatedAt; Index (UserId, Date) |
| LibraryGames | **Rohbestand**: eingelesene PGN-Sammlungen, aus denen Punktepartien ausgewaehlt werden — noch nicht gerechnet, noch nicht sortiert (Details im Punktepartie-Kapitel) | SourceFile?/SourceTitle?/SourceRef?/ExternalGameId?, MovesHash? (Index), DuplicateOfId? (self, Restrict), Kopfdaten (White?/Black?/WhiteElo?/BlackElo?/Result?/Event?/Site?/Round?/PlayedOn?/Eco?/StartFen?/PlyCount?), Annotator? (Index), CommentCount?/CommentedPlies?/CommentChars?/NagCount?/VariationCount?, Languages?, Score?, Status, GameAnalysisId? (**kein FK** — die Bibliothekszeile ueberlebt das Loeschen der Analyse), Note?, Pgn (LONGTEXT); Indizes (Status, Score), (CommentedPlies, PlyCount), SourceTitle |
| CommentSets | EIN Satz Zug-Kommentare in EINER Sprache zu EINER Partie ODER einer Kurs-Linie — getrennt vom PGN bzw. von der Linie, damit Quelle und Uebersetzung unterscheidbar bleiben (Details im Punktepartie-Kapitel) | LibraryGameId? (Cascade) ODER GameAnalysisId? (Cascade) ODER **BookPuzzleId? (Cascade, Kurs-Linie, nur Uebersetzungen)** — genau EINES, Language (≤8), Origin (Source/Machine/Human), TranslatedFrom? (≤8), Model? (≤60), Status (Draft/Ready), CreatedAt/UpdatedAt; **UNIQUE (LibraryGameId, Language)** + **UNIQUE (GameAnalysisId, Language)** + **UNIQUE (BookPuzzleId, Language)** |
| CommentTexts | Die Zeilen eines Satzes — je Halbzug eine | CommentSetId (Cascade), Ply (zaehlt wie `GameAnalysisPosition.Ply`; `-1` = vor dem ersten Zug; bei Kurs-Saetzen `-2` Kommentar, `-3` Titel, `-4` Kapitel — `CourseTextSlots`), Text (LONGTEXT), **SourceHash? (≤16, Index; Fingerabdruck der Vorlage, Pflicht bei Kurs-Saetzen, `null` bei Partien)**; **UNIQUE (CommentSetId, Ply)** |
| CourseTranslationJobs | Auftrag „Kurs in Sprache uebersetzen" — Tabelle seit 0.547.0, Auftraege + Hintergrunddienst seit 0.548.0 (`CourseTranslationJobService`, `CourseTranslationWorker`) | BookId (Cascade), Language (≤8), RequestedByUserId? (**kein FK**; `null` = Automatik), Status (Queued=0/Running=1/Done=2/Failed=3/Cancelled=4), LinesTotal/LinesDone/LinesFailed, CreatedAt, StartedAt?, FinishedAt?, LastError? (≤500); Index (Status, CreatedAt) + (BookId, Language, Status). „Ein offener je (Kurs, Sprache)/je Nutzer" erzwingt der Dienst, nicht die DB |
| RememberedPositions | Auf chessable.com „gemerkte" Stellungen (RepCheck „Remember line") **und Stellungen der Hintergrund-Analyseaufträge** (einmal je Stellung, `SourceUrl=/analysis/jobs`); die Liste trägt den jüngsten Auftrag als `Analysis` mit | UserId (Cascade), Fen (≤120), CourseId? (≤32), **CourseName? (≤200; über den Chessable-Bearer aufgelöst — Extension-mitgeliefert oder serverseitig aus der gecachten Kursliste)**, SourceUrl? (≤1000), CreatedAt; Index (UserId, CreatedAt) |
| GameRecaps | „Kurz erzählt" (0.541.0): je Partie und Sprache EINE Nacherzählung für Link-Vorschau + Partieseite (`GameRecapService`) | SavedGameId (Cascade), Language (≤8), Text (≤1000; der Dienst lässt höchstens 600 zu), Model? (≤80), CreatedAt; **UNIQUE (SavedGameId, Language)** |
| GameRoasts | „Roast my game" (0.535.0): je eigener Partie, Sprache und Stil der zuletzt gewürfelte Kommentar (`GameRoastService`) | SavedGameId (Cascade), Style (≤12), Language (≤8), Text (≤2000), Model? (≤80), **Automatic (nach der Analyse von selbst geschrieben, 0.540.0 — zählt nicht im Tagesdeckel, „Neu würfeln" setzt false)**, CreatedAt; **UNIQUE (SavedGameId, Language, Style)** |
| CommentEmbeddings | „Frag die Kommentare" (0.536.0): Kommentar-Stück einer Bibliothekspartie + Vektor | LibraryGameId (Cascade, Index), FromPly/ToPly, Text (≤1600, zugleich der Auszug), **Vector (`VECTOR(512)`, Kosinus-Index per SQL)**, Model? (≤80), CreatedAt |
| GameMoveExplanations | „Warum war das ein Fehler?" (0.534.0): ein Text je Analyse, Halbzug und Sprache, geschrieben vom Sprachmodell auf eigener Hardware (`GameMoveExplanationService`) | GameAnalysisId (Cascade), Ply, Language (≤8), Class (≤12: inaccuracy/mistake/blunder/miss), **Viewpoint (≤5: white/black = Seite des Besitzers, leer = neutral; 0.540.0)**, Text (≤1200), Model? (≤80), **MasterLibraryGameId? (kein FK) + MasterText? (≤600) — der mitgegebene Meisterkommentar zur selben Stellung (0.542.0)**, CreatedAt; **UNIQUE (GameAnalysisId, Ply, Language)** |
| SavedGames | Von chess.com/lichess (über RepCheck) gespeicherte Partien — Bereich „Partien" | UserId (Cascade), Source (≤20: chess.com/lichess), ExternalId? (≤120, Dedup), Pgn (LONGTEXT, serverseitig gebaut), White?/Black? (≤120), Result? (≤12), PlayedAt?, SourceUrl? (≤1000), **WhiteElo?/BlackElo? + TimeControl? (≤32, „180+2“) + HeadersScanned (0.526.0 — die Partienliste zeigt Wertung und Bedenkzeit wie chess.coms Übersicht; das PGN dafür zu laden wäre derselbe Fehler, den `MoveCount` schon behoben hat. Der Altbestand bekommt seine Wertungen portionsweise aus dem PGN (`HeaderBackfillPerCall` = 50 je Listenaufruf), und die Marke `HeadersScanned` unterscheidet „noch nicht nachgesehen“ von „nennt keine Wertung“; die Bedenkzeit steht in keinem alten PGN und bleibt dort leer)**, ShareToken (≤32, UNIQUE; öffentlicher Link `/g/{token}`), **GameAnalysisId? (kein FK — die Analyse der Bewertungskurve; nur vom BESITZER gesetzt, kann ins Leere zeigen)**, **OwnerSide? (≤5, white/black — selbst festgelegte Seite, schlägt die Namenszuordnung; 0.531.0)**, **ReviewLanguage? (≤8 — Sprache der Seite beim „Partie analysieren“, darin entstehen Erklärungen und Roasts; 0.540.0)**, CreatedAt; Index (UserId, CreatedAt) + **UNIQUE (UserId, Source, ExternalId)** (Dedup hart erzwungen; NULL-ExternalId = mehrfach erlaubt) |
| ScoresheetScans | Eine Formular-Einlesung: das FOTO (bleibt nach dem Einlesen liegen) + Stand + Ergebnis (0.529.0) | UserId (Cascade), SavedGameId? (Cascade — das Foto geht mit der Partie; `null`, solange gelesen wird oder wenn es scheiterte), Photo (LONGBLOB, über 12 MB verkleinert), ContentType (≤40), FileName? (≤200), **PageCount (Vorgabe 1; Seite 2+ in `ScoresheetScanPages`, 0.600.0)**, NotationLanguage (≤8, Code oder `auto`), Status (Pending/Running/Done/Failed), Error? (≤40, Grund-Code), TranscriptionJson? (LONGTEXT, letzte Antwort des Modells), ResolutionJson? (LONGTEXT, Stand je Halbzug), Model? (≤60), Attempts, Rounds, **InputTokens/OutputTokens/CostMicroUsd (Kostenbremse — nach jedem Aufruf verbucht)**, **Purpose? (≤16; `league` = Einlesung für die Vereins-Datenbank, ohne Partie in „Meine Partien")**, **ClubId? (FK LeagueClubs Restrict, nur bei `league`; 0.698.0)**, **UserId ist NULLBAR (ohne Konto über einen LeagueHub-Teilen-Link), dann AccessKey? (≤32, UNIQUE, geheimer Schlüssel) + AnonIpHash? (≤64, HMAC der IP, nach 2 Tagen geleert)**, CreatedAt, StartedAt?, FinishedAt?; Index (Status, CreatedAt), (UserId, CreatedAt), SavedGameId. Löschpfade laden das Foto nie: Konto löschen entfernt die Zeilen (`ScoresheetScanService.RemoveWithoutLoading`), Partie löschen leert nur Foto/JSON und setzt `SavedGameId` null — die Zeile zählt weiter fürs Tageskontingent (`DetachWithoutLoading`, 0.568.1) |
| ScoresheetScanPages | Seite 2 und folgende eines Formulars über mehrere Fotos (0.600.0); Seite 1 bleibt `ScoresheetScans.Photo` | ScoresheetScanId (Cascade), Page (ab 2), Photo (LONGBLOB), ContentType (≤40), FileName? (≤200); **UNIQUE (ScoresheetScanId, Page)**. Partie löschen und Konto löschen räumen sie ohne Laden ab (`RemovePagesWithoutLoading`) |
| ClubMembers | ClubHub (0.613.0): Karteiblatt eines Kindes/Jugendlichen — KEIN Konto | FirstName/LastName (≤80, Index (LastName, FirstName); **LastName darf LEER sein = nicht bekannt, nie null**), BirthDate? (DateOnly), BirthYear? (bei Datum dessen Jahr), Level? (≤60, Freitext), **FideId? (≤16, nur Ziffern) + NationalId? (≤16, Personennummer ÖSB) + PhotoVersion? (bigint, Marke des Bilds, `null` = keins) — 0.640.0**, Archived, **IsTrainer (0.620.0: Trainer als Person — in jeder Anwesenheitsliste, keine Gruppe)**, **LinkedUserId? (FK SetNull, UNIQUE — ein Konto an höchstens einem Blatt)**, LinkCode? (≤16, UNIQUE, Einmal-Code) + LinkCodeExpires?, CreatedAt, CreatedByUserId? (kein FK), UpdatedAt |
| ClubMemberPhotos | Das Bild zum Karteiblatt (0.640.0), eines je Blatt — eigene Tabelle, damit Listen die Bytes nie laden | **MemberId (PK + FK, Cascade)**, Image (MEDIUMBLOB, JPEG ≤ 1200 px), Thumb (MEDIUMBLOB, JPEG ≤ 256 px — mit Kreis das Quadrat ums Gesicht), Width, Height, **FaceX?/FaceY?/FaceR? (double, Kreis ums Gesicht als Anteile, 0.641.0; `null` = keiner)**, UpdatedByUserId? (kein FK), UpdatedAt. Blatt löschen entfernt es ohne Laden (`RemoveMemberPhotoWithoutLoading`) |
| ClubContacts | Kontakte eines Kindes, beliebig viele | MemberId (Cascade), Kind (`phone`/`email`, ≤8), Value (≤200), Label? (≤80, „Mutter Daniela"), Position; Index (MemberId, Position) |
| ClubGroups | Trainingsgruppe | Name (≤80), Schedule? (≤200, Freitext), **Weekday? (1 = Mo … 7 = So — schlägt am Trainingstag die Anwesenheitsliste vor)**, Archived, CreatedAt |
| ClubGroupMembers | Kind ↔ Gruppe | PK (GroupId, MemberId), beide Cascade |
| ClubGroupTrainers | Konto als Trainer einer Gruppe (sieht deren Kinder, mit `club.trainer`) | PK (GroupId, UserId), beide Cascade |
| ClubSessions | Trainingseinheit | GroupId (Cascade), Date (DateOnly), Topic? (≤200, Thema), Notes? (TEXT ≤2000, „Was wurde gemacht?"), CreatedByUserId? (kein FK), CreatedAt; **UNIQUE (GroupId, Date)** |
| ClubSessionPhotos | Fotos einer Einheit (0.620.0) | SessionId (Cascade), Image (LONGBLOB, JPEG ≤ 1600 px), Thumb (MEDIUMBLOB, JPEG ≤ 320 px), Width, Height, CreatedByUserId? (kein FK), CreatedAt; Index SessionId |
| ClubAttendances | Anwesenheit je Einheit und Kind (oder Trainer) | PK (SessionId, MemberId), beide Cascade; Status (1 Present, 3 Absent — 2 „entschuldigt" gab es nur in 0.613.0, `ClubAttendanceTwoStates` hat sie auf 3 gesetzt) |
| ClubNotes | Datierte Trainer-Notiz zum Lernstand | MemberId (Cascade), AuthorUserId? (kein FK — bleibt ohne Namen, wenn das Konto geht), CreatedAt, Text (TEXT ≤2000); Index (MemberId, CreatedAt) |
| LeagueClubs | Vereine als Mandanten von LeagueHub (0.698.0, siehe „LeagueHub — Vereine als Mandanten") | Id, Name (≤120, UNIQUE), TeamPrefix (≤80, Anfang der Mannschaftsnamen), AnonName (≤60), Region (≤20; `tirol` | `bayern` — Migration `LeagueClubRegion` hat `Source` umbenannt: null → tirol, ligamanager → bayern), CreatedAt — Migration legt 1 = SK Schwaz, 2 = SK Weilheim an |
| LeagueClubMembers | Gruppe → Verein (0.698.0) | PK (ClubId, GroupId), **GroupId UNIQUE** (eine Gruppe gehört zu höchstens einem Verein); FK Club Restrict, Group Cascade |
| LeagueClubGames | Vereins-Datenbank von LeagueHub (0.573.0): eine hochgeladene Partie mit mindestens einem Ligaspieler (anonymisiert = mindestens eine Seite mit dem `AnonName` des Vereins) | **ClubId (FK LeagueClubs Restrict, Index (ClubId, Year); 0.698.0)**, Year? (nur das Jahr), White/Black (≤120, anonymisiert „Schwaz"), WhiteFide?/BlackFide? (≤16, Index), WhiteElo?/BlackElo?, Result (≤12), Event? (≤200, anonym leer), Plies, Pgn (LONGTEXT, Hauptvariante ohne Kommentare), MovesHash (≤64, Index), Anonymized, UploadedByUserId? (**kein FK**, nur bei nicht anonymisierten; Konto löschen setzt null), CreatedAt? (anonym leer), **UploadShareHash? (≤64, Index; SHA-256 des Teilen-Links, über den die Partie kam — auch bei anonymisierten; `null` = angemeldet)**, **LeagueGameId? (Index, kein FK; fest zugeordnete Brettpaarung, 0.678.0 — seit 0.716.1 stabil, gelesen nur über `LeagueGameLinks.ResolveAsync`) + LeagueTnr?/LeagueRound?/LeagueMatchNo?/LeagueBoard? (Schlüssel der Paarung, Index; Sicherheitsnetz)** |
| LeagueOnlineAccounts | Online-Konten eines Ligaspielers (je FIDE-ID): aus dem Bundle-Import oder seit 0.605.0 in LeagueHub gepflegt | FideId (≤16, Index), Site (lichess/chess.com), UserName, Url, Confidence (`sicher`/`wahrscheinlich`), Evidence? (≤1000, Kommentar), **Manual (in LeagueHub gepflegt — der Import lässt sie stehen)**, UpdatedAt?, SyncedAt?, SyncCursor (ms), SyncMore, SyncError? (≤300), GameCount, **AddedBy? (≤60, 0.630.0: Nutzername bzw. „anonym" über einen Teilen-Link) + AddedShareHash? (≤64, SHA-256 des Links)** |
| LeagueOnlineGames | Geholte Partien der Online-Konten (0.605.0) | AccountId (Cascade), FideId (denormalisiert), ExternalId (**UNIQUE (AccountId, ExternalId)**), PlayedAt, Speed (bullet/blitz/rapid/classical/correspondence), Rated, White (Farbe des Spielers), Result (aus seiner Sicht), Opponent?, OpponentRating?, PlayerRating?, Line (≤400, erste 30 Halbzüge), Moves (LONGTEXT), Plies; Index (FideId, White, PlayedAt) |
| LeagueAccountSuggestions | Vorschläge der Konto-Suche (0.607.0) | FideId, Site, UserName (**UNIQUE (FideId, Site, UserName)**), Url, Score, Evidence (≤500, die Hinweise), ProfileName?, Location?, LastActive?, Status (Open/Rejected — verworfene bleiben, damit sie nicht wiederkommen), CreatedAt, DecidedAt?, **Source? (≤16; `null` = Namenssuche, `team` = Team-Suche, 0.612.0)**; Index (Status, Score) |
| LeagueScoutAccounts | Konten aus den Tiroler Lichess-Teams und ihren Team-Battles (0.612.0, `LeagueTeamScout`) | UserName (PK, ≤30, klein), DisplayName (≤30), Teams? (≤500, „; "-Liste), PlayedFor? (≤200, Teams, für die es in einem Battle spielte), **Events? (≤500, Serien der Team-Battles ohne Runde, 0.619.0)**, FoundAt, CheckedAt? (Index; null = noch nicht geprüft), Result? (≤300, Ergebnis in Worten) |
| LeagueSelfReports | Selbst gemeldete Online-Konten (0.619.0, z. B. Meldeliste der Online-TMM 2021) — stärkster Beleg der Konto-Prüfung (i) | FideId (≤16, Index), Site (≤20), UserName (≤60), Source (≤120), Team? (≤200), **Reporter? (≤60, 0.629.0: null = selbst gemeldet, sonst wer die Liste gemeldet hat, z. B. „Ranni") + Note? (≤200, seine Anmerkung)**, CreatedAt; **UNIQUE (Site, UserName, Source)**. Eingespielt je Quelle (ersetzt) |
| LeagueAccountScans | Stand der Konto-Suche je Spieler (0.607.0) | FideId (PK), BirthYear? + Federation? (laut FIDE, über Lichess — unter 18 oder unbekannt = Konten verborgen, `LeagueHiddenAccounts`), ScannedAt, Note? („verborgen …", Fehler), Found, Version (Fassung der Regeln, 0.609.0) |
| LeagueBroadcasts | Lichess-Übertragungen, deren Partien in die Karten kommen (0.608.0) | TourId (PK, ≤12), Name, Location?, StartsAt?/EndsAt?, Manual (per Link), FoundAt, ImportedAt?, Finished (Index), Games (mit Ligaspielern), Error? |
| LeagueNameAliases | Gemerkte Namens-Zuordnungen der Vereins-Datenbank (0.579.0): PGN-Name → Spieler | NameKey (≤120, UNIQUE, klein ohne Akzente/Titel), Fide? (≤16), Name (≤120), UpdatedAt — kein Verweis auf Partie oder Nutzer |
| PrepPlayers | Spieler des Partiebestands der Spielervorbereitung (0.631.0) | Name, NameKey, FideId? (Index), KeyHash (long, UNIQUE; aus „#FIDE" bzw. NameKey), Games, FirstYear?/LastYear?, MaxElo?; Index (NameKey, Games) (0.634.0) |
| PrepEvents | Turniere des Partiebestands (0.631.0) | Name, Site?, KeyHash (long, UNIQUE) |
| PrepGames | Eine Zeile je Partie aus Megabase und/oder Lumbra (0.631.0) | WhiteId?/BlackId? (keine Fremdschlüssel; Index je (Id, PlayedOn)), WhiteElo?/BlackElo?, Result (byte), PlayedOn? (JJJJMMTT), EventId?, Round?, Eco?, Plies, Moves (SAN mit Leerzeichen), MovesHash (Index), Sources (Bit 1 Mega / 2 Lumbra) |
| PrepImports | Eingespielte Pakete (0.631.0) | Source + Chunk (UNIQUE), FirstGame, Read, Added, Duplicates, Discarded, DiscardReasons?, Millis, CreatedAt |
| LeagueClubDrafts | Entwurf eines PGN-Imports (0.595.0) — liegt, bis alles importiert oder verworfen ist | **ClubId (FK Restrict, 0.698.0)**, UserId? (**kein FK**, Konto löschen räumt ab; null = Teilen-Link), AccessKey? (≤32, UNIQUE), AnonIpHash? (≤64), Source? (≤16), Label? (≤300), Pgn (LONGTEXT), StateJson? (LONGTEXT, opak), Imported? (CSV), GameCount, CreatedAt, UpdatedAt; Index (UserId, UpdatedAt) |
| LeagueMegaPlayers | Spielerverzeichnis der ganzen ChessBase-Megabase (0.575.0) für die Namenssuche in LeagueHub; wird beim Einspielen komplett ersetzt | Name (≤120), NameKey (≤120, klein ohne Akzente, Index), FideId? (≤16, Index), Games, LastYear?, MaxElo? |
| GameReconstructions | Eine Partie, die aus Bruchstücken zusammengesetzt wird („Partie rekonstruieren") — Kopfdaten; die Teile hängen daran | UserId (Cascade), Title (≤200), White?/Black? (≤120), Event? (≤200), PlayedOn? (DateOnly), Result? (≤12), Note? (≤2000), **ShareToken? (≤32, UNIQUE — öffentlicher Link `/r/{token}`, NULL = nicht geteilt) + SharedAt?**, CreatedAt, UpdatedAt; Index (UserId, UpdatedAt). Deckel 50 je Konto |
| GameReconstructionParts | EIN Bruchstück: Zugfolge ODER Stellung. **`BlackToMove`** gilt nur für eine Zugfolge OHNE Anschluss (sonst sagt es die Stellung davor bzw. die FEN); beim ersten Teil heißt es „das ist nicht die Eröffnung". `Ordinal` ist die Reihenfolge in der Partie; **`ContinuesPrevious` (Vorgabe false) sagt, ob es NAHTLOS an das vorige anschließt** — ohne das liegt dazwischen eine Lücke, und genau das ist der Normalfall | GameReconstructionId (Cascade), Ordinal, Kind (Moves/Position), Moves? (≤4000, SAN ohne Zugnummern), Fen? (≤120), **Certain (Vorgabe true — „hier bin ich mir nicht sicher" ist die Auskunft; ein per Lückensuche eingesetztes Teil steht auf false)**, FromPly? (Erinnerungs-Hinweis, keine Verankerung), Note? (≤500), CreatedAt, UpdatedAt; Index (GameReconstructionId, Ordinal). Deckel 200 je Rekonstruktion |
| PlayTimeDailies | Gespielte Rapid-/Classical-Partien je UTC-Tag/Plattform | UserId + Date + Platform (unique, Cascade), Games (Anzahl Partien), UpdatedAt; befüllt vom `PlayTimeSyncService` |
| PlayTimeSyncs | Sync-Cursor externe Spielzeit | UserId + Platform (unique, Cascade), LastGameTimestamp (ms), LastSyncedAt, LastError |
| UserApiTokens | Personal-Access-Tokens für Maschinen-Clients (chess.com-Extension) | UserId (Cascade), Name, TokenHash (SHA-256, UNIQUE), Prefix (12 char), Scope ("extension" = Browser-Erweiterung, "engine" = Engine-Provider), CreatedAt, LastUsedAt, ExpiresAt (nullable); Index (UserId, Name) |
| PasswordResetTokens | „Passwort vergessen"-Einmal-Token | UserId (Cascade), TokenHash (SHA-256-Hex, UNIQUE), CreatedAt, ExpiresAt, UsedAt (nullable); Roh-Token nur per Mail, nie gespeichert. Beim Anfordern werden ältere offene Tokens des Users entwertet |
| MenuItemSettings | Admin-Override der Menü-Sichtbarkeit | ItemKey (PK, string), Level (Enum All/Registered/Groups/Admin); fehlt eine Zeile → Default aus `MenuRegistry` |
| MenuItemGroupAccesses | Welche Gruppe sieht einen gruppen-gegateten Menüeintrag | Composite PK (ItemKey, GroupId), Cascade von MenuItemSetting + Group, Index GroupId |
| ChessableCredentials | Per-User Chessable-Bearer (1:1) | UserId (unique, Cascade), EncryptedBearer (TEXT, AES via `EncryptionService`), **ChessableUid? (≤32; beim erfolgreichen `POST /api/chessable/test` aus der Chessable-Antwort BEWIESEN gesetzt — nicht aus dem ungeprüften JWT; verknüpft den User mit seiner Chessable-Identität fürs Claimen anonymer getReview-Linien)**, CreatedAt, UpdatedAt; Plaintext nie persistiert. Wird vom `ChessableProxyService` an piratechess durchgereicht |
| LichessExplorerCacheEntries | Zwischengespeicherte Lichess-Explorer-Antworten des Lochfinders — geteilt über alle Nutzer | CacheKey (≤255, **UNIQUE**; `{Auswahl}\|{Stellung}` mit Auswahl `lichess\|<Elo>\|<Tempo>\|` bzw. `masters\|` und Stellung = erste drei FEN-Felder), Json (TEXT, kompakt: Gesamtzahl + Züge mit uci/san/Partien/Eröffnung), FetchedAt (älter als 90 Tage → wird neu geholt und überschrieben) |
| LichessEngineCredentials | Per-User Lichess-API-Token (Scope `engine:read`) für die External-Engine-Anbindung (1:1) — trägt seit 0.537.0 auch die Hintergrund-Liste für direkt angemeldete Engines, dann OHNE Token | UserId (unique, Cascade), EncryptedToken (TEXT, AES via `EncryptionService`; LEER = kein Lichess-Token hinterlegt), **BackgroundEngineId? (≤64; Hintergrund-Engine für Analyseaufträge)**, CreatedAt, UpdatedAt; Plaintext nie persistiert. Der Token listet die External Engines des Lichess-Kontos; das je Engine gelieferte `clientSecret` wird NICHT persistiert (nur MemoryCache, 10 min) und verlässt den Server nie |
| ExternalEngineRegistrations | Direkt bei RookHub angemeldete External Engines (eigener Broker, 0.537.0) — der Provider registriert sie mit einem API-Token Scope `engine` | Id (PK, ≤20, `rhe_` + 12 Zeichen), UserId (Cascade), Name (≤200, **UNIQUE (UserId, Name)** — die Identität der Registrierung), ClientSecret (≤64, für den Anfragenden; nie im Browser), ProviderSelector (≤64, sha256(`providerSecret:`+Geheimnis) hex, Index — das Geheimnis selbst wird nie gespeichert), MaxThreads, MaxHash, Variants (CSV ≤200), ProviderData? (≤500), CreatedAt, UpdatedAt, LastSeenAt? (letzter Poll, minütlich geschrieben); höchstens 32 je Konto, Konto löschen räumt ab |
| AnalysisJobs | Hintergrund-Analyseaufträge (siehe „Hintergrund-Analyseaufträge") | UserId (Cascade), Fen (≤120), Title? (≤200), EngineId (≤64, Lichess `eei_…` oder direkt angemeldet `rhe_…`), TargetDepth, MultiPv (1–5), Status (Enum Queued/Running/Paused/Done/Failed), ReachedDepth, ResultJson? (LONGTEXT, letzte Broker-Zeile), **EvalText? (≤16, Bewertung der Hauptvariante — Listen laden dafür nicht die Roh-Zeile)**, **FruitlessAttempts (Läufe ohne Tiefenfortschritt → ab 3 Failed)**, SecondsSpent, LastError? (≤500), NextAttemptAt? (Backoff), CreatedAt, UpdatedAt, LastRunAt? (sticky hash), FinishedAt?; Index (UserId, Status) + (UserId, CreatedAt) |
| AnalysisHistoryEntries | Analyse-Verlauf des Analysebretts (0.603.0, Zugbaum seit 0.604.0), höchstens 20 je Nutzer | UserId (Cascade), StartFen (≤120), Moves (TEXT, HAUPTLINIE als UCI mit Leerzeichen), MoveCount (Hauptlinie), **TreeJson? (LONGTEXT, flacher Zugbaum samt Sternen/Bewertungen; null = Eintrag von 0.603.0)**, Current (Knoten-Index, -1 = Ausgangsstellung), Ply (dessen Tiefe), NodeCount, StarCount, Title? (≤200), CreatedAt, UpdatedAt; Index (UserId, UpdatedAt) |
| MoveComparisons | „Züge vergleichen" (0.602.0): eine Stellung, 2–4 Kandidaten | UserId (Cascade), Fen (≤120), Title?, Depth, Language (≤8), Status (Candidates/Replies/Explaining/Done/Failed), EngineOwnerUserId?, BestUci? (≤10), Model?, Error? (≤200), CreatedAt/UpdatedAt/FinishedAt?; Index (UserId, CreatedAt), Status |
| MoveComparisonLines | Je gerechnete Stellung eines Vergleichs: Kandidat (Stellung nach dem Zug, 3 Linien) oder Antwort (beste Antwort auf einen SCHWÄCHEREN Kandidaten, gespielt nach dem BESTEN) | MoveComparisonId (Cascade), Kind, CandidateUci (≤10), ReplyUci? (≤10), Ordinal, Fen (≤120, leer bei Illegal), State (Pending/Done/Failed/Illegal), AnalysisJobId? (kein FK, nach dem Einsammeln null — der Auftrag wird gelöscht), ResultJson? (LONGTEXT), ReachedDepth, Explanation? (≤1500, englische SAN); Index MoveComparisonId, AnalysisJobId |
| AdminMessages | Admin↔User-Direktnachrichten (Thread je User) | UserId (Cascade, = Thread-Schlüssel/Nicht-Admin-Teilnehmer), SenderId (Audit), FromAdmin (bool, Richtung), Body (max 4000), CreatedAt, SeenByUserAt?, SeenByAdminAt?; Index (UserId, CreatedAt) + (FromAdmin, SeenByAdminAt) |
| MessageThreads | Metadaten/Zuweisung einer Konversation (1 Zeile je User) | UserId (PK + FK AppUser Cascade), ClaimedByAdminId? (welcher Admin übernommen hat, **ohne FK** → vermeidet doppelte Cascade-Pfade; Name wird beim Abruf aufgelöst), ClaimedAt?; entsteht mit der ersten Nachricht |
| CiBuildReports | Per-Push gemeldete laufende Build-SHA/Ref eines Stacks, den rookhub nicht per HTTP erreichen kann (z. B. log-watcher; `POST /api/ci/build-report`). PERSISTENT statt nur In-Memory → Admin-CI kennt die laufende Version auch nach rookhub-api-Neustart sofort | Repo (PK, ≤100), Sha? (≤64), Ref? (≤200), ReportedAt; Upsert je Repo via `GithubActionsService.ReportBuildAsync`, gelesen in `ResolveRunningBuildsAsync` |
| CourseFlashcardMarks | PERSISTENTE Flashcard-Markierung einzelner Kurs-Linien je User (Checkbox im Durchsehen; `?marked=1`-Bereich der Flashcards-Seite) | UserId (Cascade) + BookId (denormalisiert, Cascade) + BookPuzzleId (**Restrict** — wie CoursePuzzleResult), CreatedAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId). Linien-Löschpfade (`CourseAuthoringService.RemoveLinesAsync`, `BookAdminService.DeleteBook`) räumen explizit ab |
| RepertoireFlashcardMarks | PERSISTENTE Flashcard-Markierung von Repertoire-Linien je User — Besitzer UND Freigabe-Empfänger haben eigene Sätze | UserId (Cascade) + RepertoireId (Cascade) + LineKey (≤120, Frontend-Linien-Hash wie SR), CreatedAt; **UNIQUE (UserId, RepertoireId, LineKey)** |
| Worksheets | Ein AUFGABENBLATT (Stellungssammlung zum Ausdrucken). Genau EINES je Nutzer mit `IsClipboard` = die Zwischenablage (Sammelkorb; nicht löschbar, nur leerbar) | UserId (Cascade), Name (≤120, leer bei der Ablage), IsClipboard, PerPage (2/4/6), **Themes (≤300, CSV; max. 12 Themen à 40 Zeichen — Filter der Übersicht)**, **ShareToken? (≤32, UNIQUE — `/w/{token}`, NULL = nicht geteilt; MariaDB lässt beliebig viele NULLs zu) + SharedAt?**, CreatedAt, UpdatedAt; Index (UserId, IsClipboard) |
| WorksheetItems | Eine Aufgabe auf dem Blatt. Die Stellung ist AUSGESCHRIEBEN — Neuimport/Löschen der Quelle ändert ein fertiges Blatt nicht | WorksheetId (Cascade), SortOrder, Fen (≤120), Orientation (≤5, white/black), Heading (≤200), Text (≤2000), **SolutionMoves (≤1000, UCI ab der Aufgabenstellung; leer = nur zum Rechnen — nur für den geteilten Link, nie im Druck; `WorksheetService.CleanUciMoves` filtert, was der Client schickt)**, **SourceThemes (≤200, leerzeichengetrennt; Themen des Quell-Puzzles beim Senden — nicht angezeigt, reine Vorschlagsquelle für die Blatt-Themen)**, Source (Manual/Standard/Book) + SourceId? + BookId? (**kein FK**, nur Herkunftsvermerk), CreatedAt; Index (WorksheetId, SortOrder) |

Cascade Deletes: AppUser → Profile, Repertoires, Subscriptions, EndlessProgresses, EndlessSessions, UserGroups, CourseProgresses, CoursePuzzleResults, CourseAttempts, UserTrainingGoals, PlayTimeDailies, PlayTimeSyncs, WeeklyPostAttempts, SavedGames, ManualActivities, GameReconstructions (→ GameReconstructionParts); Repertoire → Files, RepertoireShares (RepertoireShare.Owner/Recipient Restrict); Group → UserGroups, BookGroupAccesses, GroupTrainingGoals; Book → BookPuzzles, CourseProgresses, CoursePuzzleResults, CourseAttempts, BookGroupAccesses, CourseShares, CourseLinks, CalculationTrees (CoursePuzzleResult.BookPuzzle + CourseAttempt.BookPuzzle + CalculationTree.BookPuzzle = Restrict, um doppelte Cascade-Pfade zu vermeiden; CourseShare.Owner/Recipient ebenfalls Restrict; CourseLink.LinkedBookId ohne FK → DeleteBook räumt beide Richtungen explizit ab); WeeklyPost → WeeklyPostAttempts; AppUser → AdminMessages + MessageThreads (über UserId, der Nicht-Admin-Teilnehmer; MessageThread.ClaimedByAdminId hat bewusst keinen FK). Admin-DeleteBook und GroupController.Delete räumen die abhängigen Kurs-/Freigabe-/Ziel-Vorlagen-Daten zusätzlich explizit ab (InMemory-Tests cascaden nicht).
Friendships nutzen Restrict (kein Cascade) wegen zwei FKs zur selben Tabelle.

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
    Controllers/            Auth, Profile, Friend, Repertoire, Extension, TournamentProxy,
                            TournamentFavorite, TournamentMonitor, Subscription, BookPuzzle,
                            Course, Calculation, Endless, Group, WeeklyPost, TrainingGoal, ClientLog,
                            Puzzle, Admin, Me, BotStats, Engine, ExternalEngine, Token, Club,
                            BaseApiController
    Services/               Auth, Profile, Friend, Repertoire, CrawlerProxy, PlayerSearch,
                            BookPuzzle, Course, CourseAccess, CourseAuthoring, Calculation,
                            FenListParser, Puzzle, EndlessProgress, TrainingGoal,
                            PlayTime, PlayTimeSync, WeeklyPost, BotStats,
                            ApiToken+ApiTokenAuthenticationHandler, DiscordLink, PgnImport,
                            SchachBotWebhook, BackgroundTaskQueue, Admin, BookAdmin,
                            AdminSeeder, AutoSubscription, RoundMonitor,
                            DailyPuzzleScheduler, Heartbeat,
                            CalcEdition, CalcSeriesAnnounce(+Scheduler), LichessEngine,
                            EngineActivityTracker, AnalysisJob(+Worker), NdjsonHeartbeatPump,
                            EngineBroker/ (eigener Broker: Hub, Registry, Emit, Sanitizer)
    Models/                 EF-Entities (1:1 zum Schema oben)
    DTOs/                   Request/Response-Typen je Endpoint-Familie
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

## Zwei Oberflächen: RookHub + Turnierseite

Turniere laufen seit v0.409.0 als **eigene Seite** unter `turnier.oberschmid.homes`
(Dev: `turnier-dev.oberschmid.homes`) — eigene PWA, eigenes App-Symbol, eigenes Image
`ghcr.io/kahalm/rookhub-turnier:{dev,latest}`. Geteilt wird alles darunter: **eine** API,
**eine** Datenbank, **ein** Konto.

- **Ein Quellbaum, zwei Angular-Projekte** (`app` und `turnier` in derselben `angular.json`).
  Die Turnierseite importiert Auth, i18n und die kleinen Bausteine per Alias `@rh/*` aus
  `src/app/` — keine Kopie, eine Änderung wirkt auf beiden Seiten.
- **Ein Dockerfile, zwei Images**: `APP_PROJECT` entscheidet, welches Bundle in den nginx kommt.
  Die CI baut beide aus demselben Kontext (`build-frontend`, `build-turnier`).
- **Anmeldung**: das JWT liegt in `localStorage` und ist damit an die Herkunft gebunden — eine
  Domain kann die andere nicht mitlesen. Zwei Wege überbrücken das, beide in
  `core/handoff.service.ts`:
  1. **Sprung über das Menü** — nimmt einen Einmal-Code mit (`POST /api/auth/handoff` →
     `?h=<code>` → `POST /api/auth/handoff/exchange`, 60 s gültig, genau einmal einlösbar).
  2. **Geteilte Anmeldung** (seit v0.413.0, `Services/SharedSessionService.cs`) — für den
     Normalfall „ich rufe die andere Seite direkt auf". Beim Anmelden legt die API ein Cookie auf
     der gemeinsamen Elterndomäne ab (`Auth:SharedSessionDomain`, z. B. `.oberschmid.homes`;
     Name `Auth:SharedSessionCookie`, auf Dev ein ANDERER als in Prod — sonst überschreiben sich
     die beiden Umgebungen gegenseitig auf derselben Domäne). Beide Oberflächen tauschen es beim
     Start über `POST /api/auth/rh-session` gegen ihre eigene Anmeldung; `logout()` beendet es über
     `POST /api/auth/rh-session/end`. Das Cookie trägt ein Token mit EIGENEM Adressaten
     (`rookhub-shared-session`) — der JWT-Handler der API weist es ab, es öffnet also nur diesen
     einen Endpunkt; dazu `HttpOnly`, `SameSite=Lax`, `Path=/api/auth/rh-session` (bis N6-001
     `/api/auth` — das bedienen auch RCT, Lernkompass, Cal.com und die Dev-Stacks unter derselben
     Elterndomäne; die alten Routen `session`/`session/end` bleiben eine Version als Übergang). **Leere Domäne = aus**
     (localhost/IP haben keine gemeinsame Domäne). Eine schon offene Seite der Gegenrichtung
     merkt eine Abmeldung erst beim nächsten Laden — sie hält ihr eigenes JWT.
- **Link-Vorschau**: `/t/{id}` lebt jetzt auf der Turnierseite. Der nginx sagt der API über
  `X-Og-Site` (aus `$host` abgeleitet), welche SPA-Shell sie anreichern soll und welche Domain in
  `og:url` gehört (`App:TurnierBaseUrl`) — sonst bekäme der Besucher die RookHub-Shell serviert
  und landete auf dem Dashboard.
- **Ohne Konto benutzbar** (0.643.0, Wunsch „vor allem Turniersuche und Turnier-Detailseiten; Speichern von Filtern und
  Turnieren braucht natuerlich einen Account"): `tournaments/calendar`, `tournaments/calendar/:id` und `tournaments/:id`
  tragen keinen `authGuard` mehr; `/tournaments` (Gemerkt), `/tournaments/history`, `/profile` und `/admin` schon. Jede
  speichernde Aktion (Suchprofil, Merken, Beobachten, Ausblenden, Melden, Vereine nachtragen) geht durch
  `src-turnier/app/core/require-account.ts` (`requireAccount`: angemeldet → weiter, sonst Anmeldung mit `returnUrl`);
  „Ausblenden" und der Schalter „ausgeblendete zeigen" erscheinen fuer Gaeste gar nicht. Gaeste behalten Filter im
  localStorage `rh.turnier.directoryView.guest` (kein Server-Abgleich) und Sterne/„Nur Favoriten" unter denselben
  Schluesseln wie die Teilen-Ansicht `/t/:id` (`public_fav_players_{id}`, `public_fav_teams_{id}`,
  `public_fav_filter_{id}`). Weil `HandoffService.consumeIncoming()` eine geteilte Anmeldung ERST NACH dem Start
  uebernimmt, laedt die Seite danach neu (`AppComponent.afterAdoption`) — sonst stuende sie als Gast da.
  Specs der betroffenen Komponenten stellen `isLoggedIn` ausdruecklich per `spyOnProperty` (Getter) ein.
- **Netz**: der Turnier-Container muss im selben Compose-Netz liegen wie die API, weil sein nginx
  `/api/` an den Servicenamen `api` weiterreicht.
- **Kachel-Proxy `/tiles/`** (Codereview 2026-09-29, F8-002): nur auf der Turnierseite (rookhub*/kidhub*/leaguehub*-Hosts
  → 404; unbekannte Hosts wie IP/localhost/E2E bleiben offen), Zoom 0–18, x/y höchstens sechsstellig; gedrosselt
  je Betrachter (20 r/s, burst 200, Schlüssel `X-Real-IP`) und je Container (50 r/s), Überlauf 429. Verlässt sich
  darauf, dass der NPM `X-Real-IP` setzt (Standard-`proxy.conf`); `DeploymentConfigTests` hält es fest.
- **Eigene Symbole** (`public-turnier/`, seit 0.433.0): Vorlagen in `design/Designer{,2,3}.png`,
  alles darunter ist ABGELEITET (Rezept in `public-turnier/ASSETS.md`). Die Rollenteilung ist
  keine Geschmacksfrage: `Designer.png` fuellt seinen Rahmen (aeusserste Ecke bei 94 % des
  Radius) und taugt fuer `any`, `Designer2.png` haelt das Motiv bei 70 % und taugt damit fuer
  `maskable` (Android darf ab 80 % beschneiden), `Designer3.png` ist das 1200×630-Vorschaubild.
  **Die Falle, die hier lange unbemerkt lief**: `public-turnier/` wird UEBER `public/` gelegt —
  eine dort FEHLENDE Datei faellt still auf RookHubs Fassung zurueck, ohne 404 und ohne
  Warnung. Das Manifest verwies auf Symbole, die es nie gab, und die Turnierseite trug deshalb
  RookHubs Logo. `TurnierAssetTests` haelt jeden Verweis gegen die Dateien auf der Platte und
  prueft zusaetzlich, dass kein `icon.svg` verwiesen wird (es gibt keine Vektorfassung).
- **Turnierverlauf** (`src-turnier/app/features/tournament-history/`, Route
  `/tournaments/history`, Navbar): gespielte und kommende Turniere mit Platz, Punkten,
  Performance-Rating und Elo-Aenderung, umschaltbar auf Freunde (alle oder einzeln).
  **Ausgewertet wird JE BEDENKZEIT-KLASSE** (Turnier/Schnell/Blitz/ohne Angabe) und **je JAHR**,
  mit Turnier- UND Partienzahl: eine Gesamtpunktzahl addierte Blitz zu Turnierschach und
  Fuenfrundige zu Elfrundigen — sie wuchs nur mit der Zeit. Eine Performance ist ausserdem NUR
  getrennt lesbar; 1900 im Blitz ist nicht 1900 im Turnierschach. **`unknown` ist eine eigene
  Gruppe** und faellt nicht heraus: die Bedenkzeit steht auf einer eigenen Seite, die erst der
  naechtliche Durchgang holt — ohne diese Gruppe war die Uebersicht direkt nach dem Deploy leer.
  **Ein REITER je Konto UND je verfolgtem Spieler** (ich zuerst, dann die Freunde, dann die
  Verfolgten) statt einer Auswahlliste; jeder Reiter
  laedt genau EIN Konto (alle auf einmal hiesse: eine chess-results-Abfrage je Freund, auch fuer
  die, die niemand ansieht), einmal geladene bleiben im Zwischenspeicher. Freunde ohne Namen im
  Profil behalten ihren Reiter — gesperrt und mit Grund, statt kommentarlos zu fehlen. Der EIGENE
  Verlauf bleibt bei jeder Auswahl dabei — „nur Freunde" waere eine Ansicht, in der man sich
  selbst sucht, und der Vergleich ist der Zweck. Zwei Dinge, die dabei nicht kippen duerfen:
  (1) Die Seite fragt NACH, solange `pending > 0` (Server holt die Ergebnisse einzeln im
  Hintergrund) — mit Deckel (`MaxPolls` 15), sonst laeuft sie endlos; ohne das Nachfragen saehe
  man eine halbe Tabelle und hielte sie fuer endgueltig. (2) Ist „alle Freunde" die GEMERKTE
  Auswahl, laedt der Verlauf erst NACH der Freundesliste — er muss wissen, wen er meint, sonst
  ist die gemerkte Auswahl beim Wiederkommen wirkungslos (genau so aufgefallen).
  **Ein Klick fuehrt auf das TURNIER, nicht ins Verzeichnis** (0.430.2). Der Kalendereintrag war
  fuer die Mehrheit der Verlaufs-Eintraege eine Sackgasse („steht (noch) nicht im Verzeichnis"),
  und das heilt nicht: der Sweep liest nur `[heute − 30 Tage, heute + 18 Monate]` — ein 2024
  gespieltes Turnier steht dort NIE. Ist es geholt → `/tournaments/{id}`; ist es das nicht →
  Holen-Auftrag einreihen und nachfragen (`MaxImportPolls` 30 × 4 s ≈ zwei Minuten, danach eine
  Meldung statt endlosen Wartens).
  **Das „+" verfolgt beliebige Personen** (0.463.0, `track-player-dialog.component.ts`): Vor- und
  Nachname eingeben, suchen (`GET /api/profile/player-search`), einen Treffer waehlen — er wird
  zum eigenen Reiter. Drei Dinge dabei: (1) Der Reiter-SCHLUESSEL ist zusammengesetzt (`u:12`
  fuer ein Konto, `t:3` fuer einen Verfolgt-Eintrag) — die beiden Kennungen kommen aus
  verschiedenen Toepfen und kollidieren zwangslaeufig; mit der Zahl allein zeigten zwei Reiter auf
  denselben Zwischenspeicher. Ein alter gemerkter Reiter (`{ userId }` im localStorage) wird
  weiter gelesen. (2) Der gemerkte Reiter wird erst verworfen, wenn BEIDE Listen (Freunde und
  Verfolgte) da sind — die Antworten kommen in beliebiger Reihenfolge, und wer nach der ersten
  urteilt, wirft einen Reiter weg, den die zweite gerade mitbringt. (3) Angelegt wird IM Dialog,
  nicht beim Aufrufer: scheitert es (Deckel erreicht, Netz weg), bleibt er offen und sagt es —
  statt dass sich ein Fenster schliesst und danach sichtbar nichts passiert. „Nicht mehr
  verfolgen" hat ein Rueckgaengig, deshalb traegt `TrackedPlayerDto` die Suchfelder mit.
- **Geteilte Anzeige-Einstellungen** (`core/shared-preference.ts`): Design-Modus UND **Sprache**
  liegen in einem Cookie auf der gemeinsamen Elterndomaene — zwei Origins teilen den
  `localStorage` nicht, eine Sprachwahl auf der einen Seite liess die andere sonst in Englisch
  sitzen. Der Mechanismus (lesen/schreiben/`visibilitychange`) steht dort EINMAL; vorher trug der
  Design-Modus seine eigene Kopie. Ohne gemeinsame Elterndomaene (localhost, IP) passiert nichts —
  der geraetelokale Wert traegt dann weiter.
- **Das Identitaets-Formular ist GETEILT** (`shared/profile-identity-form/`): Name, Anzeigename,
  E-Mail und die zwei Spielerkennungen samt SPIELERSUCHE, benutzt von RookHubs Profilseite und der
  der Turnierseite. Vorher zweimal getippt — und darum fehlte der Turnierseite die Suche, obwohl
  dort alles an den Kennungen haengt. Die Komponente aendert das uebergebene Objekt direkt und
  speichert NICHT: was gespeichert wird, entscheidet die Seite (RookHub schickt chess.com/Lichess
  mit, die Turnierseite bewusst nicht). `ngModelOptions: standalone`, damit sie sich nicht im
  `<form>` der Elternkomponente registriert.
- **Profilseite**: die Turnierseite hat ihre EIGENE (`src-turnier/app/features/profile/`, Route
  `/profile`, im Konto-Menue) — Vor-/Nachname, Anzeigename, E-Mail und die beiden
  Spielerkennungen, ueber denselben `PUT /api/profile`. Bewusst nicht RookHubs Profilseite
  wiederverwendet: die ist eine Sammlung aus Chessable-Zugang, Engine-Token, API-Tokens,
  Brett-Einstellungen und Offline-Speicher. Und bewusst nicht weggelassen: der **Nachname** ist
  die Voraussetzung fuer den Turnierverlauf (die chess-results-Spielersuche sucht ueber den
  Namen), die Kennungen entscheiden bei Namensgleichheit. Gespeichert wird nur dieser Teil —
  ein vollstaendiges Profil-Objekt zurueckzuschicken ueberschriebe die RookHub-Einstellungen mit
  dem Stand einer Seite, die sie nicht kennt.

### Dritte Oberfläche: KidHub, die Kinderseite (0.554.0)

`kidhub.oberschmid.homes` (Dev: `kidhub-dev.oberschmid.homes`) — eigenes Angular-Projekt `kidhub`
(`src-kidhub/`, `public-kidhub/`, `tsconfig.kidhub.json`, `ngsw-config.kidhub.json`), eigenes Image
`ghcr.io/kahalm/rookhub-kidhub:{dev,latest}` aus demselben Dockerfile (`APP_PROJECT=kidhub`), Host-Port Prod
**8096** / Dev **8097** (`KIDHUB_PORT`). Name vom Nutzer („KidHub", 2026-09-27). API und Endpunkte: „KidHub —
Kinderseite" unter REST API.

- **Spielen ohne Konto**: kein `visitorInterceptor`; Fortschritt nur im
  localStorage (`rh-kids-progress-v1`, `KidsProgressStore`): Sterne je Stufe (0–1 Fehler = 3, 2–4 = 2, sonst 1 —
  Tipps zählen als Fehler), der laufende Durchgang (Aufgabe + Fehler), gelöste Kurs-Linien. Stufe n ist offen,
  sobald n−1 geschafft ist.
- **Anmelden/Registrieren** (0.561.0, Wunsch 2026-09-27 „auf der ersten Seite rechts oben"): NUR auf der Startseite
  rechts oben (`isHomeUrl`, mitten in einer Stufe lenkt ein Konto-Knopf ab) — abgemeldet „Anmelden" + „Registrieren",
  angemeldet der Name + „Abmelden" (führt zurück auf `/`, nicht auf die Maske). Die Masken sind RookHubs eigene
  (`/login`, `/register`, `/forgot-password`, `/reset-password` über `@rh/features/auth`, `guestGuard`), mit
  `authInterceptor`; beim Start tauscht `HandoffService.consumeIncoming()` das geteilte Cookie gegen eine eigene
  Anmeldung — wer in RookHub oder auf der Turnierseite angemeldet ist, ist es hier auch. Seit 0.563.0 liegt der
  Fortschritt angemeldet im Konto (Abschnitt „Fortschritt im Konto" unter REST API); die Startseite sagt, wo er liegt.
- **Kein Impressum** (0.563.0, Wunsch 2026-09-27): keine Route, kein Link im Fuß. `LEGAL_SITE`
  (`src/app/features/legal/legal-site.ts`) sagt den geteilten Rechtsseiten je Oberfläche, ob es ein Impressum gibt und
  welche Adresse für Datenschutzfragen gilt — KidHub setzt `{ contactEmail: 'kidhub@oberschm.id', imprint: false }` in
  `kidhubConfig`; die Datenschutzerklärung nennt den Verantwortlichen dann über diese Adresse statt übers Impressum,
  die Anmeldemaske lässt den Impressum-Link weg, und auch die Seite zur Konto-Löschung (`/account-deletion`, von der
  Datenschutzerklärung verlinkt, deshalb auch in KidHub eine Route) nennt diese Adresse. RookHub und die Turnierseite
  nehmen die Vorgabe (`OPERATOR`). `LEGAL_SITE` trägt seit dem Codereview (F7-003) auch `kind` (`kidhub`: Fassung in
  einfacher Sprache mit Elternhinweis; `leaguehub`: Abschnitt über Ligaspieler ohne Konto) und `back` (Rücklink,
  KidHub `/` statt `/login`). `accountHome: 'rookhub'` (UX-023; KidHub, LeagueHub, ClubHub, Turnierseite): dort gibt es
  keine Karte „Konto löschen" — `/account-deletion` sagt „dein Konto hier ist ein RookHub-Konto" und verlinkt RookHubs
  `/profile?section=delete` (`accountHomeUrl()` in `partner-site.ts`, angemeldet per Einmal-Code
  `HandoffService.jumpToAccountHome`). Ohne `accountHome` (RookHub) führt der Knopf „Konto jetzt löschen" direkt dorthin;
  `?section=delete` klappt im Profil die Karte auf und scrollt zu ihr. Die Liste „Was entfernt wird" (`removedKeys` in
  `account-deletion.component.ts`) und `profile.delete.warn` geben wieder, was `ProfileService.DeleteAccountAsync`
  löscht (UX-021: eigene Kurse samt Freigaben und fremdem Fortschritt, Partien mit Formular-Fotos, Aufgabenblätter und
  Teilen-Links, KidHub-Fortschritt, Verbindungen) — wer dort etwas ergänzt oder herausnimmt, zieht die Texte nach
  (en/de/hr/hu). LeagueHub (`kind`) nennt zusätzlich Entwürfe, bleibende Vereinspartien und Teilen-Links. Wege zu den
  Rechtsseiten (UX-017): die gemeinsame Fußzeile (`app-footer`, RookHub und Turnierseite) zeigt „Impressum · Datenschutz",
  wo die App die Route hat (Impressum nur mit `imprint`), das ☰-Menü (Gast und „Konto") dieselben zwei Einträge — am
  Handy ist RookHubs Fußzeile aus. Der Rücklink (`legalBackLink`) heißt „Zurück" und geht einen Schritt zurück, wenn man
  aus der App kam (`Router.lastSuccessfulNavigation()`), sonst zum Ersatzziel `back` (Vorgabe `/login`). Ohne Impressum nennt die Datenschutzerklärung beim Verantwortlichen NUR die
  Kontaktadresse der Oberfläche. **Rechtsseiten allgemein** (Betreiber-Entscheidung 2026-09-30, UX-001):
  `environments/operator.ts` (`OPERATOR`) enthält nur noch die Kontaktadresse `rookhub@oberschm.id` — kein
  Diensteanbieter-Block mit Name/Anschrift im Impressum, keine Platzhalter.
- **Geteilt über `@rh/*`**: HTTP-Kette (connectivity, retry, renderAfterHttp), Sprachdateien (Namespace `kids.*`,
  gepflegt in en/de/hr/hu — nur diese vier bietet die Seite an), `PuzzleBoardComponent` (neues Input `autoQueen`:
  Umwandlung ohne Auswahl zur Dame), Datenschutz als eigene Route.
- **Endlos-Modus** (0.566.0, Wunsch 2026-09-27 „analog wie bei RookHub, nur mit sehr flacher Kurve … adaptiv"):
  Route `/endless`, Kachel auf der Startseite. Die KURVE rechnet der Browser (`core/kids-endless.ts`, rein): Start 700,
  +20 je Puzzle (5 Puzzles je 100 Elo), Anker wie RookHub — T1 nach 10 Puzzles = Ø Rating des ersten Fehlers der
  letzten 10 Läufe, T2 nach 25 = Ø Höchst-Rating (sauber gelöst) der letzten 5, danach wieder +20; NIE flacher als die
  Grundkurve (700 → 900 → 1200), keine steile Erst-Lauf-Kurve. Wer weit kommt, bekommt also steilere Läufe. Drei Herzen;
  ein Fehler kostet eins, ein Tipp erst ab dem ZWEITEN in derselben Aufgabe (`ENDLESS_FREE_HINTS` = 1, Wunsch 2026-09-27 —
  der erste lässt nur die Figur leuchten), höchstens eins je Aufgabe, und das Kind löst die Aufgabe trotzdem zu Ende
  (dafür meldet `KidsPuzzleComponent` jeden falschen Zug sofort per `mistake` und jeden Tipp per `hinted` mit seiner Zahl
  in der Aufgabe; für die Sterne der Stufen zählt weiter jeder Tipp). Das letzte Herz beendet den Lauf nach
  `WRONG_HOLD_MS`. Läufe + Rekord liegen NUR im Browser (`KidsEndlessStore`, `rh-kids-endless-v1`, letzte 20) — nicht im
  Konto. Die PUZZLES holt `KidsEndlessService` in Blöcken zu 20 (Nachladen bei < 5): je Fenster Zufalls-Sprung in den
  Id-Raum, die nächsten 12 des Fensters, das erste kindgerechte (≤ 3 eigene Züge, Qualitätsgrenzen der Leiter, ohne
  Rochade/en passant/Unterverwandlung; Figurenzahl frei), drei Sprünge, dann zufällig unter den ersten 200 des Fensters.
  Auf Dev gemessen (5,36 Mio. Puzzles): ~10 ms je Sprung (Plan: Primärschlüssel-Scan), kindgerecht sind je 200er-Band
  zwischen 30 % (2400) und 70 % (1000–1400).
- **Löser** `src-kidhub/app/core/kids-solver.ts` (rein, ohne Angular): EINE Form für Lichess-Puzzles
  (`startPly` 0) und Kurs-Linien (eigener `StartPly`, `-1` = kein Stellungszug; alles davor stumm vorgespult);
  im LETZTEN Zug zählt jedes Matt; Kurs-`AltMoves` sind „auch gut, aber gesucht ist ein anderer" (kein Fehler);
  falscher Zug → Fehlerpunkt, der Zug bleibt `WRONG_HOLD_MS` (2 s) mit markiertem Zielfeld stehen, dann Stellung
  zurück (0.562.0, Wunsch des Nutzers; die Stellung danach wird per `KidsSolver.fenAfter` AUSDRÜCKLICH gesetzt —
  jede geänderte Brett-Eingabe setzt sonst sofort die alte `fen`). Am PC ist Leertaste/Enter „Weiter" bzw. nach
  einer geschafften Stufe „Nächste Stufe" (`isAdvanceKey`: nicht in Feldern, auf Knöpfen/Links, bei gehaltener
  Taste; wer die Taste verbraucht, ruft `preventDefault`, sonst spränge dieselbe Taste über die Sterne hinweg).
  **PC-Aufbau** (0.562.0): Brett links, so groß wie die Fensterhöhe erlaubt (`--kid-board` in der App-Hülle, auch
  die Titelzeile richtet sich danach), rechts Aufgabentext (`[kidTask]`-Slot), Eule, Knopf; Punkte der Stufe in der
  Titelzeile; Brett blau (`boardTheme="blue"`). Bis 760px alles untereinander, Aufgabe über dem Brett. `KidsPuzzleComponent` hält ALLES in Signalen (Stellungszug und
  Gegnerantwort kommen per Timer — Angular 22 zeichnet unmarkierte Ansichten danach nicht neu) und gibt dem Brett
  nach jedem Zug ein NEUES `dests`-Objekt: nur eine geänderte Eingabe lässt das Brett einen falschen Zug optisch
  zurücknehmen.
- **Routen** `/`, `/levels`, `/levels/:level`, `/courses`, `/courses/:bookId`, `/login`, `/register`,
  `/forgot-password`, `/reset-password`, `/privacy`, `/account-deletion` — bewusst
  keine mit `/g`, `/t`, `/puzzles`: diese Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API.
- **Symbole** (0.560.0, Vorlagen 2+3 seit 0.560.2): gezeichnete Vorlagen `design/kidhub/KidHub{,2,3}.png` (Bild-KI),
  alles in `public-kidhub/` leitet `design/kidhub/derive.py` ab — es stellt bei `KidHub.png` die WEISSEN Ecken frei
  (die Vorlage hat keinen Alphakanal) und färbt ein rotlila Stück Umriss um, schneidet `KidHub2.png` (maskable) um
  das Motiv zu (dort nur ~23 % des Radius, danach 32 % — innerhalb Androids sicherer 40 %) und schreibt in die leere
  rechte Hälfte von `KidHub3.png` Name und Satz (Vorschaubild).
  Rezept in `public-kidhub/ASSETS.md`; `KidHubAssetTests` hält Manifest, `index.html` und Dateien gegeneinander
  (dieselbe Falle wie bei der Turnierseite: was in `public-kidhub/` fehlt, kommt still aus `public/`).
- **Sprache aus dem IP-Land** (0.560.0): nur wenn die ermittelte Sprache keine Kindersprache ist, fragt KidHub
  `GET /api/kids/language-hint` (Land der Besucher-IP → de/en/hr/hu, `KidsLanguageHint`; seit 0.560.1 bekommt JEDES
  andere bekannte Land Englisch — „Frankreich → Englisch"), erst ohne Land (LAN, Ausfall) Deutsch
  (Warten höchstens `HINT_TIMEOUT_MS` 4 s). Nachgeschlagen wird LOKAL (`IpCountryService`): die Länderliste von DB-IP
  („IP to Country Lite", CC BY 4.0 — daher „IP Geolocation by DB-IP" im Fuß und in der Datenschutzerklärung) lädt der
  Server beim ersten Bedarf selbst (4,5 MB, ~717 000 Bereiche, 2,2 s Einlesen, ~19 MB Speicher), cacht sie 35 Tage im
  Temp-Ordner und fragt nach einem Fehlschlag erst nach einer Stunde wieder — keine Besucher-IP geht an einen fremden
  Dienst. Bewusst NICHT beim Start: die Integrationstests starten die API. LAN-Adressen haben kein Land.
- **Sprache** (0.558.3): Startsprache wie überall (Cookie `rookhub_lang` auf `.oberschmid.homes` → localStorage →
  Browser → en). KidHub steht in `partner-site.ts` als `KIDHUB_LABELS`: KEINE Partnerseite (kein Sprung), darf
  aber das geteilte Cookie SCHREIBEN — vorher las es das Cookie nur, und eine in KidHub getroffene Wahl verlor beim
  nächsten Laden gegen RookHubs. Die Auswahl im Fuß bindet `[selected]` je Option an `translate.currentLang()`
  (mit `[value]` am `<select>` zeigte sie immer „Deutsch“, den ersten Eintrag, während die Seite Englisch sprach).
  Jede Sprache außer de/en/hr/hu wird über `LocaleService.applyUnsaved('de')` als Deutsch ANGEZEIGT, ohne die Wahl
  zu überschreiben.
- **Drossel und Netzausfall** (Codereview 2026-09-29, A10-003 — Server-Teil unter „Rate-Limit nach Zweck"):
  `KidsApiService` hält Stufen-Leiter und Kursliste (je Sprache) `CATALOG_TTL_MS` (5 min, = `max-age` des Servers) im
  Speicher — Startseite, Stufenkarte und jeder Stufenstart teilen sich EINE Abfrage; eine leere Leiter und Fehler bleiben
  nicht liegen. Ein 429 auf `levels`, `levels/{n}`, `courses` und `courses/{id}/puzzles` holt er EINMAL nach
  (`retryOnceAfter429`: `Retry-After`, sonst ein ganzes Minutenfenster — die Limiter der API schicken keins; ein zweites
  429 zeigt das Fehlerbild). Bewusst dort und nicht im geteilten `retryInterceptor`, der 429 absichtlich nicht wiederholt.
  Der Service Worker (`ngsw-config.kidhub.json`, dataGroup `kids-catalog`, freshness, 5 s) liefert Stufen und Kurse,
  die schon einmal geladen waren, auch ohne Netz; `progress`, `language-hint` und `endless/batch` bleiben draußen.
- **Emojis** (Eule, Themenbilder, Sterne) brauchen eine Emoji-Schrift auf dem Gerät — Handys/Tablets haben sie,
  der Headless-Chromium auf dem Server nicht (Screenshots zeigen dort Kästchen).

### Als ein Nutzer einsteigen — auf BEIDEN Oberflaechen (0.429.0)

Der Einstieg selbst ist unveraendert (`POST /api/admin/users/{id}/impersonate`, das Token traegt
die Rollen des ZIELS). Neu ist, dass die Turnierseite ihn anbietet: `/admin` dort ist
`src-turnier/app/features/admin/turnier-admin.component.ts` — Kontenliste plus Einstieg, und sonst
nichts. **Bewusst nicht RookHubs Admin-Panel eingebunden**: dessen zehn Laschen (Buecher,
Tagespuzzle, Puzzle-Themen, Chessable, Menue-Sichtbarkeit, CI, Rollen) fuehren zu Bereichen, die es
auf der Turnierseite nicht gibt — sie waeren mehrheitlich Wege ins Leere. Der rote Streifen ist
dagegen GETEILT (`shared/impersonation-banner/`, in beiden Huellen eingehaengt): ein Einstieg ohne
sichtbaren Hinweis ist die gefaehrliche Variante, und das darf auf keiner der beiden Seiten anders
sein. Der Ausgang fuehrt auf `/admin` (beide Seiten haben eine solche Seite) und frischt das Menue
auf.

### Turnierkalender-Filterleiste (Stand 0.421.0)

Die Leiste ist um „was ist in meiner Naehe, und wann" gebaut: **Ort** (Autocomplete gegen den
Gazetteer + `my_location`-Knopf), **Umkreis** (gestufte Werte, kein Schieber) und **Zeitraum**.
Die Textsuche (Name/Ort/Veranstalter) steht in den ausklappbaren Zusatzfiltern — wer die Seite
oeffnet, sucht meist kein Turnier, dessen Namen er schon kennt. Suchprofile leben im ⋮-Menue;
ist eines gewaehlt, ZEIGT das Ortsfeld dessen Ort, und beim Anfassen von Ort oder Radius wird
daraus ein selbst gefuehrter Mittelpunkt (`profileId` faellt weg, `lat`/`lon`/`radiusKm` gehen
mit) — sonst behauptet die Leiste eines und der Server rechnet ein anderes.

**Der Browser-Standort laeuft ueber zwei Schritte** (`src-turnier/app/core/geolocation.service.ts`
+ `GET /api/tournament-directory/places/nearest`): die Ortung liefert Koordinaten, ins Feld gehoert
ein NAME. Aufgeloest wird gegen den LOKALEN Gazetteer — die Koordinaten eines Nutzers sind das
Letzte, was diese Anwendung nach draussen geben sollte. Findet sich kein Ort, gelten die
Koordinaten trotzdem (dann stehen sie selbst im Feld). Der Standort wird NIE von selbst abgefragt:
eine unaufgeforderte Abfrage beim Seitenaufruf loest eine Berechtigungsfrage ohne erkennbaren
Anlass aus, die im Zweifel abgelehnt wird — und danach ist der Knopf wirkungslos.

**Alles, was aus einer HTTP-Antwort kommt, liegt in Signalen** (`entries`, `total`, `pins`,
`calendarDays`, `loading`, …). Das ist die Behebung des gemeldeten Fehlers „ich sehe das Ergebnis
erst, wenn ich von Karte auf Liste wechsle": `provideHttpClient()` laeuft ueber `fetch`, zone.js
traegt die Angular-Zone NICHT durch den Antwort-Strom, und eine Feldzuweisung im Abonnenten loest
darum keine Aenderungserkennung aus. Der FILTER bleibt bewusst ein einfaches Objekt — er wird nur
durch Eingaben geaendert, und die laufen in der Zone. Siehe TODO.md fuer die uebrigen Seiten.

**Die Karten-Pins** sind eine eigene Canvas-Marke (`features/tournament-directory/map-pin-marker.ts`,
erbt von `L.CircleMarker`): Leaflet kennt im Canvas nur Kreise, und `divIcon`-Marker bringen bei
ein paar tausend Marken den Browser zum Kriechen. Ueberschrieben sind DREI Dinge, und sie muessen
zusammenpassen: die gezeichnete Form (`_updatePath`), der Trefferbereich (`_containsPoint` — Kopf
UEBER dem Anker plus zulaufender Schwanz; der geerbte Kreis um den Anker laege zur Haelfte unter
dem Pin im Leeren) und die Ausdehnung fuers Neuzeichnen (`_updateBounds` — sonst schneidet der
Renderer die oberen zwei Drittel weg). Popup- und Tooltip-Offsets kommen aus
`MapPinMarker.heightAbove`.

**Karte gekappt?** (Codereview 2026-09-29, F6-006): `/api/tournament-directory/map` antwortet `{ items, truncated }`;
bei `truncated` steht unter der Karte ein Hinweis (`tournamentDirectory.mapTruncated`). Der Service liest die alte
Listenform tolerant mit.

**EINE Kurzansicht fuer alle drei Ansichten** (`tournament-card.component.ts`, Stand 0.425.0).
Liste, Karte und Kalender zeigten dasselbe Turnier dreimal verschieden: die Liste als Karte mit
Lesezeichen, die Karte als von Hand gebauten DOM-Baum ohne Aktionen, der Kalender als nackten
Knopf mit dem Namen. Wer auf der Karte ein Turnier fand, musste erst auf die Detailseite, nur um
es zu merken. Jetzt ist es eine Komponente mit vier Aktionen (merken, in den Kalender
uebertragen, ausblenden, melden), und **die Aktionen macht die Komponente SELBST** — alle vier
betreffen genau dieses Turnier, sie in drei Eltern je viermal zu verdrahten waere derselbe Code
dreimal. Nach draussen geht nur, was den Eltern gehoert: `selected` (die Liste merkt vorher ihren
Filterzustand) und `ignoredChanged` (die Liste laesst die Zeile fallen, die Karte holt ihren
Ausschnitt neu, der Kalender laedt neu — dort steht ein Turnier an mehreren Tagen).

Drei Dinge, die dabei nicht kippen duerfen: (1) Im **Karten-Popup** wird die Komponente
DYNAMISCH erzeugt (`ViewContainerRef.createComponent` + `changeDetectorRef.detectChanges()`,
Element aus `ref.location.nativeElement`), weil Leaflet den Popup-Inhalt in einem eigenen
Container ausserhalb des Templates haelt; sie wird beim naechsten Popup, beim `clearLayers` und in
`ngOnDestroy` abgeraeumt, sonst haengt je geoeffnetem Punkt eine Komponente samt Abonnements im
Speicher. (2) Der eigene Zustand (`busy`/`subscribed`/`ignored`) liegt in **Signalen** und wird
aus dem Eintrag nur VORBELEGT — den Eintrag zu mutieren erreichte die Elternanzeige nicht
(OnPush), und die HTTP-Antworten kommen ohnehin ausserhalb der Zone an. (3) Die Komponente
importiert `MatDialogModule` selbst: sie oeffnet den Melde-Dialog und steht in drei verschiedenen
Eltern, auf deren Importe darf sie sich nicht verlassen.

**Der Merken-Knopf ist ein UMSCHALTER, und die KARTE zeigt den Zustand mit** (0.430.0). Zwei
Fehler, die zusammengehoerten: ein zweiter Klick tat gar nichts (`if (subscribed) return`), und
der Pin unterschied gemerkt/nicht gemerkt ueberhaupt nicht — die Auskunft stand nur in der
Kurzansicht, also erst nach dem Klick auf den richtigen Punkt, den man ohne die Auskunft nicht
kennt. `pinStyle()` in `tournament-map.component.ts` faerbt gemerkte Punkte in einem eigenen
Farbton UND mit dickerem Ring (**zwei Kanaele** — Farbe allein trennt nicht fuer jeden), die
Abschwaechung fuer „nur ungefaehr verortet" bleibt darunter erhalten. Gemerkte werden ZULETZT
gezeichnet und liegen damit oben. Nach einem Klick faerbt `applySubscribed()` nur die Punkte
DIESES Turniers um — die Ausschnitts-Daten neu zu laden wuerde das offene Popup zuschlagen.

Im **Kalender** oeffnet ein Klick die Kurzansicht als kleines Fenster
(`tournament-card-dialog.component.ts`) statt direkt auf die Detailseite zu fuehren: im
Monatsraster ist ein Tag ein paar Zeilen hoch, und wer vergleicht, verliert beim Wegnavigieren den
Monat. Erst der Klick auf den Namen fuehrt weiter.

**Rueckmeldungen** (beide gehen in den Admin-Nachrichtenkanal, siehe API-Abschnitt):
`report-entry-dialog.component.ts` auf der Detailseite („falsches Event melden", mit dem
IST-Stand daneben und der Lern-Frage nach regionalen Turniernamen) und
`missing-tournament-dialog.component.ts` unter allen drei Ansichten („dein Turnier fehlt?",
Pflicht-Link).

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
- **Integrationstests: Aufbau je KLASSE, nicht je Test** (seit 0.496.1) — xUnit legt je Testmethode eine neue Instanz der Testklasse an. Stand `MariaDbSchema.CreateAsync` + `MigrateAsync` + `new ApiFactory` direkt in deren `InitializeAsync`, lief das je TEST: gemessen 21 vollständige Durchläufe aller 139 Migrationen und 13 Anwendungsstarts für 22 Tests, von denen 18 zwischen 14 ms und 945 ms dauern. Der Aufbau gehört deshalb in eine `MariaDbClassFixture` (`IClassFixture<…>`); je Test ruft die Klasse nur `fixture.ResetAsync()` (leert jede Tabelle, Fremdschlüssel währenddessen aus) und holt sich einen frischen DI-Scope. **Die Isolation bleibt** — jeder Test beginnt auf einer leeren Datenbank —, aber die AUTO_INCREMENT-Zähler laufen über die Tests hinweg weiter: ein Test, der sich auf „die erste Id ist 1" verlässt, fällt damit sofort auf. Eine neue Testklasse braucht eine eigene Fixture-Unterklasse (`IClassFixture<T>` unterscheidet nach TYP); wer die Anwendung hochfährt, gehört zusätzlich in `ApiFactoryCollection`. Lokal gemessen: 9:45 → 1:54

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
- **IMMER erst `git fetch`/`pull` vor jedem Tag** – ein Tag zeigt auf einen konkreten Commit; wegen der zwei Stack-Kopien am selben Remote ist der lokale HEAD oft veraltet. Vor dem Taggen `git fetch` und den AKTUELLEN `origin/master`-HEAD taggen (dessen `APP_VERSION` aus `changelog.ts` = Tag-Name), sonst zeigt der Tag auf einen alten Stand OHNE die zwischenzeitlich von der anderen Kopie gepushten Features → das `:latest`-Prod-Image ist dann unvollständig (passiert 2026-07-06: v0.266.0 getaggt, während master schon auf 0.270.0 mit dem Chapter-Feature stand).
- **CI/CD**: Docker-Images werden nach Push automatisch gebaut (GitHub Actions). Kein manueller Build nötig.
  Seit 0.434.2 laufen Test- und Build-Jobs **pfadgefiltert** (`.github/filters.yml`, von `test.yml` UND
  `docker.yml` gelesen): ein Push startet nur, was er berührt. Zwei Regeln hängen an Tests
  (`CiWorkflowTests`): ein **Tag-Lauf baut immer alle sechs Images** (`:latest` entsteht nur dort), und der
  `turnier`-, `kidhub`-, `leaguehub`- und `clubhub`-Filter enthalten den GETEILTEN Frontend-Code (alle Angular-Projekte importieren aus `src/app`).
  Ein neuer Job braucht also einen Filter — ein Tippfehler im Namen ist ein leerer Output und damit ein
  Job, der ab da nie mehr läuft.
  **Nur Release-Tags** (Codereview W2, I1-004): `docker.yml` startet nur bei `vX.Y.Z` (vorher `v*`), und der
  `changes`-Job bricht jeden Tag-Lauf ab, dessen Commit nicht auf `origin/master` liegt (`git merge-base
  --is-ancestor`). `:latest` hängt allein an dessen Output `release` — ein Sicherungs-Tag wie `vorher-umbau`
  auf einem Branch baut also nichts mehr und landet nie per Watchtower auf Prod. Also: erst master pushen,
  dann taggen.
  **Handstart** (seit 0.453.3): `gh workflow run docker.yml` baut ALLE Images und lässt vorher ALLE
  Tests laufen — bei `workflow_dispatch` bleibt der Filter-Schritt aus (dorny hielte master gegen master
  und setzte jeden Filter auf `false`), die Job-Bedingungen fangen den Fall über `github.event_name` ab.
  Gebraucht für den Fall, den die Pfadfilter selbst erzeugen: master ist rot (hier fremdverschuldet
  geerbt), der reparierende Push berührt nur Frontend-Pfade, und damit hat `build-api` zwei Versionen
  lang nicht gebaut — master grün, Code gepusht, und auf Dev läuft trotzdem der Stand von vorgestern
  (2026-09-09, Dev hing auf 0.452.1). Der Handstart auf master schiebt `:dev`, nicht `:latest`.
  **Vorbau + Umhaengen** (seit 0.494.1): `prebuild-api`/`-frontend`/`-turnier`/`-kidhub`/`-leaguehub`/`-clubhub` in `docker.yml` bauen die
  Images schon parallel zu den Tests und pushen sie unter einem Hilfs-Tag `ci-<run_id>`. Die Jobs hinter
  dem Gate bauen GAR NICHT mehr — sie haengen per `docker buildx imagetools create` nur die echten Tags
  (`:dev`/`:latest`/Semver) an dasselbe Image, eine Registry-Operation von Sekunden. Der Zwischenschritt
  ueber einen Schichten-Cache (v0.493.3) war gemessen wirkungslos: der Export der .NET-SDK-Stage nach
  `type=gha,mode=max` kostete mehr als der Build (Vorlauf 4:00 fuer einen 1:44-Build), der Cache war
  1:12 nach dem Start von `build-api` fertig, und der Lauf blieb bei 4:43 statt 5:04.
  Vier Regeln haengen an `CiWorkflowTests`: der Vorbau pusht NUR `ci-*` (niemals `:dev`/`:latest`/Semver —
  sonst zeigten die Tags, die Watchtower und der Deploy lesen, auf ungetesteten Code), er wartet NICHT auf
  `tests`, er laeuft bei Tag UND Handstart mit (sonst faellt der Build-Job mangels `needs` aus → kein
  `:latest`, kein Release, obwohl alles gruen ist), und hinter dem Gate steht `imagetools create` statt
  `build-push-action`. Ein uebriggebliebener `ci-*`-Tag ist ein Lauf mit roten Tests; Wegraeumen ist
  Kosmetik und gehoert in einen eigenen zeitgesteuerten Workflow, nicht hierher.
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

## Wichtige Konventionen

- **Zwei Anthropic-Schluessel, und der Konto-Schluessel gehoert allein dem Formular-Einlesen** (seit 2026-09-25,
  Nutzer: „ausser Scoresheet soll nichts ueber den Key laufen"). `Anthropic:ApiKey` (Compose `ANTHROPIC_API_KEY`) liest
  NUR `ClaudeScoresheetVisionClient` (Foto → Partie, Kostenbremse `ScoresheetBudget`). Puzzle-Tipps (`HintGenerationService`)
  und Kommentar-Uebersetzung (`CommentTranslationService`, `CourseTranslationService`, `tools/LibraryImport translate`) gehen ueber den
  `ClaudeJsonClient`, und der liest AUSSCHLIESSLICH `Anthropic:TextApiKey` (`ANTHROPIC_TEXT_API_KEY`) — ungesetzt sind
  beide aus, auch wenn der Konto-Schluessel da ist. Wer einen neuen Claude-Aufruf einbaut, entscheidet sich fuer einen
  der beiden und schreibt es hier dazu; `ClaudeJsonClientTests` haelt fest, dass der Konto-Schluessel den Text-Client NICHT
  einschaltet. Der Discord-Bot (`CLAUDE_API_KEY` im Bot-Stack) und der log-watcher (eigener Schluessel) haengen nicht an
  RookHub — der Bot-Chat wurde am 2026-09-25 durch Entfernen des Schluessels abgeschaltet.
  **Seit 0.533.3 gibt es fuer Tipps und Uebersetzung einen dritten Weg: eigene Hardware.** `TextLlm:BaseUrl`
  (Compose `TEXT_LLM_BASE_URL`, OpenAI-kompatibel bis `/v1`, z. B. vLLM auf dem DGX Spark) + `TextLlm:ApiKey`
  (`TEXT_LLM_API_KEY`) + `TextLlm:Model` (`TEXT_LLM_MODEL`, leer = erstes Modell unter `/models` — auf dem Spark
  wechselt das Modell) → `OpenAiJsonClient`, kostenlos je Aufruf. Die EINE Auswahlregel ist `TextJsonClients.Create`
  (API und `tools/LibraryImport`): eigene Hardware VOR `Anthropic:TextApiKey`, ohne beides aus. Nachdenken ist dort aus
  (`chat_template_kwargs.enable_thinking=false` UND `reasoning_effort: low`, `TextLlm:Thinking=true` schaltet beides
  ab) — Qwen3.5 hört auf den Vorlagen-Schalter und braucht auf dem Spark sonst Minuten je Tipp; gpt-oss ignoriert ihn
  und hört auf `reasoning_effort` (0.597.2: mit seiner Vorgabe 73 s/696 Tokens je Drei-Satz-Absatz, mit `low`
  12,7 s/116 Tokens bei gleicher Übersetzung; der jeweils fremde Schalter wird vom Server ignoriert) —, gestreamt wegen
  des 90-s-Proxys vor dem Spark (`OpenAiChat.SendAsync`), dasselbe
  JSON-Schema wie der Claude-Weg (vLLM erzwingt es per Grammatik; lehnt ein Server es ab, einmal ohne und dann dabei
  bleiben). **Höchstens `TextLlm:MaxConcurrent` Anfragen gleichzeitig** (Vorgabe 8, 1..64; Codereview A6-005):
  ein Semaphor im `OpenAiJsonClient` (Singleton) für ALLE Zwecke zusammen — Erklärungen, Nacherzählung, Roast, Tipps,
  Übersetzung, Zugvergleich; Wartende reihen sich ein. Gilt auch für `tools/LibraryImport` (`--parallel` über 8 nur
  mit `TextLlm__MaxConcurrent`). Jede Antwort verliert vor dem Zerlegen die unsichtbare Typografie von gpt-oss
  (`OpenAiJsonClient.PlainTypography`, 0.597.3: U+2010/2011/2012/2212 → „-", U+00AD weg, U+00A0/2009/202F → Leerzeichen;
  Gedankenstriche bleiben) — keine Quelle enthält sie, Suche und Kopieren stolpern darüber. Den Bestand (alle
  Maschinentexte in CommentTexts, GameRecaps, GameRoasts, GameMoveExplanations) hat die SQL-Migration
  `NormalizeLlmTypography` einmal bereinigt (von Hand geschrieben, Designer = Kopie des vorigen, Modell unverändert).
  **Ausfälle der Spark (0.624.1):** (1) *Verstummter Strom* — der HttpClient-Timeout deckt bei `ResponseHeadersRead` nur
  die Kopfzeilen; `OpenAiChat.ReadStreamAsync` gibt deshalb auf, wenn `TextLlm:StreamIdleSeconds` (Vorgabe 120, über der
  90-s-Kappung des Proxys) lang KEINE Zeile kommt (01.10.2026: sieben Stunden Stillstand ohne Fehlerzeile). (2) *Modell
  weg* — `OpenAiChat.IsTransportFailure` (keine Antwort, Strom abgerissen/verstummt, 502/503/504) zählt der
  `OpenAiJsonClient`; ab drei in Folge ist `IClaudeJsonClient.IsUnreachable` wahr, 60 s nach dem letzten Fehler verfällt
  es wieder (wer anhält, fragt nicht mehr — sonst käme es nie zurück), jede Antwort des Servers setzt es zurück. Ein
  500 oder ein unbrauchbarer Text ist KEIN Ausfall. Serien-Arbeit hält dann an, statt Arbeit als gescheitert zu
  verbuchen: Kurs-Auftrag siehe „Hintergrunddienst", `tools/LibraryImport translate` legt die Partie zurück in die
  Reihe und wartet 75 s.
- **Kurs-Übersetzung: zwei Einstellungen, Automatik nur auf Prod** (0.548.0) – `CourseTranslation:Parallel` (Vorgabe 4,
  Linien je Lauf gleichzeitig) und `CourseTranslation:AutoLanguages` (Compose `COURSE_TRANSLATION_AUTO_LANGUAGES`,
  Komma-Liste, nur die 25 Oberflächensprachen, Vorgabe LEER = aus). Prod bekommt `de,en` erst auf Zuruf in der `.env`;
  Dev bleibt aus — beide teilen sich die Spark, und denselben Bestand zweimal zu übersetzen kostete Wochen Nachtarbeit.
  Die Sperrzeiten (`TextLlm:QuietHours`) gelten für JEDEN Auftrag, auch angeforderte; das Werkzeug
  (`tools/LibraryImport translate --course`) kennt sie nicht, dort hält die Schaltuhr sie ein.
- **Puzzle-Tipps laufen auf der EIGENEN Tipp-Queue** (`HintTaskQueue` + `HintTaskWorker`, Codereview 2026-09-29, A4-002) –
  nie auf der allgemeinen `IBackgroundTaskQueue`: dort hielt ein grosser persoenlicher Kurs (je Linie Stockfish bis 30 s +
  drei LLM-Aufrufe) den einzigen Consumer stundenlang fest. Der Import reiht je Buch EINEN Lauf ein
  (`TryEnqueueBook(bookId, owner)`, hoechstens einer wartend, volle Queue verwirft statt zu warten), der Lauf
  (`HintGenerationService.GenerateForBookAsync`) holt die Linien ohne aktuelle Tipps selbst (ohne Info-Linien).
  Persoenliche Kurse: 100 Puzzles je Lauf, 300 je Besitzer und UTC-Tag (Arbeitsspeicher); Admin-/Pool-Buecher ungedeckelt.
  Ein NEUES Buch traegt seinen Besitzer erst nach dem Import — Aufrufer eines persoenlichen Imports reichen deshalb
  `ownerUserId` an `ImportFileAsync`, sonst gaelte der Kurs als Admin-Buch.
- **`SourcePgn` liegt in `BookSource` (Tabellensplitting auf `Books`), nie an `Book`** (seit 0.508.3) – Das Roh-PGN
  eines Buchs (Ø ~480 KB, bis 6 MB) hing als Property an `Book` und kam mit JEDEM `.Include(bp => bp.Book)` mit:
  `GET /api/courses/{id}/puzzles` zog 6 MB × 1.881 Linien = 11 GB aus der DB für einen Request, die Prod-API stand
  bei 23 GB RAM. Regeln:
  - `.Include(b => b.Source)` bzw. `_db.BookSources.Where(s => s.Id == id).Select(s => s.SourcePgn)` NUR dort, wo der
    Text wirklich gebraucht wird (Download, Import, Reprocess) — **niemals in Listen-Queries**. Die erlaubten
    Include-Stellen stehen in `BookSourceIncludeGuardTests` (Quelltext-Scan; ein neues Include wird dort rot).
  - Ohne Include ist `book.Source` `null` — solange die BookSource nicht ohnehin im selben Kontext getrackt ist
    (dann setzt der Fixup sie). Mit Include ist sie relational IMMER eine Instanz (Pflicht-Navigation, auch bei
    `SourcePgn = NULL`); unter InMemory nur, wenn die BookSource-Zeile existiert.
  - Jedes `new Book { … }` (auch in Tests) setzt `Source = new BookSource { … }`. EF selbst verlangt das NICHT
    (relational: INSERT ohne die Spalte → `NULL`; InMemory: Include liefert `null` → NullReferenceException, der
    Lösch-Stub in `DeleteBookAsync` wirft `DbUpdateConcurrencyException`) — deshalb wirft `AppDbContext` eine
    `InvalidOperationException`, sobald ein Book ohne Source in den Zustand Added kommt.
  - Schreiben nur über eine GELADENE Source. Löschen: das Buch löschen, die Source geht mit der Zeile.
  - **Fallen (alle still ein `UPDATE Books SET SourcePgn = …`, gegen MariaDB geprüft):** `Source { get; set; } = new();`
    als Initialisierer an `Book` (der naheliegende „Fix" für null = Datenverlust: jedes ohne Include geladene Buch
    bekäme eine leere Added-Source → `SourcePgn = NULL`); `book.Source = new BookSource { … }` an ein geladenes Buch
    hängen (ersetzt den Text); `_db.BookSources.Remove(src)` und `book.Source = null` — KEIN Delete, sondern ein
    Blanking (`SourcePgn = NULL`). Alles nie tun.
  - Rückfallschutz: `BookSourceSplitSqlTests` (SQL der Puzzle-Abfragen enthält kein `SourcePgn`, `Book` hat keine
    `SourcePgn`-Spalte), `BookSourceIncludeGuardTests` (Include-Allowlist) + `BookSourceSplitTests` (MariaDB).

- **Eine gleichnamige REGION entscheidet im Ortsnamen-Weg nicht mit** (seit 0.456.5) – Steht in derselben
  Namensgruppe auch nur ein Ort, fallen die Regionszeilen (`GeoPlaceKind.Region`) vor der Wahl heraus. Eine Region
  ist die MITTE eines Gebiets und liegt zwangslaeufig woanders als die Stadt, nach der es benannt ist — die Gruppe
  streute damit weiter als eine Stadt, die Turnierdichte entschied nicht, und es gab KEINEN Pin. Gemessen am
  2026-09-10: 63 der 100 weissrussischen Eintraege mit kyrillischem Ortstext standen auf „mehrdeutig" („Витебск":
  Stadt und PLZ-Zeile auf demselben Punkt, Oblast-Mitte 78 km weg; „Гродно" 90 km), und **639 Regionszeilen teilen
  ihren Namen mit einem Ort desselben Landes** — Tuerkei 71, Thailand 67, Aserbaidschan 65, Algerien 41, Kenia 35.
  Der Fall ist also weltweit und nicht kyrillisch. Sichtbar wurde er erst durch die Umschrift (0.456.0): vorher
  trugen kyrillische Regionszeilen einen leeren Suchnamen. Der Rueckfall auf die Regionsmitte bleibt (Feld `state`
  ueber `ResolveByRegionAsync`, und Gruppen, die NUR aus Regionen bestehen).

  **Diese Regel allein loest GROSSSTAEDTE nicht** — dort ist die Ursache eine andere: 182 Postleitzahl-Zeilen
  namens „Berlin" streuen ueber 24 km, also weiter als die 5-km-Schwelle `SameTownKm`, obwohl es EINE Stadt ist.
  Die Spannweite kann „eine Stadt mit vielen Stadtteilen" nicht von „mehrere gleichnamige Orte" unterscheiden
  („Muenster": 17 Zeilen ueber 301 km). Der Zusammenhang kann es: nach Single-Linkage gemessen (zweite Instanz,
  2026-09-10) fallen Berlin, Hamburg, Muenchen, Koeln, Bremen, Dresden und Wien **ab 8 km Lueckenschwelle** in je
  EINEN Haufen und bleiben das bis 15 km, Muenster dagegen in drei. Die beiden Regeln muessen dabei in dieser
  Reihenfolge komponieren: erst die Regionszeile heraus, dann haeufen — bei „Витебск" reisst die 78 km entfernte
  Oblast-Mitte sonst jeden Haufen auf. Steht als TODO, absichtlich noch nicht gebaut.
- **Ein Ortsname steht im Lexikon in ZWEI Schreibweisen** (seit 0.456.0) – `GeoPlace.NameNormalized` faltet
  Umlaute auf den Grundvokal (`München` -> `munchen`), `GeoPlace.NameTranscribed` haelt die ASCII-UMSCHRIFT
  (`muenchen`) und schreibt nichtlateinische Schriften um (`Київ` -> `kyiv`, `Αθήνα` -> `athina`). Gesucht wird in
  BEIDEN Spalten mit BEIDEN Textformen — Ortsnamen-Suche, Postleitzahl-Bestaetigung, Regionen. Zwei Gruende: (1)
  chess-results schreibt regelmaessig „Muenchen", und das traf `munchen` nie (53 unverortete deutsche Eintraege);
  (2) der Aufraeumteil der Normalisierung verwirft alles ausser `[a-z0-9]`, Kyrillisch fiel damit RESTLOS weg — 29 547
  von 29 571 ukrainischen PLZ-Zeilen trugen einen leeren Namen, und die Bestaetigung des PLZ-Wegs konnte dort nie
  gelingen. **Beim SUCHEN wird NICHT gefaltet** (`ue -> u` machte aus „Quedlinburg" ein `qudlinburg`) — die zweite Form
  entsteht beim IMPORT. **Die Umschrift haengt am LAND** (`NormalizeTranscribed(text, iso2)`): `и` ist ukrainisch
  ein `y` („Київ" -> `kyiv`), russisch ein `i` („Истра" -> `istra`), und GeoNames haelt es genauso — mit einer
  Tabelle fuer beide findet in einem der Laender KEIN Text seinen Ort (Russland: 2 003 Turniere, 7 % verortet, die
  groesste einzelne Luecke). Beide Seiten waehlen dieselbe Tabelle: der Lexikon-Eintrag ueber `GeoPlace.Country`,
  der Suchtext ueber das Land des Turniers. Ein Name, von dem nur eine ZAHL bleibt (die russischen PLZ-Zeilen
  heissen teils „Москва 194", in der ersten Form bleibt `194`), bestaetigt NICHTS — sonst bestaetigte er im Text
  eine Hausnummer. Nach einem Deploy einmal
  `POST /api/admin/tournament-directory/gazetteer/transcribe` laufen lassen, sonst ist die Spalte fuer den
  Altbestand leer. Ein Treffer ohne vergleichbaren Namen in BEIDEN Formen (Georgisch, Armenisch, Hebraeisch)
  bestaetigt sich ueber die LAENGE der Ziffernfolge; vorher war das stillschweigend ein Nein.
- **Verschwundene Turniere: NUR die Quelle, die ein Turnier fuehrt, darf es zurueckziehen** (seit 0.454.1) –
  Die Turniersuche zaehlt `MissedSweeps` und meldet ab dem zweiten Fehlschlag „abgesagt" (mit Benachrichtigung),
  **beschraenkt auf Eintraege MIT chess-results-Nummer**: die 15 Verbandskalender und der FIDE-Kalender fuehren
  Turniere, die dort per Definition nicht vorkommen, und sammelten sonst jede Nacht einen Fehlschlag. Am 2026-09-09
  live passiert (ein schachbund-Turnier stand nach zwei GER-Sweeps auf abgesagt, obwohl es stattfindet).
  Die Zusatzquellen ziehen seit 0.454.1 selbst zurueck (`ExternalDirectorySource.RetireVanishedAsync`) — mit vier
  Schranken: nur kuenftige Eintraege, nur ohne chess-results-Nummer, nur wenn diese Quelle der EINZIGE
  Herkunftsvermerk ist, **nur bis zum HORIZONT des Laufs** (dem spaetesten wiedergesehenen Termin — eine Quelle mit
  kurzem Vorschau-Fenster raeumt sonst alles dahinter ab), und **gar nicht**, wenn ein Lauf weniger als
  `MinSeenPercent` = 90 % seiner eigenen Kandidaten wiederbringt (dieselbe Lehre wie die MaxRows-Bremse: eine
  systematische Luecke wiederholt sich jede Nacht, die Karenz faengt sie NICHT ab). Die 90 % sind seit 0.455.0 am
  Anteil der wiedergesehenen KANDIDATEN gemessen; die erste Fassung verglich die Zahl gelieferter ZEILEN mit der Zahl
  der Kandidaten und liess alles ab der Haelfte gelten — bei Polen (620 Kandidaten, ein gewoehnlicher Tag kostet fuenf)
  waeren das 310 zugelassene Falschabsagen. Der chess-results-ANKUENDIGUNGSkalender zieht bewusst nichts zurueck — nur
  ~70 % seiner Zeilen tragen eine Kennung, und ohne stabile Kennung ist „fehlt" nicht von „umbenannt" zu unterscheiden.
  Der FIDE-Kalender zieht seit 0.455.1 ebenfalls zurueck (seine Kennungen sind stabile Ereignisnummern); dort traegt
  der Horizont besonders viel, weil ein Durchgang oft nur das laufende Jahr abfragt und ueber das naechste nichts
  sagen darf. **Spricht die Bremse an, steht das als Warnung im Log** (`RetireVanishedAsync` nimmt dafuer den Logger
  der Quelle) — ohne den Eintrag ist eine dauerhaft halb liefernde Quelle nicht von einer zu unterscheiden, bei der
  nichts verschwindet: beide ziehen nie etwas zurueck.
- **`MissedSweeps` zaehlt NAECHTE, nicht Laeufe** (seit 0.455.0) – Ein Fehlschlag wird nur gezaehlt, wenn der letzte
  laenger als `ExternalDirectorySource.MissCooldown` (20 h) zurueckliegt; `TournamentDirectoryEntry.LastMissAt` haelt
  ihn fest. Gilt fuer BEIDE Besitzer (Turniersuche und Zusatzquellen). Ohne die Sperre genuegten zwei Durchgaenge im
  Abstand von Minuten: der Aufhol-Lauf nach einem Deploy (auf Dev mehrmals am Tag) und `scripts/directory-runs.sh` mit
  bis zu elf Durchgaengen haetten abgesagt, was eine Quelle kurz nicht auswies.
- **Eine Zeile, die wir nicht lesen koennen, gilt als GELIEFERT** (seit 0.455.0) – In allen 15 Quellen steht
  `delivered.Add(...)` VOR der Pruefung auf Termin und Namen, und die Abruf-Methoden sieben Zeilen ohne lesbaren Termin
  nicht mehr aus (KNSB traegt einen festen Termin im Zeilentyp und meldet sie ueber `KnsbFetch.Unreadable`). Vorher
  sammelte eine solche Zeile Fehlschlaege und war nach zwei Naechten abgesagt — und ein geaendertes Datumsformat
  trifft nicht eine Zeile, sondern alle: genau der systematische Fall, den die Karenz nicht abfaengt.
- **Zusammenfuehren braucht mehr als zwei gemeinsame Woerter** (seit 0.455.0) – `ExternalDirectorySource.FindMatchAsync`
  (und der FIDE- sowie der Ankuendigungs-Abgleich) pruefen zusaetzlich: (1) **Fuellwoerter aus der Worthaeufigkeit der
  Foederation** (`CorpusFillerAsync`, ab 8 % der Namen; die feste `NameFiller`-Liste ist englisch und deutsch und
  liess „torneo", „scacchi", „turniej", „szach" durch), (2) **die Ortsangaben duerfen sich nicht widersprechen**
  (`PlacesAgree` — nennt eine Seite keinen Ort, wird nicht widersprochen), (3) **kein zweiter Vermerk derselben
  Quelle** am selben Eintrag (`HasOtherNoteOfSameKind`), (4) **die BEDENKZEIT-Klassen in den Namen duerfen sich
  nicht widersprechen** (`SpeedsAgree`; nennt eine Seite keine, entscheidet weiter der Wortvergleich). Punkt 4 kam
  aus dem ersten echten Lauf: in der Nacht zum 2026-09-10 liefen alle DREI tschechischen Zeilen des „UCT Chess
  Festival 09/2026" auf den Rapid-Eintrag, obwohl Blitz, Rapid und Standard je einen eigenen
  chess-results-Eintrag haben (1474369/1474368/1474370, gleicher Termin, gleicher Name bis auf das
  Bedenkzeit-Wort) — „blitz", „rapid" und „standard" stehen in `NameFiller`, der Abgleich hatte dort also gar
  keinen Unterscheider. Verglichen werden die NAMEN und nicht `entry.Speed`, damit der Vergleich symmetrisch
  bleibt (die Quellzeile hat kein solches Feld). Anlass: am 2026-09-09 waren auf Dev drei echte italienische
  Turniere (Cormòns, Frascati, Bellante, alle 20.09.) einem „Torneo Sociale Arci Scacchi Bolzano B" vom 21.09.
  zugeschlagen und als „geht darin auf" zurueckgezogen — sie waren im Verzeichnis nicht mehr zu finden. Ein
  Vereinsturnier ueber fuenf Wochen liegt im Termin-Fenster von jedem Wochenendturnier des Landes. **Falsches
  Zusammenfuehren ist der stillste Weg, auf dem ein Turnier verschwindet**, denn der eigene Eintrag traegt danach
  keinen Herkunftsvermerk mehr und keine Verschwunden-Erkennung sieht ihn.
- **Ein Herkunftsvermerk WANDERT, er wird nicht doppelt angelegt** (seit 0.453.13) – Der eindeutige Index liegt auf
  (`Kind`, `ExternalId`) und gilt ueber den GANZEN Bestand. `ExternalDirectorySource.NoteSourceAsync` sieht deshalb
  nicht nur die Vermerke des uebergebenen Eintrags, sondern fragt die Tabelle: haengt die Kennung woanders, wird der
  Vermerk UMGEHAENGT (ueber die Navigation, damit es auch bei einem noch nicht gespeicherten Eintrag geht). Am
  2026-09-09 starben daran drei Quellen gleichzeitig (Ungarn, Tschechien, Ankuendigungskalender), ausgeloest von 400
  neuen Eintraegen, die die Namens-/Terminvergleiche auf andere Eintraege verschoben haben. Und: deckt eine
  Quellen-Kennung mehrere Eintraege ab (die tschechische „2. ligy" sind die Gruppen A bis F), MUSS der Schluessel den
  Eintrag enthalten — `ChessCzDirectorySweepService.SeriesKey(series, publicId)`. Es gab DREI Fassungen dieser Einfuege-Logik
  (der gemeinsame Helfer, eine im Ankuendigungskalender, eine als `TournamentDirectoryService.NoteSource`) — seit
  0.454.1 laufen alle ueber den Helfer.
- **Eine fremde Kennung wird NIE gekuerzt** (seit 0.453.11) – `TournamentDirectorySource.ExternalId` ist 60 Zeichen
  lang und ein SCHLUESSEL. Wer laenger liefert, bekommt von `ExternalDirectorySource.NoteSource` eine
  `ArgumentException` mit Klartext und vermerkt stattdessen einen KURZSCHLUESSEL (Hash der Quellen-Kennung, siehe
  `WcuDirectorySweepService.PublicIdOf`). Abschneiden koennte zwei Turniere verschmelzen; zentral zu HASHEN waere
  noch schlimmer, weil die Quellen ihre Vermerke ueber die ROHE Kennung wiederfinden (`s.ExternalId == slug`) und
  damit jede Nacht in den eindeutigen Index liefen. Wales und Deutschland fuehren keine Turniernummer und
  scheiterten daran monatelang jede Nacht — Deutschland erst nach 283 s hoeflichen Crawlens, das den ganzen
  Durchgang wegwarf; die Meldung stand nur als innere Ausnahme eines `DbUpdateException` im Log.
- **401 ist nicht gleich Rauswurf** (seit 0.453.6) – Der Client (`authInterceptor`) beendet die Sitzung NUR, wenn der
  Server das Token ausdrücklich ablehnt: `WWW-Authenticate: Bearer error="invalid_token"` (abgelaufen, falsch signiert,
  Security-Stamp rotiert, Konto gelöscht). Ein 401 aus einem Controller — falsches aktuelles Passwort bei
  `change-password`/`DELETE profile/account`, falsches Passwort beim Login — trägt den
  Header nicht und darf den Nutzer nicht ausloggen (2026-09-09: ein Tippfehler beim Passwortwechsel warf den Nutzer
  raus, vier Fehlversuche später hielt er die App für vergesslich). Wer einen neuen 401-Grund einbaut, entscheidet
  damit über den Header — und vorher, ob es überhaupt ein 401 ist: ein ERWARTETES Nein, das schon ein anonymer Besuch
  auslöst, wird mit 204 beantwortet (`POST /api/auth/session` seit 0.478.6, vorher 401). Die Log-Überwachung zählt
  jeden 401 unter `/api/auth` je IP als abgelehnten Anmeldeversuch; 25 frische Browser-Sitzungen reichten für einen
  Brute-Force-HIGH. Serverseitig loggt `Services/JwtTokenGate.cs` JEDE Token-Ablehnung mit Grund und Pfad
  (Logger `RookHub.Api.JwtAuth`; Kibana: `message:JwtAuth*`) — vorher war ein abgelehntes Token in den Logs unsichtbar.
  Die Fehlschläge VOR der Signaturprüfung (abgelaufen, kaputtes Format, fremde Signatur) laufen vor dem Rate-Limiter
  und vor dem IpAddress-Enricher: sie tragen `IpAddress` deshalb selbst und sind je IP + Ausnahmetyp auf eine Zeile
  je Minute gedrosselt (Rest Debug, `JwtTokenGate.FailureThrottle`); kaputtes Format (`Bearer x`) ist nur Information.
  Ein Datenbankfehler WÄHREND der Prüfung lässt den Request durch (Warnung) statt 401 zu antworten: ein
  Server-Schluckauf darf keine Sitzung kosten. Die Anmeldemaske selbst schickt Angemeldete weg (`guestGuard` auf
  `/login` und `/register`, in beiden Frontends; `?switch=1` ist die bewusste Tür für einen Konto-Wechsel) — eine
  über Lesezeichen/Verlauf geöffnete Maske ließ sonst gültig Angemeldete ihr Passwort umsonst tippen.
- **Eine selbst geoeffnete Transaktion MUSS in der Execution-Strategy laufen** (seit 0.453.7) – Die Verbindung nutzt
  `EnableRetryOnFailure`, und die dadurch aktive `MySqlRetryingExecutionStrategy` WIRFT bei
  `BeginTransactionAsync` („does not support user-initiated transactions"): bei einem Wiederholversuch waere
  unklar, ob nur die Anweisung oder der ganze Block erneut laufen soll. Muster in
  `AdminService.ClearPuzzlesAsync` und `GazetteerImportService.ReplaceAsync` — `CreateExecutionStrategy()` +
  `ExecuteAsync(...)` um die ganze Transaktion, und der Block muss WIEDERHOLBAR sein (nicht vom Stand eines
  abgebrochenen Versuchs abhaengen). **Kein Unit-Test sieht das**: die InMemory-Datenbank kennt keine
  Transaktionen, der Code nimmt dort den nicht-relationalen Zweig. Am 2026-09-09 kostete es den kompletten
  Postleitzahlen-Import (alle sieben Laender mit 500, die Verortung blieb auf dem Ortsnamen). Die Wache ist
  deshalb eine QUELLTEXT-Pruefung: `TransactionStrategyTests`.
- **Kurs ⇄ Repertoire liegt in `Services/CourseRepertoireConversionService.cs`** (seit 0.499.4) — BEIDE Richtungen, denn es ist dieselbe Operation mit vertauschten Rollen (Quell-PGN holen, Ziel anlegen, Original ERST DANACH entfernen). Vorher stand die eine im `CourseService` und die andere ausgeschrieben im `RepertoireController`; eine Regel im Controller ist für jeden anderen Aufrufer unerreichbar und für einen Service-Test unsichtbar. **DI-Falle**: `CourseService` hängt schon an `RepertoireService` — der umgekehrte Weg wäre ein Zyklus, den der Container erst beim Auflösen meldet; dieser Dienst hängt an beiden und wird nur von den Controllern gerufen. Der Leer-Fall wirft eine `CourseConversionException` (erbt von `InvalidOperationException`, trägt `Code`) — der Controller fängt sie VOR dem allgemeinen 400-Zweig, damit nur DIESE Antwort ein `code`-Feld trägt.
- **Handgespiegelte Listen bekommen auf BEIDEN Seiten einen Test mit LITERALEN Werten** — nie einen, der die Gegenseite importiert (dann wandert ein Fehler mit). Festgenagelt sind heute: die neun SR-Intervalle (`RepertoireTrainingService.DefaultLevels` ↔ `repertoire-sr.util.ts`), der Linien-Hash (`ChessableTrainedLineService.LineKeyFromSans` ↔ `repertoire-line-key.util.ts`) und seit 0.499.3 die fünf Buch-Themen (`BookThemeTags.ValidKeys` ↔ `ALL_THEMES` in `features/courses/course-themes-dialog.component.ts`; dort ist die REIHENFOLGE die Anzeige-Reihenfolge und darf abweichen, die MENGE nicht — ein unbekannter Key wäre eine Checkbox, deren Haken beim Speichern still verfällt). Der Kommentar am Test nennt die Gegenseite beim Namen. Seit 0.641.0 auch der Kreis ums Gesicht in ClubHub (`ClubFace.Normalize` ↔ `clampFace` in `src-clubhub/app/core/face.ts`).
- **Versuchs-Werte kommen aus `Services/AttemptRecording.cs`** (seit 0.499.0) — Zeit auf `MaxSeconds` (86400) und Tipp-Stufe auf `MaxHints` (3) klemmen, Spielweise über `SolveMode.Normalize`, Startzeit = Versuchszeitpunkt minus der GEKLEMMTEN Zeit: `AttemptRecording.From(…)` liefert alles vier als `AttemptCore`. Benutzt von `PuzzleService` (beide Recorder), `BookPuzzleService` (drei), `CourseService.RecordResultAsync` und `WeeklyPostService.RecordAttemptAsync` — vorher siebenmal von Hand, und genau dort auseinandergelaufen (der Standard-Puzzle-Pfad klemmte die Zeit nur fürs Log). Die Log-Meldungen bleiben dabei WÖRTLICH gleich: Kibana-Auswertungen und der log-watcher hängen an den Message-Templates, geändert hat sich nur, woher die Werte kommen. `[Range]` am DTO (Wochenpost) bleibt der Vertrag zum Client; der Helfer ist dort ein No-op. `EndlessProgressService` normalisiert nur den Modus (kein Zeit-/Tipp-Feld am Lauf) und benutzt weiter `SolveMode.Normalize` direkt.
- **PGN wird mit `Services/PgnWriter.cs` geschrieben** (seit 0.499.7) — `Escape` (erst der Backslash, DANN das Anführungszeichen; umgekehrt verdoppelt der zweite Durchgang die gerade gesetzten Backslashes), `Tag(name, value)` → `[Name "…"]\n`, `CleanComment` und `MoveText(sans, startFen, comments, result, before)` mit Zugnummern (ab Grundstellung „1. e4 e5 2. …“, ab FEN mit Schwarz am Zug „12... Nf6 13. …“; Kommentar HINTER dem Halbzug, Schlüssel `-1` = Einleitung; `before` = roher Text VOR dem Halbzug, heute der `[%tqu]`-Marker). Benutzt von `CoursePgnExporter`, `SharedLineService.BuildLinePgn`, `SavedGameService.BuildPgn` und (nur das Escape) `ChessableReviewParser`. **Die FORMEN der vier bleiben verschieden und werden NICHT angeglichen** — sie stecken in geteilten Links und gespeicherten Partien: der Kurs-Export hängt sein `*` selbst mit einem Leerzeichen davor an (`result: null`, deshalb steht bei einer zuglosen Info-Linie `" *"`), die anderen beiden bekommen den Ergebnis-Token vom Writer (`"*"` ohne führendes Leerzeichen). `SavedGameService.Header` bleibt eigen: es ERSETZT das Anführungszeichen durch ein Apostroph, statt es zu maskieren. Golden-Tests halten alle drei Ausgaben zeichengenau fest (`CoursePgnExporterTests`, `SharedLineServiceTests`, `SavedGamePgnTests`) — wer am Writer dreht, sieht dort sofort, was er verschiebt.
- **DER PGN-Baumparser ist `Services/PgnMoveTree.cs`** (seit 0.499.6; Varianten nur bis `MaxVariationDepth` = 64
  geschachtelt, der Repertoire-Upload lehnt tiefere Schachtelung mit 400 ab statt am StackOverflow zu sterben —
  Codereview A6-001) — `ParseSections` (Abschnitte am `[Event `-Header, je Abschnitt `[White]`/`[Black]`/`[FEN]` + Zugbaum) plus `Tokenize`/`ParseMoveTokens`/`IsMoveToken`/`ExtractMovetext`/`StartFenOf`. Benutzt von `RepertoireAnalyzeService` (filtert auf `Moves.Count > 0` und hat als einziger den Fallback „ganzer Text als Movetext“, wenn kein Abschnitt Züge trägt) und `RepertoireLineSource` (nimmt ZUG-LOSE Abschnitte MIT — an ihrer Position hängt der `gameIndex`, über den Client und Server dieselbe Linie meinen). Vorher lag der Parser zweimal wörtlich da. `PgnMove` ist jetzt ein Typ im Namensraum `RookHub.Api.Services` (vorher in `RepertoireLineSource` geschachtelt). **Was bewusst NICHT dorthin gehört, weil es eigene Semantik hat**: `PgnParser` („1/2“ als Ergebnis-Token, `CleanSan`-Kanonisierung), `ChessableTrainedLineService.MainlineSans` (überspringt Varianten ganz und weist jedes Token MIT Punkt ab — wegen „e.p.“; geteilt sind nur `IsMoveNumber`/`IsResultToken`/`StartFenOf`) und `ReconstructionChain.SplitMoves` (behält Suffix-Annotationen, entfernt nur die innersten Klammerpaare, anderer Zugnummern-Regex).
- **Statistik-Kennzahlen kommen aus `Services/AttemptStats.cs`** (seit 0.499.5) — Serien (`Streaks`, Liste NEUESTER ZUERST), Trefferquote (`Accuracy`, eine Nachkommastelle, 0 bei 0 Versuchen), 200er-Rating-Bänder (`RatingBands`), Aktivitäts-Tage (`Activity` + `ActivityWindowStart` = heute − 364) und die Themen-Top-20 (`TopThemes`, bei Gleichstand nach Namen). Benutzt von `PuzzleStatsService` UND `CourseStatsService`: deren EF-Abfragen sind verschieden (andere Tabellen, Themen aus `PuzzleTags` gegen den `BookPuzzle.Tags`-String) und bleiben je Dienst — gleich war immer nur die Rechnung danach, und die stand zweimal ausgeschrieben da. Die DTOs teilen sich entsprechend `AttemptStatsDto` (TotalAttempts/Solved/Accuracy/CurrentStreak/BestStreak/TrainingCount/EasyCount); `PuzzleStatsDto` ergänzt NUR `PuzzleElo`/`PuzzleEloPerLevel`, `CourseStatsDto` nichts. **Die JSON-Feldnamen sind der Vertrag, die Reihenfolge nicht** (sie verschiebt sich durch die Vererbung). Frontend-Gegenstück: `AttemptStatsDto` in `features/puzzles/puzzle.service.ts`; die Statistikseite liest die fünf Kacheln über EIN `current` statt fünf Modus-Weichen.
- **Import-/Aufbereitungs-Pipeline versionieren** – Ändert sich die Transformation Roh-PGN → gespeicherte `BookPuzzles` (bzw. abgeleitete Repertoire-Daten) so, dass BEREITS importierte Datensätze unvollständig/veraltet werden (Beispiel: nachträgliche Pro-Zug-Kommentar-Extraktion), MUSS `ImportPipeline.CurrentVersion` (in `Services/ImportPipeline.cs`) um 1 erhöht und die Versionshistorie im Doc-Kommentar ergänzt werden. Bücher/Repertoires mit kleinerer `ImportVersion` gelten dann als „veraltet" und werden über den „Aktualisieren (N)"-Knopf (Sektion Kurse/Repertoires, `ReprocessBannerComponent` → `/api/courses|repertoires/reprocess`) neu aufbereitet — **in-place per LineId** (Fortschritt/Statistik-FKs bleiben erhalten), Quelle ist `Book.Source.SourcePgn` (bzw. Chessable-Re-Fetch). `ImportFileAsync` aktualisiert bestehende Linien NUR, wenn das Buch veraltet ist; sonst überspringt es sie (idempotenter Resume). **Ändert piratechess die PGN-Erzeugung** (neuer Header, andere Zugtext-Form), genügt seit 0.509.0 ebenfalls der Bump: Chessable-Kurse mit `[ChessableOid]` holen ihre Zugtexte beim „Aktualisieren" aus dem geteilten Linien-Cache neu (`StaleAction.Cache`, Abschnitt „Chessable-Kurse mit oids kommen aus dem Linien-Cache"), Chessable-Repertoires mit oids seit 0.510.0 genauso (je Datei, ausgeblendete Partien bleiben).
- **Kalkulations-Modus ist KEIN Solver** – `features/courses/calc/` (Route `/courses/:bookId/calc`) ist bewusst nicht von `BasePuzzleSolver` abgeleitet: es gibt nichts zu lösen, keine Zeit-/Elo-Wertung und keine Lösungs-Anzeige. Er nutzt nur die `PuzzleBoardComponent` im `visualization`-Modus (Brett bleibt eingefroren, Klicks werden als Koordinaten erfasst). Zwei Eigenschaften dürfen dabei NICHT verloren gehen: (1) das Brett bleibt strikt auf der Ausgangsstellung — kein `fen`-Update beim Navigieren, `actualFen` dient nur der Legalitätsprüfung; (2) die Lösung wird nicht ausgeliefert (siehe `CalculationService`) — beim Erweitern der Kalkulations-DTOs also **niemals** `BookPuzzle.Moves` durchreichen. Ohne Konto (Kurz-URL `/{slug}`) tritt `LocalCalculationBackend` (localStorage) an die Stelle des Servers: **jeder Schreibweg dort muss einen Fehlschlag als Fehler melden** (`writeCalcLocal*` gibt `null` zurück, wenn nichts geschrieben wurde) — ein `of(...)` mit dem bloß gerechneten Stand zeigte „gespeichert", obwohl bei gesperrtem/vollem Speicher (Privatmodus, Quota) nichts liegt; die Ansicht ersetzt den Hinweis „liegt nur auf diesem Gerät" dann durch „konnte gerade gar nicht gespeichert werden" (`localSaveFailed`). Bei aktivem Kapitelfilter (`/{slug}/{kapitel}`) gehört auch die angezeigte Gesamtsumme dem KAPITEL (`chapters[]`), nicht dem Buch — Liste und Summe müssen denselben Zuschnitt haben.
- **Vollbild-Brett gehört in die BRETT-Komponenten** – Der Vollbild-Knopf (`shared/fullscreen/`, echtes Element-Vollbild über Taskleiste/Browserleiste) sitzt in den drei Brett-Komponenten selbst (`PuzzleBoardComponent`, `AnalysisBoardComponent`, `pgn-viewer/ChessBoardComponent`) — deshalb haben ALLE Bretter ihn automatisch (Standard/Endless/Buch/Kurs/Daily/Wochenpost, Kalkulation, Durchsehen, Repertoire-Trainer, Analyse, PGN-Viewer), ohne ihn in jedem Consumer zu wiederholen (`[allowFullscreen]="false"` schaltet ihn im Puzzle-Brett ab). Zwei Dinge dürfen dabei nicht kippen: (1) ins Vollbild geht eine ÄUSSERE Hülle (`.board-fs-host`/`.ab-fs-host`/`.cb-fs-host`), deren Größe der Browser auf 100 % × 100 % erzwingt (UA-`!important` schlägt sogar Author-`!important` — dem Vollbild-Element selbst eine Größe zu geben ist zwecklos, Regression 0.322.0: Brett füllte die Breite und lief unten raus); das Brett wird DARIN per Flex zentriert als `min(100vw,100vh)`-Quadrat mit schwarzen Balken. Der Brett-Wrapper bleibt exakt die Brettfläche, sonst rechnen die absolut positionierten Auflagen (Umwandlungs-Auswahl, Viz-Ring) gegen den Bildschirm statt gegen das Brett; (2) im Vollbild rendert der Browser **nur diesen Teilbaum** — Bedienelemente, die dort erreichbar bleiben müssen, gehören ins Vollbild-Element (Vollbild-Knopf; die Solver-Aktionen Tipp/Zurücksetzen/Mausrutscher/Aufgeben liegen seit 0.338.0 als `BoardFsActionsComponent` per `<ng-content>` + `data-fs-only` in den schwarzen Balken, Sichtbarkeitsregeln geteilt in `solver-actions.util.ts`). CDK-Overlays (`matTooltip`, Snackbar, Dialog, Menü) waren dort früher unsichtbar — seit 0.339.0 zieht der `FullscreenOverlayService` (app-weit in `AppComponent`) den Overlay-Container fürs Vollbild ins Vollbild-Element um, sie erscheinen also normal; das war ein echter Hänger, weil ein modaler Dialog („Ganz schön lang"-Nachfrage, `disableClose`) blockierte, ohne klickbar zu sein. Seit 0.478.3 laufen die Overlays dafür bewusst OHNE Popover-API (`provideFullscreenSafeOverlays()`): ein offenes Popover schließt der Browser beim Umhängen still. Brett- und App-Vollbild unterscheidet `isElementFullscreen()` — `!!document.fullscreenElement` ist in BEIDEN wahr. Für Elemente im Vollbild-Element selbst bleibt das native `title`-Attribut die einfachere Erklärung. chessground legt Figuren per Pixel-Transform ab: nach jeder Größenänderung `redrawAll()` (ResizeObserver in allen drei Brettern).
- **UI-Dichte-Regel (seit UI-Welle 2/3, v0.334.0)** – Pro Screen genau EINE primäre Aktion
  (mat-flat/raised, farbig), höchstens drei sekundäre sichtbar; alles Weitere gehört ins ⋮-Menü
  (Solver: `PuzzleActionBarComponent`, Karten: Overflow-Menü wie `course-card`). Erklärtexte: pro
  Karte höchstens EIN Satz Fließtext — mehr gehört hinter ein `HelpHintComponent`-?-Icon
  (`shared/help-hint`, Tooltip mit `\n\n`-Absätzen), nie als gestapelte `<p class="muted">`.
  Inhaltsseiten zentrieren ihren Container auf `max-width: min(var(--page-max-width), 96vw)`
  (CSS-Variable in `styles.scss`, aktuell 1240px; Ausnahme: Admin-Tabellen 1400px).
  Dashboard-Kacheln neuer Features kommen NICHT in `DEFAULT_VISIBLE` (Default = Trainings-Kern;
  Rest ist über „Anpassen" zuschaltbar). Ohne diese Regel wächst die Dichte mit jedem Feature
  zurück (gemessen im UI-Review 2026-07-26, siehe TODO.md). Zustandsfarben (Fehler, Erfolg, Warnung, Info, Akzent)
  kommen aus den semantischen Tokens in `src/_tokens.scss` (`var(--rh-…)`, hell/dunkel), nicht als Hex-Wert
  (Codereview F8-003, Details in `src/frontend/CLAUDE.md`).
- **Puzzle-Modi konsistent halten** – Standard (`puzzle.component`), Endless (`endless-puzzle.component`) und Book/Course/Weekly/Daily (`book-puzzle.component` – ist selbst schon Mehr-Modus-Template) sollen optisch + funktional so ähnlich wie möglich bleiben. Wenn ein Modus eine UI-/UX-Erweiterung bekommt (z. B. „Tags ausklappbar", „Eval-Button", „Viz-Pfeil"), **immer kurz nachfragen**, ob das nicht auch in den anderen zwei Modi sinnvoll wäre. Gemeinsame Bausteine in dedizierte Komponenten (`PuzzleTagsComponent`, `VizCardComponent`, `ReviewNavComponent`, `ThemePickerComponent`) auslagern statt 3-fach kopieren; die Solver-Mechanik liegt in `BasePuzzleSolver`.
- **Buch-/Kurs-FENs sind nicht immer legal** – Chessable-Muster-/Info-Diagramme (`IsInfoOnly`) benutzen bewusst ILLEGALE Stellungen (z. B. ganz ohne König); chess.js/Gera.Chess werfen dort. Jede FEN-Ladung in einem Buch-/Kurs-Pfad muss das aushalten: im Frontend `tryLoadFen` (+ `replayIllegalFen` aus `illegal-board.util` fürs Durchklicken) statt `new Chess(fen)`, im Backend der permissive Pfad (`PermissiveSan`). Besonders heikel sind **Template-gebundene Getter** (z. B. `commentBlocks`): ein Wurf dort passiert MITTEN in der Change-Detection und lässt alles darunter unrendert (Kommentar, Info-Karte, „Weiter", Teilen) — die Seite wirkt „kaputt", obwohl das Brett stimmt (0.317.2).
- **Variablen-Muster in den Compose-Dateien** – drei Fälle, und zwar in ALLEN fünf Dateien gleich (`compose.yml.example`, `compose.vpn.example`, `compose.vpn.yml`, `compose.dev.yml`, `compose.dev.vpn.yml`): **Pflichtwert** → `${VAR}` ohne Default (fehlt er, ist der Stack sowieso kaputt); **optionales Feature** → `${VAR:-}` (leer = Feature aus, alle betroffenen Endpoints sind fail-closed: Bot-Stats 503 `not-configured`, CI-Report 401, Webhook deaktiviert); **Wert, dessen Fehlen still Schaden anrichtet** → `${VAR:?Meldung}`, damit `docker compose` mit einer Meldung abbricht statt den Container in eine Neustartschleife zu schicken (heute: `JWT_KEY` und `ENCRYPTION_KEY` — ein leerer Encryption-Key wäre keine abgeschaltete Verschlüsselung, sondern eine Schein-Verschlüsselung mit dem öffentlich bekannten SHA256("")). Wenige echte Vorgabewerte sind bewusst gesetzt (`EMAIL_SMTP_PORT:-587`, `EMAIL_FROM_NAME:-RookHub`, `EMAIL_USE_STARTTLS:-true`, `CHESSABLE_API_URL:-…`). Die frühere Fassung dieser Regel („keine `:-`-Defaults in den Beispielen") beschrieb den Ist-Zustand nicht — es gab 16 pro Datei — und ließ den nötigen `:?`-Guard als Ausnahme unsichtbar. `DeploymentConfigTests.EveryCompose_GuardsEncryptionKey_AndPassesOptionalSecrets` nagelt das fest.
- **Platzhalter-Geheimnisse gelten nirgends als Schlüssel** (`Services/SecretConfigCheck.cs`) – alle Repos sind öffentlich, und der alte JWT-Platzhalter `change_me_to_a_secure_key_at_least_32_chars` bestand mit 43 Byte die Längenprüfung (Admin-JWT für jeden Repo-Leser). Erkannt werden die Präfixe `change_me`/`changeme`/`your_` (Groß-/Kleinschreibung egal) und die Spitzklammer-Form `<…>` aus den Compose-Kommentaren. Folgen: `Jwt:Key`/`Encryption:Key` → in Production **Startabbruch**, sonst Error-Log; eingehend geprüfte Geheimnisse (`Discord:LinkSecret`, `SchachBot:StatsSecret` samt Ergebnis-GET-Signatur) → **Feature aus** wie bei leerem Wert + Error-Log; nur mitgeschickte Schlüssel (`Crawler:ApiKey`, `Chessable:ServiceKey`, `SchachBot:WebhookSecret`) → nur Error-Log (das Loch sitzt bei der Gegenstelle). Die `.env*.example` führen die fail-closed-Schlüssel LEER (`JWT_KEY`, `ENCRYPTION_KEY`, `DISCORD_LINK_SECRET`, `SCHACH_BOT_STATS_SECRET`; in `.env.vpn.example` auch `CHESSABLE_SERVICE_KEY`), kein Geheimnis teilt sich einen Platzhalter — `SecretConfigCheckTests.BeispielEnv_ohneBenutzbarePlatzhalter` nagelt das fest. Neues eingehendes Geheimnis → in `SecretConfigCheck.InboundSecrets` eintragen und beim Lesen `SecretConfigCheck.Usable(...)` nehmen.
- **i18n-Validierung**: Nach jeder Änderung an `src/frontend/app/src/assets/i18n/*.json` alle 25 Sprachdateien mit `JSON.parse` validieren — Trailing-Comma-Fehler bricht ngx-translate komplett, UI zeigt dann nur noch Schlüssel statt Texte
- **Erwartete Fehler als Domänen-Ausnahme, nicht als BCL-catch im Controller** (Codereview 2026-09-29, A10-005) – Ein Dienst wirft `NotFoundException` (404), `DomainValidationException` (400), `ConflictException` (409) oder `ForbiddenException` (403) aus `Exceptions/DomainExceptions.cs` mit einer für NUTZER geschriebenen Meldung; der global registrierte `DomainExceptionFilter` macht daraus `{ message }` mit dem Status — dieselbe Antwort wie das frühere `NotFound(new { message = ex.Message })`. Der Controller fängt nichts. Alles andere (auch die BCL-Basistypen selbst) läuft in den globalen Handler: 500 + Error-Log mit Stacktrace. Vorher fingen 28 Controller KeyNotFound/InvalidOperation/Argument/UnauthorizedAccess selbst, und jeder echte Fehler dieser Typen (Dictionary-Zugriff, LINQ-`First`, verworfener DbContext) kam als 4xx mit Framework-Text beim Client an, ohne Log. **Bewusst ein MVC-Filter, kein `IExceptionHandler`**: der globale Handler sitzt VOR `UseSerilogRequestLogging`, und eine bis dorthin durchlaufende Ausnahme schreibt das Request-Log fest als „responded 500" auf Error. Umgestellt sind bisher `RolesAdminController` und die beiden Punktepartie-Controller (samt `RoleAdminService`/`GuessSessionService`); der Rest folgt Controller für Controller. Die Typen erben vom bisher gefangenen BCL-Typ — ein alter `catch` fängt sie weiter, ein Dienst darf also vor seinen Aufrufern umgestellt werden (beim Umstellen trotzdem jeden Aufrufer prüfen: `DomainValidation` erbt von `InvalidOperation`, nicht von `Argument`; Tests mit `Assert.ThrowsAsync<…>` prüfen den EXAKTEN Typ).
- **Periodische Hintergrunddienste erben von `PeriodicWorker`** (`Services/PeriodicWorker.cs`, Codereview 2026-09-29,
  A8-008) – `BackgroundServiceExceptionBehavior` steht in `Program.cs` ausdrücklich auf `StopHost`. Jeder Worker
  implementiert `LogFailure` mit einem EIGENEN wörtlichen Template (kein `{Text}`-Parameter, kein gemeinsames
  Template), weil log-watcher/Kibana nach `labels.MessageTemplate` gruppieren; ein umgezogener Dienst behält sein
  altes Template wortgleich.
- **Fluent-Konfiguration neuer Entitäten gehört in `Data/Configurations/`** (Codereview 2026-09-29, A9-005) – je
  Entität eine `IEntityTypeConfiguration<T>` (`internal sealed class <T>Configuration`) in der Domänen-Datei
  (`AccountConfigurations.cs`, `LeagueConfigurations.cs`, `ClubConfigurations.cs`, …), eingesammelt per
  `ApplyConfigurationsFromAssembly`; `AppDbContext.cs` hält nur DbSets und `ConfigureConventions`, der Schutz neuer
  Bücher ohne BookSource steht in `AppDbContext.BookSource.cs`. Wächter: `DbContextConfigurationTests` (kein
  `.Entity<` in `AppDbContext*.cs`, keine Entität doppelt konfiguriert).
- **Literal-Routen vor Parameter-Routen**: z.B. `GET /api/weekly-posts/progress` MUSS vor `GET /api/weekly-posts/{id}` deklariert sein, sonst matcht der Router „progress" als ID
- Crawler-Proxy-Endpoints müssen mit tatsächlichen Crawler-Routen übereinstimmen
- Angular nutzt lazy-loaded standalone components (kein NgModule)
- JWT-Claims: `ClaimTypes.NameIdentifier` = UserId, `ClaimTypes.Name` = Username
- PGN-Upload-Limit: 10 MB pro Datei (in `RepertoireService`)
- Alle Controller holen UserId via `User.FindFirstValue(ClaimTypes.NameIdentifier)`
- Friendship-Status ist eine State Machine: Pending → Accepted/Declined; nur der Addressee kann Accept/Decline ausführen
- **Scanner-Pfade bekommen 404, nicht die Startseite** (seit 0.481.2) – Der SPA-Fallback (`try_files … /index.html`) beantwortet JEDEN unbekannten Pfad mit 200. Ein .env-Scan am 2026-09-17 bekam so für 291 von 313 Pfaden „200“ — preisgegeben wurde nichts, aber für einen Scanner ist das ein Treffer. Die Regex-Locations in `src/frontend/nginx.conf` (Punkt-Pfade, `env.*`, Konfig-/Backup-/Skript-/Archiv-Endungen, `wp-`/`phpmyadmin`/`cgi-bin`/`vendor/`) antworten deshalb mit 404. Zwei Dinge dürfen dabei nicht kippen: (1) **alle `/api/`-Locations tragen `^~`** — sonst fängt die Punkt-Regel `/api/.env` ab, bevor die API es loggt, und der log-watcher (`suspicious_requests`) wird für API-Scans blind; (2) die Regeln stehen VOR der OG-Weiche (bei Regex-Locations gewinnt der erste Treffer), und `/.well-known/assetlinks.json` bleibt eine EXAKTE Location. `.json`/`.js` stehen bewusst nicht in der Liste — das sind echte Bundle-Dateien. `DeploymentConfigTests.ScannerPaths_Get404_WhileAppFilesAndApiStayUntouched` prüft die Muster gegen Scanner-Pfade UND gegen Dateien/Routen der App — wer eine Endung ergänzt, ergänzt dort beide Listen. Die IP-Sperre bekannter Scanner-Netze liegt NICHT hier, sondern im Nginx Proxy Manager (`/data/nginx/custom/http_top.conf`).
- Stockfish-WASM **NICHT** über Service-Worker cachen außer in eigener assetGroup `engine` (installMode prefetch) — der Glue muss bei `instantiateStreaming`-Fehler auf `instantiate(arrayBuffer)` zurückfallen, sonst hängt die Analyse
- HMAC-Webhooks zum Bot: gleiches Secret-Pattern (`SchachBot:WebhookSecret` für Tagespuzzle/Wochenpost, `SchachBot:StatsSecret` für Bot-Stats-Pull) — `ComputeHmacHex` aus `SchachBotWebhookService` wiederverwenden; eingehende Bot-Signaturen prüft `BotRequestSignature.Verify`/`CheckPath` (eine Implementierung für `player-progress` und die Ergebnis-GETs)
