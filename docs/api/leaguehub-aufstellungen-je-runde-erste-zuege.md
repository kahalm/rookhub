# LeagueHub — Aufstellungen je Runde + erste Züge (0.721.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → LeagueHub — Aufstellungen je Runde + erste Züge (0.721.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Wunsch 2026-10-08: „für jede Runde einen Knopf, der mir alle Aufstellungen für diese Runde anzeigt — dann bei jeder Partie die
Möglichkeit, die ersten paar Züge einzugeben."

* **Knopf** „Alle Aufstellungen der Runde N" unter der Auswahl der Ligaseite (`league-page`), klappt `shared/round-lineups.component.ts`
  auf: je Begegnung Heim – Gast mit Mannschaftsergebnis (½), darunter je Brett Heimspieler (Titel, Elo), Farbe des Heimspielers,
  Ergebnis (Sicht Heim – Gast wie chess-results), Gastspieler, Züge. Eigene Begegnung (Rahmen) über `own` (`LeagueClub.OwnsTeam`).
  Begegnung ohne Brettpaarungen = „Noch keine Aufstellung". CSS-Grid statt Tabelle — am Handy stehen Heim/Gast untereinander.
  Zustand in der Adresse `&aufstellungen=1` und mit der Runde im Tab (`leaguehub-round` → `aufstellungen: true`, nur wenn offen).
  **Namen → Spielerkarte** (0.727.2, Wunsch 2026-10-09: „namen sollten klickbar sein (selbe info wie bei der prognose)"): Heim- und
  Gastname mit FIDE-ID (`homeFide`/`awayFide` aus `LeagueGames.HomeFide/AwayFide`, leer = `null`) sind `button.pl` wie in `lh-fixture`
  und öffnen dieselbe `lh-player-card` (`open(fide, Farbe des Spielers an diesem Brett, Brett, null)` — Gast = Gegenfarbe von
  `homeColor`, unbekannt = keine Vorgabe; Mandant `?club=` über den Interceptor). Ohne FIDE-ID bloßer Text (wie in der Prognose).
* **Erste Züge** (`shared/board-moves.component.ts` + Dialog `shared/moves-editor.component.ts`): Anzeige „1.e4 c5 2.Sf3" (deutsche
  Figurenbuchstaben über `de()`), „Züge eingeben/ändern" öffnet Brett (`app-chess-board`, gedreht aus Sicht des eigenen Spielers) +
  Textfeld, beide synchron (Brett hängt an, Tippen stellt das Brett bis zum ersten falschen Zug), Zurück, Speichern, Löschen.
  Auch an den Paarungen der eigenen gespielten Begegnung in `lh-fixture` (die Ansicht holt dafür die Aufstellungen der Runde nach;
  über einen Teilen-Link nicht). `LeagueViewBuilder`/Prognose unberührt.
* **Speicherung** `LeagueGameMoves` am natürlichen Schlüssel (Tnr, Runde, Begegnung, Brett) — die Zeilen von `LeagueGames`
  werden beim Aktualisieren zusammengeführt, der Schlüssel bleibt. Regeln in `Services/League/LeagueGameMoves.cs`: `Parse` nimmt
  Zugnummern, `{…}`/`(…)`, Ergebnis, deutsche Buchstaben (S/L/T/D, auch bei der Umwandlung), `0-0`; höchstens 60 Halbzüge;
  Legalität über `SavedGameService.LegalSans` (dieselbe Prüfung wie Vereinspartien), gespeichert als englische SAN.
* **Rechte**: lesen `league.view`; schreiben `league.contribute` (wie Vereinspartien anlegen) — ohne `league.manage` nur an
  Begegnungen mit einer Mannschaft des eigenen Vereins (403 `foreignMatch`) und nur eigene Einträge ändern/löschen (403 `notYours`,
  Regel wie `CanDelete` der Vereinspartien); Verwalter (live über `PermissionResolver`) alles. Kampflose/unbesetzte Bretter: 400 `noGame`.
  Mandant über `?club=` (`LeagueClubAsync`). Controller `LeagueLineupsController` (eigene Datei, gleiche Route `api/league`).
* **Partie je Brett statt Zug-Eingabe** (0.724.0, Wunsch 2026-10-08: „ich habe eine Partie drinnen: Hess, Max – Platzgummer, Fabian —
  wenn ich das hab, dann sollt er nicht Züge eingeben lassen, sondern die Partie ausweisen"): die Aufstellung löst je Brett die
  vorhandene Partie mit DERSELBEN Regel wie die Paarungen gespielter Runden auf — `LeagueFixtureGames.ForGamesAsync` (seit 0.724.0 der
  Kern, `ForFixtureAsync` = gefiltert auf eine Mannschaft): feste Zuordnung (`LeagueGameLinks.ResolveAsync`) → Raten unter Partien ohne
  lebende Zuordnung (eine geratene Partie höchstens EINEM Brett) → Spielerkarten ±3 Tage. Vereinspartien nur des Vereins der Anfrage,
  archivierte nie. EIN Aufruf für die ganze Runde: Rundentermin, Vereinspartien des Vereins (Jahr ODER zugeordnet), Auflösung (≤ 2
  Abfragen) und Spielerkarten NUR der Bretter ohne Vereinspartie. Je Brett `game: { source (club|profile), clubGameId, plies, result,
  white, black, firstMoves (die ersten 10 Halbzüge, englische SAN), canEdit }` — OHNE PGN (das holt „Nachspielen": Vereinspartie
  `GET …/club/games/{id}` samt Bewertungen, Spielerkarte `GET …/round/{r}/games?team=<Heim>`). Mit Partie ist `canEditMoves = false`;
  ein alter Handeintrag kommt weiter als `moves` und `canDeleteMoves` (löschen darf, wer ihn auch ändern dürfte). Oberfläche:
  `shared/board-game.component.ts` („Partie vorhanden · 81 Halbzüge · 1.e4 c5 2.Sf3 …", Nachspielen, Analyse per Einmal-Code, mit
  `canEdit` Bearbeiten/Korrigieren — alle vier seit 0.727.2 als `.btn-link` auf einer Grundlinie, `.bg-actions` mit
  `align-items: baseline`; vorher waren Bearbeiten/Korrigieren nackte `<a>` in Browser-Linkfarbe ohne Polster und standen höher),
  `board-moves` mit `replaced` (grau „ersetzt durch die Partie", nur Löschen). In `lh-fixture`
  erscheint die Zug-Zeile nur an Brettern OHNE Partie. Der Server lehnt Schreiben an einem Brett mit Partie NICHT ab (Rechte unverändert).
* **Laufende Runde aus zugeordneten Partien** (0.739.0, Wunsch 2026-10-10: „die laufende Aufstellung schon sehen — ich weiß ja
  die Paarungen"): ist ein Brett noch ohne Spieler (chess-results hat die Runde nicht), aber eine Vereinspartie FEST darauf
  zugeordnet, kommt das Brett aus ihr (`LeagueFixtureGames.ProvisionalAsync`, `Pairing.Provisional` + `HomeWhite`): Namen/FIDE aus
  der Partie, Elo aus der Partie sonst der Meldeliste der Liga, Heimfarbe über die anonymisierte eigene Seite (sonst die Mannschaft
  der weißen FIDE-ID), Ergebnis aus der Partie. **Die eigene Seite („Schwaz") zeigt den internen echten Spieler NUR mit
  `revealOwn`** — gesetzt von `GET /api/league/{tnr}/round/{r}/games` und den Aufstellungen (angemeldete Vereinsmitglieder; bewusste
  Ausnahme von „Real-Namen nie ausgeben", Entscheidung des Users 10.10.), nie über `/api/league/s/{token}/games`. `LineupBoard.Provisional`;
  `lh-fixture` lädt die Paarungen auch für eine offene Runde und zeigt sie mit Hinweis „vorläufig", sobald ein Brett
  vorläufig ist (leere Bretter „offen") — angemeldet seit 0.739.0, über den Teilen-Link seit 0.838.2 (Wunsch 2026-10-25: „seh ich
  die Paarungen von Runde 3 nicht im shared link"; der Server liefert dort über `/s/{token}/games` dieselben vorläufigen Bretter mit
  anonymisierter eigener Seite). Sobald chess-results die Runde bringt, gilt wieder die öffentliche Paarung.
* **Auch über den Teilen-Link** (0.838.0, Wunsch 2026-10-25: „die Aufstellung für die Runden (das mit den Partien) soll auch bei
  den anonymen Links verfügbar sein"): `GET /api/league/s/{token}/lineups` (`LeagueShareController.Lineups`, ohne Anmeldung,
  Rate-Limit `anonymous-tournament`) liefert dieselbe Antwort wie der angemeldete Endpunkt für Liga, Runde und Verein DES LINKS —
  nur lesend: `canEdit`, `canEditMoves`, `canDeleteMoves` und `game.canEdit` immer `false` (`LineupsAsync` mit `userId 0`,
  `canContribute`/`canManage` aus), die eigene Seite einer vorläufigen Aufstellung bleibt anonymisiert (`revealOwn: false`, wie
  `/s/{token}/games`), Vereinspartien nur des Vereins des Links; 404 = Link ungültig oder Runde unbekannt. Oberfläche: auf der
  Teilen-Seite (`share-page`) unter der Begegnung derselbe Textknopf „Alle Aufstellungen der Runde N" wie auf der Ligaseite
  (Zustand `?aufstellungen=1`), `lh-round-lineups` mit `shareToken` (lädt über den Link), `shareTeam` (die geteilte Begegnung = „wir"
  fürs Drehen des Bretts; nur dort „Nachspielen", PGN über `/s/{token}/games`, weil der Link nur deren Partien kennt) und
  `coveredFides` (FIDE-IDs/Schlüssel der Meldeliste des Links — nur diese Namen öffnen die Spielerkarte, mit Token; der Server
  antwortet für andere 404). `lh-board-game` zeigt über den Link kein „Bearbeiten/Korrigieren"; „Analyse" seit 0.838.3 an der
  geteilten Begegnung (RookHub `s/{token}/club-games/{id}`, siehe Prognosen-Doku).
* **Teilpartien** (0.725.0, Frage 2026-10-08: „fließen die [ersten Züge] in die Eröffnungsbäume bei der Vorbereitung ein?" — „ja mach
  das so"): jeder Eintrag in `LeagueGameMoves` ist eine TEILPARTIE beider Spieler (`Services/League/LeaguePartialGames.cs`), gerechnet
  bei jedem Lesen (Join der kleinen Zug-Tabelle auf `LeagueGames`, nur gespielte Bretter), nicht gespeichert: Kopf aus der Paarung —
  Namen, FIDE-IDs, Elo, Farben aus `HomeColor`, Datum = Rundentermin, Event „{Liga} {Saison}, Runde N" (Saison nur, wenn sie nicht im
  Namen steht), `Round`, `Board`, Ergebnis Sicht Weiß (`WhiteBlackResult` → PGN), `[LeagueSource "Ligarunde"]`. **Fließt ein** über
  `LeagueProfileStore.BoardAsync` (= `WithClub` + Teilpartien): Karte (`RebuildAsync`: `n`/`g`, Abschnitte, `src.Ligarunde`), Baum
  (`TreeAsync`, `source=board|both`), gefiltertes Profil (`ProfileAsync`) und `GamesAsync` (→ Prep-Karte `PrepCardService`,
  `TrainingLinesService.LeagueGamesAsync`, Team-Suche). **Nicht** in `recent` (Karte: `Build` und `Recent` überspringen
  `LeaguePartialGames.Is`), nicht in `RecentAsync`/PGN-Download (auch Prep: Recent/PGN ohne Teilpartien). **Ausschluss** (`FilterAsync`,
  ohne Vereinskontext — die Karte ist für alle Vereine dieselbe, die Züge sind global): Teilpartie fällt weg, wenn (a) eine Vereinspartie
  IRGENDEINES Vereins fest auf das Brett zugeordnet ist (Schlüssel bzw. `LeagueGameId`), oder (b) eine Partie derselben beiden Spieler
  (FIDE-ID, wo beide Seiten eine haben, sonst Nachname; Farben wie am Brett) ±3 Tage am Rundentermin liegt — geprüft gegen die
  Vereinspartien BEIDER Spieler (alle Vereine, auch über die echte FIDE-ID/den echten Namen hinter „Schwaz"; nur Jahr → gleiches Jahr)
  und die fremden Partien der eigenen Karte. Archivierte nie. Gewählt statt `ForGamesAsync`: das braucht einen Verein (Pool + Raten
  über `AnonName`) und je Karte die Runde aller Teilpartien — die Namens/Datums-Regel ist dieselbe wie dort die Spielerkarten-Stufe.
  Grenzfall: dieselben zwei Spieler zweimal im selben Jahr mit einer Vereinspartie nur mit Jahr → Teilpartie weg. Prep zusätzlich:
  hat der Bestand (Mega/Lumbra) die Partie (gleiche Farbe, ±3 Tage, gleicher Gegner-Nachname), fällt die Teilpartie weg.
  **Nachrechnen**: `SaveAsync` (Speichern und Löschen) ruft `RefreshCardsAsync` — `RebuildAsync` beider FIDE-Spieler +
  `PatchViewCountsAsync`, synchron wie `ClubGameWriter.RefreshCardsAsync` nach einem Upload (zwei Karten; kein Hintergrundlauf).
  Die Prep-Karte bleibt bis zu 15 min im Speicher. Kosten je Lesen ohne Teilpartie: eine Abfrage; mit: zusätzlich Rundentermine,
  Ligen und eine Vereinspartien-Abfrage ohne PGN.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/{tnr}/round/{round}/lineups` | `league.view` | `{ tnr, round, date, canEdit, matches[{ matchNo, home, away, homePts, awayPts, own, boards[{ board, homePlayer, homeTitle, homeElo, awayPlayer, awayTitle, awayElo, homeColor, result, forfeit, moves, canEditMoves, game?, canDeleteMoves, homeFide, awayFide }] }] }` — `homeFide`/`awayFide` (0.727.2) = FIDE-IDs der Paarung oder `null`; `game` (0.724.0) = vorhandene Partie des Bretts `{ source, clubGameId, plies, result, white, black, firstMoves, canEdit }` oder `null`, dann `canEditMoves = false`; 404 ohne Runde |
| GET | `/api/league/s/{token}/lineups` | – (Teilen-Link) | Dieselbe Antwort für die Runde des Links (0.838.0), nur lesend (`canEdit`/`canEditMoves`/`canDeleteMoves`/`game.canEdit` = `false`), eigene Seite anonymisiert; 404 Link ungültig/Runde unbekannt |
| PUT | `/api/league/{tnr}/round/{round}/match/{matchNo}/board/{board}/moves` | `league.contribute` | `{ moves }` (leer = löschen) → `{ moves }` (englische SAN); 404 ohne Paarung, 403 `foreignMatch`/`notYours`, 400 `noGame`/`tooLong`/`illegal` (+ `move`, `ply`) |
| DELETE | `/api/league/{tnr}/round/{round}/match/{matchNo}/board/{board}/moves` | `league.contribute` | → 204; 404/403 wie oben |

Tests: `LeagueGameMovesTests` (Parse, Rechte, 404, Lesen, Schlüssel überlebt neue Ids; seit 0.724.0 Partie je Brett: fest zugeordnete
Vereinspartie, anderer Verein sieht sie nicht, archivierte zählt nicht, Spielerkarte, geratene Partie nur einmal; 0.727.2 FIDE-IDs;
0.838.0 über den Teilen-Link nur lesen, eigene Seite anonym, ungültiger Link 404), `EndpointAuthInventoryTests` (Whitelist), `LeagueGameMovesSqlTests` (Migration + eindeutiger
Schlüssel gegen MariaDB), `LeaguePartialGamesTests` (0.725.0: Teilpartie bei beiden Spielern mit Kopf, Karte/Profil/Baum ohne `recent`,
Löschen rechnet nach + Ansicht, zugeordnete/gleichnamige Vereinspartie und Kartenpartie ±3 Tage schlagen sie, Quellen-Zeile, Trainingslinien,
Prep-Karte samt Bestands-Ausschluss); Frontend `round-lineups`/`board-game`/`moves-editor`/`league-page`/`fixture-view`-Specs.

**Stellung eingeben + Zugfolgen vorschlagen** (0.723.0, Wunsch 2026-10-08: „lass mich dort auch direkt Stellungen eingeben. schau
dann in der lokalen Lichess-DB nach, welche Eröffnungen am häufigsten zu der Stellung kommen, und schlag sie mir vor"):
* **Zweiter Modus „Stellung" im Zug-Editor** (`moves-editor`, Umschalter „Züge | Stellung"): FEN-Feld (einfügen) UND Aufstell-Brett
  `shared/setup-board.component.ts` — ein eigenes 8×8-Raster mit den cburnett-Bildern (`/piece/cburnett/*.svg`), weil `app-chess-board`
  nur legale Züge kennt; Palette mit 12 Figuren + ✕ (Figur wählen, Feld antippen; dieselbe Figur noch einmal = Feld leeren; ein zweiter
  König derselben Farbe ersetzt den ersten), „Grundstellung", „Brett leeren", Seite am Zug. Startet mit der Stellung nach den eingegebenen
  Zügen. FEN ↔ Brett synchron: das Brett schreibt die FEN neu (Rochaderechte aus König/Turm auf den Ausgangsfeldern, e.p. „-", Zähler
  „0 1"), eine getippte FEN stellt das Brett, solange sie lesbar ist (sonst Hinweis, Brett bleibt). Gültigkeit (`positionProblem`): je
  genau ein König, keine Bauern auf der 1./8. Reihe, Material (`materialProblem`, 0.723.1: ≤ 8 Bauern, mehr D/T/L/S als in der
  Grundstellung nur so viele, wie Bauern fehlen — Prod-Befund: Sf3 GESETZT statt gezogen ergab drei Springer und nur „keine Zugfolge"),
  die Seite, die NICHT am Zug ist, nicht im Schach (chess.js `skipValidation` + `isAttacked`). Reine Regeln in `core/position-setup.ts`
  (auch `placePiece`/`movePiece`/`removePiece`/`blackOf`).
* **Ziehen + Rechtsklick** (0.723.1): Figuren lassen sich auf dem Aufstell-Brett mit Maus UND Finger ziehen — Pointer Events (kein
  HTML5-Drag-and-drop, das gibt es auf Touch nicht), Zuhörer am `window` je Geste, `setPointerCapture` auf dem Brett. Erst ab 6 px
  Bewegung (`DRAG_THRESHOLD_PX`) wird aus dem Tipp ein Ziehen, darunter bleibt es der Klick; nach einem Ziehen wird der eine Klick,
  den der Browser hinterherschickt, verschluckt. Geisterfigur (`position: fixed`) folgt dem Zeiger, Ausgangsfeld abgedunkelt, Ziel
  umrandet; Loslassen auf ein anderes Feld = `pieceMoved` (ersetzt), außerhalb des Bretts = `pieceRemoved`, aufs Ausgangsfeld oder
  Escape (am `window` abgefangen, damit der Dialog offen bleibt) = Abbruch. Aus der Palette (`paletteDown`, an den Palette-Knöpfen)
  aufs Brett = `pieceDropped`. Felder MIT Figur und die Palette haben `touch-action: none` (Wisch zieht statt zu scrollen), leere
  Felder `manipulation` (dort lässt sich die Seite weiter scrollen). Mit gewählter Figur: Rechtsklick (`contextmenu`, auf dem Brett
  immer `preventDefault`) bzw. langer Druck auf Touch (400 ms ohne Bewegung, `LONG_PRESS_MS`; Bewegung vorher = Ziehen) setzt
  denselben Typ in Schwarz (`squareAltClick` → `onSquare(i, true)`).
* **Meldungen nach der Suche** (0.723.1): `games === 0` → „Der Explorer kennt diese Stellung nicht (0 Partien) — Figuren und Seite am
  Zug prüfen"; `games > 0` ohne Wege → „Stellung bekannt (≈ n Partien), aber keine Zugfolge innerhalb von 20 Halbzügen gefunden";
  `truncated` → „Suche am Budget abgebrochen — vielleicht gibt es mehr".
* **Fortsetzungsrunden** (0.727.3, Prod-Befund 09.10.2026: D00 Levitsky `rnbq1rk1/pp3ppp/2pb1p2/8/2BP4/1QN1P3/PP3PPP/R3K1NR b KQ - 0 1`,
  15 Halbzüge tief, 146 Partien — kalt nach 20 s/~240 Abfragen 0 Wege, `truncated`; zwei Aufrufe danach fanden 5 Wege, weil die Antworten
  im 24-h-Speicher lagen und die Suche mit denselben Abfragen 575 bzw. 840 Knoten weit kam): kommt eine Antwort mit `truncated`, startet
  `moves-editor` von selbst eine weitere Runde (derselbe GET), bis `MAX_ROUNDS` = 3 Runden (`core/position-setup.ts`), Fortschritt „Runde 2
  von 3 … (bisher n Wege)"; jede Antwort ersetzt die vorige und steht sofort da (sie ist vollständig nach `estGames` sortiert). Endet früher,
  sobald eine Runde nicht mehr `truncated` ist (oder `failed`). Danach mit `truncated` der Knopf „Weiter suchen" (eine Runde je Klick), bei
  0 Wegen „Stellung bekannt (≈ 146 Partien), aber in 3 Runden keine Zugfolge gefunden — tiefe Stellung; „Weiter suchen" setzt fort". Ein
  429 (Rate-Limit `explorer-paths`, 10/min — drei Runden passen) beendet die Runden mit Hinweis, der bisherige Stand bleibt, „Weiter suchen"
  bleibt. Jede Änderung der Stellung (`clearResult`) verwirft laufende Runden (`searchSeq`).
* **„Zugfolgen vorschlagen"** → `GET /api/explorer/paths` (Knopf nur, wenn `/api/repertoires/explorer/sources` `local` meldet; ein 400
  `noLocalExplorer` blendet ihn ebenfalls aus). Liste: Eröffnungsname, je Vorschlag die Zugfolge deutsch („1.e4 c5 2.Sf3 …",
  `formatMoves`), „≈ 2,8 Mio. Partien" (`formatGames`) und Anteil an den Partien der Stellung; Antippen übernimmt die Zugfolge in den Modus
  „Züge" (Speichern wie bisher). Keine Treffer: „Keine Zugfolge gefunden (…)". Hinweis unter dem Feld: die Zugzahl ist bei einer von Hand
  aufgebauten Stellung unbekannt, die Suche prüft bis 20 Halbzüge.
* **Suche** (`Services/Engines/ExplorerPathFinder.cs`, nur lokaler Explorer, Auswahl Lichess ab 1600, Blitz/Schnell/Klassisch): vorwärts von der
  Grundstellung, Knoten = (Stellung, Tiefe) — Zugumstellungen werden je Tiefe EINMAL erweitert, alle Vorgänger bleiben (jeder Pfad zählt).
  „Beste zuerst" nach Wahrscheinlichkeit (Produkt der Zuganteile) · 0,3 je noch nötigem Zug (`NeedWeight` 1,2 im Logarithmus), FLIESSEND
  bis zu 8 Abfragen gleichzeitig (eine langsame Abfrage — HDD, bis Sekunden — hält die anderen nicht auf). Seit 0.727.3 0,2 je nötigem Zug (`NeedWeight` 1,6, siehe Messung unten). Schranken: Anteil ≥ 1 %, die 8
  häufigsten Züge je Knoten; Halbzug-Grenze + Parität (Seite am Zug der Zielstellung); Felder-Abstand `diff > 2·Rest + 4` → weg; Material
  nimmt nur ab; Bauern gehen nicht zurück (Zuordnung jedes Zielbauern zu einem eigenen Bauern dahinter, Linienwechsel = Schlagzüge, die die
  Materialbilanz des Gegners hergeben muss); Zug-Untergrenze je Farbe (fehlende Zielfelder der Figuren + Bauernschritte, Rochade füllt zwei)
  ≤ eigene Züge bis zur Grenze. Umwandlungen sind ausgenommen. Ende, wenn die Schwelle jede offene Stellung SCHÄTZUNGSWEISE
  (`exp(Priority)` = Wahrscheinlichkeit · e^(−NeedWeight·nötige Züge)) schlägt, oder am Budget (600 Abfragen seit 0.727.3, vorher 400, bzw. 20 s — kalt bleibt die Zeit die harte Grenze, warm sind 600 Abfragen in Sekunden durch; `truncated`). Schwelle
  (`Cutoff`, 0.724.1): mit zehn Funden der zehntbeste, mindestens 0,1 % des besten (`MinRelative`); mit WENIGER Funden 0,5 % des besten
  (`MinShareOfBest`). **Abfragen, die noch unterwegs sind, zählen beim Abbruch NICHT mit** — die Suche startet nur keine neuen mehr
  und wartet auf sie (bis 0.723.1 zählten sie: hing eine Abfrage mit hoher Schätzung an der HDD, füllten die anderen sieben Plätze sich
  mit immer schlechteren Umwegen bis ins Budget — Prod Alapin kalt 243 Abfragen/20 s `truncated`, warm 3,8 s). Antworten 24 h in
  einem EIGENEN gedeckelten Speicher (keyed Singleton `ExplorerPathFinder.CacheServiceKey`, `CacheSizeLimit` 20 000 Stellungen à grob
  2–10 KB → höchstens ~200 MB, eine Suche braucht 70–300), dazu weiter 1 h unter dem Schlüssel des Lochfinders
  (`RepertoireExplorerService.LocalMemoryKey`, beide werden gelesen; der Lochfinder selbst bleibt bei 1 h). Rang: `estGames` = Partien(Grundstellung) · Π Anteil;
  `games` = Partien der Zielstellung, `opening` dessen Name (sonst der letzte benannte Zug des besten Wegs). Die Rochade kommt vom Explorer
  als König-schlägt-Turm (`e1h1`) und geht als `e1g1` zurück.
* **Client-Abbruch = 499, kein Error** (0.727.4, Prod 09.10.: `GET /api/explorer/paths responded 500` mit `TaskCanceledException` aus
  `LocalExplorerClient.GetAsync`): bricht der Browser ab (neue Stellung, neu geladen, Fortsetzungsrunde verworfen), bricht das
  Request-Token die laufenden Abfragen ab; `FindAsync` bricht dann ab, wartet die übrigen `inflight`-Abfragen ab (keine verwaisten Tasks)
  und wirft die `OperationCanceledException` weiter. Der global registrierte `Filters/ClientAbortedExceptionFilter` macht aus JEDER
  `OperationCanceledException` bei abgebrochenem `HttpContext.RequestAborted` 499 ohne Rumpf, Log nur Debug — gilt damit auch für
  `/api/explorer/position`, `/games` und den Lochfinder (`explorer-analysis`), die vorher genauso als 500 im Log standen. Das
  Budget-Token (`budget.CancelAfter`) bleibt `truncated` ohne Ausnahme; ein Abbruch OHNE `RequestAborted` (eigene Frist, HttpClient-Timeout)
  bleibt ein echter Fehler. Tests: `ClientAbortedExceptionFilterTests`, `ExplorerPathFinderTests.Find_RequestAborted_…`/`Find_TimeBudget_…`.
* **Seite am Zug** (0.724.1, Prod-Befund: Italienisch mit „Weiß am Zug" aufgebaut = 135 Tempoverlust-Partien statt 3 Mio. mit
  Schwarz): `movesMade`/`expectedSide` in `core/position-setup.ts` schätzen die Züge je Seite (Figur nicht auf einem Grundfeld
  ihres Typs = 1, Bauernschritte ab der Grundreihe mit Doppelschritt = 1, Kg1+Tf1 bzw. Kc1+Td1 zusammen 1): Weiß mehr → Schwarz am
  Zug, gleich → Weiß, Weiß weniger → „unmöglich" (Hinweis, Suche bleibt offen). Im Modus „Stellung" stellt jedes Setzen/Ziehen die
  Seite AUTOMATISCH um („Seite am Zug automatisch: Schwarz (Weiß hat 4 Züge gemacht, Schwarz 3)"); Umschalten von Hand oder eine
  eingefügte FEN hebt die Automatik für diese Stellung auf (dann Warnung `sideWarning`), Grundstellung/Brett leeren/Wechsel aus
  „Züge" schalten sie wieder ein. Server: bei weniger als 1000 Partien oder ohne Zugfolge EINE Abfrage mit der anderen Seite am Zug
  → `otherSideGames`; die Seite meldet „Mit Schwarz am Zug kennt der Explorer die Stellung (≈ 3 Mio. Partien)" mit dem Knopf
  „Seite wechseln und erneut suchen" (`otherSideHint`: bei 0 Partien ab einer, sonst ab zehnmal so vielen).
* **Palette** (0.724.1, Wunsch „König unter König"): zwei Reihen, oben Weiß K D T L S B, darunter Schwarz in derselben Folge,
  ✕ als eigene 7. Spalte über beide Reihen; Zellen mindestens 40 px.
* **Gewicht gemessen 0.727.3** (09.10.2026, lokaler Explorer, je Lauf frischer Speicher, ohne Budget; Live-Test
  `ExplorerPathFinderLiveTests.MeasureNeedWeight`, `ROOKHUB_TEST_EXPLORER_WEIGHTS`) — Abfragen bis zum ersten Fund / bis zum Ende:

  | Stellung | 0,8 | 1,2 (bis 0.727.2) | 1,6 (seit 0.727.3) |
  |---|---|---|---|
  | Alapin | 47 / 234 | 59 / 228 | 34 / 103 |
  | Najdorf | 81 / 245 | 39 / 166 | 35 / 129 |
  | Berlin | 44 / 92 | 81 / 91 | 38 / 74 |
  | QGD | 23 / 115 | 23 / 104 | 24 / 94 |
  | D00 (15 Halbzüge) | 1741 / 4416 | 498 / 1378 | 397 / 441 |
  | Summe | 1936 / 5102 | 700 / 1967 | 528 / 841 |

  Top-5 je Stellung bei allen drei Gewichten gleich (Alapin: Weg 6 mit 0,01 % fällt bei 1,6 weg). Das Gewicht wirkt auch aufs Ende
  (die Schätzung `exp(Priority)` gegen die Schwelle), deshalb sinkt dort mehr. D00 braucht kalt also weiter ~2 Runden.
* **Gemessen 0.724.1** (lokaler Explorer, warm, je Stellung frischer Speicher; Abfragen vorher → nachher): Alapin 349 → 140
  (Wege 1–5 gleich, Weg 6 mit 0,04 % entfällt), Berlin nach 3…Sf6 139 → 90, Najdorf 174 → 168, QGD nach 3…Sf6 98–181 → 150 (zehn
  Wege gleich); der alte Code war ordnungsabhängig und lief bei Wiederholung bis 400/`truncated`. Kalt (~12 Abfragen/s auf der HDD)
  heißt das für Alapin ~12 s statt Budget. Zweiter Lauf aus dem Speicher: 0–6 Abfragen.
* **Gemessen** (08.10.2026, Najdorf nach 5…a6, gegen den lokalen Explorer): warm 1,0 s / 331 Abfragen, 10 Wege, an der Spitze
  1.e4 c5 2.Sf3 d6 3.d4 cxd4 4.Sxd4 Sf6 5.Sc3 a6 (≈ 2,8 Mio., 89 %); kalt (Platte) ~10–20 Abfragen/s, der erste Lauf erreichte dort das
  20-s-Budget mit derselben Rangfolge. QGD nach 3…Sf6 120 Abfragen, Berlin nach 3…Sf6 213 (nur zwei Wege über 1 % je Zug).
* Tests: `ExplorerPathFinderTests` (gefälschter Baum: Sizilianisch über 1.e4 c5 2.Sf3 und 1.Sf3 c5 2.e4, Rangfolge, Zugumstellung,
  Parität, Abstandsschranke, Budget, Speicher, ungültige FEN, Controller 400, Rate-Limit; seit 0.724.1 Springer-Pendelzüge als wertlose
  Umwege, langsame Abfrage unterwegs, `otherSideGames`, 24-h-Speicher mit eigener Uhr; seit 0.727.3 `cached`, Vorgabe 600/1,6, `MaxQueries`-Deckel samt Folgerunden aus dem Speicher), `ExplorerPathFinderLiveTests` gegen den echten
  Explorer nur mit `ROOKHUB_TEST_EXPLORER_URL` (optional `…_FEN`, `…_MAXQ`; `Measure` misst Alapin/Najdorf/Berlin/QGD/Italienisch,
  `…_SHARE` setzt `MinShareOfBest`; `MeasureNeedWeight` vergleicht Gewichte); Frontend `moves-editor`- (seit 0.727.3 Runden: automatisch, Fortschritt, Zwischenwege, Ende, „Weiter suchen", 429, Abbruch bei neuer Stellung), `position-setup`- und
  `setup-board`-Specs (Pointer-Folgen, Tipp vs. Ziehen, Palette, gedreht, Rechtsklick, langer Druck).
