# Chessable-Integration (auth, leitet an piratechess-API weiter)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Chessable-Integration (auth, leitet an piratechess-API weiter)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


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
