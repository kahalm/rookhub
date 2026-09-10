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
| POST | `/api/auth/login` | Login, gibt JWT zurück (gültig 30 Tage, mit `rememberMe` 90) |
| POST | `/api/auth/forgot-password` | „Passwort vergessen" `{ email }` — schickt (falls die Adresse zu einem aktiven Konto gehört) einen einmaligen Reset-Link (TTL 1 h) per Mail. Antwortet IMMER 200 (keine User-Enumeration). Versand via `PasswordResetService` + `IEmailSender` (SMTP/MailKit); ohne `Email:SmtpHost` wird die Mail nur geloggt. Link-Basis = `App:BaseUrl` |
| POST | `/api/auth/reset-password` | Neues Passwort setzen `{ token, newPassword }` — 204 bei Erfolg, 400 bei ungültigem/abgelaufenem/verbrauchtem Token. Token ist einmalig (`UsedAt`) |
| POST | `/api/auth/session` | Geteilte Anmeldung der Schwesterseite übernehmen — Nachweis ist das Cookie auf der gemeinsamen Elterndomäne (`SharedSessionService`). 401 = keine, ohne Unterscheidung; ein untaugliches Cookie wird dabei gelöscht |
| POST | `/api/auth/session/end` | Geteilte Anmeldung beenden (Abmelden) — löscht das Cookie, immer 204 |

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
| GET | `/api/friends/requests` | Offene (eingehende) Anfragen |
| GET | `/api/friends/requests/sent` | Von mir gesendete, noch nicht angenommene (Pending) Anfragen — für „wartet auf Bestätigung" in der Freundesliste. Literal-Route vor `{...}` |
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
| GET | `/api/challenges/outgoing/pending-counts` | Pro Freund (Map `toUserId`→Count) die von mir geschickten, noch OFFENEN (Pending) Challenges — für die „Freund (n)"-Klammer im „An Freund schicken"-Menü. Nur Freunde mit n > 0. Literal-Route vor `{id}` |
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
| POST | `/api/repertoires/{id}/convert-to-course` | „Repertoire → Kurs umwandeln": legt aus dem kombinierten Repertoire-PGN einen persönlichen Kurs an (`CourseService.UploadPersonalCourseAsync`). Nur bei Puzzle-PGN im Chessable-Stil (FEN + Trainingsmarker); reines Eröffnungs-Repertoire → 400. Nur der Besitzer (verschiebt/löscht das Original) |
| POST | `/api/repertoires/{id}/share` | „Repertoire mit ausgewählten Personen teilen" (Batch) `{ recipientUserIds[] }` — nur der Besitzer; Empfänger müssen befreundet sein (Admin an alle). Antwort `{ shared, skipped[] }` (Gründe `self`/`not_found`/`not_friends`/`duplicate`); Notification `repertoire_shared`. Empfänger sehen/öffnen/downloaden/trainieren es (eigener SR-Fortschritt), können es NICHT bearbeiten/löschen/weiterteilen. 403 wenn nicht Besitzer |
| GET | `/api/repertoires/{id}/shares` | Mit welchen Nutzern ist dieses eigene Repertoire geteilt (für den Teilen-Dialog); 403 wenn nicht Besitzer |
| DELETE | `/api/repertoires/{id}/share/{recipientId}` | Freigabe für einen Empfänger zurücknehmen (idempotent); 403 wenn nicht Besitzer |
| GET | `/api/repertoires/reprocess/status` | Aufbereitungs-Status der eigenen Repertoires (heute meist 0; live ausgewertet). Literal-Route vor `{id}` |
| POST | `/api/repertoires/reprocess` | Markiert veraltete eigene Repertoires auf die aktuelle Pipeline-Version (heute No-op für abgeleitete Daten) |
| GET | `/api/repertoires/{id:int}/flashcards` | PERSISTENT als Flashcard markierte Linien `{ lineKeys }` — Besitzer UND Freigabe-Empfänger, jeweils EIGENER Satz (404 ohne Lese-Zugriff) |
| POST/DELETE | `/api/repertoires/{id:int}/flashcards/{lineKey}` | Flashcard-Markierung einer Linie setzen/entfernen (idempotent) → `{ marked }`; LineKey = Frontend-Linien-Hash (`repertoire-line-key.util.ts`, wie SR — Re-Import mit geänderter Zugfolge lässt Markierungen ins Leere laufen, gewollt). Frontend: Checkboxen der Linienliste + „(n)"-Knopf → `/repertoires/:id/flashcards?marked=1` |

### Extension API (auth, CORS für chess.com)
| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/extension/repertoires?kind=opening` | Leichtgewichtige Liste (id, name, fileCount, kind, totalSizeBytes); `kind` filtert auf `none|opening|middlegame|endgame`. Nur Repertoires mit `UseForExtension=true` (Default true, im Bearbeiten-Dialog abwählbar); gilt ebenso für das Positions-Set der Abweichungsanalyse (`RepertoireAnalyzeService`) |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |
| POST | `/api/extension/training-activity` | Meldet ein Häppchen AKTIVER Chessable-Trainingszeit `{ secondsActive (1–3600), movesTrained?, linesTrained?, courseId?, courseName?, courseKind? }` — Modus-Labels („Practice Moves“) als Kursname werden verworfen und via Kurs-ID aus der gecachten Kursliste geheilt (von RepCheck auf chessable.com gemessen). Append-only → `ChessableActivities`; fließt in die Kategorie „Chessable" des Trainingsziele-Trackers. Zeitstempel serverseitig |
| POST | `/api/extension/remember-line` | Merkt eine auf chessable.com angezeigte Stellung `{ fen, courseId?, courseName?, sourceUrl? }` → `RememberedPositions` (append-only, Verwendungszweck offen). **Kursname**: die Extension liefert ihn (über den erfassten Chessable-Bearer aus der Chessable-API) mit; fehlt er, löst der Server ihn aus dem gespeicherten Bearer des Users auf — cache-first aus `ChessableCredential.CachedCoursesJson`, sonst best-effort Live-Abruf (`ChessableProxyService.GetCoursesAsync`). `GET /remembered-lines` trägt bei Alt-Einträgen ohne Namen den Cache-Namen nach |
| POST | `/api/extension/chessable/line-trained` | „Linie auf Chessable trainiert" `{ bid, oid }` — markiert die Linie in den RookHub-Gegenstücken: Kurs-Linie gilt als gelöst (idempotenter CoursePuzzleResult, bewusst OHNE CourseAttempt), im Repertoire-Trainer wird eine neue Linie „gelernt" (Stufe 1) bzw. eine FÄLLIGE eine SR-Stufe vorgerückt (Chessable ersetzt das Review; nicht fällige/pausierte bleiben unangetastet). CardKey = serverseitiger SPIEGEL des Frontend-Linien-Hashs (`ChessableTrainedLineService.LineKeyFromSans` ↔ `repertoire-line-key.util.ts`, cyrb53 — MUSS synchron bleiben, Vektoren-Test); Linie via PGN-Header `[ChessableOid]`. Unbekannte bid/oid = kein Fehler |
| POST | `/api/extension/chessable/problem-moves` | „Schwierige Züge" ablegen (Batch-Upsert je User+bid+oid): `{ bid, entries: [{ oid, nHard?, problemMoves?, lastReviewed? }] }` — nHard aus getList, Zug-Details (`game.problemMoves.thisUser`, opakes JSON ≤16 KB, `{}` löscht alte Fehlzüge) + lastReviewed ("never"→null) aus getGame; fehlende Felder lassen den gespeicherten Wert stehen. Quelle: RepCheck-Capture beim Training/Kurs-Holen |
| POST | `/api/extension/chessable/session-moves` | „Sitzungszüge" ablegen (APPEND-ONLY, kein Upsert): `{ bid, entries: [{ oid, moves }] }` — je trainierter Linie der rohe `moves`-Block aus Chessables eigenem Session-Report (`saveProgressAndReturnNewProgressInfo`-REQUEST, von RepCheck v1.54.0 mitgeschnitten; die Antwort enthält Konto-Daten und bleibt tabu). Enthält je Halbzug u. a. `wrong[]` (falsch gespielte Züge), Overstudy-/Alternative-Flags, Level, Punkte. Opak (nur Array-Form + ≤64 KB geprüft), jeder Durchlauf = eigene Zeile in `ChessableSessionMoves` (Auswertung offen); per-User-Deckel 200k Zeilen (älteste raus). NUR authentifiziert — kein Anon-Pfad |
| POST | `/api/extension/chessable/review-lines` | „getReview-Linien" ablegen (Batch je User+bid+oid): `{ bid, entries: [{ oid, json }] }` — das ROHE getReview-JSON EINER trainierten Linie (opak, ≤256 KB/Linie), erst beim Kurs-Aufbau geparst (`ChessableReviewParser`). Zweite Linien-Quelle NEBEN getGame: `UpsertBatchAsync` legt/aktualisiert die Roh-Zeile ab, dann best-effort `MergeIntoCourseAsync` — die Lücken (oid noch kein BookPuzzle) werden ins Kurs-Buch `chessable-u{userId}-{bid}.pgn` als `BookPuzzle.Source="review"` eingespielt. **getGame gewinnt** (oid-basiert): ein echter getGame-Import ersetzt einen Review-Füller IN-PLACE (dieselbe Zeile, `Source→null`, Fortschritt/FKs bleiben) statt ein Duplikat anzulegen. Antwort `{ stored, merged }`. Quelle: RepCheck-Capture beim Training. Wird von RepCheck erfasst beim Durchtrainieren (getReview) |
| POST | `/api/extension/chessable/review-lines/anon` | **AllowAnonymous** (per-IP-RL) — token-lose Ablage von getReview-Linien für Nutzer OHNE RookHub-Token: `{ uid, bid, entries: [{ oid, json }] }`. Statt eines Accounts identifiziert die **Chessable-uid** (client-seitig aus dem Chessable-JWT decodiert) die Linien; sie landen in `AnonymousChessableReviewLines` (Upsert je uid+bid+oid), **kein** Merge (kein Zielkonto). Werden GECLAIMT beim **erfolgreichen Chessable-Bearer-Test** (`POST /api/chessable/test`): dort ist die uid von Chessable BEWIESEN zurückgegeben (`TestAsync`) — NICHT aus dem ungeprüften JWT decodiert (sonst könnte man per gefälschtem JWT fremde Anon-Daten claimen). Der Test setzt `ChessableCredential.ChessableUid` und ruft `ClaimAnonForUidAsync` (übernimmt in `ChessableReviewLines` + baut die Kurse). Missbrauchs-Schranken: per-IP-RL, 16 MB/Request, **`MaxAnonRowsPerUid`=5000** (kein neuer oid je uid darüber), Retention 90 Tage (nächtlich, `ChessableCourseRefreshScheduler`). Akzeptiertes Rest-Risiko: wer eine fremde numerische uid KENNT, kann Linien unter ihr vorbelegen (getGame gewinnt, Inhalt löschbar, gedeckelt). RepCheck sendet hierher NUR nach expliziter Einmal-Zustimmung; Ziel = konfigurierte URL, sonst Default `rookhub.oberschmid.homes`. Antwort `{ stored }` |
| GET | `/api/extension/remembered-lines?take=200` | Gemerkte Stellungen des Users (neueste zuerst) |
| POST | `/api/extension/games` | Speichert die aktuell auf chess.com/lichess angeschaute Partie (Button „Partie speichern") `{ source, moves[], externalId?, white?, black?, result?, sourceUrl?, playedAt? }` → `SavedGames`. Server baut das PGN aus der SAN-Zugliste + Headern und vergibt ein `ShareToken`. Dedup über (UserId, Source, ExternalId). Sichtbar im Bereich „Partien" (`/api/games`) |

### Gespeicherte Partien (auth + öffentlicher Teilen-Link)
Bereich „Partien" (`/games`): zeigt die über die RepCheck-Extension von chess.com/lichess gespeicherten Partien. Nachspielen (PGN-Viewer-Dialog), „In Analyse öffnen" (PGN via Router-State an `/analysis`), Löschen, und Teilen über einen eindeutigen öffentlichen Link `/g/{shareToken}` (kein Login). Logik in `SavedGameService`; Menü-Key `games` (Default `Registered`).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/games?take=200` | Auth | Eigene gespeicherte Partien (neueste zuerst, ohne PGN) |
| GET | `/api/games/shared/{token}` | AllowAnonymous | Öffentliche Sicht einer geteilten Partie inkl. PGN (ohne Besitzer-Daten). Literal-Route VOR `{id}` |
| GET | `/api/games/{id}` | Auth | Detail einer eigenen Partie inkl. PGN (Nachspielen/Analysieren) |
| DELETE | `/api/games/{id}` | Auth | Eigene Partie löschen |

Akzeptiert sowohl JWT (User-Login) als auch ApiToken (`Authorization: Bearer rkh_…`). Bei ApiToken muss `scope=extension` sein (sonst 403). Policy-Scheme im Auth-Stack routet das Bearer-Format automatisch zum passenden Handler.

CORS (`ExtensionPolicy`, nur für `ExtensionController`): erlaubt `https://www.chess.com`, `https://lichess.org`, `https://www.chessable.com`, `https://chessable.com` mit `GET`+`POST`, ohne `AllowCredentials` (Auth strikt über Bearer-Header). Gilt für den Userscript-`fetch`-Pfad; die Extension-Variante geht ohnehin CORS-frei über ihren Background-Worker. Die Default-CORS-Policy (Frontend) erlaubt `http://localhost:4200` + `http://localhost:8085`.

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

> **Abschaltbar: `Chessable:Enabled=false`** (`CHESSABLE_ENABLED=false`, Vorgabe an). Dann antwortet
> `/api/chessable/*` mit **404**, und die Import-Lanes sowie der naechtliche Kurslisten-Refresh
> laufen gar nicht erst an. Der Weg ueber die **RepCheck-Extension** (`/api/extension/*`) bleibt
> UNBERUEHRT — genau darum geht es: **auf PROD seit 2026-09-09 abgeschaltet**, alle sollen vorerst
> die Extension benutzen. Der Menue-Eintrag `chessable` steht dort ohnehin schon auf `Admin`, die
> Seite ist also auch ohne den Schalter nicht erreichbar; der Schalter schliesst die Endpunkte und
> den Nachtlauf.

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

Sichtbarkeit wie bei `/api/friends/{userId}/stats`: die Daten sind auf chess-results oeffentlich,
die VERKNUEPFUNG von Konto und Spielerkennung ist es nicht (`PublicProfileDto` gibt die
ChessResultsId bewusst nicht heraus, und dabei bleibt es).

**Der Verlauf entsteht im HINTERGRUND** (`PlayerHistoryScheduler`, 04:30 UTC nach dem
Verzeichnis-Sweep, plus ein Lauf zehn Minuten nach dem Start): `RefreshAllAsync` frischt jede
Identitaet mit Nachnamen auf und holt die fehlenden Seiten sequenziell. `PlayerHistory:MaxCardsPerRun`
(200) deckelt die SUMME der Abrufe eines Laufs (Karten + Bedenkzeiten), damit ein Vielspieler die
uebrigen Konten nicht aushungert; die LISTEN laufen auch nach dem Deckel weiter, denn sie machen
neue Turniere ueberhaupt sichtbar. Vorher entstand der Verlauf nur beim Ansehen (25 Karten je
Aufruf, Nachfragen rund eine Minute) — wer die Seite schloss, liess den Rest liegen.

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
| GET/POST | `/api/tournament-monitor[/{id}]` | Per-Turnier-User-Einstellungen + Runden-Monitor (Round-Watch, Auto-Subscribe) |

### Turnierverzeichnis / Turnierkalender (auth)
Gefuellt vom naechtlichen Sweep der chess-results-Turniersuche (`TournamentDirectoryScheduler`,
03:00 UTC; Nachbarlaender taeglich, uebrige Foederationen rotierend). Rein lesend — hier wird
nichts gecrawlt.

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
| GET | `/api/tournament-directory?from&to&lat&lon&radiusKm&fed&speed&q&weekendOnly&minPlayers&profileId&kinds&ageGroups&genders&adultsOnly&hideLeagues&page&pageSize` | Turnierliste; Umkreis via Bounding-Box (SQL) + Haversine (C#). `q` sucht in Name, Ort UND Veranstalter. Die fuenf Publikums-/Formatfilter binden als EIN Objekt (`DirectoryAudienceQuery`) und gelten fuer Liste, Karte und Kalender gleich: `kinds` individual/team/unknown — **„individual" schliesst `unknown` MIT ein**: die Quelle sagt nur „ist MANNSCHAFTS-Turnierart", alles andere ist Einzel, und `unknown` heisst „Abfrage lief hier noch nicht / fiel aus" (kein eigener Fall fuer den Suchenden; als eigener gefuehrt lieferte „Einzel" eine halb leere Liste). In der SPALTE bleibt `Unknown` stehen, damit ein Netzausfall den Bestand nicht auf Einzel umschreibt, `ageGroups` u8…u20/youthUnspecified/senior (**Ueberschneidung**, nicht Gleichheit — „u12" findet die U8-U18-Meisterschaft), `genders` open/female/male, `adultsOnly` (kein JUGENDmerkmal; Senioren bleiben sichtbar), `hideLeagues`. Ein unbekannter Wert ist ein **400**, kein stilles Ignorieren |
| GET | `/api/tournament-directory/map?bbox=minLat,minLon,maxLat,maxLon` | Kartenmarker im Ausschnitt (gedeckelt) |
| GET | `/api/tournament-directory/calendar?year&month` | Ein Monat: `tournaments` (jedes Turnier EINMAL) + `days` (je Tag nur die Nummern der laufenden). Mehrtaegige Turniere stehen an JEDEM ihrer Tage — voll ausgeschrieben waren das 5962 Eintraege fuer 200 Turniere, also ~3 MB je Monat; das Frontend setzt es in `expandCalendar` wieder zusammen |
| GET | `/api/tournament-directory/{publicId}` | Einzelnes Turnier (auch abgesagte). `publicId` ist die IDENTITAET: die chess-results-Nummer oder `f<FIDE-Nummer>` |
| GET | `/api/tournament-directory/places?q=` | Ortsvorschlaege aus dem Gazetteer (PLZ oder Name) |
| GET | `/api/tournament-directory/places/nearest?lat&lon` | Naechstgelegener Gazetteer-Ort zu Koordinaten — fuer das Ortsfeld, wenn der BROWSER den Standort liefert (die Koordinaten des Nutzers verlassen den Server nicht). 204, wenn im Umkreis von 200 km kein Ort im Lexikon liegt |
| POST | `/api/tournament-directory/{publicId}/report` | „Falsches Event melden" — Rueckmeldung zu einem Eintrag `{ message?, location?, kind?, ageGroups?, gender?, speed?, isLeague?, namePattern?, sourceLink? }`, ALLE Felder freiwillig. Landet im bestehenden **Admin-Nachrichtenkanal** (`AdminMessageService.SendFromUserAsync`) statt in einer eigenen Tabelle: dort gibt es Oberflaeche, Glocke und — entscheidend — einen Rueckweg zum Melder. `namePattern` ist die Lern-Frage („bei uns heissen die Jugendturniere Schachrallye") und wandert in die Wortlisten des `TournamentClassifier` |
| POST/DELETE | `/api/tournament-directory/{publicId}/ignore` | Ein Turnier FUER MICH ausblenden bzw. wieder zeigen (idempotent). Es verschwindet aus Liste, Karte und Kalender — und aus der naechtlichen Umkreis-Meldung; nur mit `audience.includeIgnored=true` kommt es mit (und traegt dann `ignored: true`). Die DETAILseite zeigt es immer, dorthin ist man absichtlich gegangen |
| POST | `/api/tournament-directory/suggest-source` | „Mein Turnier fehlt" `{ link, message? }` — Hinweis auf eine noch nicht gecrawlte Quelle. Der **Link ist Pflicht** (nur absolutes http/https): ein Verbandskalender laesst sich zusaetzlich auswerten, eine Aufzaehlung im Freitext nicht |
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
| Schottland (Chess Scotland) | `sc`+Hash | 1 + bis 43 | 44 kuenftige bis 2028, Bedenkzeit-Klasse STRUKTURIERT. Die Liste nennt keinen Ort; in den Ausschreibungen steht bei 8 von 43 eine Postleitzahl — **die Verortung bleibt schwach** |
| Rumaenien (FRSah) | `ro<nr>` | 1 Abruf | Wie England „The Events Calendar" — aber die Spielstaette steckt schon IM Ereignis, der zweite Abruf entfaellt. Die 5 Eintraege OHNE Spielstaette sind genau die nationalen Mannschaftsligen, und genau die fehlen auf chess-results. `robots.txt` antwortet selbst mit 403 (siehe Doku) |
| Wales (WCU) | `wl`+Hash | 1 Abruf | Die billigste Quelle. 30 von 38 mit vollstaendiger Postleitzahl. KEINE eigene Kennung — sie entsteht aus Termin + Anschrift, bewusst OHNE den Namen (derselbe Veranstaltungsort erscheint als „Best Western", „Bst Western", „Bet Western"; ein korrigierter Tippfehler im Namen darf keine Dublette erzeugen). Weil dieser Schluessel laenger ist als die 60 Zeichen von `TournamentDirectorySource.ExternalId`, vermerkt die Quelle ihren KURZSCHLUESSEL (`PublicIdOf`) — roh gab es jede Nacht „Data too long for column" |
| Niederlande (KNSB) | `nl`+Hash | 2 Abrufe (15 s Pause) | 177 kuenftige gegen 12. Bedenkzeit-Klasse kommt strukturiert aus einer Taxonomie. **Der Spielort fehlt strukturell** — er stuende nur auf der Detailseite, und 177 × 15 s waeren 45 Minuten; bewusst nicht gebaut. Die Eintraege stehen in Liste und Kalender, nicht auf der Karte |
| England (ECF) | `en<nr>` | 12 Seiten, **~200 s** | 278 kuenftige Turniere (gemessen 2026-09-09), **86 % nicht auf chess-results**. Die mit ABSTAND langsamste Quelle, und nicht wegen des Servers: ihre robots.txt verlangt „Crawl delay: 10", das Warten IST die Laufzeit. Sie ist der Grund, warum `TournamentDirectoryService.DefaultCrawlerTimeoutSeconds` 600 s betraegt — mit den frueheren 180 s lief sie in JEDER Nacht in den Timeout, ohne je ein Turnier zu liefern. Die EINZIGE Quelle mit Koordinaten (`geo_lat`/`geo_lng` im Spielstaetten-Endpunkt, 168 von 256) → `GeoSource.SourceProvided`, kein Geocoding. Ihre Schlagworte sind gepflegt: „Meeting" ist kein Turnier, „Online" hat keinen Ort, „Juniors Only" ist eine verlaessliche Jugend-Angabe |
| Deutschland (schachbund) | `de`+Hash | 2 je Region (25) | Ein reines MELDE-System: hier stehen Vereins-Abendturniere, Jugend-Cups, Fernschach, Problemschach und Schach960 — Arten, die chess-results nie fuehrt. Die SEITE traegt die Anschrift (105 von 106), der FEED Rundenzahl und Bedenkzeit; beides wird gebraucht. „europa"/„welt" sind keine Laender und bekommen keine Foederation. Der Slug ist laenger als 60 Zeichen: vermerkt wird der Kurzschluessel (`PublicIdOf`) |
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
| POST | `/api/book-puzzles/{id}/attempt/anonymous` | Anon | Anonymer Versuch (Session-ID, je Session/Puzzle dedupliziert) |
| GET | `/api/book-puzzles/{id}/results?since=` | AllowAnonymous | Solver-Liste (je User, inkl. Discord) + Versuchs-/Lösungszähler + `anonymousSolvedCount`. Löser-Status: nur wer im **ersten** Versuch löste, gilt als Löser |
| POST | `/api/book-puzzles/{id}/track` | AllowAnonymous | „Track solves" eines per Link geteilten Puzzles: erfasst den **Erstversuch** des Besuchers (eingeloggt via Token, sonst `{ solved, sessionId }`) in `SharedPuzzleAttempts` (Unique `(BookPuzzleId, IdentityKey)` → nur 1. Versuch zählt; `solved=false` = Fehlzug/Aufgeben/Reset) und liefert `{ solved, failed }` |
| GET | `/api/book-puzzles/{id}/track-counts` | AllowAnonymous | Aktuelle „Track solves"-Zähler `{ solved, failed }` |
| GET | `/api/book-puzzles/daily/leaderboard?month=yyyy-MM` | AllowAnonymous | Monats-Wertung des Tagespuzzles (für den Bot): je User Punkte (10 je Erstversuch-Lösung + Tages-Rang-Bonus 5/3/1), `solved`, `golds`; absteigend nach Punkten. Default = laufender UTC-Monat. Literal-Route **vor** `daily/{date}` |
| GET | `/api/book-puzzles/daily/hall-of-fame?top=5` | AllowAnonymous | All-time-Bestenlisten: meiste gelöste Dailies, meiste 🥇 (Tage als schnellster Erstversuch-Löser), schnellste je gelöste Lösung. `top` 1–25 |
| GET | `/api/book-puzzles/daily/{date}` | AllowAnonymous | Tagespuzzle für UTC-Datum (`yyyyMMdd` oder `today`); legt on-demand eine persistierte Zuordnung in `DailyPuzzles` an — aber NUR für heute/gestern (ältere Daten: gespeicherte Zuordnung oder 404; verhindert anonyme Write-Amplification per Datums-Enumeration) |
| GET | `/api/book-puzzles/by-line-id?lineId=xxx` | AllowAnonymous | Lookup für schach-bot |
| GET | `/api/book-puzzles/books` | AllowAnonymous | Buch-Liste mit Counts — nur **lesbare** Bücher (`BookAccess`) |
| POST | `/api/admin/book-puzzles/import` | Admin | Bulk-Import aus JSON |
| POST | `/api/admin/book-puzzles/daily/{date}/regenerate` | Admin | Tagespuzzle eines UTC-Datums neu generieren: Datum/Link bleibt, bisheriges Puzzle wird `Retired=true` gesetzt (nie wieder in Daily/Random/Blind), neues aus dem forDaily-Pool zugeordnet |
| POST | `/api/admin/book-puzzles/{id}/regenerate-hints` | Admin | Tipps eines einzelnen Buch-Puzzles synchron (neu) generieren (force). 400 ohne `Anthropic:ApiKey`, 404 wenn Puzzle/keine Tipps; sonst die generierten Tipps |
| POST | `/api/admin/books/{bookId}/generate-hints?force=` | Admin | Tipps für ein ganzes Buch im Hintergrund erzeugen (Queue); `force` regeneriert auch vorhandene, sonst nur fehlende/veraltete. Antwort `{ queued }` |

**Zugriff auf die offenen Buch-Endpoints (`Services/BookAccess.cs`, seit 0.317.1)**: EINE Regel für
`{id}/next`, `{id}/random`, `/random?bookId=` und `/books`. Anonym sichtbar ist ein Buch nur, wenn ein Admin
es bewusst geöffnet hat — `Book.IsPublic` (öffentlicher Kurs) oder Mitgliedschaft in einem offenen Pool
(`ForDaily`/`ForRandom`/`ForBlind`); eingeloggte sehen zusätzlich eigene (`OwnerUserId`), per `CourseShare`
geteilte und über `BookGroupAccess` (inkl. „Everyone") freigegebene Bücher; Admins alles. Altbestand ohne
`Book`-Zeile bleibt ungegatet (dort kann keine Freigabe hängen). **Bewusst weiter offen**: `GET
/api/book-puzzles/{id}` (Einzel-Puzzle per Id) — Basis für Teilen-Links, Tagespuzzle, OG-Vorschau und den
Bot-Lookup per LineId. Bewusst NICHT identisch mit `CourseService.CanAccessAsync`: die Pool-Flags öffnen nur
Einzel-Puzzles/Zufallsziehungen, nicht den strukturierten Kurs (Kapitel/Fortschritt/Offline-Export). Folge für
den schach-bot: sein `/kurs`-Katalog (`/books` + `?bookId=`) enthält nur noch Pool-/öffentliche Bücher.

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

Der `mode`-Parameter bei `/next` akzeptiert `sequential` (Buchreihenfolge, `after` = überspringen) oder `random` (zufällig, `exclude` vermeidet Wiederholung); `completed` wenn alle gelöst. **Random-Pool: jedes Puzzle nur EINMAL pro Durchgang** — neben den gelösten (CoursePuzzleResults) werden auch die seit dem letzten Reset GESCHEITERTEN ausgeschlossen (CourseAttempt mit `AttemptedAt >= CourseProgress.ResetAt`; `ResetAt==null` ⇒ alle bisherigen Versuche zählen). Erst `POST /reset` (rückt `ResetAt` vor + leert die gelöste Menge) bringt sie zurück. Im Solver-„abgeschlossen"-Panel gibt es dafür im Random-Modus einen „Von vorn"-Knopf. Sequential bleibt unverändert (nur gelöste raus).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses` | Auth | Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle) |
| GET | `/api/courses/access` | Auth | `{ hasAccess }` — Basis für die Menü-Sichtbarkeit (Admin: true wenn Bücher existieren) |
| POST | `/api/courses` (Alt-Route `/api/courses/upload`) | Auth | „Neuen Kurs erstellen“: legt einen persönlichen Kurs an (eigenes Buch, nur für den Besitzer sichtbar). Multipart mit `name` und OPTIONALEM `file` (.pgn, max. 10 MB) — **ohne Datei entsteht ein LEERER Kurs**, der danach über die Detailseite Kapitel für Kapitel gefüllt wird (`ImportVersion` steht sofort auf der aktuellen Pipeline-Version, damit ein handgepflegtes Buch nicht im „Aktualisieren“-Banner hängt); ohne Datei ist `name` Pflicht (400), weil es keinen Dateinamen zum Ableiten gibt. MIT Datei gilt die alte Regel: Puzzle-PGN im Chessable-Stil, sonst 400 und kein Buch |
| GET | `/api/courses/{bookId}/chapters` | Auth | Kapitel des Buchs in Lesereihenfolge inkl. Fortschritt je Kapitel (`index`/`name`/`puzzleCount`/`solvedCount`/`progressPercent`); `name=null` = Sammel-„ohne Kapitel" |
| GET | `/api/courses/{bookId}/next?mode=&after=&exclude=&chapterIndex=` | Auth | Nächstes ungelöstes Puzzle (siehe `mode` oben); mit `chapterIndex` auf das Kapitel beschränkt (Pool + Fortschritt) |
| POST | `/api/courses/{bookId}/results` | Auth | Lösungsversuch aufzeichnen (idempotent); validiert Puzzle↔Buch |
| GET | `/api/courses/{bookId}/puzzles` | Auth | Alle Puzzles eines (zugänglichen) Buchs am Stück — für Offline-Speichern. **Kalkulationsbücher → 404** (der Voll-Export enthielte `Moves`, also die Lösung; „öffentlich" heißt Kurs-Zugriff für JEDEN angemeldeten Nutzer). Frontend blendet Offline-Speichern/Durchsehen/Flashcards bei diesen Büchern aus |
| GET | `/api/courses/by-slug/{slug}` | **AllowAnonymous** | Kurz-Alias (`Book.PublicSlug`, nur öffentliche Bücher) → `{ bookId, isCalculation }`; 404 bei unbekanntem Alias. `isCalculation` entscheidet, ob `/{slug}` in den Kalkulations-Modus oder in den Solver springt (ein Kalkulationsbuch hat nur Info-Linien → der Solver meldete sofort „abgeschlossen") |
| GET | `/api/courses/by-slug/{slug}/{chapter}` | **AllowAnonymous** | `/{slug}/{kapitel}` → `{ bookId, isCalculation, chapter, chapterIndex }`. Der Kapitel-Teil der URL IST der Kapitelname (getrimmt, ohne Groß-/Kleinschreibungs-Unterschied; gesucht über ALLE Linien inkl. `IsInfoOnly`, sonst fände ein Kalkulationsbuch gar kein Kapitel); zurück kommt die Schreibweise aus dem Buch. `chapterIndex` = SOLVER-Index (`ChapterOrder`, nur Quiz-Linien) für `courses/:bookId/chapter/:index/:mode` — `null` bei Kalkulationsbüchern (dort filtert der Modus über den NAMEN) und bei reinen Info-Kapiteln. 404 bei unbekanntem Alias ODER Kapitel |
| GET | `/api/courses/stats` | Auth | Aggregierte Kurs-Puzzle-Statistik des Users (TotalAttempts/Solved/Accuracy/Streaks; **ohne Elo** — Kurs-Puzzles haben kein User-Elo). Quelle: `CourseAttempt`. Literal-Route vor `{bookId}` |
| GET | `/api/courses/history?page=&pageSize=` | Auth | Paginierte Kurs-Versuchs-History (neueste zuerst) inkl. Buch-Puzzle-Infos (LineId/Title/BookRating/Difficulty). Literal-Route vor `{bookId}` |
| GET | `/api/courses/stats/breakdown` | Auth | Aufschlüsselung der Kurs-Versuche nach Tag/Thema (aus `BookPuzzle.Tags`), Rating-Band (aus `BookPuzzle.BookRating`) und Aktivität (`PuzzleBreakdownDto`). Literal-Route vor `{bookId}` |
| POST | `/api/courses/{bookId}/reset` | Auth | Fortschritt des Kurses zurücksetzen |
| POST | `/api/courses/{bookId}/convert-to-repertoire` | Auth | „Kurs → Repertoire umwandeln": legt aus dem Kurs-PGN (`CourseService.ConvertToRepertoireAsync` → `RepertoireService.CreateFromPgnAsync`, `UseForExtension=false`) ein neues Repertoire an; Original-Kurs bleibt. Zugriff wie andere Kurs-Endpoints (kein Zugriff → 404) |
| GET | `/api/courses/reprocess/status` | Auth | Aufbereitungs-Status der verwaltbaren Kurse (Admin: alle; sonst eigene): `{ currentVersion, total, stale, reprocessableLocally, refetchable, needsReimport }` — Basis fürs „Aktualisieren (N)"-Banner. Literal-Route vor `{bookId}` |
| POST | `/api/courses/reprocess` | Auth | Bereitet alle veralteten verwaltbaren Kurse neu auf: lokal in-place aus `Book.SourcePgn` (Fortschritt/IDs bleiben), Chessable-Altbestand ohne Quelle wird als Re-Fetch-Job eingereiht; sonst übersprungen. Antwort `{ reprocessed, updatedLines, enqueued, skipped }` |
| POST | `/api/courses/{bookId}/share` | Auth | „Kurs mit ausgewählten Personen teilen" (Batch) `{ recipientUserIds[] }` — nur der Besitzer eines persönlichen Kurses; Empfänger müssen befreundet sein (Admin an alle). Antwort `{ shared, skipped[] }` (übersprungen mit Grund `self`/`not_found`/`not_friends`/`duplicate`); legt je neuem Empfänger die Notification `course_shared` an. 403 wenn nicht Besitzer |
| GET | `/api/courses/{bookId}/shares` | Auth | Mit welchen Nutzern ist dieser eigene Kurs geteilt (für den Teilen-Dialog); 403 wenn nicht Besitzer |
| DELETE | `/api/courses/{bookId}/share/{recipientId}` | Auth | Freigabe des eigenen Kurses für einen Empfänger zurücknehmen (idempotent); 403 wenn nicht Besitzer |
| POST | `/api/courses/{bookId}/link` | Auth | Kurs mit einem anderen (zugänglichen) Kurs verknüpfen (Buch↔Workbook) `{ linkedBookId }` — persönlich, symmetrisch, je Buch max. 1 Partner (ersetzt bestehende). 400 self-link, 404 unzugänglich |
| GET | `/api/courses/{bookId}/link` | Auth | Aktuell verknüpfter Partner-Kurs `{ linkedBookId, linkedDisplayName }` (leer wenn keiner) — für den Schnellwechsel im Solver. Literal-Route |
| DELETE | `/api/courses/{bookId}/link` | Auth | Verknüpfung dieses Kurses lösen (beide Richtungen, idempotent) |
| GET | `/api/courses/{bookId:int}/flashcards` | Auth | PERSISTENT als Flashcard markierte Linien des Users in diesem Kurs `{ lineIds }` (kein Zugriff → 404) |
| POST/DELETE | `/api/courses/{bookId:int}/flashcards/{lineId:int}` | Auth | Flashcard-Markierung setzen/entfernen (idempotent) → `{ marked }`; 404 wenn kein Kurs-Zugriff oder Linie nicht im Buch. Logik in `FlashcardMarkService`; Frontend: Checkboxen im Durchsehen + „Markierte (n)"-Knopf bzw. ⋮-Menü der Detailseite → `/courses/:bookId/flashcards?marked=1` |

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
| GET | `/api/courses/{bookId:int}` | Auth | Detailbild: Metadaten, Fortschritt, `canManage`, Kapitel-Verwaltungssicht (`LineCount`/`QuizCount`/`SolverIndex`/`FirstLineId`) |
| GET | `/api/courses/{bookId:int}/lines?chapter=` | Auth | Linien EINES Kapitels (leer = „ohne Kapitel") — mit `MoveCount`, aber **ohne Zugfolge** |
| POST | `/api/courses/{bookId:int}/lines` | Besitzer/Admin | Stellungen als Text einfügen `{ chapter?, text }` → `{ added, chapter, issues[], totalLines }`. Parser = `Services/FenListParser.cs` (eine FEN je Zeile, führende Nummer „1:"/„2." wird ignoriert, Kommentar nach `\|` oder in `{…}`; **keine** Legalitätsprüfung, nur Struktur). Bereits im Buch vorhandene FENs → `issues` mit Grund `duplicate`; max. 500 Zeilen (`too_many`) |
| DELETE | `/api/courses/{bookId:int}/lines/{lineId:int}` | Besitzer/Admin | Einzelne Linie löschen (räumt Restrict-Abhängige ab: CoursePuzzleResults/CourseAttempts/CourseInfoViews/BookPuzzleAttempts/DailyPuzzles/CalculationTrees) |
| PUT | `/api/courses/{bookId:int}/calculation` | Besitzer/Admin | Kalkulations-Modus des Kurses ein-/ausschalten `{ isCalculation }` → `{ isCalculation }`. Ändert KEINE Linien (Analysebäume/Lösungen bleiben), nur Einstieg + Fortschritts-Zählung. Bewusst hier statt im Admin-Bücher-Tab: wer die Stellungen einfügt, entscheidet auch, wie sie serviert werden |
| PUT | `/api/courses/{bookId:int}/chapters/rename` | Besitzer/Admin | Kapitel umbenennen `{ chapter, newName }` (leerer Name = „ohne Kapitel"); 400 wenn Zielname existiert |
| POST | `/api/courses/{bookId:int}/chapters/delete` | Besitzer/Admin | Ganzes Kapitel = alle seine Linien löschen `{ chapter }` |
| POST | `/api/courses/{bookId:int}/chapters/reset` | Auth | **Einzel-Kapitel-Reset des EIGENEN Fortschritts** `{ chapter }` — leert CoursePuzzleResults/CourseAttempts/CourseInfoViews dieses Kapitels. Buchweites `CourseProgress.ResetAt` bleibt (ist buchweit), eigene `CalculationTrees` bleiben ebenfalls (Nutzerarbeit) |

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
`{id}/random`. **Warum das nötig ist**: ein öffentlicher Kalkulations-Kurs BRAUCHT `Book.IsPublic` für
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
| PUT | `/api/calc-editions/{bookId}` | Besitzer/Admin | Ausgabe anlegen/ändern (Upsert je Kapitel) `{ chapter, title?, videoUrl?, publishAt, testerPreviewAt? }` |
| DELETE | `/api/calc-editions/{bookId}/{editionId}` | Besitzer/Admin | Ausgabe löschen |
| GET | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Kalkulations-Serie Phase 2: Verteiler-Mitglieder inkl. Tester-Häkchen (`{ userId, username, isTester, createdAt }`) |
| PUT | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Mitglied hinzufügen/ändern per Benutzername `{ username, isTester }`; 404 wenn Nutzer unbekannt |
| DELETE | `/api/calc-editions/{bookId}/members/{userId}` | Besitzer/Admin | Mitglied aus dem Verteiler entfernen |
| GET | `/api/calc-editions/{bookId}/views` | Besitzer/Admin | Kalkulations-Serie Phase 3: „Gesehen"-Übersicht — welches Verteiler-Mitglied welche Ausgabe wann geöffnet hat (`{ editionId, chapter, userId, username, viewedAt }`) |

**Serien-Freigabe-Benachrichtigung (Phase 3b):** `CalcSeriesAnnounceScheduler` (HostedService, Standard alle 5 min, Config `CalcSeries:AnnounceIntervalSeconds` 60..3600) ruft `CalcSeriesAnnounceService.RunOnceAsync`: fällige Ausgaben → In-App-Benachrichtigung `calc_series_edition_released` (Daten `book`/`chapter`, Link `/courses/{bookId}`) an den Verteiler. Tester werden zum früheren `TesterPreviewAt` informiert, alle übrigen Mitglieder zur öffentlichen `PublishAt`. Idempotent über `CalcEdition.TesterAnnouncedAt`/`PublishAnnouncedAt`; die öffentliche Runde schließt die Tester-Runden-Empfänger über die GESPEICHERTE Liste `CalcEdition.TesterAnnouncedUserIds` (CSV) aus — NICHT über das veränderliche `IsTester`-Flag (sonst würde ein spät hinzugefügter Tester verloren gehen bzw. ein ent-Tester-tes Mitglied doppelt benachrichtigt). **Kein Mail-Kanal** (es gibt kein Mail-Opt-out-Modell — bewusst nur In-App).

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

### Externe Engine (auth) — Lichess-External-Engine-Protokoll als CLIENT
Das Analysebrett kann statt der Browser-WASM-Engine eine **externe Engine** rechnen lassen: Stockfish auf
dem eigenen Rechner (offizieller Lichess-Provider, `lichess-org/external-engine`) oder eine gemietete
Cloud-Engine (stockfishcloud.com tritt selbst als Provider auf; Chessify lässt sich über sein
UCI-Tunnel-Binary vom Provider wrappen). RookHub implementiert dafür **keine eigene Engine-Infrastruktur**,
sondern spricht die offene Lichess-API als Client: der User hinterlegt einen Lichess-API-Token (Scope
`engine:read`, AES-verschlüsselt in `LichessEngineCredentials`), RookHub listet damit die auf DIESEM
Lichess-Konto registrierten External Engines und reicht Analyse-Anfragen an den Broker
(`engine.lichess.ovh`) durch — der ndjson-Stream geht 1:1 an den Browser.

**Warum als Server-Proxy und nicht direkt aus dem Browser**: das `clientSecret` einer Engine ist ein
Dauer-Geheimnis (wer es hat, kann fremde Rechenzeit verbrauchen) und bleibt deshalb serverseitig
(`LichessEngineService`, MemoryCache je `userId:engineId`, TTL 10 min). Nebeneffekt: die CSP
(`connect-src 'self'`) bleibt unangetastet und die eigene Engine ist auch vom Handy aus nutzbar.
Logik in `Services/LichessEngineService.cs`; URLs konfigurierbar (`Lichess:ApiUrl`/`Lichess:BrokerUrl`) —
zugleich die Vorbereitung auf einen späteren RookHub-EIGENEN Broker (Phase 2, gleiche Endpoints).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/engine/credentials` | Status + maskierter Token (`{ hasCredentials, maskedToken }`) |
| POST | `/api/engine/credentials` | Lichess-Token setzen/überschreiben `{ token }` (max. 200 Zeichen) |
| DELETE | `/api/engine/credentials` | Token löschen |
| GET | `/api/engine/external` | Registrierte External Engines des Kontos — **ohne `clientSecret`** (`{ hasCredentials, tokenInvalid, engines[] }`). Immer 200: `tokenInvalid` sagt, WARUM die Liste leer ist (Lichess wies den Token ab) |
| POST | `/api/engine/external/{id}/analyse` | Analyse anfordern → **`application/x-ndjson`-Stream** (durchgereicht). Body = `EngineAnalyseRequest` (`sessionId`, `initialFen`, `moves[]`, `multiPv`, GENAU EINES von `depth`/`movetime`/`nodes`, optional `threads`/`hash`); Threads/Hash werden serverseitig auf die von Lichess gemeldeten Engine-Maxima geklemmt, `variant` ist fest `chess`. Abbruch = Verbindung schließen (wandert über den Broker zum Provider) |

| PUT | `/api/engine/background` | Hintergrund-Engines für Analyseaufträge setzen `{ engineIds: [] }` (leere Liste = entfernen; jede muss registriert sein → sonst 404, höchstens 8). `GET /api/engine/external` liefert sie als `backgroundEngineIds` mit — der Live-Picker blendet sie aus. **MEHRERE sind der Sinn** (0.460.0): der Worker rechnet je ENGINE genau einen Auftrag, es laufen also so viele Aufträge nebeneinander, wie hier stehen. Mit einer einzigen ist die Warteschlange strikt seriell — auf Dev blockierte EIN zäher Auftrag (Tiefe 22, 5 Linien, 31 min) alle 49 wartenden. Ein neuer Auftrag geht auf die Engine mit der KÜRZESTEN Schlange (`AnalysisJobService.PickBackgroundEngineAsync`), nicht reihum: reihum trifft daneben, sobald eine Engine an einer zähen Stellung hängt |

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
`KEEP_ALIVE` im Provider hoch (siehe `engine-provider/README.md`) UND diese Laufzeit-Unterscheidung hier. Bleibt die erste Datenzeile binnen
`AnalysisJobs:FirstLineTimeoutSeconds` (300) aus, wird pausiert statt den Slot des Users unbegrenzt zu halten.
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
**„Im Analysebrett öffnen" (0.384.0)** hängt `engine`/`depth`/`lines` des Auftrags an die URL: das Brett wählt genau
diese Engine (auch die sonst ausgeblendete Hintergrund-Engine, einmalig und NICHT als Dauerwahl gespeichert) und
setzt die Suche fort, statt bei Tiefe 0 zu beginnen — der Provider hat die Stellung noch im Hash. Zahlen im
Engine-Kontext laufen einheitlich über `formatKiloNps`/`formatKiloNodes` (kN, Tausendertrennung, keine
Nachkommastellen) — vorher sprang die Einheit je nach Tempo zwischen N/s, kN/s und MN/s.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/analysis-jobs/live` | NUR der laufende Stand der gerade rechnenden Aufträge (`{ id, depth, nps, seconds }`) — aus dem Arbeitsspeicher, ohne DB; die Auftragsliste holt ihn im Sekundentakt (Literal-Route vor `{id:int}`) |
| GET | `/api/analysis-jobs` | Eigene Aufträge (neueste zuerst) inkl. Status (`queued/running/paused/done/failed`), `reachedDepth`, `resultJson`, `secondsSpent`, `lastError` |
| POST | `/api/analysis-jobs` | Anlegen `{ fen, targetDepth (1–60), multiPv (1–10), engineId?, title? }` — `engineId` fehlend = Hintergrund-Engine aus dem Profil (keine → 400); FEN muss legal sein; max. 50 offene je User |
| POST | `/api/analysis-jobs/batch` | Mehrfachauswahl `{ fens[] (1–200), targetDepth, multiPv, engineId? }` → `{ created[], skipped[{fen, reason}] }` mit `invalid` (keine legale FEN) / `duplicate` (nicht gescheiterter Auftrag zur Stellung existiert — auch innerhalb des Batches) / `limit` (Deckel offener Aufträge); nie 4xx wegen einzelner Stellungen |
| PUT | `/api/analysis-jobs/{id}` | Anpassen `{ targetDepth?, multiPv?, title?, engineId? }` nach den Regeln oben; ein Engine-Wechsel bricht den Lauf ab und reiht neu ein (Ergebnis bleibt, neue Engine startet mit kaltem Hash bei `ReachedDepth`); 404 wenn nicht eigener |
| POST | `/api/analysis-jobs/{id}/restart` | Wieder einreihen (Fehlversuchs-Zähler + Backoff gelöscht, laufender Lauf abgebrochen); das ERGEBNIS bleibt — die Suche setzt bei `ReachedDepth` an. Ein Auftrag mit erreichtem Ziel bleibt `Done` |
| DELETE | `/api/analysis-jobs/{id}` | Löschen (laufende Suche wird abgebrochen) |

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
Anzeige bleibt bei Tiefe 27 stehen). Der Client-Parser ignoriert Leerzeilen. Reißt ein Stream trotzdem
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
und `preflight.py` (prüft den Token via `POST /api/token/test` VOR dem Start) sowie `patch_provider.py`
— EIN Eingriff in den geholten Provider (angewandt NACH der Prüfsummen-Kontrolle, fehlende Textstelle =
Build-Abbruch statt stiller No-op): ein **Lebenszeichen** im Analyse-Stream, wenn
`HEARTBEAT_SECONDS` (15) lang nichts nach OBEN ging — als **Wiederholung der letzten
weitergegebenen `info`-Zeile**, NICHT als Leerzeile: der Broker liest den Upload als UCI und
verwirft eine Leerzeile (0.458.5, gemessen: 48 gesendet, 0 angekommen, Verbindung trotzdem gekappt;
mit der wiederholten Zeile 3 gesendet und 28 statt 25 Datenzeilen angekommen). Grund: der Provider reicht nur `info`-Zeilen MIT
`score` weiter, und zwischen zwei tiefen MultiPV-Iterationen vergehen Minuten — der Broker (bzw. das CDN
davor) schloss die stumme Verbindung, bei uns sichtbar als `HttpIOException: The response ended
prematurely` alle 5–9 min. Der Auftrag kam dadurch nie über Tiefe 29 hinaus (jeder Neustart rechnet von
Tiefe 1 hoch). Das ist dieselbe Klasse Fehler wie der `NdjsonHeartbeatPump` auf der Strecke API→Browser,
nur einen Hop weiter vorne (Provider→Broker→API).

**Gemessen wird der UPLOAD, nicht die Engine** (0.458.4) — die erste Fassung wartete auf Stille der
ENGINE (`recv` mit Zeitschranke) und feuerte deshalb NIE: Stockfish schweigt während einer langen
Iteration gar nicht, es schickt laufend `info depth … currmove …`, und genau die filtert der Provider
weg. Die Zeitschranke fiel nie, obwohl nach oben minutenlang nichts ging — der Fall, für den das
Lebenszeichen gebaut war, war der einzige, den es nicht abdeckte. Auf Dev hingen daran **23
Analyse-Aufträge bei Tiefe 20/22**, alle mit „letzte Datenzeile vor 60,0 s" (der Broker kappt nach 60 s
Stille, im Provider-Log als `400 uci protocol error: expected bestmove before end of stream`) — und weil
ein gescheiterter Auftrag die Stellung mit LEERER Kandidatenliste abschliesst (`IngestFinishedAsync`),
standen 25 Stellungen dauerhaft ohne Bewertung, drei Partien galten als `Done`. Jetzt zählt die Zeit seit
der letzten WEITERGEGEBENEN Zeile, und `recv` wird in Sekundenscheiben abgefragt, damit die Schranke auch
bei plappernder Engine fällt; `test/heartbeat.test.py` prüft beide Lagen und hat für den zweiten Fall
einen Notausgang (ohne den Eingriff kommt dort NIE etwas an — der Test hinge statt zu scheitern).

Zwei Fallen, die dort
bewusst adressiert sind: der Provider-Token braucht `engine:read` **und `engine:write`** (er
REGISTRIERT die Engine; RookHub selbst genügt `engine:read`) — ohne Vorabprüfung endete das in einem
401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt; und die Registrierung
wird über den **Namen** identifiziert (gleicher Name = Aktualisierung, zwei Rechner brauchen zwei
Namen, sonst überschreiben sie sich). **`ENGINE_COUNT` (0.378.0)**: ein Container kann mehrere
Provider = mehrere registrierte Engines fahren (`ENGINE_<i>_NAME/_MAX_THREADS/_MAX_HASH` je Engine,
sonst `ENGINE_NAME <i>`) — gedacht als „Server Live" + „Server Hintergrund" für die Hintergrund-
Analyseaufträge; beide dürfen alle Kerne haben, weil RookHub den Hintergrund pausiert, sobald Live
rechnet. Stirbt ein Provider, endet der Container mit dessen Code (restart zieht alle neu). Der
Entrypoint ist deshalb bash (`wait -n`); `ENTRYPOINT_DRY_RUN=1` zeigt nur die Aufrufe —
`engine-provider/test/entrypoint.test.sh` prüft damit den Argument-Aufbau.

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

**Die Seite waehlt man im Bestand NICHT** (`CreateGuessSessionRequest.GuessWhite` weglassen): man
uebernimmt die des GEWINNERS, das ist der Sinn der Uebung. `GuessSessionService.WinnerSideAsync`
nimmt dafuer das ERGEBNIS, sonst die BEWERTUNG der letzten gerechneten Stellung (ab 1,5 Bauern
Unterschied) — und faellt sonst auf Weiss zurueck. Der Umweg ueber die Bewertung ist kein Sonderfall:
die zehn Meisterpartien aus Capablancas *Chess Fundamentals* tragen in der Kopfzeile nur `*`, weil
das Buch die Ergebnisse allein im Fliesstext nennt. Bei den EIGENEN Analysen bleibt die Wahl.

**Ein Deckel je Besitzer** (`MaxSessionsPerOwner` = 50, raeumt nur BEENDETE Durchlaeufe weg): `POST`
legt auch ohne Anmeldung Zeilen an, und ohne Deckel waechst die Tabelle mit allem, was der
Rate-Limiter durchlaesst.

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

`annotated` wird IN SQL ermittelt (`Pgn LIKE '%{%'`): jede geschweifte Klammer in einem PGN ist ein
Kommentar, und so bleibt das LONGTEXT-Feld ausserhalb der Antwort. Menue-Key `guess`, Stufe **All**;
die Route traegt entsprechend keinen `authGuard` mehr.

### Client-Diagnostik (offen)
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| POST | `/api/client-log` | AllowAnonymous + RL | Client-seitiges Diagnose-Event `{ kind, detail?, url? }` (v. a. Browser-Engine-Crash/Hänger) — wird strukturiert mit Marker „ClientLog" geloggt (→ ES/Kibana), nichts in der DB. `heartbeat*`-Kinds auf Information, sonst Warning. Frontend: `ClientLogService` (gedrosselt), Engine-Services melden via `reportEngineEvent`-Hook |

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
| POST | `/api/training-goals/sync-play` | Auth | Gespielte Rapid-/Classical-Partien (Lichess/chess.com) des eigenen Users sofort synchronisieren |
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
| PlayerHistorySyncs | Wann die Trefferliste EINES Spielers zuletzt geholt wurde (TTL 12 h). Nach einem Fehlschlag bleibt der Zeitstempel ALT, damit der naechste Aufruf es wieder versucht statt zwoelf Stunden zu warten | PlayerKey (PK, ≤40), LastFetchedAt, LastError? (≤500) |
| UserViewStates | Anzeige-Zustand EINER Seite fuer EINEN Nutzer (heute die Filterleiste des Turnierkalenders). Fuer den Server **OPAK** — nur JSON-Gueltigkeit, Objekt-Form und Groesse werden geprueft; er wird nie abgefragt. `ViewKey` kommt aus `ViewStateService.AllowedKeys`, sonst waere das ein freier Speicher je Nutzer | UserId (Cascade), ViewKey (≤64), Json (**text**, ≤8192 Zeichen — `varchar(8192)` zaehlte in utf8mb4 mit 32 KB gegen das 64-KB-Zeilenlimit), UpdatedAt; **UNIQUE (UserId, ViewKey)**. Wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) |
| GeoPlaces | GeoNames-Ortslexikon (CC BY 4.0) | Country (ISO2), PostalCode?, Name, NameNormalized, Lat/Lon, Kind (PostalCode/City/Region), Population; Index (Country, PostalCode), (Country, NameNormalized) |
| Repertoires | PGN-Sammlungen | UserId, Name, Description, Kind (Enum None/Opening/Middlegame/Endgame), IsPublic, CreatedAt, UpdatedAt, **ImportVersion (Pipeline-Version; < CurrentVersion ⇒ veraltet/reprozessierbar — heute meist No-op, da live ausgewertet)** |
| RepertoireFiles | Einzelne PGNs | RepertoireId, FileName, PgnContent (LONGTEXT), FileSize |
| TournamentSubscriptions | Turnier-Abo | UserId + CrawlerTournamentId (unique pair), TournamentName, EventDate (`DateOnly?`, Turniertermin — steuert Refresh-Crawl + Bot-Turnier-Einordnung) |
| TournamentFavorites | Markierte Turniere | UserId + CrawlerTournamentId |
| TournamentUserSettings | Per-Turnier-User-Einstellungen | UserId + TournamentId, Highlights/Notes/Pinning |
| TournamentMonitors | Runden-Monitor | TournamentId, RoundsCount, LastSeenRound, AutoSubscribed; `RoundMonitorService` checkt periodisch |
| Puzzles + PuzzleAttempts | Standard-Puzzle-Pool + Versuche | klassische Lichess-Puzzles + Pro-User-Versuche (UserId Cascade) |
| Tags + PuzzleTags | Normalisierte Puzzle-Themen für schnellen Themen-Filter | Tag.Name (unique); PuzzleTag composite PK (PuzzleId, TagId) + denormalisiertes Rating, Index **(TagId, Rating)** → indexgestützter Themen-Filter statt LIKE-Scan. Import pflegt automatisch; **einmaliger Backfill bestehender Puzzles via `POST /api/admin/puzzles/backfill-tags`** (Hintergrund-Job). Bis Backfill: Fallback auf LIKE |
| BookPuzzles | Buch-Puzzles | LineId (unique), BookFileName (indexed), Round, Fen, Moves, Title, Chapter, Comment, **MoveComments (LONGTEXT, JSON `{plyIndex:text}`; Pro-Zug-Kommentare der Hauptlinie, Schlüssel = 0-basierter Halbzug NACH dem Zug, -1 = Einleitung; beim Durchspielen/Review angezeigt)**, Difficulty, BookRating, Tags, **HintsJson (LONGTEXT, JSON `{lang:[h1,h2,h3]}`; vorberechnete gestufte Tipps de/en/hr, per LLM erzeugt) + HintsVersion (int, 0=keine; entkoppelt von Book.ImportVersion) + HintsFlagged (bool; Admin-Review-Flag „dumme Tipps", per Solver-Button)**, **Retired (indexed; ausgemustert → nicht mehr in Daily/Random/Blind-Pools)**, **Source (≤16, nullable; null = vollwertig/getGame, "review" = aus getReview vorbelegter Lücken-Füller — zählt als vollwertig gecacht (Overlay-✓, kein getGame-Re-Fetch; getReview≡getGame für die Linie) und wird, falls getGame doch mal für den oid importiert wird, per oid IN-PLACE ersetzt)** |
| SharedPuzzleAttempts | „Track solves" geteilter Einzel-Puzzles (opt-in per Teilen-Link `?track=1`) — Erstversuch je Besucher | BookPuzzleId (indexed), **IdentityKey** (`u:{userId}` eingeloggt / `s:{sessionId}` anonym), Solved (true nur saubere Erstlösung; Fehlzug/Aufgeben/Reset = false), **HintsUsed (höchste angesehene Tipp-Stufe 0–3 beim Erstversuch)**, CreatedAt; **UNIQUE (BookPuzzleId, IdentityKey)** = nur 1. Versuch zählt. Kein harter FK (Index genügt) |
| BookPuzzleAttempts | Buch-/Tagespuzzle-Versuche | BookPuzzleId (Restrict) + UserId (Cascade, nullable für Anon) + AnonymousSessionId, Solved, TimeSeconds, AttemptedAt, **HintsUsed (höchste angesehene Tipp-Stufe 0–3)**; Index (BookPuzzleId, AttemptedAt) + (BookPuzzleId, UserId) + **UNIQUE (BookPuzzleId, AnonymousSessionId)** (eine anonyme Lösung je Session; auth. Versuche = NULL-Session → mehrfach erlaubt) |
| Books | Buch-Metadaten | FileName (unique), Title, Author, **Kind** (Enum Puzzle/Study, Default Puzzle; steuert das Trainingsziel-Routing der Kurszeit), **IsCalculation (bool, Default false; „Kalkulationsbuch" = Stellungen ohne Lösung → Kurs öffnet den Kalkulations-Modus statt des Solvers; geschaltet auf der Kurs-Detailseite von Besitzer/Admin, nicht im Admin-Tab)**, **SourcePgn (LONGTEXT, nullable; Roh-PGN als Reprocessing-Quelle, null bei Altbestand/JSON-Import)**, **ImportVersion (Pipeline-Version; < CurrentVersion ⇒ veraltet → Reprocess-Knopf)** |
| CalculationTrees | Selbst eingeklickter Analysebaum EINES Users zu EINER Stellung eines Kalkulationsbuchs (Kalkulations-Modus; es gibt keine Lösung, der Nutzer legt seine Varianten für beide Seiten selbst an) | UserId (Cascade) + BookId (denormalisiert für die „bearbeitet"-Zähler, Cascade) + BookPuzzleId (**Restrict**, wie CoursePuzzleResult — vermeidet doppelte Cascade-Pfade), **TreeJson (LONGTEXT; für den Server OPAK, nur JSON-Gültigkeit + Maximalgröße geprüft; LEER erlaubt = Zeile trägt nur Trainings-Werte, „hat Baum" ist überall `TreeJson != ''`, nicht „Zeile existiert")**, **ChosenSan (20)/ChosenUci (10) = die eine Festlegung, SecondsSpent (int, Default 0, aufsummiert), SecondsToken (64, nullable) + SecondsTokenApplied (int, Default 0) = Idempotenz-Marke des zuletzt verbuchten Zeit-Deltas samt darunter angerechneter Sekunden (Retry darf die addierte Zeit nicht doppelt buchen), Grade (int?, 0–4 = benannte Stufe `CalculationGrade`, `null` = unbewertet ≠ Stufe 0 „nicht gelöst"; Punkte sind eine Ableitung via `CalculationGrades.PointsFor` und werden NICHT gespeichert)**, CreatedAt, UpdatedAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId) |
| CalcEditions | Kalkulations-SERIE (Phase 1, eigener Bereich à la Wochenpost): terminiert EIN Wochen-Kapitel eines Kalkulationsbuchs (Video + Freigabe). Kapitel OHNE Ausgabe = ungegatet (Übergang); Gating im `CalculationService` (Wochen mit Ausgabe versteckt bis `PublishAt`, für Tester ab `TesterPreviewAt` — Phase 2; Owner/Admin sehen Entwürfe). Verwaltung nur Besitzer/Admin | BookId (Cascade von Book), Chapter (≤300, = Wochen-Kapitelname), Title? (≤300), VideoUrl? (≤500), PublishAt (DateTime), TesterPreviewAt? (DateTime, früher), CreatedAt, UpdatedAt, PublishAnnouncedAt?/TesterAnnouncedAt? (Ankündigungs-Marker, Phase 3b), TesterAnnouncedUserIds? (CSV der Tester-Runden-Empfänger); **UNIQUE (BookId, Chapter)** |
| CalcSeriesMembers | Kalkulations-SERIE (Phase 2): privater VERTEILER eines Serien-Buchs. Mitgliedschaft ist ein zusätzlicher Zugriffspfad in `CourseAccess.CanAccessAsync` — sobald das Buch nicht mehr `IsPublic` ist, sehen nur noch Mitglieder (+ Owner/Admin/Share/Gruppe) den Kurs. `IsTester` gibt einem Mitglied Frühzugang (Wochen ab `TesterPreviewAt`). Verwaltung nur Besitzer/Admin | BookId (Cascade von Book), UserId, IsTester (bool), CreatedAt; **UNIQUE (BookId, UserId)** |
| CalcEditionViews | Kalkulations-SERIE (Phase 3): „Gesehen"-Vermerk — ein Verteiler-MITGLIED hat eine Stellung einer terminierten Woche geöffnet. Erfassung automatisch in `CalculationService.GetPositionAsync` (nur Mitglieder; Owner/Admin/öffentliche Betrachter zählen nicht), einmalig je Ausgabe+Nutzer. Übersicht nur Besitzer/Admin | CalcEditionId (Cascade von CalcEdition), UserId, ViewedAt; **UNIQUE (CalcEditionId, UserId)** |
| DailyPuzzles | Persistierte Tagespuzzle-Zuordnung je UTC-Datum | Date (PK, DATE), BookPuzzleId (Restrict), CreatedAt; vom `DailyPuzzleScheduler` (00:00 UTC) gesetzt oder on-demand bei `/daily/{date}` (nur heute/gestern); Admin-Regenerate ändert nur `BookPuzzleId` (Datum bleibt) |
| Groups | Benutzergruppen | Name (unique), Description, CreatedAt |
| UserGroups | User<->Gruppe (n:m) | Composite PK (UserId, GroupId), Cascade von AppUser + Group |
| EndlessProgresses | Endless Config+Highscore | UserId (unique, nullable), AnonymousSessionId, StartElo, Themes, FasttrackThreshold1/2, StockfishDepth, Highscore, ActiveGameState (LONGTEXT) |
| EndlessSessions | Abgeschlossene Endless Sessions | UserId (nullable), AnonymousSessionId, Timestamp, TotalSolved, MaxRating, DurationSeconds, ConfigJson (TEXT), MistakeAtRatings |
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
| RememberedPositions | Auf chessable.com „gemerkte" Stellungen (RepCheck „Remember line") **und Stellungen der Hintergrund-Analyseaufträge** (einmal je Stellung, `SourceUrl=/analysis/jobs`); die Liste trägt den jüngsten Auftrag als `Analysis` mit | UserId (Cascade), Fen (≤120), CourseId? (≤32), **CourseName? (≤200; über den Chessable-Bearer aufgelöst — Extension-mitgeliefert oder serverseitig aus der gecachten Kursliste)**, SourceUrl? (≤1000), CreatedAt; Index (UserId, CreatedAt) |
| SavedGames | Von chess.com/lichess (über RepCheck) gespeicherte Partien — Bereich „Partien" | UserId (Cascade), Source (≤20: chess.com/lichess), ExternalId? (≤120, Dedup), Pgn (LONGTEXT, serverseitig gebaut), White?/Black? (≤120), Result? (≤12), PlayedAt?, SourceUrl? (≤1000), ShareToken (≤32, UNIQUE; öffentlicher Link `/g/{token}`), CreatedAt; Index (UserId, CreatedAt) + **UNIQUE (UserId, Source, ExternalId)** (Dedup hart erzwungen; NULL-ExternalId = mehrfach erlaubt) |
| PlayTimeDailies | Gespielte Rapid-/Classical-Partien je UTC-Tag/Plattform | UserId + Date + Platform (unique, Cascade), Games (Anzahl Partien), UpdatedAt; befüllt vom `PlayTimeSyncService` |
| PlayTimeSyncs | Sync-Cursor externe Spielzeit | UserId + Platform (unique, Cascade), LastGameTimestamp (ms), LastSyncedAt, LastError |
| UserApiTokens | Personal-Access-Tokens für Maschinen-Clients (chess.com-Extension) | UserId (Cascade), Name, TokenHash (SHA-256, UNIQUE), Prefix (12 char), Scope ("extension"), CreatedAt, LastUsedAt, ExpiresAt (nullable); Index (UserId, Name) |
| PasswordResetTokens | „Passwort vergessen"-Einmal-Token | UserId (Cascade), TokenHash (SHA-256-Hex, UNIQUE), CreatedAt, ExpiresAt, UsedAt (nullable); Roh-Token nur per Mail, nie gespeichert. Beim Anfordern werden ältere offene Tokens des Users entwertet |
| MenuItemSettings | Admin-Override der Menü-Sichtbarkeit | ItemKey (PK, string), Level (Enum All/Registered/Groups/Admin); fehlt eine Zeile → Default aus `MenuRegistry` |
| MenuItemGroupAccesses | Welche Gruppe sieht einen gruppen-gegateten Menüeintrag | Composite PK (ItemKey, GroupId), Cascade von MenuItemSetting + Group, Index GroupId |
| ChessableCredentials | Per-User Chessable-Bearer (1:1) | UserId (unique, Cascade), EncryptedBearer (TEXT, AES via `EncryptionService`), **ChessableUid? (≤32; beim erfolgreichen `POST /api/chessable/test` aus der Chessable-Antwort BEWIESEN gesetzt — nicht aus dem ungeprüften JWT; verknüpft den User mit seiner Chessable-Identität fürs Claimen anonymer getReview-Linien)**, CreatedAt, UpdatedAt; Plaintext nie persistiert. Wird vom `ChessableProxyService` an piratechess durchgereicht |
| LichessEngineCredentials | Per-User Lichess-API-Token (Scope `engine:read`) für die External-Engine-Anbindung (1:1) | UserId (unique, Cascade), EncryptedToken (TEXT, AES via `EncryptionService`), **BackgroundEngineId? (≤64; Hintergrund-Engine für Analyseaufträge)**, CreatedAt, UpdatedAt; Plaintext nie persistiert. Der Token listet die External Engines des Lichess-Kontos; das je Engine gelieferte `clientSecret` wird NICHT persistiert (nur MemoryCache, 10 min) und verlässt den Server nie |
| AnalysisJobs | Hintergrund-Analyseaufträge (siehe „Hintergrund-Analyseaufträge") | UserId (Cascade), Fen (≤120), Title? (≤200), EngineId (≤64, Lichess eei_…), TargetDepth, MultiPv (1–5), Status (Enum Queued/Running/Paused/Done/Failed), ReachedDepth, ResultJson? (LONGTEXT, letzte Broker-Zeile), **EvalText? (≤16, Bewertung der Hauptvariante — Listen laden dafür nicht die Roh-Zeile)**, **FruitlessAttempts (Läufe ohne Tiefenfortschritt → ab 3 Failed)**, SecondsSpent, LastError? (≤500), NextAttemptAt? (Backoff), CreatedAt, UpdatedAt, LastRunAt? (sticky hash), FinishedAt?; Index (UserId, Status) + (UserId, CreatedAt) |
| AdminMessages | Admin↔User-Direktnachrichten (Thread je User) | UserId (Cascade, = Thread-Schlüssel/Nicht-Admin-Teilnehmer), SenderId (Audit), FromAdmin (bool, Richtung), Body (max 4000), CreatedAt, SeenByUserAt?, SeenByAdminAt?; Index (UserId, CreatedAt) + (FromAdmin, SeenByAdminAt) |
| MessageThreads | Metadaten/Zuweisung einer Konversation (1 Zeile je User) | UserId (PK + FK AppUser Cascade), ClaimedByAdminId? (welcher Admin übernommen hat, **ohne FK** → vermeidet doppelte Cascade-Pfade; Name wird beim Abruf aufgelöst), ClaimedAt?; entsteht mit der ersten Nachricht |
| CiBuildReports | Per-Push gemeldete laufende Build-SHA/Ref eines Stacks, den rookhub nicht per HTTP erreichen kann (z. B. log-watcher; `POST /api/ci/build-report`). PERSISTENT statt nur In-Memory → Admin-CI kennt die laufende Version auch nach rookhub-api-Neustart sofort | Repo (PK, ≤100), Sha? (≤64), Ref? (≤200), ReportedAt; Upsert je Repo via `GithubActionsService.ReportBuildAsync`, gelesen in `ResolveRunningBuildsAsync` |
| CourseFlashcardMarks | PERSISTENTE Flashcard-Markierung einzelner Kurs-Linien je User (Checkbox im Durchsehen; `?marked=1`-Bereich der Flashcards-Seite) | UserId (Cascade) + BookId (denormalisiert, Cascade) + BookPuzzleId (**Restrict** — wie CoursePuzzleResult), CreatedAt; **UNIQUE (UserId, BookPuzzleId)** + Index (UserId, BookId). Linien-Löschpfade (`CourseAuthoringService.RemoveLinesAsync`, `BookAdminService.DeleteBook`) räumen explizit ab |
| RepertoireFlashcardMarks | PERSISTENTE Flashcard-Markierung von Repertoire-Linien je User — Besitzer UND Freigabe-Empfänger haben eigene Sätze | UserId (Cascade) + RepertoireId (Cascade) + LineKey (≤120, Frontend-Linien-Hash wie SR), CreatedAt; **UNIQUE (UserId, RepertoireId, LineKey)** |

Cascade Deletes: AppUser → Profile, Repertoires, Subscriptions, EndlessProgresses, EndlessSessions, UserGroups, CourseProgresses, CoursePuzzleResults, CourseAttempts, UserTrainingGoals, PlayTimeDailies, PlayTimeSyncs, WeeklyPostAttempts, SavedGames, ManualActivities; Repertoire → Files, RepertoireShares (RepertoireShare.Owner/Recipient Restrict); Group → UserGroups, BookGroupAccesses, GroupTrainingGoals; Book → BookPuzzles, CourseProgresses, CoursePuzzleResults, CourseAttempts, BookGroupAccesses, CourseShares, CourseLinks, CalculationTrees (CoursePuzzleResult.BookPuzzle + CourseAttempt.BookPuzzle + CalculationTree.BookPuzzle = Restrict, um doppelte Cascade-Pfade zu vermeiden; CourseShare.Owner/Recipient ebenfalls Restrict; CourseLink.LinkedBookId ohne FK → DeleteBook räumt beide Richtungen explizit ab); WeeklyPost → WeeklyPostAttempts; AppUser → AdminMessages + MessageThreads (über UserId, der Nicht-Admin-Teilnehmer; MessageThread.ClaimedByAdminId hat bewusst keinen FK). Admin-DeleteBook und GroupController.Delete räumen die abhängigen Kurs-/Freigabe-/Ziel-Vorlagen-Daten zusätzlich explizit ab (InMemory-Tests cascaden nicht).
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
                            über den Lichess-Broker mit dem Analysebrett (läuft NICHT im Stack)
src/
  api/RookHub.Api/
    Controllers/            Auth, Profile, Friend, Repertoire, Extension, TournamentProxy,
                            TournamentFavorite, TournamentMonitor, Subscription, BookPuzzle,
                            Course, Calculation, Endless, Group, WeeklyPost, TrainingGoal, ClientLog,
                            Puzzle, Admin, Me, BotStats, Engine, BaseApiController
    Services/               Auth, Profile, Friend, Repertoire, CrawlerProxy, PlayerSearch,
                            BookPuzzle, Course, CourseAccess, CourseAuthoring, Calculation,
                            FenListParser, Puzzle, EndlessProgress, TrainingGoal,
                            PlayTime, PlayTimeSync, WeeklyPost, BotStats,
                            ApiToken+ApiTokenAuthenticationHandler, DiscordLink, PgnImport,
                            SchachBotWebhook, BackgroundTaskQueue, Admin, BookAdmin,
                            AdminSeeder, AutoSubscription, RoundMonitor,
                            DailyPuzzleScheduler, Heartbeat,
                            CalcEdition, CalcSeriesAnnounce(+Scheduler), LichessEngine,
                            EngineActivityTracker, AnalysisJob(+Worker), NdjsonHeartbeatPump
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
    nginx.conf              Proxy /api/ → api:8080, OSM-Kachel-Cache, OG-Weiche, SPA-Fallback
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
     Start über `POST /api/auth/session` gegen ihre eigene Anmeldung; `logout()` beendet es über
     `POST /api/auth/session/end`. Das Cookie trägt ein Token mit EIGENEM Adressaten
     (`rookhub-shared-session`) — der JWT-Handler der API weist es ab, es öffnet also nur diesen
     einen Endpunkt; dazu `HttpOnly`, `SameSite=Lax`, `Path=/api/auth`. **Leere Domäne = aus**
     (localhost/IP haben keine gemeinsame Domäne). Eine schon offene Seite der Gegenrichtung
     merkt eine Abmeldung erst beim nächsten Laden — sie hält ihr eigenes JWT.
- **Link-Vorschau**: `/t/{id}` lebt jetzt auf der Turnierseite. Der nginx sagt der API über
  `X-Og-Site` (aus `$host` abgeleitet), welche SPA-Shell sie anreichern soll und welche Domain in
  `og:url` gehört (`App:TurnierBaseUrl`) — sonst bekäme der Besucher die RookHub-Shell serviert
  und landete auf dem Dashboard.
- **Netz**: der Turnier-Container muss im selben Compose-Netz liegen wie die API, weil sein nginx
  `/api/` an den Servicenamen `api` weiterreicht.
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
  **Ein REITER je Konto** (ich zuerst, dann die Freunde) statt einer Auswahlliste; jeder Reiter
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
  (`CiWorkflowTests`): ein **Tag-Lauf baut immer alle drei Images** (`:latest` entsteht nur dort), und der
  `turnier`-Filter enthält den GETEILTEN Frontend-Code (beide Angular-Projekte importieren aus `src/app`).
  Ein neuer Job braucht also einen Filter — ein Tippfehler im Namen ist ein leerer Output und damit ein
  Job, der ab da nie mehr läuft.
  **Handstart** (seit 0.453.3): `gh workflow run docker.yml` baut ALLE drei Images und lässt vorher ALLE
  Tests laufen — bei `workflow_dispatch` bleibt der Filter-Schritt aus (dorny hielte master gegen master
  und setzte jeden Filter auf `false`), die Job-Bedingungen fangen den Fall über `github.event_name` ab.
  Gebraucht für den Fall, den die Pfadfilter selbst erzeugen: master ist rot (hier fremdverschuldet
  geerbt), der reparierende Push berührt nur Frontend-Pfade, und damit hat `build-api` zwei Versionen
  lang nicht gebaut — master grün, Code gepusht, und auf Dev läuft trotzdem der Stand von vorgestern
  (2026-09-09, Dev hing auf 0.452.1). Der Handstart auf master schiebt `:dev`, nicht `:latest`.
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
  `change-password`/`DELETE profile/account`, falsches Passwort beim Login, „keine geteilte Anmeldung" — trägt den
  Header nicht und darf den Nutzer nicht ausloggen (2026-09-09: ein Tippfehler beim Passwortwechsel warf den Nutzer
  raus, vier Fehlversuche später hielt er die App für vergesslich). Wer einen neuen 401-Grund einbaut, entscheidet
  damit über den Header. Serverseitig loggt `Services/JwtTokenGate.cs` JEDE Token-Ablehnung mit Grund und Pfad
  (Logger `RookHub.Api.JwtAuth`; Kibana: `message:JwtAuth*`) — vorher war ein abgelehntes Token in den Logs unsichtbar.
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
- **Import-/Aufbereitungs-Pipeline versionieren** – Ändert sich die Transformation Roh-PGN → gespeicherte `BookPuzzles` (bzw. abgeleitete Repertoire-Daten) so, dass BEREITS importierte Datensätze unvollständig/veraltet werden (Beispiel: nachträgliche Pro-Zug-Kommentar-Extraktion), MUSS `ImportPipeline.CurrentVersion` (in `Services/ImportPipeline.cs`) um 1 erhöht und die Versionshistorie im Doc-Kommentar ergänzt werden. Bücher/Repertoires mit kleinerer `ImportVersion` gelten dann als „veraltet" und werden über den „Aktualisieren (N)"-Knopf (Sektion Kurse/Repertoires, `ReprocessBannerComponent` → `/api/courses|repertoires/reprocess`) neu aufbereitet — **in-place per LineId** (Fortschritt/Statistik-FKs bleiben erhalten), Quelle ist `Book.SourcePgn` (bzw. Chessable-Re-Fetch). `ImportFileAsync` aktualisiert bestehende Linien NUR, wenn das Buch veraltet ist; sonst überspringt es sie (idempotenter Resume).
- **Kalkulations-Modus ist KEIN Solver** – `features/courses/calc/` (Route `/courses/:bookId/calc`) ist bewusst nicht von `BasePuzzleSolver` abgeleitet: es gibt nichts zu lösen, keine Zeit-/Elo-Wertung und keine Lösungs-Anzeige. Er nutzt nur die `PuzzleBoardComponent` im `visualization`-Modus (Brett bleibt eingefroren, Klicks werden als Koordinaten erfasst). Zwei Eigenschaften dürfen dabei NICHT verloren gehen: (1) das Brett bleibt strikt auf der Ausgangsstellung — kein `fen`-Update beim Navigieren, `actualFen` dient nur der Legalitätsprüfung; (2) die Lösung wird nicht ausgeliefert (siehe `CalculationService`) — beim Erweitern der Kalkulations-DTOs also **niemals** `BookPuzzle.Moves` durchreichen. Ohne Konto (Kurz-URL `/{slug}`) tritt `LocalCalculationBackend` (localStorage) an die Stelle des Servers: **jeder Schreibweg dort muss einen Fehlschlag als Fehler melden** (`writeCalcLocal*` gibt `null` zurück, wenn nichts geschrieben wurde) — ein `of(...)` mit dem bloß gerechneten Stand zeigte „gespeichert", obwohl bei gesperrtem/vollem Speicher (Privatmodus, Quota) nichts liegt; die Ansicht ersetzt den Hinweis „liegt nur auf diesem Gerät" dann durch „konnte gerade gar nicht gespeichert werden" (`localSaveFailed`). Bei aktivem Kapitelfilter (`/{slug}/{kapitel}`) gehört auch die angezeigte Gesamtsumme dem KAPITEL (`chapters[]`), nicht dem Buch — Liste und Summe müssen denselben Zuschnitt haben.
- **Vollbild-Brett gehört in die BRETT-Komponenten** – Der Vollbild-Knopf (`shared/fullscreen/`, echtes Element-Vollbild über Taskleiste/Browserleiste) sitzt in den drei Brett-Komponenten selbst (`PuzzleBoardComponent`, `AnalysisBoardComponent`, `pgn-viewer/ChessBoardComponent`) — deshalb haben ALLE Bretter ihn automatisch (Standard/Endless/Buch/Kurs/Daily/Wochenpost, Kalkulation, Durchsehen, Repertoire-Trainer, Analyse, PGN-Viewer), ohne ihn in jedem Consumer zu wiederholen (`[allowFullscreen]="false"` schaltet ihn im Puzzle-Brett ab). Zwei Dinge dürfen dabei nicht kippen: (1) ins Vollbild geht eine ÄUSSERE Hülle (`.board-fs-host`/`.ab-fs-host`/`.cb-fs-host`), deren Größe der Browser auf 100 % × 100 % erzwingt (UA-`!important` schlägt sogar Author-`!important` — dem Vollbild-Element selbst eine Größe zu geben ist zwecklos, Regression 0.322.0: Brett füllte die Breite und lief unten raus); das Brett wird DARIN per Flex zentriert als `min(100vw,100vh)`-Quadrat mit schwarzen Balken. Der Brett-Wrapper bleibt exakt die Brettfläche, sonst rechnen die absolut positionierten Auflagen (Umwandlungs-Auswahl, Viz-Ring) gegen den Bildschirm statt gegen das Brett; (2) im Vollbild rendert der Browser **nur diesen Teilbaum** — Bedienelemente, die dort erreichbar bleiben müssen, gehören ins Vollbild-Element (Vollbild-Knopf; die Solver-Aktionen Tipp/Zurücksetzen/Mausrutscher/Aufgeben liegen seit 0.338.0 als `BoardFsActionsComponent` per `<ng-content>` + `data-fs-only` in den schwarzen Balken, Sichtbarkeitsregeln geteilt in `solver-actions.util.ts`). CDK-Overlays (`matTooltip`, Snackbar, Dialog, Menü) waren dort früher unsichtbar — seit 0.339.0 zieht der `FullscreenOverlayService` (app-weit in `AppComponent`) den Overlay-Container fürs Vollbild ins Vollbild-Element um, sie erscheinen also normal; das war ein echter Hänger, weil ein modaler Dialog („Ganz schön lang"-Nachfrage, `disableClose`) blockierte, ohne klickbar zu sein. Für Elemente im Vollbild-Element selbst bleibt das native `title`-Attribut die einfachere Erklärung. chessground legt Figuren per Pixel-Transform ab: nach jeder Größenänderung `redrawAll()` (ResizeObserver in allen drei Brettern).
- **UI-Dichte-Regel (seit UI-Welle 2/3, v0.334.0)** – Pro Screen genau EINE primäre Aktion
  (mat-flat/raised, farbig), höchstens drei sekundäre sichtbar; alles Weitere gehört ins ⋮-Menü
  (Solver: `PuzzleActionBarComponent`, Karten: Overflow-Menü wie `course-card`). Erklärtexte: pro
  Karte höchstens EIN Satz Fließtext — mehr gehört hinter ein `HelpHintComponent`-?-Icon
  (`shared/help-hint`, Tooltip mit `\n\n`-Absätzen), nie als gestapelte `<p class="muted">`.
  Inhaltsseiten zentrieren ihren Container auf `max-width: min(var(--page-max-width), 96vw)`
  (CSS-Variable in `styles.scss`, aktuell 1240px; Ausnahme: Admin-Tabellen 1400px).
  Dashboard-Kacheln neuer Features kommen NICHT in `DEFAULT_VISIBLE` (Default = Trainings-Kern;
  Rest ist über „Anpassen" zuschaltbar). Ohne diese Regel wächst die Dichte mit jedem Feature
  zurück (gemessen im UI-Review 2026-07-26, siehe TODO.md).
- **Puzzle-Modi konsistent halten** – Standard (`puzzle.component`), Endless (`endless-puzzle.component`) und Book/Course/Weekly/Daily (`book-puzzle.component` – ist selbst schon Mehr-Modus-Template) sollen optisch + funktional so ähnlich wie möglich bleiben. Wenn ein Modus eine UI-/UX-Erweiterung bekommt (z. B. „Tags ausklappbar", „Eval-Button", „Viz-Pfeil"), **immer kurz nachfragen**, ob das nicht auch in den anderen zwei Modi sinnvoll wäre. Gemeinsame Bausteine in dedizierte Komponenten (`PuzzleTagsComponent`, `VizCardComponent`, `ReviewNavComponent`, `ThemePickerComponent`) auslagern statt 3-fach kopieren; die Solver-Mechanik liegt in `BasePuzzleSolver`.
- **Buch-/Kurs-FENs sind nicht immer legal** – Chessable-Muster-/Info-Diagramme (`IsInfoOnly`) benutzen bewusst ILLEGALE Stellungen (z. B. ganz ohne König); chess.js/Gera.Chess werfen dort. Jede FEN-Ladung in einem Buch-/Kurs-Pfad muss das aushalten: im Frontend `tryLoadFen` (+ `replayIllegalFen` aus `illegal-board.util` fürs Durchklicken) statt `new Chess(fen)`, im Backend der permissive Pfad (`PermissiveSan`). Besonders heikel sind **Template-gebundene Getter** (z. B. `commentBlocks`): ein Wurf dort passiert MITTEN in der Change-Detection und lässt alles darunter unrendert (Kommentar, Info-Karte, „Weiter", Teilen) — die Seite wirkt „kaputt", obwohl das Brett stimmt (0.317.2).
- **Variablen-Muster in den Compose-Dateien** – drei Fälle, und zwar in ALLEN fünf Dateien gleich (`compose.yml.example`, `compose.vpn.example`, `compose.vpn.yml`, `compose.dev.yml`, `compose.dev.vpn.yml`): **Pflichtwert** → `${VAR}` ohne Default (fehlt er, ist der Stack sowieso kaputt); **optionales Feature** → `${VAR:-}` (leer = Feature aus, alle betroffenen Endpoints sind fail-closed: Bot-Stats 404, CI-Report 401, Webhook deaktiviert); **Wert, dessen Fehlen still Schaden anrichtet** → `${VAR:?Meldung}`, damit `docker compose` mit einer Meldung abbricht statt den Container in eine Neustartschleife zu schicken (heute: `JWT_KEY` und `ENCRYPTION_KEY` — ein leerer Encryption-Key wäre keine abgeschaltete Verschlüsselung, sondern eine Schein-Verschlüsselung mit dem öffentlich bekannten SHA256("")). Wenige echte Vorgabewerte sind bewusst gesetzt (`EMAIL_SMTP_PORT:-587`, `EMAIL_FROM_NAME:-RookHub`, `EMAIL_USE_STARTTLS:-true`, `CHESSABLE_API_URL:-…`). Die frühere Fassung dieser Regel („keine `:-`-Defaults in den Beispielen") beschrieb den Ist-Zustand nicht — es gab 16 pro Datei — und ließ den nötigen `:?`-Guard als Ausnahme unsichtbar. `DeploymentConfigTests.EveryCompose_GuardsEncryptionKey_AndPassesOptionalSecrets` nagelt das fest.
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
