# Extension API (auth, CORS für chess.com)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Extension API (auth, CORS für chess.com)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Code: `Controllers/Chessable/ExtensionController.cs` (Repertoire, Analyse, Merken, Partien, Token) und als Partial
`ExtensionController.Chessable.cs` (Chessable-Ingest, Trainings-Mitschnitte, Fortschritt, Linien-Cache, Fehlermeldungen;
zweite Zerlegungsrunde W7b) — Route, Auth, CORS und Token-Scope stehen einmal an der Hauptdatei.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/extension/repertoires?kind=opening` | Leichtgewichtige Liste (id, name, fileCount, kind, totalSizeBytes); `kind` filtert auf `none|opening|middlegame|endgame`. Nur Repertoires mit `UseForExtension=true` (Default true, im Bearbeiten-Dialog abwählbar); gilt ebenso für das Positions-Set der Abweichungsanalyse (`RepertoireAnalyzeService`) |
| GET | `/api/extension/repertoires/{id}/pgn` | Kombinierter PGN-Text |
| POST | `/api/extension/analyze-game` | Abweichungsanalyse `{ moves[] (≤ 600), kind?, refresh? }` gegen das gecachte Positions-Set des Kontos (`RepertoireAnalyzeService`) → `{ deviation, gaps, inRepertoire, fenBeforeDeviation, repertoireFileCount, illegalMoveAt, repertoireTruncated }`. Deckel (Codereview N8-005): 30/min je Konto (Policy `extension-analyze`, sonst 429); `refresh` baut höchstens einmal je Konto und Minute neu, sonst gilt der Cache; je Konto ein Neuaufbau zur Zeit, im ganzen Prozess höchstens 2 (`PositionSetBuildGate`, sonst nach 5 s 429 `reason: "busy"`); ein Set liest höchstens 64 MB PGN (gemessen an `FileSize`, in Portionen ≤ 16 MB), ein KONTO hält über alle Arten zusammen höchstens 500 000 Stellungen — darüber Teil-Set mit `repertoireTruncated: true`; `repertoireFileCount` = alle markierten Dateien; eigener Cache mit Größengrenze (2 Mio. Stellungen) |
| POST | `/api/extension/training-activity` | Meldet ein Häppchen AKTIVER Chessable-Trainingszeit `{ secondsActive (1–3600), movesTrained?, linesTrained?, courseId?, courseName?, courseKind? }` — Modus-Labels („Practice Moves“) als Kursname werden verworfen und via Kurs-ID aus der gecachten Kursliste geheilt (von RepCheck auf chessable.com gemessen). Append-only → `ChessableActivities`; fließt in die Kategorie „Chessable" des Trainingsziele-Trackers. Zeitstempel serverseitig |
| POST | `/api/extension/remember-line` | Merkt eine auf chessable.com angezeigte Stellung `{ fen, courseId?, courseName?, sourceUrl? }` → `RememberedPositions` (append-only, Verwendungszweck offen). **Kursname**: die Extension liefert ihn (über den erfassten Chessable-Bearer aus der Chessable-API) mit; fehlt er, nimmt der Server ihn nur aus der gecachten Kursliste `ChessableCredential.CachedCoursesJson` (kein Live-Abruf, Codereview N8-006). `GET /remembered-lines` trägt bei Alt-Einträgen ohne Namen den Cache-Namen nach |
| POST | `/api/extension/chessable/line-trained` | „Linie auf Chessable trainiert" `{ bid, oid }` — markiert die Linie in den RookHub-Gegenstücken: Kurs-Linie gilt als gelöst (idempotenter CoursePuzzleResult, bewusst OHNE CourseAttempt), im Repertoire-Trainer wird eine neue Linie „gelernt" (Stufe 1) bzw. eine FÄLLIGE eine SR-Stufe vorgerückt (Chessable ersetzt das Review; nicht fällige/pausierte bleiben unangetastet). CardKey = serverseitiger SPIEGEL des Frontend-Linien-Hashs (`ChessableTrainedLineService.LineKeyFromSans` ↔ `repertoire-line-key.util.ts`, cyrb53 — MUSS synchron bleiben, Vektoren-Test); Linie via PGN-Header `[ChessableOid]`. Unbekannte bid bzw. unbekannte GÜLTIGE oid = kein Fehler (leeres Ergebnis); eine oid außerhalb der Regel (z. B. „0“ oder größer als `int.MaxValue`) → 400. **oid-Regel** für line-trained, problem-moves, session-moves und review-lines (Codereview A3-013): positive Ganzzahl aus ASCII-Ziffern nach der piratechess-Regel (`ChessableIds.TryParseOid`), gespeichert kanonisch ohne führende Nullen; Spiegel ist piratechess `BrowserCourseAssembler.TryParseOid` — bei Änderung beide Seiten |
| POST | `/api/extension/chessable/problem-moves` | „Schwierige Züge" ablegen (Batch-Upsert je User+bid+oid): `{ bid, entries: [{ oid, nHard?, problemMoves?, lastReviewed? }] }` — nHard aus getList, Zug-Details (`game.problemMoves.thisUser`, opakes JSON ≤16 KB, `{}` löscht alte Fehlzüge) + lastReviewed ("never"→null) aus getGame; fehlende Felder lassen den gespeicherten Wert stehen. Quelle: RepCheck-Capture beim Training/Kurs-Holen |
| POST | `/api/extension/chessable/session-moves` | „Sitzungszüge" ablegen (APPEND-ONLY, kein Upsert): `{ bid, entries: [{ oid, moves }] }` — je trainierter Linie der rohe `moves`-Block aus Chessables eigenem Session-Report (`saveProgressAndReturnNewProgressInfo`-REQUEST, von RepCheck v1.54.0 mitgeschnitten; die Antwort enthält Konto-Daten und bleibt tabu). Enthält je Halbzug u. a. `wrong[]` (falsch gespielte Züge), Overstudy-/Alternative-Flags, Level, Punkte. Opak (nur Array-Form + ≤64 KB geprüft), jeder Durchlauf = eigene Zeile in `ChessableSessionMoves` (Auswertung offen); per-User-Deckel 200k Zeilen (älteste raus). NUR authentifiziert — kein Anon-Pfad |
| POST | `/api/extension/chessable/review-lines` | „getReview-Linien" ablegen (Batch je User+bid+oid): `{ bid, entries: [{ oid, json }] }` — das ROHE getReview-JSON EINER trainierten Linie (opak, ≤256 KB/Linie), erst beim Kurs-Aufbau geparst (`ChessableReviewParser`). Zweite Linien-Quelle NEBEN getGame: `UpsertBatchAsync` legt/aktualisiert die Roh-Zeile ab, dann best-effort `MergeIntoCourseAsync` — die Lücken (oid noch kein BookPuzzle) werden ins Kurs-Buch `chessable-u{userId}-{bid}.pgn` als `BookPuzzle.Source="review"` eingespielt. **getGame gewinnt** (oid-basiert): ein echter getGame-Import ersetzt einen Review-Füller IN-PLACE (dieselbe Zeile, `Source→null`, Fortschritt/FKs bleiben) statt ein Duplikat anzulegen. Antwort `{ stored, merged }`. Quelle: RepCheck-Capture beim Training. Wird von RepCheck erfasst beim Durchtrainieren (getReview) |
| POST | `/api/extension/chessable/review-lines/anon` | **AllowAnonymous** (per-IP-RL) — token-lose Ablage von getReview-Linien für Nutzer OHNE RookHub-Token: `{ uid, bid, entries: [{ oid, json }] }`. Statt eines Accounts identifiziert die **Chessable-uid** (client-seitig aus dem Chessable-JWT decodiert) die Linien; sie landen in `AnonymousChessableReviewLines` (Upsert je uid+bid+oid), **kein** Merge (kein Zielkonto). Werden GECLAIMT beim **erfolgreichen Chessable-Bearer-Test** (`POST /api/chessable/test`): dort ist die uid von Chessable BEWIESEN zurückgegeben (`TestAsync`) — NICHT aus dem ungeprüften JWT decodiert (sonst könnte man per gefälschtem JWT fremde Anon-Daten claimen). Der Test setzt `ChessableCredential.ChessableUid` und ruft `ClaimAnonForUidAsync` (übernimmt in `ChessableReviewLines` + baut die Kurse). Missbrauchs-Schranken: per-IP-RL, 16 MB/Request, **`MaxAnonRowsPerUid`=5000** (kein neuer oid je uid darüber), Retention 90 Tage, für uids ohne verknüpftes Konto 14 Tage (täglich, `AnonymousDataRetentionService` — läuft IMMER, auch mit `Chessable:Enabled=false`; bis 2026-09-30 hing sie am Kurslisten-Refresh und lief auf PROD deshalb nie). Akzeptiertes Rest-Risiko: wer eine fremde numerische uid KENNT, kann Linien unter ihr vorbelegen (getGame gewinnt, Inhalt löschbar, gedeckelt). RepCheck sendet hierher NUR nach expliziter Einmal-Zustimmung; Ziel = konfigurierte URL, sonst Default `rookhub.oberschmid.homes`. Antwort `{ stored }` |
| POST | `/api/extension/chessable/cached-lines` | Welche Linien liegen schon im **geteilten piratechess-Rohdaten-Cache**: `{ oids: ["…"], bid }` → `{ oids }` (gecachte Teilmenge, nur Existenz, nie Inhalt; ≤10000, oid = positive Ganzzahl). **Nur für einen Kurs aus der bestätigten Kursliste des Nutzers** (`ChessableCredential.CachedCoursesJson`, `ChessableImportService.HasVerifiedCourseAsync`, ohne Live-Abruf): ohne `bid` oder für einen anderen Kurs → leere Liste; sonst fragt RookHub piratechess mit dem `bid` (nur Linien, mit denen piratechess diesen Kurs füllt). Dieselbe Schranke im Ingest: eine Linie `null` (aus dem Cache) für einen nicht bestätigten Kurs → **403**, der Chunk-Import wird dann geschlossen — vorher holte sich jedes Konto über oids die Linien fremder, bezahlter Kurse (Codereview 2026-09-29). Auf PROD (`Chessable:Enabled=false`, kein „Testen") gilt das praktisch für fast alle: die Extension holt dann alle Linien selbst. RepCheck ≥1.57.0 überspringt beim „Kurs holen" für Treffer den Chessable-Abruf und schickt im Ingest (`ingest`, `ingest/chunk`, `ingest/live`) je Kapitel `lineOids` parallel zu `lines` — eine Linie `null` = Inhalt aus dem Cache (RepCheck schickt `bid` bei cached-lines noch nicht mit → bis dahin keine Treffer). piratechess ordnet die Linien dann **über die oid** statt positionsbasiert zu (vorher landete ein Teil der Linien eines Kapitels unter der oid/dem Namen der ersten Einträge) und legt Browser-Linien seinerseits im Cache ab (nie überschreibend). Der FINALE Chunk trägt bei vollständig geholtem Buch `courseJson` + `complete: true` → piratechess cacht den Kurs als Ganzes (nur ohne Lücken, gleiche Kapitelzahl, noch kein Eintrag). piratechess nicht erreichbar → leere Liste (Extension holt alles selbst) |
| POST | `/api/extension/chessable/unexpected-response` | RepCheck ≥1.60.0 hat beim „Kurs holen" eine **unerwartete Chessable-Antwort** bekommen und den Abruf gestoppt: `{ bid, courseName?, endpoint (getCourse/getList/getGame), lid?, oid?, status?, reason (http/json/error/shape), message?, snippet? (≤2000; RepCheck entfernt E-Mail- und IP-Adressen), extensionVersion? }` → `{ banned, adminNotified }`. Jede Meldung ist eine Warnung im Log (`ChessableUnexpectedResponse`, Tags `chessable,extension,crawl`) und wird regelmäßig durchgesehen (TODO.md „Periodisch") — nur am Wortlaut ist eine Anti-Crawling-Maßnahme von einer harmlosen Abweichung zu unterscheiden. **Sieht die Antwort nach einer Sperre aus** (`ChessableResponseAlertService.LooksBanned`: Fehlermeldung bzw. Nicht-JSON-Ausschnitt mit banned/suspended/blocked/deleted — dieselbe Wortliste wie `looksBanned` in RepCheck, nur gemeinsam ändern), geht eine Admin-Nachricht im Thread des Nutzers raus (`SendFromUserAsync`, Glocke bei allen Admins), höchstens eine je Nutzer in 24 h (erkannt am Präfix `[RepCheck] Chessable-Sperre gemeldet`). Anlass: `{"error":{"message":"User is banned or deleted"}}` lag seit dem 30.06.2026 für 31 Linien als „gecacht" im Linien-Cache |
| POST | `/api/chessable/admin/repertoire-cleanup?dryRun=true` | **Admin** — Altlasten-Bereinigung in Chessable-Repertoires (`RepertoirePgnCleanup`, v0.477.0): **nie löschen, nur ausblenden** über den Header `[RookHubHidden "…"]` IN der Partie (alle Leser gehen über `RepertoirePgnCleanup.WithoutHidden`: kombinierte PGN-Auslieferung, Datei-Download, `RepertoireLineSource`, Positions-Set der Extension; Header entfernen = wieder sichtbar). Regel 1: oid an Partien mit verschiedenen Linien (Stellung+Hauptvariante) → Wahrheit aus dem piratechess-Linien-Cache (`ChessableProxyService.GetCachedLinePgnsAsync`), sonst Ausschlussregel (Inhalt steht schon unter anderer eindeutiger oid), sonst früheste Partie; den übrigen oid weg (`[RookHubRemovedOid]`), ausgeblendet nur wenn ihr Inhalt schon sichtbar existiert. Regel 2: gleiche Züge mit genau EINER bekannten oid → früheste Partie trägt sie, Rest ausgeblendet. Gleiche Züge unter verschiedenen oids (gewollte Chessable-Wiederholungen) und oid-lose Dubletten bleiben. `dryRun=true` (Standard) = Bericht über alle Dateien; `false` = anwenden. Läuft zusätzlich einmal nach dem Start (`RepertoireCleanupBackfillService`, 90 s verzögert, Merkfeld `RepertoireFile.CleanupVersion`) und inline nach jedem Live-Append. Browser-Importe (`ingest`, `ingest/chunk`) in ein VORHANDENES Repertoire laufen seit v0.477.1 über denselben Anhänge-Weg — vorher ersetzten sie die Datei, und ein Mitschnitt mit einzelnen Kapiteln löschte alle übrigen Partien; ersetzt wird nur noch beim serverseitigen Neuabruf. piratechess nicht erreichbar → Datei bleibt unverändert, nächster Start |
| GET | `/api/extension/remembered-lines?take=200` | Gemerkte Stellungen des Users (neueste zuerst) |
| DELETE | `/api/extension/token/self` | „Trennen" in RepCheck (Codereview S1-007): widerruft GENAU den API-Token, mit dem die Anfrage kommt (Claim `api_token_id` aus `ApiTokenAuthenticationHandler`), ohne Id vom Client — ein Extension-Token kann nur sich selbst widerrufen, `DELETE /api/profile/tokens/{id}` sperrt ihm der Scope-Zaun. 204 auch, wenn der Token inzwischen schon weg war; 400 mit JWT (kein Token zum Widerrufen). RepCheck ≥ 1.68.11 ruft das vor dem lokalen Löschen und wertet 404 als älteren Server („nur hier getrennt") |
| POST | `/api/extension/games` | Speichert die aktuell auf chess.com/lichess angeschaute Partie (Button „Partie speichern") `{ source, moves[], externalId?, white?, black?, result?, sourceUrl?, playedAt?, whiteElo?, blackElo?, timeControl? }` → `SavedGames`. Server baut das PGN aus der SAN-Zugliste + Headern und vergibt ein `ShareToken`. Dedup über (UserId, Source, ExternalId) — aber nur, solange die neuen Züge die gespeicherten fortsetzen (dann heilt ein längerer bzw. erstmals bewerteter Save in place, gleicher Link) oder deren Anfang sind (dann bleibt die gespeicherte); eine ANDERE Zugfolge wird eine eigene Partie ohne ExternalId (Warnung im Log) und bekommt ihren eigenen Link — RepCheck meldete auf dem lichess-Analysebrett jede Partie als `analysis` (N8-001). Bringt der Heal neue Züge, fallen wie beim Korrigieren Analyse-Verknüpfung, Fehler-Training und Nacherzählung weg (N8-002; nur Elo nachgetragen: bleiben). Sichtbar im Bereich „Partien" (`/api/games`). **`analyze: true`** (0.524.0) reiht die Partie gleich zur Analyse ein — scheitert das (keine Engine, Deckel), bleibt sie trotzdem gespeichert | **Seit 0.669.0 wird jede so gespeicherte Partie gleich analysiert** (`SavedGameService.AnalyzeAsync`, das Flag `analyze` ist nur noch Verträglichkeit; Fehler = Partie bleibt trotzdem gespeichert). **Re-Save heilt seit 0.725.3 auch fehlende Kopfdaten** (`SavedGameService.FillsMissing`: Spieler, Bedenkzeit, Datum — ohne neue Züge/Elo); was dem Heil-Save selbst fehlt, kommt aus der gespeicherten Partie (Spalten UND PGN-Kopf).
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
an der Eigentumspruefung vorbei. Jetzt: `DrainNextAsync` und `RunAsync` (`ChessableFetchLane`) nehmen sie nie, der Resume-Dienst laesst sie
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
nicht kennt, fragt deshalb `ChessableImportService.ResumeChapterOffsetAsync` (`ChessableBrowserImport`; den Chunk-Ablauf fuehrt
`ChessableBrowserIngest`): trifft er mit Versatz 0 auf belegte
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
ausserdem gar keinen Auftrag mehr an — er wuerde nie abgearbeitet und blockierte den Kurs. Ein Re-Fetch prüft das
Eigentum am Kurs wie der Import, samt Drossel der Kursliste (`CourseListRefreshCooldown`, 5 min; Codereview A3-014).

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
