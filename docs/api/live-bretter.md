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

`{id}` ist die chess-results-Nummer wie in `/tournaments/:id`. **Keine Lichess-Kennung von außen**: gestreamt wird nur eine
Runde der in `TournamentBroadcastLinks` verknüpften Übertragung (chess-results-Runde → Lichess-Runde: Zahl im Rundennamen,
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
  3. **eigene Engine** (1.015.0, Freigabe des Users) über den vorhandenen `IEngineBroker` — kein neuer Engine-Weg
     (`LiveEngineEval.cs`). `LiveEngineQueue` (Singleton, über ALLE Runden): höchstens EINE Rechnung gleichzeitig, je Brett
     nur die neueste Stellung (ein neuer Zug ersetzt den wartenden Wunsch an seinem Platz), reihum über die Bretter; vor dem
     Rechnen und vor dem Übernehmen wird geprüft, ob die Stellung noch gebraucht wird (Zuschauer mit Balken, Partie steht
     noch dort, Feed offen). `BrokerLiveEngineEvaluator`: 1 Linie, `movetime` 1,5 s, 2 Threads, 64 MB, harte Grenze 6 s,
     Sitzung `rh-live-boards`; meldet sich beim `EngineActivityTracker` als Live-Rechnung an (ein Hintergrund-Auftrag auf
     DERSELBEN Engine pausiert, andere laufen weiter); Ergebnis je Stellung 6 h gecacht, UCI-Wert (Seite am Zug) in
     Weiß-Sicht gedreht. Ist keine Engine konfiguriert/online → still leer (Auswahl 60 s gecacht, 503 wählt neu).
  Quelle im Eval: `source` = `broadcast` | `cloud` | `engine`, dazu `depth`; die Karte zeigt „+0.4 · Tiefe 18“ und die
  Quelle als Tooltip.

  **Welche Engine** (`LiveBoards:Engine:*`, `RegistryLiveEngineSelector`):

  | Schlüssel | Vorgabe | Bedeutung |
  |---|---|---|
  | `Enabled` | `true` | aus = nur Strom + Cloud |
  | `OwnerUserName` | erstes Admin-Konto (kleinste Id) | wessen Engines |
  | `EngineId` | leer | feste Engine (`rhe_…`, auch `eei_…`); leer = erste ONLINE eigene `rhe_`-Engine des Kontos, die NICHT in seiner Hintergrund-Liste steht (= seine Live-Engine) — die Hintergrund-Engines der Analyseaufträge bleiben ungestört |
  | `MovetimeMs` | 1500 | Rechenzeit je Stellung (200–10000) |
  | `Depth` | 0 | statt der Zeit eine feste Tiefe (z. B. 18); `MaxSeconds` bricht trotzdem ab |
  | `MaxSeconds` | 6 | harte Grenze je Stellung |
  | `Threads` / `HashMb` | 2 / 64 | gedeckelt auf das, was die Engine anbietet |

  Prod: ohne Einstellung rechnet die Live-Engine des ersten Admin-Kontos, sobald ihr Provider online ist. Teilt sich der
  Admin diese Engine mit seinem eigenen Analysebrett, stellt der Broker die Suchen hintereinander (eine Suche je Prozess).
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
  (404/429), „Verbindung unterbrochen“ und neuer Versuch nach 3 s / 10 s / 30 s.

## Tests

`tests/RookHub.Api.Tests/Tournaments/Live/` (Zerleger in Stücken, Parser %clk/%eval, Zuordnung, Hub-Lebenszyklus, Finder,
Controller-Zugriff), `LichessLiveSmokeTests` liest mit `ROOKHUB_LIVE_LICHESS=1` den echten Bundesliga-Strom ein paar
Sekunden; Frontend `live-boards.*.spec.ts` (EventSource gemockt).
