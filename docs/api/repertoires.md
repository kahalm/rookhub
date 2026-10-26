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
| GET | `/api/repertoires/shared-lines` | „Meine Teilen-Links" (Codereview A2-006, 25.10.2026): eigene öffentliche Linien-Links `[{ id, shareToken, title, repertoireName, repertoireId, createdAt }]`, neueste zuerst (`repertoireId` null = aus RepCheck). Frontend: Karte `profile/shared-links-card.component.ts` im Profil |
| DELETE | `/api/repertoires/shared-lines/{lineId:int}` | Linien-Link widerrufen → 204, danach `/l/{token}` 404; 404 für fremde/fehlende. Erneutes Teilen derselben Linie gibt einen NEUEN Link. Löschen eines Repertoires räumt dessen `/l/`-Links mit ab (vorher überlebten sie) |
| POST | `/api/repertoires/position-lookup` | „In welchen Repertoires?" `{ fen }` → Repertoire → Kapitel → Linie aus dem gecachten Stellungs-Index des Kontos (`RepertoirePositionLookupService`, eigene + geteilte Repertoires). Kein eigenes Rate-Limit: liest nur den Index, und das Panel lädt bei jedem Schritt durch eine Partie neu |
| POST | `/api/repertoires/position-tree` | Baummodus derselben Suche `{ fen, maxDepth }` → je Repertoire ein über Linien UND Varianten zusammengeführter Zugbaum `{ repertoires[{ …, occurrences, truncated, moves[] }], truncated }`. Deckel (Codereview N7-001): Policy `repertoire-scan` (120/min je Konto, gemeinsam mit `similar-positions`; dazu höchstens 2 gleichzeitige Scans je Konto, 1 wartet, neuester zuerst — sonst 429); Zeitbudget 8 s je Anfrage (`TreeBudget`), danach der bisherige Stand mit `truncated: true` der ANTWORT; `truncated` je Repertoire heißt nur Knoten-Obergrenze (`MaxTreeNodes`); das Panel bestellt eine überholte Anfrage ab; der Durchlauf endet mit der abgebrochenen Anfrage |
| POST | `/api/repertoires/similar-positions` | Ähnlichkeitssuche (`RepertoireSimilarityService`, Metrik `PositionSimilarity`) `{ fen, preset?, minScore?, limit?, includeMirrored?, sameSideToMove?, repertoireIds?, move?, onlyWithMove? }` → `{ matches[], preset, minScore, limit, compared, move, onlyWithMove, truncated }`. Deckel wie `position-tree` (Policy `repertoire-scan`, Zeitbudget 8 s `Budget`, dann `truncated: true` mit den Treffern des schon verglichenen Teils). Der Brett-Walk (`RepertoireLineSource.WalkPositions`) hört nach `MaxPositionsPerLine` (400) je Linie auf, statt die Linie ohne Meldung zu Ende zu spielen |
| GET | `/api/explorer/position?fen=&source=&database=&ratings=&speeds=` | **Eröffnungs-Explorer des Analysebretts** (0.504.0, `ExplorerController`, nur angemeldet): Zugstatistik EINER Stellung `{ status, retryAfterSeconds?, source, database, total, white, draws, black, opening?, eco?, moves[{ uci, san, games, white, draws, black, averageRating?, opening?, eco? }] }`; `status` ∈ `ok`/`tokenMissing`/`tokenInvalid`/`rateLimited`/`failed` (immer 200), keine FEN / unbekannte Auswahl → 400. `ratings`/`speeds` als Komma-Liste. Dieselbe Datenstrecke wie der Lochfinder (`RepertoireExplorerService.PositionAsync`) |
| GET | `/api/explorer/games?fen=&source=&database=&ratings=&speeds=` | Eine Handvoll Partien (≤ 5), die die Stellung erreicht haben (0.505.0): Meister = die bestbewerteten, Lichess = bestbewertete + jüngste, ohne Doppelte `{ status, retryAfterSeconds?, games[{ id, white, whiteRating?, black, blackRating?, winner?, date?, speed?, url? }] }`. Der Client fragt die Stellung NACH einem Zug = die Partien mit diesem Zug. Nur Arbeitsspeicher (1 h), kein DB-Speicher. `url` fehlt bei LOKALEN Meisterpartien (Lumbra — die Kennung gibt es auf lichess.org nicht) |
| GET | `/api/explorer/paths?fen=&maxPlies=20&source=local` | **Zugfolgen zu einer Stellung** (0.723.0, `ExplorerPathFinder`, LeagueHub-Zugeditor): die häufigsten Wege von der Grundstellung zu `fen` (Brett + Seite am Zug, Rochade/e.p. egal) NUR aus dem LOKALEN Explorer → `{ opening?{ eco, name }, games, paths[{ moves (SAN), uci, estGames, share }], searched, queries, cached, truncated, failed, otherSideGames }`, höchstens 10 nach `estGames`; `cached` (0.727.3) = Antworten aus dem Speicher statt vom Explorer (nur Information); `otherSideGames` (0.724.1) = Partien derselben Stellung mit der ANDEREN Seite am Zug, nur gefragt bei weniger als 1000 Partien oder ohne Zugfolge (sonst `null`). 400 `reason` ∈ `noLocalExplorer`/`invalidFen`/`onlyLocal`; Rate-Limit `explorer-paths` (10/min je Konto). Regeln unter „LeagueHub — Aufstellungen je Runde + erste Züge" |
| GET | `/api/repertoires/explorer/sources` | Welche Explorer-Quellen es gibt `{ online, local, localRatings[], localSpeeds[] }` — `local` nur mit `LichessExplorer:LocalUrl` (0.503.0) |
| POST | `/api/repertoires/{id:int}/explorer-analysis` | **Lochfinder + Linien-Häufigkeiten** (0.502.0) `{ color?, chapterColors, database, ratings[], speeds[], thresholdPercent, includeHoles, includeLineFrequencies }` (+ `source`: `online`/`local`, 0.503.0) → `{ complete, positionsAnalyzed, positionsPending, rateLimited, retryAfterSeconds?, tokenMissing, tokenInvalid, fetchFailed, holes[], lineFrequencies? }`. Antwortet nach ~20 s Abfragezeit mit dem bisherigen Stand (`complete: false`) — der Client fragt erneut, das Abgefragte liegt im Speicher. Lesend: Besitzer ODER Freigabe-Empfänger (sonst 404); ungültige Auswahl → 400. Siehe „Lochfinder" unten |


**Deckel je Konto für die Online-Quelle** (Codereview N7-003): `GET /api/explorer/position`, `GET /api/explorer/games` und
`POST …/explorer-analysis` zählen bei Quelle `online` (auch leer = Vorgabe) gemeinsam gegen `Explorer:OnlinePerAccountPerMinute`
(Vorgabe 30) je Konto und Minute (`ExplorerOnlineQuota`, Singleton) — die Lichess-Leitung ist global, ein Konto konnte sie
sonst dauerhaft belegen. Darüber 429 wie bei jeder Policy (`{ message, code: "rate_limited", retryAfterSeconds }` +
`Retry-After`, Log über `RateLimitRejectionHandler` mit Policy `explorer-online`). `source=local` bleibt frei.
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
