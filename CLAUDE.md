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

| PUT | `/api/engine/background` | Hintergrund-Engine für Analyseaufträge setzen `{ engineId }` (null/leer = entfernen; muss registriert sein → sonst 404). `GET /api/engine/external` liefert sie als `backgroundEngineId` mit — der Live-Picker blendet sie aus |

### Hintergrund-Analyseaufträge (auth) — „diese Stellung rechnen, sobald die Hintergrund-Engine frei ist"
`AnalysisJobs`: eine Stellung mit Zieltiefe + Linienzahl, abgearbeitet vom `AnalysisJobWorker` (Hosted
Service, Singleton) auf der **Hintergrund-Engine** des Users über denselben Broker-Pfad wie live — je
User EIN laufender Auftrag, ohne den 10-Minuten-Deckel des Live-Proxys. **Vorrang der Live-Analyse**:
der `EngineActivityTracker` (Singleton; zählt die Live-Streams je User, ersetzt das frühere statische
`ActiveStreams`) feuert `LiveStarted` beim Übergang 0→1 → der Worker bricht den laufenden Auftrag
sofort ab (Status `Paused`; der Provider stoppt Stockfish, die Hashtabelle bleibt warm) und setzt erst
nach `AnalysisJobs:IdleGraceSeconds` (20 s) Ruhe fort. **Fortsetzungs-Regel**: der Broker liefert nach
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
nicht laden müssen. **Grenzen und Terminalzustände (0.383.0, aus dem Review)**: `MultiPv` ist auf **1..5**
gedeckelt — das Protokoll-Maximum von `work.multiPv`, im Worker ein zweites Mal geklemmt (ein größerer Wert
wird vom Broker abgewiesen und der Auftrag liefe endlos in die Wiederholung). Der Worker bricht **nicht mehr
selbst** ab, wenn die Hauptvariante die Zieltiefe erreicht: die Engine bekommt `depth` als Limit und beendet den
Stream selbst — sonst blieben die Linien 2..K eine Iteration flacher (jede `pv` traegt ihre eigene Tiefe). Ein Lauf
ohne Tiefenfortschritt erhoeht `FruitlessAttempts`; ab `MaxFruitlessAttempts` (3) gilt der Auftrag als `Failed`
(Matt-/Pattstellungen liefen sonst ewig im 30-s-Takt). Bleibt die erste Datenzeile binnen
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
und `preflight.py` (prüft den Token via `POST /api/token/test` VOR dem Start). Zwei Fallen, die dort
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
- **IMMER erst `git fetch`/`pull` vor jedem Tag** – ein Tag zeigt auf einen konkreten Commit; wegen der zwei Stack-Kopien am selben Remote ist der lokale HEAD oft veraltet. Vor dem Taggen `git fetch` und den AKTUELLEN `origin/master`-HEAD taggen (dessen `APP_VERSION` aus `changelog.ts` = Tag-Name), sonst zeigt der Tag auf einen alten Stand OHNE die zwischenzeitlich von der anderen Kopie gepushten Features → das `:latest`-Prod-Image ist dann unvollständig (passiert 2026-07-06: v0.266.0 getaggt, während master schon auf 0.270.0 mit dem Chapter-Feature stand).
- **CI/CD**: Docker-Images werden nach Push automatisch gebaut (GitHub Actions). Kein manueller Build nötig.
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
