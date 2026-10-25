# LeagueHub — Tiroler Ligen, Aufstellungs-Prognosen (Admin + öffentliche Teilen-Links, 0.569.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → LeagueHub — Tiroler Ligen, Aufstellungs-Prognosen (Admin + öffentliche Teilen-Links, 0.569.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Portierung der Python-Fassung (`~/claude/league-analyzer`, live bis zum Prod-Tag unter
`leaguehub.oberschmid.homes` als eigener Stack `/opt/stacks/leaguehub`). **Bestand übernommen** einmalig über den Bündel-Import
(`export_bundle.py` → `POST /api/league/admin/import`); der Endpunkt samt `LeagueImportService` ist **entfernt in 0.721.1** (Wunsch
2026-10-08: er leerte ALLE `LeagueTournaments` — seit Bayern und der Bundesliga gefährlich). Neue Ligen kommen je Saison über
`admin/chessresults/import`, `admin/ligamanager/import` und `admin/zugspitze/import`. Rechte: `league.view` lesen,
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
  (fertig gerechnete Liga-Ansicht als JSON — gerechnet beim Aktualisieren, nicht je Aufruf; `Json` ist Concurrency-Token:
  Ansichten nur über `LeagueService.PatchViewsAsync` bzw. `RebuildViewsAsync` schreiben (Kollision → neu laden), nie
  direkt laden-ändern-speichern — Codereview N4-007).
  **Caches** (Codereview N4-003): zerlegte Profil-Partien und geteilte Meldelisten liegen im Keyed-Cache
  `league-profile-games` (`LeagueProfileStore.CacheServiceKey`, SizeLimit 10 000 Partien, 10 min ungenutzt), Schlüssel
  FIDE-ID + `UpdatedAt` bzw. Tnr/Runde/Team + `GeneratedAt`. Die öffentliche Ansicht eines Teilen-Links
  (`GET api/league/s/{token}` und `…/sources`, `LeagueService.PublicShareAsync`) liegt dort je Link 60 s
  (`ShareViewTtl`), Schlüssel Token + `GeneratedAt` + `ViewsEpoch` (prozessweiter Zähler, den jedes Rechnen und jeder
  `PatchViewsAsync` hochzählt) — der Link selbst wird bei jedem Aufruf geprüft.
- **Rechenkern** `Services/League/LeagueEngine.cs`: 1:1-Portierung von `features.py`/`model.py` —
  Merkmale NUR aus Wissen vor der Runde, logistische Regression mit den eingebetteten Gewichten
  `Assets/league-model.json` (trainiert in Python, `export_weights.py`; Bayern seit 0.708.0 eigenes Modell, siehe „Modell je Region"), Normierung je Match auf B Bretter,
  Brett-Wahrscheinlichkeiten über elementarsymmetrische Polynome (Bretter folgen der Meldeliste),
  Sonntag vorab = Mischung aus „Samstag gespielt ja/nein". **Maßgeblich ist die C#-Fassung** (die Python-Fassung wird nicht
  mehr gepflegt). Paritäts-Nachweis zuletzt 2026-10-08 gegen den echten Bestand: 88 Begegnungen, 1 676 Wahrscheinlichkeiten,
  Abweichung 0 (0.569.0 ebenso). Der Test dafür (`LeaguePythonParityTests`, las das Prod-Bündel `data.json.gz`) ist mit dem
  Bündel-Import entfernt.
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
  `LeagueClubGames` als „Vereins-Datenbank"; online je Seite `COUNT(DISTINCT ExternalId)`. Seit 0.725.0 die Zeile „Ligarunde (erste
  Züge)" (Schlüssel `Ligarunde`): Teilpartien aus `LeagueGameMoves` je Brett einmal, ohne die mit voller Partie (`LeaguePartialGames.FilterAsync`;
  die Kartenpartien derselben Spielerpaare sammelt `CountBoard` über `onGame` beim ohnehin laufenden Durchgang). Prod 01.10.2026: rund 34.800
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
  **Drehen, zuschneiden, zweites Exemplar** (0.754.0, Formular-Stapel 2: Landesliga-Fotos mit Weiß- und Schwarz-Exemplar
  quer nebeneinander): statt `fileIds` geht `pages: [{ fileId, rotate?, crop?, views?: [{ fileId, rotate?, crop? }] }]`.
  `rotate` ∈ 0/90/180/270 (im Uhrzeigersinn, nach EXIF), `crop` = `[links, oben, rechts, unten]` als Anteile 0…1 des
  GEDREHTEN Bilds (`ScoresheetImage.Transform`). Das Ergebnis ist das gespeicherte Hauptfoto — die Kästen beziehen sich
  darauf; `views` werden Ansichten derselben Seite (`ScoresheetScanView`, wie „weiteres Foto dieser Seite"), dieselbe
  `fileId` darf mehrfach vorkommen (ein Foto, zwei Ausschnitte). 400 zusätzlich `invalidTransform`, `invalidLayout`,
  `tooManyViews`.
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
  `LeagueGameId` roh. (3) **Heilung beim Start** (`LeagueGameLinks.HealOnStartupAsync` in `Startup/DatabaseSetup.cs`, idempotent, wirft nie):
  alle mit Schlüssel neu auflösen; gültige Ids ohne Schlüssel bekommen ihn; tote Ids ohne Schlüssel werden über
  `LeaguePairingFinder` neu gefunden (`AutoPick`, genau EIN genauer Treffer; PGN-Datum, sonst das Jahr) oder geleert (Warnung).
- **Leere Bretter der laufenden Runde** (0.744.0, Wunsch 2026-10-10: „wie kann ich eine Partie der aktuellen Runde zuweisen?"):
  die FIDE-Suche fand Bretter ohne Spieler nie. `LeaguePairingFinder.OpenBoardsAsync` hängt an jede Vorschlagsliste die leeren
  Bretter (beide Spieler `null`, nicht kampflos) der EIGENEN Begegnungen (`OwnsTeam`) mit Rundentermin höchstens `OpenRoundDays`
  (21) Tage her bzw. `DayTolerance` voraus (`Today` für Tests). Farbe nach Regel (`HomeWhiteByRule`: Tirol Heim Weiß an ungeraden,
  Bayern an geraden Brettern); die eigene Seite der Partie = die anonymisierte, sonst die mit der FIDE-ID eines eigenen Spielers
  der Meldeliste — passt die Farbe nicht, kein Vorschlag; unbekannt → alle Bretter, nie „genau". `Option.Open` = true,
  `White`/`Black` = MANNSCHAFTEN; LeagueHub lässt bei der Wahl die Spieler stehen (`setPairing`, `choosePairing`), Text „…, noch
  nicht besetzt". Mehrere passende Bretter → keine Vorwahl.
- **Vorläufige Zuordnung zur laufenden Runde** (0.832.0, Wunsch 2026-10-24: „er sollte direkt nach dem Einlesen, wenn er es
  zuordnen kann, die Matchings für die aktuelle Runde schreiben — und später nur bestätigen"; Anlass: zehn Formulare vom Spieltag,
  Runde 3 ohne Aufstellung auf chess-results, nur eine bekam eine — und die falsche, Runde 1 des Gegners).
  `Services/League/LeaguePairingFinder` hängt an die leeren Bretter EINE vorläufige Wahl (`Option.Provisional`, im DTO
  `provisional` + `uncertain`), gerechnet in `Services/League/LeagueProvisionalPairing.cs`:
  (1) **Begegnung**: Gegner (FIDE-ID, sonst Namensschlüssel) in der Meldeliste der gegnerischen Mannschaft einer leeren eigenen
  Begegnung, sonst der eigene Spieler in der Meldeliste der eigenen; mit Tag ±`DayTolerance`, ohne Tag die jüngste Runde ≤ heute
  (Saison passt zum Jahr); mehrere gleich gute → keine. (2) **Brett**: Farbe nach `HomeWhiteByRule` — die TMM-Regel „Heim Weiß an
  ungeraden Brettern" ist an allen gespielten Brettern der Saison 2026/27 belegt (`LeagueGames.HomeColor`: ungerade `w`, gerade
  `s`, ausnahmslos; Bayern umgekehrt); dazu die Meldereihenfolge BEIDER Seiten gegenüber den schon zugeordneten Partien derselben
  Begegnung (kleinere Meldenummer sitzt weiter vorn; Untergrenze 1 + Zahl der kleineren, Obergrenze n − Zahl der größeren, feste
  Bretter grenzen direkt ein), belegte Bretter fallen weg; bleibt mehr als eines, das, das der Schätzung aus den bisherigen
  Runden am nächsten liegt (1 + Schnitt der eingesetzten Mannschaftskollegen mit kleinerer Meldenummer, `HomeRb`/`AwayRb`; ohne
  Verlauf die Meldenummer) → `uncertain`. (3) **Vorwahl**: `AutoPick` = genau EIN genauer Treffer, sonst die vorläufige; die
  Prüfseite (`club-scan-page`, `ScanHeaderForm.loadPairings`) wählt dasselbe vor, Etikett „vorläufig (Aufstellung noch nicht auf
  chess-results)" (+ „Brett geschätzt"; `pairingText`). (4) **Gespeichert** über `LeagueGameLinks.Set` — ein Brett ohne Spieler
  setzt `LeagueClubGames.LeaguePairingProvisional` (auch bei Wahl von Hand). Nach jedem Speichern (`AddAsync`, PGN-Import)
  verteilt `RebalanceAsync` ALLE vorläufigen Partien der Begegnung gemeinsam neu (die mit den wenigsten möglichen Brettern zuerst)
  — mit jeder weiteren Partie wird die Brettfolge genauer; bestätigte bleiben stehen. Wege: Formular-Add ohne/mit Vorwahl,
  PGN-Import (fehlt `leagueGameId`), manual-scan des Watchers mit `clubGameId` (`AssignPairingIfUnlinkedAsync`, nur ohne
  Zuordnung) und `POST /api/league/club/games/{id}/pairing/auto` (league.manage; nur Partien OHNE Zuordnung — eine falsche erst
  mit `PUT games/{id}` `leagueGameId: 0` lösen) → `{ leagueGameId }`. (5) **Bestätigen**: `LeagueGameLinks.RelinkAsync` (nach jedem
  `LeagueRefresh.ReplaceAsync`, auch Ligamanager/Zugspitze/chess-results-Import, und beim Start) ruft
  `LeagueProvisionalPairing.ConfirmAsync`: Brett jetzt besetzt und jede bekannte FIDE-ID der Partie (auch die interne hinter
  „Schwaz") sitzt in ihrer Farbe → bestätigt (Info); sonst an einem anderen Brett derselben Begegnung → umgehängt + bestätigt
  (Info); sonst Zuordnung gelöst (Warnung). Noch leere Bretter bleiben vorläufig. Von Hand: `POST …/club/games/{id}/pairing/confirm`
  (wer bearbeiten darf) → `{ changed }`; die Vereinsliste zeigt unter der Partie „Ligapartie vorläufig — …" mit Knopf „Bestätigen"
  (DTO `leaguePairingProvisional`, nur mit `leagueGameId`). (6) **Kein Fehlgriff mehr wie Partie 225**: hat die Partie eine eigene
  Seite („Schwaz" bzw. FIDE-ID eines eigenen Spielers) und saß deren Spieler an einer Paarung ohne eigene Mannschaft nicht, ist
  sie kein Vorschlag (also auch nie `AutoPick`).
- **Ältere Fassungen archivieren** (0.691.0, Wunsch 2026-10-06: „wenn eine 2. Partie über ein Scoresheet hinzugefügt wird, die schon
  eingegeben ist, das alte archivieren"): `LeagueClubGames.ArchivedAt`/`ReplacedById`, gesetzt in `LeagueClubService.ArchiveOlderVersionsAsync`
  nach jedem Formular-Add (`AddAsync`, nicht beim PGN-Import). Dieselbe Partie = dieselbe feste Ligapaarung, oder (ohne zwei verschiedene
  Paarungen) dasselbe Jahr + dieselben Spieler (FIDE-ID inkl. der internen hinter „Schwaz", sonst Name) + dieselben ersten
  `SameGamePrefixPlies` (16) Halbzüge. Die Analyse der alten geht. **Globaler Query-Filter** `ArchivedAt == null` auf `LeagueClubGame`:
  archivierte sind überall unsichtbar; `IgnoreQueryFilters()` nur in Konto löschen (`ProfileService`), Rückbau eines Teilen-Links
  (`DeleteByShareAsync`) und Zuordnen nach dem Anmelden (Claims). Antwort von `POST …/club/games`: `replaced` = Zahl archivierter.
- **Endpunkte** (`Controllers/League/LeagueController.cs`): `GET /api/league/index`, `GET /api/league/sources`, `GET /api/league/{tnr}`,
  `GET /api/league/player/{fide}` (+`/pgn`), `POST/DELETE /api/league/share`, `POST /api/league/update`
  (+`/status`; Knopf, KEIN Zeitplan — ein Lauf auf einmal, neuer Start frühestens nach 2 min),
  `POST /api/league/admin/rebuild`, `POST /api/league/admin/chessresults/import` (eine chess-results-Liga je Saison über den Crawler,
  Bundesliga, siehe „Österreichische Bundesliga"), `POST /api/league/admin/ligamanager/import` (Bayern, siehe unten),
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
  aktualisiert“. Leer bleiben darf eine Seite nur, wenn auch der Bestand dafür leer ist (Saisonbeginn). (Der Bündel-Import
  `admin/import`, der alle Ligen in EINER Transaktion ersetzte, ist seit 2026-10-08 entfernt.)
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
  mit `Assets/league-model.json` (Python, unverändert — `LeagueEngineTests` gleich), Bayern
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
  Rechnen), Trainingszeilen der Python-Fassung (`rows.json`) über `LeagueLevels.FromTmm` (1–4 → 3–6; der Bündel-Import, der sie
  ebenfalls nutzte, ist seit 2026-10-08 entfernt). **Modell unverändert**: `Assets/league-model.json`
  heißt `lvl4`/`lvl5`/`lvl6` statt Pythons `lvl2`–`lvl4` (Gewichte gleich, Feld `levels` erklärt es), `gk_q` = q_same in Stufe 6 —
  `LeagueBundesligaTests.TirolModel_LogitOfAShiftedRow_EqualsThePythonLogit`, `LeagueTrainingTests` (rows.json über `FromTmm`) und die
  Python-Parität (lokal mit dem Prod-Bündel 2026-10-08: 88 Begegnungen, 1 676 Wahrscheinlichkeiten, Abweichung 0,000000; der Test
  scheiterte seit 0.66x an den alten `locked`-Status der Python-Ansicht und ist mit dem Bündel-Import entfernt) liefern dieselben Zahlen. Backtest `Hits` sitzt auf 3–6, für die
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
  **Import je Saison** (0.720.0, `Services/League/ChessResultsLeagueImport.cs`): `POST /api/league/admin/chessresults/import`
  (league.manage) `{ tnr, season ("2026/27"), level (1–6), league, grp?, stage? ("Liga"|"Playoff"), name? }` (+ `?dryRun=true`) holt die
  vier Seiten über den Crawler (`GET {Crawler}/api/league/{tnr}`, HttpClient `LeagueCrawler` wie „Daten aktualisieren") und schreibt
  Turnier-Zeile (`Source` null, Start/End/Rounds aus den Rundenterminen) + `LeagueRefresh.ReplaceAsync` (stabile Brettpaarungs-Ids,
  `RelinkAsync`) in EINER Transaktion, dann `RebuildViewsAsync` (nur die laufende Saison bekommt Ansichten — Vorsaisonen sind nur
  Merkmale). Antwort `{ tnr, name, season, level, dryRun, counts{ rounds, roundsPlayed, matches, teams, boardGames, boardGamesPlayed,
  boardPlayersUnmatched, boards, players, playersWithFide, fideNotInLeagues, fideWithoutCard, firstRound, lastRound, roundBlocks },
  views }`; 400 `invalidLeague` (Tnr ≥ 900 000 000, Saison nicht „JJJJ/JJ", Stufe außerhalb 1–6, Liga leer), 404 `notFound` (vier leere
  Seiten), 409 `conflict` (Nummer gehört Ligamanager/Zugspitze), 502 `incomplete` (eine leere Seite bei Bestand — nichts ersetzt),
  503 `unreachable`. Danach holt „Daten aktualisieren" die Ligen der laufenden Saison wie jede TMM-Liga. **Turniernummern** (chess-results,
  Veranstalter ÖSB): 1. BL 2026/27 **1404438**, 2025/26 1163837, 2024/25 929746; 2. BL West 1410724 / 1221888 / 966389; Mitte
  1409864 / 1174791 / 936680; Ost 1418722 / 1179184 / 956466 (2026/27 / 2025/26 / 2024/25). **Was der Crawler liefert** (08.10.2026):
  12 Teams (West 2025/26: 11 + spielfrei), 6 Bretter, 11 Runden, Meldelisten 208–232 Spieler ALLE mit FIDE-ID (Ost 2026/27: 229/230),
  Heim an ungeraden Brettern Weiß, Statistik (art=20) erst nach Saisonstart, ungespielte Runden mit Gast „FED" und „Brett nicht
  besetzt". Gegen Prod 08.10.: 1 013 FIDE-IDs der zwölf Ligen haben keine Spielerkarte (je Liga 119–224) — Lumbra/Megabase nachfiltern
  ist ein eigener Datenjob. **Reihenfolge** egal (keine FIDE-Nachfüllung wie in Bayern, alle Meldelisten tragen IDs); danach
  `admin/rebuild` bzw. „Daten aktualisieren". `LeagueAnalysisQueue.BuildAsync` nimmt seit 0.720.0 die jüngste Saison JE REGION
  (vorher global — eine Region ohne neue Saison fiel heraus).
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
  `shared/player-card.component.ts` (Dialog, Vorgabe = Farbe an diesem Brett; seit 0.727.0 Link „📷 Fotos" →
  `https://ligafotos.<Elterndomäne>/#spieler=<Name>` über `ligaFotosUrl`, seit 0.727.5 **nur für Admins** (`AuthService.isAdminLive`
  = Live-Stand aus `/api/auth/permissions`, sonst Token-Rolle; sonst kein Link), nicht über Teilen-Links/Dev — die Foto-Seite
  ist ein eigener Python-Dienst außerhalb dieses Repos, `~/claude/liga-fotos-app`, Login per `rh_session` + `league.view`), reine Regeln in
  `core/league-format.ts`. „Daten aktualisieren" fragt alle 4 s `/api/league/update/status` nach und lädt danach frisch.
