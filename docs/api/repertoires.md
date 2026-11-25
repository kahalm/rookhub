# Repertoires (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Repertoires (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

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
| GET | `/api/repertoires/{id:int}/training/config` | Trainer-Einstellungen dieses Repertoires für den anfragenden Nutzer: `{ effective[9], user[9], repertoire[9]?, source, lineOrder? }` — die effektiven SR-Intervalle (Override des Repertoires > global je Nutzer > Vorgabe) samt beider Ebenen und die gewählte Reihenfolge der Linien (`lineOrder`, `null` = nichts gewählt; siehe „Trainer: Reihenfolge der Linien“), seit 0.999.3 dazu `hasOpponent` (das Repertoire hat einen gespeicherten Gegner — nur dann steht `opponent` zur Wahl). Besitzer UND Freigabe-Empfänger; 404 ohne Lesezugriff |
| PUT | `/api/repertoires/{id:int}/training/line-order` | Reihenfolge der Linien im Trainer setzen `{ order }` (0.996.0) — je Nutzer UND Repertoire, auch für ein geteiltes fremdes; `order` ∈ `leastTrained`/`mostTrained`/`random`/`repertoire`/`frequency`/`opponent` (`RepertoireLineOrders.All`; `opponent` nur für ein Repertoire mit gespeichertem Gegner). 204; 400 `{ message: "Line order is not available for this repertoire." }` bei jedem anderen Wert, 404 ohne Lesezugriff |
| PUT | `/api/repertoires/{id}/training/chapter-colors` | Eigene Trainingsfarbe je Kapitel `{ colors: { Kapitel: "w"\|"b" } }` (1.020.2) — je Nutzer und Repertoire (Besitzer ODER Freigabe-Empfänger, Spalte `RepertoireTrainingPrefs.ChapterColorsJson`), übergebene Kapitel überschreiben, die übrigen bleiben; gelesen mit `GET …/training/config` (`chapterColors`). 404 ohne Zugriff, 400 bei Farbe ≠ w/b, Name > 200 Zeichen, > 1000 Kapitel. Client: `repertoire-color.util.ts` schreibt weiter lokal und meldet jede Festlegung an den `RepertoireTrainingService` (`setChapterColorSink`); Trainer und Linienliste arbeiten beim Öffnen den Konto-Stand ein (`mergeChapterColorsFromServer`: Konto gewinnt, nur am Gerät Bekanntes geht hoch) |
| GET | `/api/repertoires/shared-lines` | „Meine Teilen-Links" (Codereview A2-006, 25.10.2026): eigene öffentliche Linien-Links `[{ id, shareToken, title, repertoireName, repertoireId, createdAt }]`, neueste zuerst (`repertoireId` null = aus RepCheck). Frontend: Karte `profile/shared-links-card.component.ts` im Profil |
| DELETE | `/api/repertoires/shared-lines/{lineId:int}` | Linien-Link widerrufen → 204, danach `/l/{token}` 404; 404 für fremde/fehlende. Erneutes Teilen derselben Linie gibt einen NEUEN Link. Löschen eines Repertoires räumt dessen `/l/`-Links mit ab (vorher überlebten sie) |
| POST | `/api/repertoires/{id:int}/share-line` | Öffentlichen Nur-Ansehen-Link `/l/{token}` anlegen (nur Besitzer) `{ pgn, title?, chapter? }` → `{ shareToken, expiresAt }`. `chapter: true` (0.915.0) = ganzes Kapitel, je Linie eine Partie im PGN, Deckel 512 KB statt 32 KB; die öffentliche Seite zeigt dann eine Linienauswahl (Pfeil hoch/runter). **Ablauf (0.915.0): 30 Tage** (`SharedLineService.Lifetime`); dieselbe Linie/dasselbe Kapitel erneut teilen gibt denselben Token und verlängert auf 30 Tage ab jetzt (auch einen abgelaufenen). Links von vor 0.915.0 haben `ExpiresAt = null` und laufen nicht ab |
| GET | `/api/repertoires/shared-line/{token}` | Öffentlich (ohne Anmeldung): `{ title, repertoireName, pgn, createdAt, expiresAt, isChapter }`; 404 bei unbekanntem ODER abgelaufenem Token |
| POST | `/api/repertoires/position-lookup` | „In welchen Repertoires?" `{ fen }` → Repertoire → Kapitel → Linie aus dem gecachten Stellungs-Index des Kontos (`RepertoirePositionLookupService`, eigene + geteilte Repertoires). Kein eigenes Rate-Limit: liest nur den Index, und das Panel lädt bei jedem Schritt durch eine Partie neu |
| POST | `/api/repertoires/position-tree` | Baummodus derselben Suche `{ fen, maxDepth }` → je Repertoire ein über Linien UND Varianten zusammengeführter Zugbaum `{ repertoires[{ …, occurrences, truncated, moves[] }], truncated }`. Deckel (Codereview N7-001): Policy `repertoire-scan` (120/min je Konto, gemeinsam mit `similar-positions`; dazu höchstens 2 gleichzeitige Scans je Konto, 1 wartet, neuester zuerst — sonst 429); Zeitbudget 8 s je Anfrage (`TreeBudget`), danach der bisherige Stand mit `truncated: true` der ANTWORT; `truncated` je Repertoire heißt nur Knoten-Obergrenze (`MaxTreeNodes`); das Panel bestellt eine überholte Anfrage ab; der Durchlauf endet mit der abgebrochenen Anfrage |
| POST | `/api/repertoires/similar-positions` | Ähnlichkeitssuche (`RepertoireSimilarityService`, Metrik `PositionSimilarity`) `{ fen, preset?, minScore?, limit?, includeMirrored?, sameSideToMove?, repertoireIds?, move?, onlyWithMove? }` → `{ matches[], preset, minScore, limit, compared, move, onlyWithMove, truncated }`. Deckel wie `position-tree` (Policy `repertoire-scan`, Zeitbudget 8 s `Budget`, dann `truncated: true` mit den Treffern des schon verglichenen Teils). Der Brett-Walk (`RepertoireLineSource.WalkPositions`) hört nach `MaxPositionsPerLine` (400) je Linie auf, statt die Linie ohne Meldung zu Ende zu spielen |
| GET | `/api/explorer/position?fen=&source=&database=&ratings=&speeds=` | **Eröffnungs-Explorer des Analysebretts** (0.504.0, `ExplorerController`, nur angemeldet): Zugstatistik EINER Stellung `{ status, retryAfterSeconds?, source, database, total, white, draws, black, opening?, eco?, moves[{ uci, san, games, white, draws, black, averageRating?, opening?, eco? }] }`; `status` ∈ `ok`/`tokenMissing`/`tokenInvalid`/`rateLimited`/`failed` (immer 200), keine FEN / unbekannte Auswahl → 400. `ratings`/`speeds` als Komma-Liste. Dieselbe Datenstrecke wie der Lochfinder (`RepertoireExplorerService.PositionAsync`) |
| GET | `/api/explorer/games?fen=&source=&database=&ratings=&speeds=` | Eine Handvoll Partien (≤ 5), die die Stellung erreicht haben (0.505.0): Meister = die bestbewerteten, Lichess = bestbewertete + jüngste, ohne Doppelte `{ status, retryAfterSeconds?, games[{ id, white, whiteRating?, black, blackRating?, winner?, date?, speed?, url?, path? }] }`. `top=` (0.981.0, nur `database=masters`): statt der fünf bis zu 15 der bestbewerteten Partien (wer nach Elo nachzählt, braucht mehr als fünf — so zählt `GameMasterBook`); ohne den Parameter bleibt die Antwort wie bisher. `path` (0.980.0) = seiteninterner Pfad der Ansicht „Meisterpartie“ `/explorer/game/masters/<id>`, nur bei LOKALEN Meisterpartien (dort fehlt `url`); die Explorer-Tabelle des Analysebretts verlinkt ihn, die Partie-Zusammenfassung nimmt ihn als Linkziel an (`docs/api/partie-zusammenfassung.md`, „Links auf Meisterpartien“). Der Client fragt die Stellung NACH einem Zug = die Partien mit diesem Zug. Nur Arbeitsspeicher (1 h), kein DB-Speicher. `url` fehlt bei LOKALEN Meisterpartien (Lumbra — die Kennung gibt es auf lichess.org nicht) |
| GET | `/api/explorer/master-games/{id}` | **Eine Meisterpartie des lokalen Explorers** (0.980.0, `ExplorerMasterGameController`, **ohne Anmeldung**, Policy `anonymous-tournament`) → `{ id, pgn }` (PGN mit Kopfzeilen, vom lila-openingexplorer `masters/pgn/{id}`). `id` = genau 8 Zeichen `A–Z a–z 0–9`, sonst 400 ohne Anfrage an den Explorer; unbekannt 404; Explorer nicht eingerichtet/erreichbar 503. Ein Tag im Arbeitsspeicher. Ansicht dazu: `/explorer/game/masters/:id` (`?ply=`). |
| GET | `/api/explorer/paths?fen=&maxPlies=20&source=local` | **Zugfolgen zu einer Stellung** (0.723.0, `ExplorerPathFinder`, LeagueHub-Zugeditor): die häufigsten Wege von der Grundstellung zu `fen` (Brett + Seite am Zug, Rochade/e.p. egal) NUR aus dem LOKALEN Explorer → `{ opening?{ eco, name }, games, paths[{ moves (SAN), uci, estGames, share }], searched, queries, cached, truncated, failed, otherSideGames }`, höchstens 10 nach `estGames`; `cached` (0.727.3) = Antworten aus dem Speicher statt vom Explorer (nur Information); `otherSideGames` (0.724.1) = Partien derselben Stellung mit der ANDEREN Seite am Zug, nur gefragt bei weniger als 1000 Partien oder ohne Zugfolge (sonst `null`). 400 `reason` ∈ `noLocalExplorer`/`invalidFen`/`onlyLocal`; Rate-Limit `explorer-paths` (10/min je Konto). Regeln unter „LeagueHub — Aufstellungen je Runde + erste Züge" |
| GET | `/api/repertoires/explorer/sources` | Welche Explorer-Quellen es gibt `{ online, local, localRatings[], localSpeeds[] }` — `local` nur mit `LichessExplorer:LocalUrl` (0.503.0) |
| POST | `/api/repertoires/{id:int}/explorer-analysis` | **Lochfinder + Linien-Häufigkeiten** (0.502.0) `{ color?, chapterColors, database, ratings[], speeds[], thresholdPercent, includeHoles, includeLineFrequencies }` (+ `source`: `online`/`local`, 0.503.0) → `{ complete, positionsAnalyzed, positionsPending, rateLimited, retryAfterSeconds?, tokenMissing, tokenInvalid, fetchFailed, holes[], lineFrequencies? }`. Antwortet nach ~20 s Abfragezeit mit dem bisherigen Stand (`complete: false`) — der Client fragt erneut, das Abgefragte liegt im Speicher. Lesend: Besitzer ODER Freigabe-Empfänger (sonst 404); ungültige Auswahl → 400. Siehe „Lochfinder" unten |


**Deckel je Konto für die Online-Quelle** (Codereview N7-003): `GET /api/explorer/position`, `GET /api/explorer/games` und
`POST …/explorer-analysis` zählen bei Quelle `online` (auch leer = Vorgabe) gemeinsam gegen `Explorer:OnlinePerAccountPerMinute`
(Vorgabe 30) je Konto und Minute (`ExplorerOnlineQuota`, Singleton) — die Lichess-Leitung ist global, ein Konto konnte sie
sonst dauerhaft belegen. Darüber 429 wie bei jeder Policy (`{ message, code: "rate_limited", retryAfterSeconds }` +
`Retry-After`, Log über `RateLimitRejectionHandler` mit Policy `explorer-online`). `source=local` hat seit R4-018 einen
eigenen, weiteren Topf (`Explorer:LocalPerAccountPerMinute`, Vorgabe 300, Policy `explorer-local`), und `LocalExplorerClient`
schickt prozessweit höchstens 16 Abfragen zugleich an den lokalen Explorer (`MaxConcurrent`; wer länger als 30 s auf einen
Platz wartet, bekommt `null` wie bei „nicht erreichbar") — die Platte des lokalen Explorers teilen sich alle Konten.
## Neues Repertoire aus Auswahl (0.809.0)

Wunsch 2026-10-19: „mehrere kapitel oder/und lines markieren und ein neues repertoir daraus machen". Linienliste →
⋮ der Leiste → „Neues Repertoire aus Auswahl…" öffnet `CreateSubsetDialogComponent`: Kapitel ankreuzen (= alle seine
Linien, dreistufig) und/oder einzelne Linien; vorgewählt sind die als Flashcard **markierten** Linien (die Markierung
selbst bleibt unberührt). Reine Client-Sache, kein neuer Endpunkt: `POST /api/repertoires` (Name, Beschreibung
„Auswahl von n Linien aus …", **privat**, `kind` und `useForExtension` wie die Quelle) + `POST /api/repertoires/{id}/files`
mit `auswahl.pgn` = die ORIGINALabschnitte (`viewerService.rawGames`, `repertoire-subset.util.ts`, Repertoire-Reihenfolge,
jeder einmal; Varianten, Kommentare, `[FEN]` und interne Marker `[%alt]`/`[%info]` bleiben). Die effektiven
Kapitelfarben der Quelle (auch automatisch erkannte) werden als lokale Overrides fürs neue Repertoire gesetzt. Geht auch
aus einem nur geteilten Repertoire (die Kopie gehört dann dem Nutzer). Während des Einlesens großer Repertoires gesperrt.

## Großes PGN parallel lesen (0.810.0)

Repertoire 16 auf Prod (4,9 MB, 817 Abschnitte) zählte beim Öffnen ~5 s hoch; ~80 % der Zeit ist chess.js (jeder Zug wird
gespielt). `shared/pgn-viewer/pgn-parse-pool.ts` → `parsePgnTextParallel`: Abschnitte in Stapeln (`BATCH` 20) an bis zu 4 Web
Worker (`pgn-parse.worker.ts`, Kerne − 1), Ergebnis in Textreihenfolge, Züge bekommen danach wieder den `Move`-Prototyp
von chess.js. Rückfall auf die Portionen im Hauptthread (`parsePgnTextWithSourceAsync`): ohne `Worker`, unter
`MIN_GAMES_FOR_WORKERS` (120) Abschnitten oder wenn kein Worker startet; scheitert ein Worker unterwegs, liest der
Hauptthread dessen Stapel. Genutzt von der Detailseite (`RepertoireViewerService.loadPgnAsync`) und dem Trainer. Gebündelt
über `webWorkerTsConfig` (`tsconfig.worker.json`, nur Projekt `app`); CSP `worker-src 'self'` reicht. Gemessen in Chrome
(16 Kerne): 2 348 ms → 721 ms.

## Baum: Pfeile + Ziehen auf dem Brett (0.834.0)

Wunsch 2026-10-25: alle Fortsetzungen im Baum als Pfeile, Pfeilgröße = Häufigkeit auf Lichess, im Brett-Vollbild immer an,
Figuren ziehen folgt den Linien. Rein Frontend: `repertoire-tree-arrows.util.ts` (`treeArrows`: Breite
`ARROW_MIN`…`ARROW_MAX` = 3…18 nach √(Partien/max) aus der schon geladenen Baum-Beliebtheit `treePopularity`, SAN über
`canonSan` abgeglichen; ohne Explorer-Daten `ARROW_UNIFORM` 9). `BoardArrow.lineWidth` → chessground `modifiers.lineWidth`.
`ChessBoardComponent` meldet `fullscreenChange` (nur eigenes Brett-Vollbild) und hat `resync()` für abgewiesene Züge.
Detailseite: Brett im Baum spielbar, `onTreeBoardMove` → `treeService.selectChild`; Schalter in localStorage
`rh.repertoire.treeArrows`.
Zurück (0.835.0): im Brett-Vollbild Knopf `.tree-fs-back` (über `ng-content select="[boardFsOverlay]"` der Brett-Komponente in deren Vollbild-Teilbaum), dazu Pfeil links im Baum → `treeBack()` = `treeService.goUp()`.
Eigene Züge (0.837.1): ist in der Baum-Stellung die Seite des Repertoires am Zug (Brett-Ausrichtung = Kapitelfarben), richtet sich die Pfeilbreite nach der Linienzahl je Zug (`TreeChild.count`), nicht nach Lichess (`treeArrows(…, ownMove)`).

## Trainer: Fehler stehen lassen, Fehlerzähler, Tipp, Überspringen (0.886.0)

Wünsche des Users 07.11.: „bei einem falschen zug lass die stellung stehen", „wenn ich einen falschen zug mache schreib wie
oft der schon falsch war", „hintfunktion", „button skip line", „beim playable move … länger stehen lassen", „link zur
chessable line".

- `GET /api/repertoires/{id:int}/training/chessable-misses` (Lesezugriff wie das PGN, sonst 404) → `{ "<oid>": [{ mid, mode,
  count, last }] }`: aus `ChessableSessionMoves.MovesJson` (`wrong[]` je `mid` und `practiceMode`, über alle Durchläufe
  summiert, `last` = zuletzt falsch gespielter Zug wie mitgeschnitten), nur oids, die im PGN des Repertoires als
  `[ChessableOid]` stehen, nur Linien mit Fehlern; EINE Abfrage je Repertoire (`ChessableLineHistory.MissesByOidAsync`). Der
  Trainer lädt das einmal (nur wenn das PGN `[ChessableOid` enthält) und rechnet `mid` mit Farbe und Startseite der Linie in
  den Halbzug um (`repertoire-trainer-misses.util.ts`): 2·mid (+1 Schwarz, −1 wenn die Linie mit Schwarz beginnt).
- Nach einem Fehlzug zeigt der Trainer „Hier auf Chessable schon n× falsch — zuletzt mit 7…Sc6" (nur Einträge der eigenen
  Farbe) und „Diese Linie in RookHub: bisher n× falsch" (`lapses` der SR-Karte — RookHub speichert Fehler je LINIE, nicht je
  Zug); ohne Daten nichts. Der falsche Zug bleibt rot stehen, Brett gesperrt; „Nochmal"/Tippen/Leertaste/Enter nimmt ihn
  zurück (der Fehler bleibt offen wie vorher: „Mausrutscher" verzeiht, ein Neuversuch zählt ihn).
- `POST …/training/line-review` hat `hinted` (bool): fehlerfrei, aber mit Tipp → die Karte rückt NICHT vor (Stufe bleibt,
  mindestens 1; weder `Reps` noch `Lapses`), fällig nach dem Intervall ihrer Stufe (`ScheduleLevel(…, hinted)`, Spiegel
  `applySrReview(…, hinted)` für offline). Ein Fehler in der Linie schlägt den Tipp (Stufe 1). Begründung: das SR-Modell
  kennt nur richtig/falsch; „falsch" hätte die Karte auf Stufe 1 geworfen und als Fehlversuch gezählt — der Tipp soll aber
  ausdrücklich kein Fehler sein, nur kein Fortschritt.
- Tipp (💡, Taste H, nur Abfragen, auch `?opponent=`): Stufe 1 grünes Startfeld, Stufe 2 grüner Pfeil, Stufe 3 spielt den
  Zug und rückt weiter (ohne „richtig"-Zähler). Sitzungszähler `hints`, nicht `wrong`.
- „Linie überspringen" (Taste S): ohne SR-Bewertung zur nächsten Linie der Warteschlange (im Gegner-Modus ist die
  Warteschlange die Gegner-Reihenfolge); hatte die Linie schon einen Fehler (auch einen noch offenen), geht `correct: false`
  raus. Im „Linie fertig"-Kasten = „Weiter".
- „Linie pausieren" (Taste P, 0.889.0): wie Überspringen (keine Bewertung, ein schon gemachter Fehler geht als
  `correct: false` raus), dazu `POST …/training/pause { lineKeys: [key], paused: true }` und die Linie verlässt die
  Warteschlange (Gegner-Modus: nächste in Gegner-Reihenfolge). Snackbar „Rückgängig" → `paused: false`, Linie wieder an die
  aktuelle Stelle. Lokal sofort gespiegelt (Offline-Kopie, `applyPause`), offline/bei Fehler über die Offline-Queue; eine
  Bewertungsantwort dreht die lokale Pause nicht zurück. Pausierte Linien fehlen in Abfragen, Lernen UND der
  Gegner-Reihenfolge; nur eine ausdrücklich gewählte (`?line=` bzw. Gegner-Einstieg) erscheint, mit Hinweis „pausiert"
  (dann kein Pausieren-Knopf). „Überspringen" lässt Pool/Pause unverändert — die Linie kommt in der nächsten Runde wieder.
- Nachbetrachtung (0.890.0): im „Linie fertig"-Kasten (`LINE_DONE`, alle Modi) durch die gespielte Linie klicken —
  `TrainerSessionState.reviewPly` (0 … `currentPly`), Brett/letzter Zug/Formen/Kommentar über `viewFen`/`viewLastMove`/
  `annotationShapes`/`reviewComment`; ◀ ▶-Knöpfe, ←/→/Pos1/Ende (nur dort, lösen kein „Weiter" aus), Klick auf einen Zug.
  Schalter „Engine" (`rookhub_rep_train_review_engine`, Vorgabe aus) → `TrainerReviewEngine` nutzt den
  `AnalysisEngineService` des Analysebretts (MultiPV 1; Bewertungsleiste quer + beste Linie), startet/stoppt bei jedem
  Zustandswechsel über `syncReviewEngine`; offline deaktiviert. „Im Analysebrett öffnen" = `/analysis?fen=&moves=<UCI>&orientation=`
  im neuen Tab. Die SR-Bewertung geht wie bisher beim Linienende raus; Durchklicken ändert nichts.
- Geduldeter Zug (`[%alt]`) steht 3,5 s (`TOLERATED_HOLD_MS`) mit „Auch spielbar — jetzt den Hauptzug.", Klick/Leertaste/
  Enter überspringt.
- Linien mit `[ChessableOid]` zeigen hinter dem Linienlabel einen Textlink „Chessable" (`https://www.chessable.com/variation/<oid>/`).

## Trainer: Reihenfolge der Linien (0.996.0)

Wunsch des Users 19.11.: „wenn ich ein repo reviewe und er hat lines die schon 1 mal trainiert wurden und welche die noch
0 mal waren, die mit 0mal als erstes bringen … aka sortiere nach zeit die das braucht bis es wieder kommt wenn ich es löse“
und „beim train auch einstellbar … irgendwo im menu für das repo umstellbar: reihenfolge. random, nach reihenfolge im repo,
wenig trainerte zuerst, viel trainierte zuerst“.

**Eine Einstellung je Nutzer UND Repertoire**, sechs Werte (`LINE_ORDERS` in `repertoire-line-order.ts` ↔
`RepertoireLineOrders.All`, handgespiegelt mit literalen Tests auf beiden Seiten; der sechste, `opponent`, seit 0.999.3
und nur für Repertoires mit gespeichertem Gegner — siehe „Trainer gegen den gespeicherten Gegner“ unten):

| Wert | Name | Regel |
|---|---|---|
| `leastTrained` | Wenig trainierte zuerst — **Vorgabe** | kürzeste Wiederkehr zuerst: Sortierschlüssel ist das Intervall, das die Linie bekäme, WENN sie jetzt richtig gelöst wird = Intervall der Stufe `min(Stufe + 1, 9)` aus den EFFEKTIVEN Intervallen (`hoursUntilReturnIfSolved`, Spiegel von `ScheduleLevel`). Mit den Vorgabe-Intervallen also Stufe 0 vor 1 vor 2 …; bei frei eingestellten Intervallen entscheidet die Zeit, nicht die Stufennummer. Kein Zustand = Stufe 0 |
| `mostTrained` | Viel trainierte zuerst | die Umkehrung |
| `random` | Zufällig | gemischt (das Verhalten der Abfrage bis 0.995.0) — auch im Lernen-Modus |
| `repertoire` | Reihenfolge im Repertoire | wie im PGN |
| `frequency` | Häufigste zuerst | absteigend nach Linien-Häufigkeit (Lichess-Explorer, `TrainerFrequencyLoader`), Linien ohne Wert zuletzt — der frühere Geräte-Schalter. OHNE Häufigkeiten (offline, kein Token, Abruf gescheitert) gilt die Vorgabe |
| `opponent` | Nach dem Gegner | in der Reihenfolge des GESPEICHERTEN Gegners (wahrscheinlichste zuerst, was er nicht kennt dahinter in Repertoire-Reihenfolge). Nur wählbar, wenn das Repertoire einen gespeicherten Gegner hat (`GET …/training/config` → `hasOpponent`; der Server lehnt den Wert sonst mit 400 ab); OHNE Gegner-Sitzung (offline, Gegner inzwischen entfernt) gilt die Vorgabe |

- **Gleichstand** (gleiche Wiederkehr bzw. Häufigkeit): beim Abfragen zufällig, beim Lernen in Repertoire-Reihenfolge.
  Die Sortierung ist stabil. Eine reine Funktion: `orderLines(lines, rule)` in `repertoire-trainer-queue.util.ts`.
- **Geltung**: Abfragen (fällige Pool-Linien) UND Lernen (ungelernte Linien — sie haben keinen Trainingsstand, `leastTrained`/
  `mostTrained` ergeben dort über die Gleichstand-Regel die Repertoire-Reihenfolge wie bisher). **Unberührt**: die
  AUSDRÜCKLICHE Gegner-Sitzung (`?opponent=`, Reihenfolge des Gegners ohne Fälligkeit) und eine ausdrücklich gewählte
  Einzellinie (`?line=`). Gegen den GESPEICHERTEN Gegner eines „Prep: …“ gilt die Einstellung seit 0.999.3 (Abschnitt unten).
- **Jeder Neuaufbau** geht durch dieselbe Stelle (`buildQueue` → `ordered`): Start, Moduswechsel, „Erneut trainieren“,
  „Alle jetzt fällig machen“/„Alle in den Pool“, Zurücksetzen, Wechsel der Reihenfolge. `extendQueue` gibt es nur in der
  ausdrücklichen Gegner-Sitzung, `continueQueue` nur gegen den gespeicherten Gegner. Nach „Übungszyklen“ → Speichern holt der Trainer die Intervalle neu; sie gelten ab dem nächsten Aufbau.
- **Speicherung**: Tabelle `RepertoireTrainingPrefs` (UserId + RepertoireId eindeutig, `LineOrder`), gelesen mit
  `GET …/training/config` (`lineOrder`, `null` = nichts gewählt — die Vorgabe wird NICHT gespeichert), geschrieben mit
  `PUT …/training/line-order`. Besitzer und Freigabe-Empfänger haben je ihre eigene Zeile (wie der SR-Fortschritt). Der
  Server prüft den Wert gegen die Liste (400) und reiht selbst nichts — Linien, Fälligkeit und Häufigkeiten liegen im Frontend.
  Der Trainer holt die Konfiguration seit 0.996.0 beim Öffnen mit (ein Abruf: effektive Intervalle + Reihenfolge; ein Ausfall
  hält ihn nicht auf → Werte der Offline-Kopie bzw. Vorgaben).
- **Alter Schalter „Häufigste zuerst“** (Gerät, `rookhub_rep_train_freq_order`): wird nur noch gelesen. Ist er an und für
  das Repertoire nichts gewählt, gilt `frequency` weiter (`effectiveLineOrder`); jede Wahl für das Repertoire schlägt ihn.
- **Offline**: die Offline-Kopie trägt `lineOrder` (beim Herunterladen, beim Online-Öffnen des Trainers und beim Speichern
  im Dialog nachgezogen); fehlend/unbekannt = Vorgabe, `frequency` = Vorgabe (keine Häufigkeiten). Umstellen geht offline
  nicht (kein Knopf) — die Wahl liegt am Server.
- **Oberfläche**: EIN Dialog `TrainOrderDialogComponent` (`openTrainOrderDialog`) — (a) Knopf mit der geltenden Reihenfolge in
  der Kopfleiste des Trainers (wo der Umschalter saß; die Wahl baut die Sitzung neu auf, ein laufender Häufigkeits-Abruf wird
  abgebrochen), (b) „Reihenfolge im Training“ im ⋮ der Linienleiste der Detailseite, (c) erster Eintrag im ⋮ der
  Repertoire-Karte (auch geteilte Repertoires). Speichern scheitert → Snackbar, Dialog bleibt offen. Gestaltung: `docs/DESIGN.md` 5.6/8.1.
- **Kein zweiter Ort** reiht fällige Repertoire-Linien: kein Backend-Endpunkt gibt eine Reihenfolge vor
  (`GET …/training/lines` liefert Zustände ungeordnet), es gibt keine Abfrage über mehrere Repertoires und keinen
  Dashboard-Einstieg mit eigener Warteschlange; der Abweichungs-Trainer (`/repertoires/deviations`) hat kein SR.

## Trainer gegen den gespeicherten Gegner eines „Prep: …"-Repertoires (0.999.3)

Wunsch des Users 19.11. (Repertoire „Prep: …“ mit `OpponentJson`): „wenn ich bei …/repertoires/308/train auf review gehe
bringt er immer alle lines. sollte hier nur die fälligen bringen. einzige ausnahme: nicht trainierte sind auch fällig.“
Bis 0.999.2 war der Trainer eines solchen Repertoires (ohne `?opponent=` in der Adresse, `useStoredOpponent`) dieselbe
Sitzung wie von der Spielerkarte: ALLE Linien der Farbe in der Reihenfolge des Gegners, unabhängig von Fälligkeit und
Modus — und die Reihenfolge-Einstellung wirkte dort nicht. Seit 0.999.3 sind es zwei Sitzungsarten:

| | Abfragen | Lernen | Reihenfolge | Knopf „Reihenfolge“ |
|---|---|---|---|---|
| **gespeicherter Gegner** (Prep-Repertoire über „Trainieren“, `/repertoires/:id/train` ohne `?opponent=`) | nur FÄLLIGE (`isStateDue`) und noch NIE TRAINIERTE (kein Zustand bzw. nicht im Pool) Linien der Farbe, nie pausierte — `isStateDueOrUntrained` | alle Linien der Farbe (zum Durchgehen) | die gewählte (`orderLines`), Vorgabe `leastTrained`; bei Gleichstand von `leastTrained`/`mostTrained` die Reihenfolge des Gegners statt Zufall/PGN (untrainierte zuerst, darin die wahrscheinlichsten; dann Stufe 1 nach Wahrscheinlichkeit …) | ja, mit sechs Werten |
| **ausdrücklicher Gegner** (`?opponent=` von der Spielerkarte: Linie anklicken, „Alle trainieren“) — unverändert | alle Linien der Farbe | alle Linien der Farbe | die des Gegners; mit `?line=` ab dieser Linie | nein |
| **ohne Gegner** — unverändert | fällige Pool-Linien | ungelernte Linien | die gewählte | ja, fünf Werte |

- **Einstiegslinie** (`?line=`) in der Sitzung gegen den gespeicherten Gegner: kommt immer und zuerst (auch nicht fällig oder
  pausiert), danach ALLE übrigen Linien der Regel in der gewählten Reihenfolge — es wird nichts „ab der Linie“ abgeschnitten
  (das macht nur die ausdrückliche Sitzung). Reine Funktion: `storedOpponentLines` in `repertoire-trainer-queue.util.ts`.
- **Reihung des Gegners lädt noch / Abruf gescheitert** (`opponentRank` null): Gleichstand in Repertoire-Reihenfolge, Hinweis
  `opponentFailed`; ohne Einstiegslinie wartet der Trainer auf die Reihung (wie bisher). Kommt sie nach, während die
  Einstiegslinie läuft, wird nur der Rest neu gereiht — die laufende Linie springt nicht (`continueQueue`: alles bis zur
  laufenden Linie bleibt, schon Gespieltes kommt nicht noch einmal; `extendQueue` bleibt der ausdrücklichen Sitzung).
- **Nichts fällig und nichts neu** → Leerzustand wie beim normalen Abfragen („Gerade nichts fällig. Nächste Wiederholung in …“,
  die nächste Fälligkeit nur der Linien DIESER Sitzung) mit EINER Aktion **„Alle Linien üben“**: bringt einmalig alle Linien
  der Farbe ohne Fälligkeitsfilter — eine ausdrückliche Wiederholung NUR in der Sitzung (`practiceAll`), am Server wird
  nichts fällig gemacht (anders als „Alle jetzt fällig machen“). Gibt es gar keine Linie (alle pausiert), fehlt der Knopf.
- **Ende der Sitzung**: war sie aufs Fällige beschränkt, sagt der Abschluss „Alle n fälligen und neuen Linien gegen diesen
  Gegner durch.“ und der Knopf heißt „Alle Linien üben“; nach einem Durchgang über alle Linien wie bisher „Alle n Linien
  gegen diesen Gegner durch.“ + „Von vorn“. Beide Knöpfe sind dieselbe Aktion (`restart()`): einmal alle Linien. Die
  ausdrückliche Wiederholung endet mit dem Moduswechsel.
- **`frequency`** gegen den gespeicherten Gegner: nach der Reihung des Gegners werden die Häufigkeiten geholt (Ladeansicht mit
  „Überspringen“ wie sonst; läuft schon eine Einstiegslinie, im Hintergrund — danach wird nur der Rest neu gereiht).
- **Hinweis unter der Kopfleiste** passt zur geltenden Reihenfolge: „Reihenfolge nach Gegner …“ (`opponentOrder`) nur bei
  `opponent` bzw. in der ausdrücklichen Sitzung; bei `leastTrained`/`mostTrained` „Gegen diesen Gegner: nur die Kapitel deiner
  Farbe … Bei gleichem Trainingsstand kommt zuerst, was er am wahrscheinlichsten spielt.“ (`opponentPrepTies`), sonst nur
  die Farbe (`opponentPrep`). Die Hinweise an der Stellung (`opponentNote`) bleiben in beiden Sitzungsarten.
- **Alter Geräte-Schalter „Häufigste zuerst“** zählt gegen den gespeicherten Gegner nicht (er hatte in der Gegner-Sitzung nie
  Wirkung); nichts gewählt = `leastTrained`. Eine seit 0.996.0 für ein Prep-Repertoire gewählte Reihenfolge wirkt jetzt.
- **Offline** gibt es keine Gegner-Sitzung (die Reihung käme vom Server, `useStoredOpponent` läuft nur nach einem
  geglückten Online-Abruf): die Offline-Kopie eines Prep-Repertoires wird normal abgefragt — fällige Pool-Linien,
  untrainierte über „Lernen“ —, ein gewähltes `opponent` fällt dort auf die Vorgabe. Unverändert seit 0.894.0.
- **Server**: `GET …/training/config` liefert `hasOpponent` (gültiges `OpponentJson`), `PUT …/training/line-order` nimmt
  `opponent` nur mit gespeichertem Gegner an (`RepertoireLineOrders.IsValidFor`; 400 „Line order is not available for this
  repertoire.“ — derselbe Rumpf wie bei einem unbekannten Wert). Ein gespeichertes `opponent` bleibt stehen, wenn der
  Gegner später fehlt; das Frontend fällt dann auf die Vorgabe (`effectiveLineOrder(…, hasOpponent)`).

## Bereich „Letzte Woche" (0.888.0)

`GET /api/repertoires` liefert je Eintrag `lastTrainedAt` = jüngstes `RepertoireCardState.LastReviewedAt` DES AUFRUFERS (eine
gruppierte Abfrage, `RepertoireService.FillLastTrainedAsync`), `null` = nie trainiert. Die Liste zeigt ganz oben
„Letzte Woche": Repertoires mit Aktivität (max(lastTrainedAt, createdAt)) in den letzten 7 Tagen, jüngste zuerst, höchstens 8
(`recentRepertoires` in `repertoire-list.component.ts`); die Karten stehen zusätzlich in ihrem eigentlichen Bereich.

**Mehrere Stellungen auf einmal** (`RepertoireBoardContinuations`, 0.987.0, kein eigener Endpunkt): das Trainer-Notizbuch
fragt für alle Eröffnungsstellungen in EINEM Lauf über die eigenen Linien (geteilte zählen nicht), ob das Figurenfeld
vorkommt und welche Züge das Repertoire dort spielt (Hauptzug + abzweigende Varianten), Zeitbudget 4 s — Details in
`docs/api/trainingsvideos.md`, „Trainer-Notizbuch“.
