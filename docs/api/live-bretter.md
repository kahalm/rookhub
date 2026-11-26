# Live-Bretter der Turnierseite (1.014.0)

Wunsch 2026-11-25: bei Turnieren, denen man auf der Turnierseite folgt, die Bretter live einblenden, sobald es eine
**Lichess-Übertragung** (Broadcast) gibt — z. B. österreichische 1. Bundesliga 2026/27 (chess-results 1404438): Klick auf
eine Mannschaft zeigt deren sechs Bretter auf einer Seite.

## Endpunkte (`LiveBoardsController`, Route `api/live-boards` — bewusst NICHT unter `/api/tournaments`, siehe nginx)

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/live-boards/{id}` | **AllowAnonymous** (`directory-read`) | Verknüpfung + Runden (`LiveStatus`); ohne Verknüpfung sucht der Server einmal automatisch. 404 = Turnier nicht geholt |
| GET | `/api/live-boards/{id}/rounds/{round}/stream?eval=1` | **AllowAnonymous** (`directory-read`) + höchstens 6 gleichzeitige Ströme je IP (`SseConnectionLimiter`, `LiveBoards:MaxStreamsPerIp`, gesamt `LiveBoards:MaxStreams` 2000) | Server-Sent Events der Runde. 404 `live_not_linked` ohne Verknüpfung, 429 `live_too_many_streams` |
| PUT | `/api/live-boards/{id}/link` `{ url }` | Konto (`user-crawl`) | Von Hand verknüpfen: Turnier-, Runden- oder Partie-Link von `lichess.org/broadcast/…`. 400 `live_invalid_url`, 404 `live_broadcast_not_found` / `live_tournament_not_found` |
| DELETE | `/api/live-boards/{id}/link` | `tournaments.manage` | Verknüpfung lösen (falsch zugeordnet); danach sucht das nächste Lesen wieder |
| GET | `/api/live-boards/pgn/{roundId}/{gameId}` | **AllowAnonymous** (`directory-read`) | PGN einer Live-Partie (Kopfzeilen, alle bisherigen Züge, `[%clk]`; `application/x-chess-pgn`) für „Analysieren“ (1.026.0). Aus dem Hub (offener Strom bzw. 30 min gemerkte fertige Runde), sonst nur für Runden, die Lichess kennt, aus dem 60 s gecachten Runden-PGN. 404 `live_game_not_found` |
| POST | `/api/live-boards/views` `{ tournamentId, view }` | **AllowAnonymous** (`live-view-write`, 30/min) | Geteilte Ansicht anlegen (1.029.0) → `{ token, tournamentId, view, canEdit, editKey }`; `editKey` nur hier. 404 `live_not_linked` |
| GET | `/api/live-boards/views/{token}` | **AllowAnonymous** (`directory-read`) | Ansicht lesen; `canEdit` für diesen Aufrufer (Konto des Erstellers oder Header `X-Live-View-Key`). 404 `live_view_not_found` |
| PUT | `/api/live-boards/views/{token}` `{ view }` | **AllowAnonymous** (`live-view-write`), nur Besitzer | Ansicht ändern; alle offenen Ströme folgen. 403 `live_view_forbidden` |
| GET | `/api/live-boards/views/{token}/stream?eval=` | **AllowAnonymous** (`directory-read`) + Stromgrenzen wie oben | SSE der Ansicht: `view`, dann wie der Runden-Strom; bei Änderung erneut `view` + `snapshot`, `view-gone` wenn gelöscht |
| GET | `/api/live-boards/external?url=` | **AllowAnonymous** (`directory-read`) | „Brett hinzufügen“ aus einer FREMDEN Übertragung (1.025.0): Partie-, Runden- oder Turnier-Link → `{ roundId, roundName, tourName, url, ongoing, finished, gameId, games[] }`. 400 `live_invalid_url`, 404 `live_broadcast_not_found` / `live_ext_game_not_found`, 503 `live_lichess_busy` |

`{id}` ist die chess-results-Nummer wie in `/tournaments/:id`. Gestreamt wird eine
Runde der in `TournamentBroadcastLinks` verknüpften Übertragung (fremde Runden nur als `extra=`, siehe unten) (chess-results-Runde → Lichess-Runde: Zahl im Rundennamen,
sonst Reihenfolge, `LichessBroadcasts.RoundFor` / `TournamentLiveService.RoundsOf`).

### SSE-Format

```
retry: 5000

event: snapshot
data: {"state":"live","round":1,"layout":{…},"games":[…]}

event: game
data: {"id":"1pPEJGno","boardTag":"1.1","white":{"name":…,"elo":2616,"clockSeconds":4896,…},"fen":…,"lastMove":"f8e8","result":"*","eval":null,…}

event: layout        (eine neue Partie hat die Zuordnung geändert)
event: state         {"state":"live|connecting|reconnecting|finished"}
: ping               (alle 20 s)
```

`layout` = `LiveRoundLayout`: Mannschaftsturnier `matches[]` (Begegnung aus der chess-results-Paarung, `boards[]` mit
`gameId` + `homeIsWhite`), Einzelturnier `boards[]` (chess-results-Brett mit `gameId`), `unassigned[]` = übertragene
Partien ohne Brett.

## Weitere Bretter (1.025.0, Wunsch 2026-11-26)

„bau ein, dass ich auch noch weitere Bretter hinzufügen kann“ — und „auch aus ANDEREN Übertragungen: Schwaz-Mannschaftskampf
ansehen und daneben die WM-Partie live“.

- **Adresse** (Teil des geteilten Links, auch für Gäste):
  - `add=` — Bretter DERSELBEN Runde, Schlüssel nach der chess-results-Paarung bzw. Brettposition: Mannschaftsturnier
    `Begegnung.Brett` (`2.3` = Begegnung 2, Brett 3), Einzelturnier `Brett` (`7`), übertragene Partie ohne Brett (`unassigned`) ihre
    Lichess-Kennung. **Warum nicht die Lichess-Kennung?** Beim Einzelturnier gibt es das Brett schon vor Rundenbeginn (die Partie
    erscheint, sobald sie übertragen wird), und setzt der Übertrager die Runde neu auf (neue Kennungen), stimmt der Link weiter.
    Höchstens 24 (`LIVE_MAX_ADDED`). Nur mit `team`/`player` sichtbar — ohne Filter sieht man ohnehin alle Bretter.
  - `ext=roundId.gameId,…` — Bretter aus fremden Übertragungen (dort ist die Kennung ab dem Einfügen bekannt, eine chess-results-
    Paarung gibt es nicht). Höchstens 12 Bretter aus 4 Runden (`ExternalRoundRegistry.MaxBoardsPerStream`/`MaxRoundsPerStream`).
- **Strom**: `…/stream?extra=roundId.gameId,…` — EINE EventSource je Seite. Der Server meldet sich für jede fremde Runde am selben
  `BroadcastLiveHub` an (ein Lichess-Strom je Runde für alle Zuschauer, Leerlauf-Schließen wie gehabt) und reicht ihre Ereignisse im
  selben SSE-Strom weiter: `ext-snapshot { round, tourName, roundName, url, state, games }`, `ext-game { round, game }`,
  `ext-state { round, state }`, `ext-error { round, code }` (`live_ext_not_found` | `live_ext_limit`). Das Abo trägt einen
  **Partiefilter** (`Subscribe(…, only)`): nur die gewählten Partien gehen hinaus, und NUR für sie wird bewertet (Cloud/Engine) — eine
  Runde mit 100 Brettern rechnet für ein hinzugefügtes Brett nicht alle.
- **Missbrauchsschutz** (`ExternalRoundRegistry`, Singleton, keine DB): Runde muss Lichess bekannt sein (`/api/broadcast/-/-/{roundId}`,
  Treffer 5 min, Fehltreffer 10 min gecacht); serverweit höchstens `LiveBoards:MaxExternalRounds` (Vorgabe 10) verschiedene fremde
  Runden gleichzeitig (Zählung je Runde, eine schon beobachtete kostet keinen Platz); dazu wie bisher `SseConnectionLimiter` und das
  Rate-Limit `directory-read`. Link-Prüfung `LichessBroadcasts.BoardLinkOf` (nur Wirt lichess.org, Kennungen `[A-Za-z0-9]{8}`), im
  Browser gespiegelt `parseBroadcastLink`.
- **Oberfläche**: Rahmenknopf „Brett hinzufügen“ (`add`) in der Werkzeugzeile → Dialog (`LiveAddBoardDialogComponent`, 560 px): oben
  „Aus dieser Runde“ mit Suche (nur mit Filter), darunter „Aus einer anderen Lichess-Übertragung“ mit Link-Feld + „Laden“; ein
  Partie-Link wählt seine Partie gleich aus. Zeilen schalten um (☑), „Fertig“ schreibt `add`/`ext`. Hinzugefügte Bretter stehen hinter
  den Mannschafts-/Spielerbrettern (Karte mit Begegnung als Zusatzzeile und ✕), fremde mit „Turnier · Runde“ als Zusatzzeile; der
  Link zu Lichess und „Warte auf n Partie(n) …“ stehen in der Kopfleiste über dem Raster. Seit 1.028.0 alles in EINEM Raster (siehe
  Oberfläche).
- **Analysieren** (1.026.0, Wunsch 2026-11-26): an jeder Karte (eigene, hinzugefügte, fremde) ein Symbolknopf `biotech` → neuer Tab
  `https://rookhub…/analysis?live=roundId.gameId` (Partnerseite aus `partnerSiteUrl`, Prod ↔ Prod, Dev ↔ Dev; ohne Partner kein
  Knopf). Das Analysebrett holt das PGN über `GamesService.livePgn` und lädt es wie `?pgn=` (alle Züge nachspielbar, Kopfzeilen,
  Uhren) — auch ohne Anmeldung (Menüeintrag `analysis` = `All`). Kein PGN in der Adresse: mit Uhren wären es schnell > 8 KB.
  Der Hub hält dafür je Partie das zuletzt übertragene Roh-PGN (`Feed._pgn`, `BroadcastLiveHub.PeekPgn`).
- **Elo-Schnitt** (1.025.0): `LiveMatchSlot.homeElo/awayElo` + `…EloBoards` — Elo der Spieler an den Brettern der Begegnung, aus dem
  Strom, sonst aus der Startliste; ohne Elo zählt nicht. Im Kopf der Begegnung „Ø 2315“ unter dem Namen, Tooltip „Schnitt über n Bretter“.

## Geteilte, lebende Ansicht (1.029.0, Wunsch 2026-11-26)

„Wenn ich den Link share, nicht mit round=2&team=…&eval=1&ext=…, sondern einfach einen Hash — wenn ich Bretter ergänze oder
entferne oder etwas umstelle, sollen alle mit dem Link das auch sehen.“

- **Link** `/live/{token}` (Turnierseite, Route `live/:token`, dieselbe `LiveBoardsComponent`): Token 10 Zeichen base62
  (`RandomNumberGenerator`, ~59 bit). Die langen Adressen bleiben gültig.
- **Anlegen**: „Link teilen“ auf der langen Adresse → `POST …/views` mit der aktuellen Ansicht (Runde immer ausdrücklich, wie bisher
  im langen Link) → Bearbeitungsschlüssel (24 Zeichen) in `localStorage rookhub_live_view_keys` (`device`, höchstens 50, älteste raus;
  so kann auch ein Gast seine Ansicht weiter ändern) → Seite wechselt mit `replaceUrl` auf `/live/{token}` → kurzer Link wird geteilt.
  Scheitert das Anlegen (z. B. 429), wird die lange Adresse geteilt. In der Tabelle steht nur der SHA-256 des Schlüssels; angemeldet
  zählt zusätzlich das Konto (`OwnerUserId`). Nur für verknüpfte Turniere.
- **Besitzer** (`canEdit`): Runde, Mannschaft/Spieler, Bretter hinzufügen/entfernen und der Balken wirken sofort lokal und gehen
  entprellt (`LIVE_VIEW_SAVE_MS` 400 ms, mehrere Klicks = ein PUT) als `PUT …/views/{token}` mit Header `X-Live-View-Key`. Bis dahin
  überschreibt ein `view`-Ereignis die eigene Änderung nicht. Hinweiszeile „Geteilte Ansicht — Änderungen sehen alle mit dem Link.“
- **Zuschauer** (schlank entschieden): sie folgen der Ansicht. Ihr Balken-Schalter gilt nur für sie (Strom mit `eval=`, nicht
  gespeichert, die Ansicht überschreibt ihn nicht). Jede ANDERE Änderung (Runde, Mannschaft, Brett) macht daraus ihre eigene Ansicht:
  Sprung auf die lange Adresse mit der Änderung — die geteilte bleibt unberührt. Hinweiszeile sagt das.
- **Strom** `…/views/{token}/stream`: EINE Verbindung je Zuschauer. Der Server liest die Ansicht, schickt `view` und streamt die Runde
  wie `…/rounds/{n}/stream` (Filter, `add`, `ext` aus der Ansicht). Ändert der Besitzer sie, weckt `LiveViewChanges` (Singleton, im
  Speicher — die API läuft als eine Instanz) alle offenen Ströme dieser Kennung: sie beenden die Abos am Hub, lesen neu und hängen sich
  um (neuer `view` + `snapshot`, ggf. `ext-snapshot`). KEIN zusätzlicher Lichess-Strom: der `BroadcastLiveHub` hält je Runde einen,
  ein Rundenwechsel nutzt dessen 90-s-Nachlauf. Runde ohne Übertragung: `state offline`, dann Warten auf die nächste Änderung mit
  Lebenszeichen. Gelöscht/abgelaufen: `view-gone` → Hinweis „Diese geteilte Ansicht gibt es nicht mehr.“
- **Speicherdauer**: 30 Tage ohne Aufruf (`Retention:LiveViewDays`), Deckel 50 000 (`Retention:LiveViewCap`), Aufräumlauf
  `AnonymousDataRetentionService`; „zuletzt gesehen“ wird höchstens stündlich nachgeführt (`SharedLiveViews.TouchEvery`). Konto
  gelöscht → seine Ansichten auch (`TournamentDataEraser`). Vorschau: `docs/api/link-vorschau.md`.

## Ablauf im Server (`Services/Tournaments/Live/`)

- **Finden** (`BroadcastFinder`, Singleton): `/api/broadcast/top` (10 min gecacht) — steht in `info.standings`/`website`
  `tnr{id}`, ist es diese (die Bundesliga trägt `tnr1404438`). Sonst nur für ein gerade laufendes Turnier (Termin um heute,
  ohne Termin zuletzt vor ≤ 3 Tagen geholt) die Lichess-Suche mit Wörtern des Namens (AUT → Austria, Jahreszahlen weg; die
  Suche verlangt ALLE Wörter) und bei höchstens drei Kandidaten der FIDE-Abgleich der zuletzt begonnenen Runde (≥ 4 IDs der
  Startliste, ≥ Hälfte der Runde). Kein Treffer → 6 h Ruhe je Turnier.
- **Strom** (`BroadcastLiveHub`, Singleton): je Lichess-Runde mit Zuschauern GENAU EINE Verbindung zu
  `GET /api/stream/broadcast/round/{id}.pgn`. Erster Zuschauer → einmal `/api/broadcast/round/{id}.pgn` (Vollstand), dann
  der Strom; Lichess schickt nach dem Verbinden alle Partien noch einmal — Unverändertes (gleicher Hash) geht nicht hinaus.
  Abriss → neu verbinden, Pause 2 s … 60 s, nach 429 90 s für alle Abrufe. Letzter Zuschauer weg → Strom nach 90 s zu (ein
  Neuladen reißt ihn nicht ab). Schließen und Entfernen des Feeds geschehen atomar unter dem Hub-Lock, Anmelden
  ebenso (Sperrreihenfolge Hub → Feed) — ein Zuschauer gerät nie an einen sterbenden Feed (1.014.1). Fertige Runden (Lichess `finished` oder alle Partien mit Ergebnis): kein Strom, die
  Endstellungen bleiben 30 min im Speicher.
- **Zerlegen** (`BroadcastPgnSplitter`, `LiveGameParser`): Partien aus beliebigen Stücken, fertig beim Ergebnis-Token; je
  Partie FEN (Gera.Chess), letzter Zug (UCI), Uhren aus `[%clk]` (letzter Wert je Seite), `[%eval]`, Namen/Elo/Titel/FIDE-ID/
  Mannschaft aus den Kopfzeilen.
- **Zuordnung** (`LiveBoardAssembler`): FIDE-ID (Startliste ↔ `WhiteFideId`/`BlackFideId`), Rückfall Name (Wörter sortiert,
  ohne Akzente). Einzel: Paarung der Runde (Namen → Startliste). Mannschaft: der Crawler kennt je Runde nur die Begegnungen —
  die Partie gehört zur Begegnung der beiden Mannschaften der Spieler (Startliste, sonst `WhiteTeam`/`BlackTeam`). Innerhalb
  der Begegnung ordnet der Lichess-Tag `Round "1.n"`: die Übertragung nummeriert blockweise je Begegnung in Brettreihenfolge
  (1. BL R1: „1.1"–„1.6" Pinggau–Schwaz, „1.7"–„1.12" nächste Begegnung …, Farben abwechselnd); nur ohne Tag die
  Meldelisten-Nummer (1.014.1). Brettnummer = Position in dieser Ordnung. Einzelturnier: Brettnummer = chess-results.
- **Engine-Balken** — Rückfallkette je Stellung, nur solange ein Zuschauer der Runde ihn an hat (`?eval=1`):
  1. `[%eval]` aus dem Strom (die Bundesliga schickt KEINS, gemessen 25.11.2026, nur `[%clk]`);
  2. Lichess Cloud-Eval (`LichessCloudEval`: je Stellung gecacht, Treffer 6 h, Fehltreffer 15 min, ein Abruf je Sekunde,
     nach 429 Pause) — kennt Mittelspielstellungen meist nicht;
  3. **Hintergrund-Engines** (seit 1.027.0; vorher 1.015.0 eine einzelne „Live-Engine“ mit 1,5 s). Befund 26.11.: auf Prod
     rechnete die Live-Engine NIE — die Auswahl nahm ohne Einstellung das erste Admin-Konto (Id 2, keine Engines), alle Engines
     gehören Id 5; im Strom kamen nur Cloud-Werte. Wünsche des Users: „nur die Bretter rechnen, die jemand ansieht“, „kannst du
     die nicht einfach in die Hintergrundengines werfen?“, „Stellungen nur bis Tiefe 30 rechnen“.
     - **Nur Sichtbares**: der Strom bekommt den Filter der Ansicht (`team`, `player`, `add`; `LiveView.Visible`), das Abo
       (`RoundSubscription.SetVisible`) bewertet nur diese Bretter, mit Filter erst ab der Zuordnung (vorher nichts). Ohne
       Filter alle Bretter der Runde, fremde Bretter (`ext`) ohnehin nur die gewählten. Bedarf = Vereinigung über alle
       Zuschauer mit Balken; Cloud-Eval genauso nur für Angezeigte.
     - **Cloud genügt ab Tiefe 30**: eine Cloud-Bewertung wird sofort gezeigt; ist sie ≥ `Depth` (30), rechnet keine Engine.
     - **Wo**: `LiveEngineQueue` (Singleton, über ALLE Runden) verteilt auf die Hintergrund-Engines des Kontos der
       Analyseaufträge (`RegistryLiveEnginePoolSource`: `OwnerUserName`, sonst die Haus-Engine `EngineOwnerResolver.HouseOwnerAsync`,
       sonst das erste Admin-Konto MIT Hintergrund-Engines; nur online stehende `rhe_`-Engines der Hintergrund-Liste, ohne
       `AnalysisJobs:ExplicitOnlyEngineIds`; Liste 30 s gecacht). PARALLEL: je freie Engine eine Stellung, reihum über die
       Bretter, gedeckelt durch `MaxParallel` (0 = alle).
     - **Neben den Analyseaufträgen** (`Services/Engines/LiveEngineLane.cs`): eine Engine ist frei, wenn dort KEIN Auftrag
       rechnet und niemand am Analysebrett live darauf rechnet — ein laufender Auftrag wird nie abgewürgt. Warten Live-Stellungen,
       beansprucht die Schlange die Engines (`Claim`): der `AnalysisJobWorker` startet dort keinen NEUEN Auftrag (Vorrang vor
       allen Stufen von `JobPriority`), und endet einer, weckt er die Schlange (`JobEnded`). Die Live-Rechnung meldet sich beim
       `EngineActivityTracker` an (Admin-Seite „Engines“ zeigt sie als Live) und ab OHNE Ruhephase (`End(…, grace: false)`) —
       ist die Stellung fertig, darf der Worker sofort wieder. Zum Datenbank-Auftrag (`AnalysisJobs`) wurde sie bewusst nicht:
       je Zug eine Zeile anlegen/löschen, Takt des Workers als Verzögerung, kein Abbruch beim nächsten Zug und keine
       Zwischenstände ohne eigenen Weg.
     - **Je Brett nur die neueste Stellung**: ein neuer Zug ersetzt den wartenden Wunsch an seinem Platz und bricht die laufende
       Rechnung des alten ab; sieht niemand das Brett mehr mit Balken (Filter, Balken aus, Seite zu), bricht sie beim nächsten
       Zwischenstand ab.
     - **Zwischenstände**: `BrokerLiveEngineEvaluator` rechnet bis `depth` 30 (1 Linie, Threads/Hash wie die Hintergrund-Aufträge
       = was die Engine anbietet, Sitzung `rh-live-boards`, harte Grenze `MaxSeconds` 180 s — das Erreichte gilt); jede tiefere
       Zeile geht als Bewertung `source: "engine", depth: n` an den Balken (höchstens alle 0,7 s je Brett), tiefer ersetzt flacher.
       Ergebnis je Stellung 6 h gecacht (`IsSettled`), auch für andere Bretter/Runden mit derselben Stellung.
     - **Wie viele parallel**: so viele, wie Hintergrund-Engines frei sind (Prod: alle online stehenden Engines der Hintergrund-
       Liste von kahalm, ohne Lc0). Rechnen dort gerade Partieanalysen, kommen die Live-Bretter erst dran, wenn ein Auftrag endet
       — dann aber vor jedem neuen Auftrag; bei vielen sichtbaren Brettern (z. B. ganze Runde ohne Filter) läuft die
       Partieanalyse so lange langsamer. Liefert eine Engine nichts (Provider weg), wird sie 60 s ausgelassen.
  Quelle im Eval: `source` = `broadcast` | `cloud` | `engine`, dazu `depth`; die Karte zeigt „+0.4 · Tiefe 18“ und die
  Quelle als Tooltip.

  **Einstellungen** (`LiveBoards:Engine:*`, `LiveEngineOptions`):

  | Schlüssel | Vorgabe | Bedeutung |
  |---|---|---|
  | `Enabled` | `true` | aus = nur Strom + Cloud |
  | `OwnerUserName` | Haus-Engine, sonst erstes Admin-Konto mit Hintergrund-Engines | wessen Engines |
  | `EngineIds` (Liste, auch `EngineId`) | leer | nur diese Engines (online, `rhe_`); leer = die Hintergrund-Liste ohne Nur-auf-Anforderung |
  | `Depth` | 30 | Zieltiefe (10–60); Cloud mindestens so tief → keine Engine |
  | `MinShowDepth` | 20 | Zwischenstände erst ab dieser Tiefe an den Balken (vorher schlägt er nach einem Zug kurz aus); bis dahin hält die Karte den letzten Wert (1.027.1) |
  | `MaxSeconds` | 180 | harte Grenze je Stellung (10–1800) |
  | `MaxParallel` | 0 | höchstens so viele Bretter gleichzeitig (0 = alle freien Engines) |
  | `Threads` / `HashMb` | 0 / 0 | 0 = was die Engine anbietet (wie die Hintergrund-Aufträge) |
- Lichess-Abrufe mit dem User-Agent des übrigen Lichess-Codes (`HttpClientsSetup`, Client `lichess-live`, ohne
  Gesamt-Timeout für den Strom), ohne persönliche Daten.

Fertige Partie mit Signal-Königszug des Siegers am Ende (DGT-Brett, 1.022.0): `LiveGameParser.Parse` lässt ihn weg, das Brett
zeigt die Stellung davor — Regel in `Services/Games/SignalKingMove.cs` (siehe `docs/api/leaguehub-vereins-datenbank.md`).

## Proxy-Kette

- Turnier-nginx (`src/frontend/nginx.conf`): eigene `location ^~ /api/live-boards/` mit `proxy_buffering off`,
  `proxy_cache off`, 3600 s Zeitschranken und `proxy_pass_header X-Accel-Buffering` (sonst verschluckt nginx den Kopf und der
  Nginx Proxy Manager davor puffert). `DeploymentConfigTests.LiveBoardsLocation_…` hält das fest.
- API: `text/event-stream` steht nicht in den Typen der Response-Compression, der Controller setzt
  `Cache-Control: no-cache, no-transform`, `X-Accel-Buffering: no` und schaltet das Puffern ab (`DisableBuffering`).

## Oberfläche (Turnierseite, `features/tournaments/live/`)

- Seite `/tournaments/:id/live?round=&team=&player=` (`LiveBoardsComponent`), auch ohne Konto. Detailseite: Knopf
  „Live-Bretter“ in der Aktionsleiste, sobald verknüpft; sonst ⋮ → „Übertragung verknüpfen“. Paarungen: „Live-Bretter dieser
  Runde“ neben der Rundenwahl. Mannschaftsfenster: „Live-Bretter“ (Mannschaft + ggf. Runde), sonst leiser Hinweis.
  Begegnungen eines Spielers (Einzelturnier): „Live-Bretter“ mit `?player=`.
- Mannschaft gewählt: nur ihre Begegnung, Bretter aus ihrer Sicht (sie steht unten), Stand oben (chess-results, solange
  leer: Summe der Lichess-Ergebnisse). Ohne Wahl alle Begegnungen aus Sicht der Heimmannschaft.
- **Ein Raster** (1.028.0, Wunsch 2026-11-26 „ich hätt die Bretter gern in einer Wurscht, damit ich 8 auf einmal gut sehe“):
  `LiveBoardsComponent.allBoards` reiht Begegnung(en)/Runde, hinzugefügte (`add`), fremde (`ext`) und weitere übertragene Partien
  in EINE Folge, gezeichnet in einem `.board-grid` (`auto-fill, minmax(min(100%, 240px), 1fr)`, Seite bis 1880 px): 1920 px → 7
  Spalten, 1280 px → 4–5, 390 px → 1. Herkunft je Karte als Zusatzzeile; Stand/Elo-Schnitt je Begegnung und Links der fremden
  Runden als kompakte Kopfleiste darüber.
- **Zugnummer** (1.028.0, Wunsch „zeig auch irgendwo die aktuelle Zuganzahl an“): im Kartenkopf „Zug n“ aus `plies`
  (`liveMoveNumber`: laufend `floor(plies/2)+1` = der Zug, der gerade gespielt wird, fertig `ceil(plies/2)`, vor dem ersten Zug
  nichts), Tooltip wer am Zug ist.
- Brett = geteiltes `app-chess-board` (nur Anzeige; ein Klick tut vorerst nichts), letzter Zug markiert, Uhren (die Seite am
  Zug zählt sekündlich herunter — Näherung ab dem Eintreffen; im E-Ink-Modus steht sie), Ergebnis als Chip.
- Engine-Balken: Schalter in der Kopfzeile, gemerkt je Gerät (`localStorage rookhub_live_eval`, `device`) und seit 1.024.0
  zusätzlich in der Adresse (`?eval=1|0`, `replaceUrl`). Steht `eval` in der Adresse (geteilter Link), gilt das vor der
  Gerätevorliebe, ohne sie zu überschreiben. Gäste dürfen den Balken nutzen (der Strom ist `AllowAnonymous`, auch mit `eval`).
- **Link teilen** (1.024.0, Wunsch 2026-11-26: „einen speziellen Livestream … auch für anonyme User sharen“): Knopf im Kopf,
  Link = `/tournaments/{id}/live?round=n[&team=…|&player=…][&eval=1]` — die Runde immer ausdrücklich (ohne sie zeigte der Link
  am nächsten Spieltag die nächste Runde). Am Handy Teilen-Blatt, sonst Zwischenablage. Gast-Prüfung 26.11. im echten
  Headless-Browser gegen Prod: keine Umleitung zur Anmeldung, keine 401, Mannschaft aus `?team=` und Runde aus `?round=`
  greifen; einziger Fehler war `eval=1` → 400 (behoben 1.023.1). Vorschau im Messenger: `docs/api/link-vorschau.md`.
- Strom im Browser: `EventSource` (Chrome 111 kann es), `LiveBoardsService.stream` — gibt der Browser die Verbindung auf
  (404/429), neuer Versuch nach 3 s / 10 s / 30 s. Seit 1.025.0 mit `ngsw-bypass=1`: im Prod-Build lief der Strom sonst durch den
  Service Worker (ngsw reicht jede nicht gecachte Anfrage per `respondWith` durch), Chrome beendet einen untätigen Worker nach ~30 s —
  im Prod-Log endeten die Browser-Ströme nach 29–60 s, während der Server-Strom > 90 s hielt. Ein Abriss bei schon geladenen
  Brettern wird erst nach 10 s angezeigt (`LIVE_LOST_GRACE_MS`): bis dahin bleiben „Live“ und die Bretter mit dem letzten Stand stehen.

## Tests

`tests/RookHub.Api.Tests/Tournaments/Live/` (Zerleger in Stücken, Parser %clk/%eval, Zuordnung, Hub-Lebenszyklus, Finder,
Controller-Zugriff), `LichessLiveSmokeTests` liest mit `ROOKHUB_LIVE_LICHESS=1` den echten Bundesliga-Strom ein paar
Sekunden; Frontend `live-boards.*.spec.ts` (EventSource gemockt).
