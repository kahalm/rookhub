# Spielervorbereitung (Prep) — jeder Spieler des Partiebestands (0.631.0–0.639.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Spielervorbereitung (Prep) — jeder Spieler des Partiebestands (0.631.0–0.639.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Ein Gegner lässt sich aus dem ganzen Partiebestand vorbereiten (ChessBase-Megabase + Lumbras GigaBase), nicht nur aus
den Tiroler Ligen. Rechte: `prep.view` (suchen, Karte lesen), `prep.manage` (einspielen, unsichere Konten, Konto-Suche
und -Pflege) — Admin erfüllt alles. Menüpunkt „Analyse & Sammlung" → „Gegner vorbereiten" nur mit `prep.view`.
Code: `Controllers/Prep/PrepController.cs`, `Services/Prep/*`, Oberfläche `features/prep/*` (siehe `src/frontend/CLAUDE.md`).

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
- Mit FIDE-ID kommen dazu: Liga-, chess-results- und Vereinspartien ohne Dubletten (`LeagueProfileStore.GamesAsync`; seit 0.725.0
  auch Teilpartien aus eingegebenen ersten Zügen, ohne `recent`/PGN und ohne die, deren volle Partie der Bestand hat),
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
- Frist statt 500 (Hotfix 0.725.1; Prod 08.10.: 500 nach 60 s, nginx kappte, `RequestAborted` ließ den Abbruch aus
  `BatchStatsAsync` durch): Gesamtfrist je Anfrage `Prep:TrainingDeadlineSeconds` (20, höchstens 30), Explorer davon
  `Prep:TrainingExplorerSeconds` (12) bzw. was nach 2 s Reserve übrig ist; Stellungen in der Reihenfolge der VORLÄUFIGEN
  Reihung (`TrainingLinesService.Prioritize`: Rank nur aus seinen Partien, je Linie ihre Lücken) und gedeckelt auf
  `Prep:TrainingExplorerMaxPositions` (1 500); der Rest ist `pending` → `explorerIncomplete` + `explorerPending`.
  `BatchStatsAsync(positions, query, ct, budget)` läuft an einer EIGENEN Frist, nicht am Anfrage-Token (Abbruch der
  Anfrage stoppt die Arbeit nicht, nie eine Ausnahme), Stellungen 24 h im Speicher (`BatchMemoryTtl`). Unvollständige
  Ergebnisse werden NICHT gecacht — die nächste Anfrage kommt mit den gespeicherten Stellungen weiter.
- Hintergrund statt „Weiter rechnen" (0.725.2): der Deckel `Prep:TrainingExplorerMaxPositions` gilt nur für NEU
  anzufragende Stellungen (`BatchStatsAsync(…, maxNew, parallelism)`, gespeicherte immer mit; `BatchResult.FromMemory/
  Asked/Capped`) — vorher schnitt er die ganze Liste ab, und Runden kamen nie über 1 500 hinaus. Bleibt eine Abfrage
  unvollständig, startet `TrainingLinesContinuation` (Singleton, eigener DI-Bereich) für denselben Cache-Schlüssel einen
  Hintergrund-Durchlauf: alle restlichen Stellungen in Priorisierungs-Reihenfolge, 8 gleichzeitig, Zeitlimit 5 min, kein
  Anfrage-Token, höchstens EIN Lauf je Nutzer; danach Reihung aus dem Speicher und das vollständige Ergebnis in den
  15-min-Cache. Während er läuft, liest der Vordergrund nur den Speicher (budget 0, maxNew 0). Antwort `explorerRunning`.
- Dauerhafter Stellungs-Speicher + nächtliches Vorrechnen (0.884.0, Prod-Log 07.11.: 4 177 Lücken eines Gegners = 162 s im
  Hintergrund, jedes Deploy leerte den 24-h-Arbeitsspeicher): Antworten des LOKALEN Explorers liegen zusätzlich in der Tabelle
  `LocalExplorerPositions` (`KeyHash` char(32) ascii = 16 Byte SHA-256 über `local|{query.CachePrefix}{Stellungsschlüssel}`,
  `Payload` = Brotli-gepacktes kompaktes JSON von `ExplorerPositionStats`, `FetchedAt`, Index auf `FetchedAt`). Bewusst NICHT
  `LichessExplorerCacheEntries` (Online-Explorer): andere Gültigkeit, ein Vielfaches an Zeilen, gebündeltes Schreiben statt
  Lesen-dann-Schreiben je Stellung, 255-Zeichen-Textindex. `BatchStatsAsync` liest Arbeitsspeicher → DB (gesammelt per `IN`,
  Portionen à 500, ohne den Abbruch der Anfrage; Treffer zurück in den Arbeitsspeicher, `BatchResult.FromDb`, Logzeile „aus DB")
  → Explorer; Neues gebündelt per `INSERT … ON DUPLICATE KEY UPDATE` (200 Zeilen je Anweisung, im langen Lauf alle 500
  Antworten zwischendurch). Gültig `LichessExplorer:LocalStoreDays` Tage (Vorgabe 45, 0 = aus); Fehler der DB nur als Warnung.
  `TrainingLinesContinuation` jetzt `Prep:TrainingContinuationParallelism` gleichzeitig (Vorgabe 16, vorher fest 8).
  **Vorrechnen** `TrainingPrewarmScheduler` (PeriodicWorker, pausierbar, Takt 5 min, Schalter `Prep:TrainingPrewarm` Vorgabe an,
  nie ohne `LichessExplorer:LocalUrl`): nur im Fenster `Prep:TrainingPrewarmWindow` (Vorgabe `03:30-05:30`,
  `Prep:TrainingPrewarmTimeZone` Vorgabe Europe/Vienna), bricht am Fensterende ab; eine vollständige Nacht ruht bis zur nächsten.
  `TrainingPrewarm` löscht zuerst Abgelaufenes, nimmt dann jeden Nutzer mit `UseForExtension`-Repertoires, ermittelt
  (`TrainingLinesService.PrewarmPositionsAsync`) die Gegnerzug-Knoten aller markierten Repertoires je Farbe wie für einen Gegner
  ohne Partien (`NeedsExplorer` mit leerer Analyse, Kapitelfarben wie der Trainer) und holt für JEDE Stufenkombination
  (`TrainingExplorer.StageSets(LocalRatings)` = 8: 1600 | 1600,1800 | 1600,1800,2000 | 1800,2000 | 1800,2000,2200 | 2000,2200 |
  2200,2500 | 2500) die fehlenden in Portionen à 1 000 (`Prep:TrainingPrewarmParallelism`, Vorgabe 8; nur DB, nicht der
  Arbeitsspeicher). Zwischen den Portionen fragt er den Admin-Schalter „Hintergrundjobs pausieren" (dann Ende). Je Nutzer/Kombination
  eine INFO-Zeile „Trainingslinien vorrechnen: Nutzer …, Stufen … — … Stellungen, … vorhanden, … geholt, … ohne Antwort, … ms".
  Größe: Payload im Mittel ~560 B (gemessen an 152 Stellungen des lokalen Explorers, max ~920 B), mit Schlüssel, Index und
  InnoDB-Verschnitt ≈ 0,9 KB je Zeile; ein Nutzer mit ~7 500 Gegnerzug-Stellungen × 8 Kombinationen ≈ 60 000 Zeilen ≈ 55 MB.
  Nicht abgedeckt: eigene Kapitelfarben der Karte (`chapterColors`) und Linien mit eigener Startstellung, die ein Gegner erreicht
  hat — die holt weiter die Abfrage selbst.
- Chessable-Stand je Linie + ungelernte Alternativen (0.885.0, Wünsche 07.11.: „wann ich die das letztemal auf chessable
  trainiert habe und auf welcher stufe", „wie oft der schon falsch war", „link zur chessable line", „ungelernte alternativen
  brauchst du mir nicht bringen"): `Services/Chessable/ChessableLineHistory` liest je Nutzer und Linie (`[ChessableOid]`, ohne
  bid — Varianten-Ids sind bei Chessable kursübergreifend eindeutig) gebündelt `ChessableSessionMoves` (jüngstes `CreatedAt` =
  zuletzt trainiert; Stufe = niedrigste `level` der eigenen, nicht-informativen Züge dieses Durchlaufs, `practiceMode` =
  Trainingsfarbe; `wrong[]` je `mid` über alle Durchläufe summiert) und als Rückfall für die Stufe `ChessableReviewLines`
  (`level` = Weiß, `level_b` = Schwarz laut Fixture, Hinweis „Stand vom …"). `mid` = Zeile (voller Zug) → Halbzug 2·mid (+1 für
  Schwarz, −1 wenn die Linie mit Schwarz am Zug beginnt). **Anlegen „Prep: …"**: je Linie mit oid Kopfzeilen
  `[ChessableLastTrained "yyyy.MM.dd"]`, `[ChessableLevel "n"]` (nur bekannt), `[ChessableUrl "https://www.chessable.com/variation/<oid>/"]`,
  Anfangskommentar `{Chessable: zuletzt trainiert 05.11.2026, Stufe 7 — <url>}` bzw. `{Chessable: noch nicht trainiert — <url>}`
  und vor jedem Zug mit Fehlern `{Chessable: hier 3× falsch (zuletzt 7…Sc6)}` (Zug wie mitgeschnitten); Züge, Varianten und
  Linien-Schlüssel bleiben (Tests Server + Trainer-Parser). Linien ohne oid bleiben unverändert. **Liste**: je gezeigter Linie
  `chessableLastTrained` (`yyyy-MM-dd`), `chessableLevel`, `chessableUrl` (eine Abfrage je Tabelle für die gezeigten Linien).
  **Alternativen**: eine Linie, deren White- oder Black-Header einen durch „|" getrennten Teil mit „Alternative" am Anfang trägt
  (z. B. „12 | Steinitz ohne f2-f4 | Alternative 7...a5 #1"), bleibt nur, wenn es einen Durchlauf ohne `alternativeSkipped` gibt;
  sonst fällt sie vor der Reihung weg (zählt nicht gegen die 50), Antwort `skippedAlternatives`, Beschreibung des „Prep: …"
  „N ungelernte Alternativen ausgelassen.". Die `alternative`-Flags der getReview-Rohdaten werden NICHT ausgewertet (ohne
  Beispieldaten nicht prüfbar, und es hieße, je Anfrage alle LONGTEXT-Rohantworten zu laden).
- Eigene Startstellung (0.717.0, Screenshot 08.10.: „Prep: Stoettner" bestand aus 50 Chessable-Übungen aus Modellpartien):
  Eine Linie mit `[FEN]` zählt nur, wenn er ihre Startstellung in einer Partie erreicht hat (`OpponentTrainingLines.StartReached`:
  Grundstellung oder `Stats[start].Reached > 0`); sonst bleibt sie „nie erreicht"/`source = none` — OHNE Schätzung, denn der
  Explorer kennt ab der FEN die Züge, aber nicht die Wahrscheinlichkeit, dort je hinzukommen (kurze Übungen mit 1–2
  Gegnerzügen schlugen sonst jede echte Eröffnungslinie). `NeedsExplorer` fragt für solche Linien nichts ab. Linienliste
  der Repertoire-Seite nummeriert die Zugvorschau ab dem Start-FEN („12. Bc4 …" statt „1. Bc4 …", `startNumbering`).
- Gefolgt, aber ohne Quelle danach (0.718.1): `source = none` mit `ownMoves > 0` (er folgt der Linie, so weit seine Partien
  reichen, danach kein Explorer) ist eine eigene Stufe ZWISCHEN „mit Quelle" und `deviates` (Auffüllregel darin) — vorher
  stand die Linie, der er folgt, hinter der, der er widerspricht. Ganz hinten bleiben nur `none` ohne eigenen Treffer.
- Farbe aus Sicht des Gegners (0.733.1, Prod 10.10.: „hab gesagt er spielt weiß, er hat trotzdem dann lines aus meinem
  weißrepertoir genommen"): `color` bleibt MEINE Farbe — die Rechnung war richtig, die Bedienung mehrdeutig. Die
  Beschreibung des „Prep: …" schreibt beide Seiten aus (`TrainingLinesService.Description`: „Trainingslinien gegen Hess, Max
  — er hat Weiß, ich spiele Schwarz — aus …") und nennt nur die Repertoires, aus denen übernommene Linien stammen
  (vorher „gegen Hess, Max (Weiß)" = meine Farbe, dazu alle Quellen). Oberfläche: Haupt-`src/frontend/CLAUDE.md`
  „Spielerkarte geteilt + Spielervorbereitung".
- Abweichungen (0.891.0, Wunsch 07.11.: „in welchem zug er einen zug macht den ich nicht kenne … trainiere abweichungen"):
  `GET /api/prep/player/{id}/deviations` (prep.view) und `GET /api/league/player/{fide}/deviations` (`LeagueTrainingController`,
  league.view, kein Teilen-Link), Parameter wie `training-lines` (`take` höchstens 50). Quellen/Farbe/Kapitelfarben wie die
  Trainingslinien (`TrainingLinesService.LoadSourcesAsync`, gemeinsam mit `ComputeCoreAsync`), Rechnung `OpponentDeviations.Find`:
  Partien des Gegners mit der anderen Farbe als Präfixbaum (40 Halbzüge) durch EIN Brett; Abweichung = Repertoire-Stellung
  (`RepertoireReach.Key`), Gegner am Zug, sein Zug führt in eine Stellung, die das Repertoire nicht kennt (Zugumstellung zurück
  ins Repertoire = keine). Gleiche Stellung + gleicher Zug über verschiedene Wege = eine Abweichung (Partien addiert, Pfad = der
  häufigste Weg). Reihung: Partien ↓, jüngstes Jahr ↓, kürzerer Pfad (mit seinen eigenen Zahlen ist „Häufigkeit × Wahrscheinlichkeit
  des Pfads" genau die Partienzahl; keine Lichess-Schätzung — eine Abweichung stammt immer aus seinen Partien). Antwort
  `{ repertoires, repertoire, color, colors, games, elo, total, deviations[{ path, fen, san, games, reached, lastYear,
  repertoire }], more, positions }`; `positions` = Schlüssel → `{ n, y, m: { SAN: Partien } }` für die Pfad-Stellungen und den
  Fortsetzungsbaum (bis `TailPlies` = 16 Halbzüge nach der Abweichung; `m` nur in Stellungen, in denen ER am Zug war — für meine
  Antworten gibt es keine Vorgabe, der Trainer urteilt per Engine und schlägt die Stellung nach meiner Antwort nach). `elo` für die
  Maia-Stufe (Prep jüngste Elo/`MaxElo`, Liga Meldeliste, sonst 1800). 15 min im `IMemoryCache` wie die Trainingslinien.
- Partien je Stellung für den Trainer (0.891.0, Wunsch 07.11.: „rechts eine meldung ab wann ich von gespielten partien abgewichen
  bin"): `training-lines?positions=true` liefert zusätzlich `positions` (Form wie oben, aus `OpponentTrainingLines.Analysis`: nur
  Repertoire-Stellungen, die er erreicht hat, `m` = seine Züge über `NextSan`) — einmal je Sitzung, nicht je Zug.
