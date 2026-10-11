# Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?"

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?"“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-09-29: „soll diese durchrechnen und schaun, warum Zug 1 besser ist als Zug 2 — vor allem im Vergleich: was
sind bei Zug 2 die besten Züge, und warum gehen die bei Zug 1 nicht (so gut)". `Services/Engines/MoveComparisonService.cs`,
Tabellen `MoveComparisons` + `MoveComparisonLines`, Pumpe `MoveComparisonPumpService` (5 s, `MoveComparison:PumpIntervalSeconds`).
* **Zwei Durchgänge über die Hintergrund-Aufträge** (NICHT die Live-Engine — rechnet auch, wenn die Seite zu ist):
  (1) je Kandidat (2–4) die Stellung DANACH mit `CandidateLines` (3) Linien = die besten Antworten des Gegners; daraus
  der beste Kandidat (`BestUci`, Sicht der Seite am Zug, Matt ± 100 000). (2) Je SCHWÄCHEREM Kandidaten dessen
  `RepliesTested` (3) beste Antworten, gespielt nach dem BESTEN: geht die Antwort dort nicht → `Illegal` (ohne Auftrag,
  selbst eine Auskunft), sonst ein Auftrag mit 1 Linie = die eigene Erwiderung und die Bewertung. Matt/Patt auf dem Brett
  braucht keinen Auftrag (`Terminal`).
* **Engine wie die Punktepartie** (`Services/Engines/EngineOwnerResolver.cs`, von `GameAnalysisService` mitbenutzt): eigene
  Hintergrund-Engine, sonst die Haus-Engine; auf der Haus-Engine höchstens `HouseMaxDepth` 24 (eigene bis 30, Vorgabe 22).
  Die Aufträge sind normale (nicht `Background`) — sie verdrängen Vertiefung/Meisterpartien. `remember: false`, und
  `AnalysisJobService.ListAsync` blendet Aufträge eines Vergleichs aus. **Fertige Aufträge werden eingesammelt**: Ergebnis
  in die Zeile (`ResultJson`, `ReachedDepth`), Auftrag GELÖSCHT — sonst stünden sie in der Liste und fielen irgendwann dem
  Trimmer (`MaxJobsPerUser`) zum Opfer. Ein verschwundener Auftrag macht die Zeile `Failed`, nicht den Vergleich.
* **Begründung** je schwächerem Kandidaten nur mit Modell auf eigener Hardware (`IsLocal`), im Hintergrund
  (`MoveComparisonExplainJobs` gegen Doppelstarts, ein Neustart lässt die Pumpe neu anstoßen). Fakten wie bei „Warum war das
  ein Fehler?" (`ExplanationFacts`: `FirstMoveAttacks`, `LineEvents` — die Bilanz der Antwort-Linie ab der Stellung VOR dem
  schwächeren Zug samt diesem, die der eigenen Erwiderung ab der Stellung nach dem besten Zug samt der Antwort), Stufen in
  Worten (`LevelName`), „fast gleich" unter 0,3 Bauern; Leser = die Seite am Zug. Genannte Züge nur aus den Linien
  (`MentionsOnly`, eine Nachfrage), gespeichert in englischer SAN, `PieceLetters` beim Lesen. Ohne Modell endet der
  Vergleich ohne Text; ein unbrauchbarer Text lässt ihn trotzdem `Done` werden (sonst hinge er auf „wird erklärt").
* Deckel: `MaxOpenPerUser` 3 laufende, `MaxKeptPerUser` 50 gespeicherte (älteste fertige gehen beim Anlegen).
* Frontend: ⋮-Menü der Stellung (`position-menu.component.ts`, angemeldet immer — ob eine Engine bereitsteht, sagt der
  Dialog) → `move-compare-dialog.component.ts` (Engine-Vorschläge des Analysebretts vorausgewählt, `[candidates]`, dazu
  jeder legale Zug) → Seite `/analysis/compare/:id` (`move-comparison.component.ts`, fragt alle 3 s nach, solange es
  läuft; Brett aus Sicht der Seite am Zug, Klick auf Zug/Antwort/Erwiderung stellt die Stellung auf).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/move-comparisons/status` | `{ engineAvailable, ownEngine, explanations, maxCandidates, defaultDepth, maxDepth, openComparisons, maxOpen }` (Literal vor `{id}`) |
| GET | `/api/move-comparisons` | Die letzten 20 eigenen (`moves` als SAN, `bestSan`) |
| GET | `/api/move-comparisons/{id}` | Stand: `status` (`candidates`/`replies`/`explaining`/`done`/`failed`), je Kandidat `evalText` (WEISS-Sicht), `replies` (beste Antworten, Linie als SAN), `tests` (dieselben nach dem besten Zug: `state` `pending`/`done`/`failed`/`illegal`, `line` = eigene Erwiderung), `explanation`; beste zuerst, solange gerechnet wird Reihenfolge der Auswahl. Laufende Zeilen zeigen den Zwischenstand ihres Auftrags |
| POST | `/api/move-comparisons` | `{ fen, moves[] (UCI, Rochade auch König-schlägt-Turm), depth?, lang?, title? }`; 400 `reason` ∈ invalid-fen/game-over/invalid-move/too-few-moves/too-many-moves/too-many-open/no-engine/too-many-jobs |
| DELETE | `/api/move-comparisons/{id}` | Löschen samt offener Aufträge |

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
(`Services/Engines/NdjsonHeartbeatPump.cs`): schweigt der Broker 20 s, geht eine Leerzeile raus. Grund: bei
MultiPV 5 liegen ab Tiefe ~27 Minuten zwischen zwei Zeilen, und NPM (Default `proxy_read_timeout`
60 s) kappte den Stream, den der Browser dann als „fertig" wertete (Prod-Log: Streams mit exakt ~61 s,
Anzeige bleibt bei Tiefe 27 stehen). Der Client-Parser ignoriert Leerzeilen und Steuerzeilen ohne `pvs`
(das `{"keepalive":true}` des Providers — durchgelassen stünde die Anzeige alle 15 s bei Tiefe 0 ohne
Linien). Reißt ein Stream trotzdem
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
und `preflight.py` (prüft den Token via `POST /api/token/test` VOR dem Start). **Seit 0.537.0 zeigt `ROOKHUB_URL`
beide Basen (`--lichess`, `--broker`) auf RookHub** und `ROOKHUB_API_TOKEN` ist der Token dafür (eigener
Broker, siehe oben; seit 0.538.0 hat er mit `ROOKHUB_URL` VORRANG vor einem daneben stehenden `LICHESS_API_TOKEN`,
und ohne `ROOKHUB_URL` ist er ein Abbruch mit Klartext — bei Lichess gilt er nicht); ohne `ROOKHUB_URL` läuft alles
wie bisher über Lichess. **EIN Container bedient EINEN Broker; beide Wege gleichzeitig sind ZWEI Container** (0.538.0,
Wunsch 2026-09-26 „lichess & eigener Broker", bewusst nicht ein Container, der beides kann): `compose.yml` hat den
zweiten Dienst `engine-provider-lichess` am Profil `lichess` mit eigener `.env.lichess` (Vorlage
`.env.lichess.example`, OHNE `ROOKHUB_URL`); `docker compose --profile lichess up -d` bzw. dauerhaft
`COMPOSE_PROFILES=lichess` in der `.env`. Compose braucht die `.env.lichess` nur bei aktivem Profil (geprüft). Über
Lichess gehört nur die Live-Engine, die Hintergrund-Engines in den direkten Container (IP-Drosselung). In RookHub
stehen Engines beider Quellen ohnehin in EINER Liste (`GET /api/engine/external`, `EngineRegistry`), die
Hintergrund-Liste darf `rhe_` und `eei_` mischen. **Das Lebenszeichen patcht das
Image seit 0.478.11 nicht mehr hinein** (bis dahin `patch_provider.py`): der gepinnte Stand (`d0eeb242`, 2026-09-06) erfüllt die zwei Regeln
des Brokers, an denen RookHub hängt, und `test/provider.test.py` prüft sie gegen einen nachgebauten Broker.

1. **Jede Suche endet mit `bestmove`.** Der Broker verlangt das seit lila-engine `0e1223b` (2026-09-06) und
   antwortet sonst `400 uci protocol error: expected bestmove before end of stream`. Der vorher gepinnte
   Provider (`a6ef15a8`) schickte nie eins, bekam die 400 bei JEDER Suche, schlief danach 5 s und holte erst
   dann den nächsten Auftrag. Gemeldet als „es dauert relativ lang, bis die Cloud-Engine anspringt": ein Zug
   eine Sekunde nach einer beendeten Suche wartete 4,1 s auf die erste Zeile (gegen den echten Broker
   gemessen, 2026-09-15). Die 400 stand schon vorher im Provider-Log, galt aber als Folge gekappter
   Verbindungen — so blieb die Verzögerung neun Tage unbemerkt.
2. **Lebenszeichen alle 15 s** (`{"keepalive":true}`). Der Provider reicht nur `info`-Zeilen MIT `score`
   weiter, zwischen zwei tiefen MultiPV-Iterationen vergehen Minuten, und der Broker kappt nach 60 s Stille —
   bei uns sichtbar als `HttpIOException: The response ended prematurely`; Aufträge kamen nie über Tiefe 29
   hinaus, 23 hingen auf Dev bei Tiefe 20/22. Bis 0.478.10 lieferte das ein eigener Eingriff
   (`patch_provider.py`: Wiederholung der letzten `info`-Zeile — eine Leerzeile verwirft der Broker). Dieselbe
   Klasse Fehler wie der `NdjsonHeartbeatPump` auf der Strecke API→Browser, nur einen Hop weiter vorne. Das
   Keepalive kommt beim Empfänger als eigene ndjson-Zeile an: der Worker zählt es als Lebenszeichen
   (`StreamTally.IsKeepalive`), der Browser verwirft es im Parser (`ExternalEngineService.analyse`).

**Ein Eingriff bleibt (0.535.1, `patch_force_close.py`)**: der asynchrone Provider lädt Suchen über eine
aiohttp-Sitzung mit Verbindungs-Pool hoch; eine inzwischen von der Gegenseite geschlossene Pool-Verbindung
fällt erst beim nächsten Upload auf — Auftrag schon abgeholt, Engine hat `go`, Upload stirbt im ersten Byte,
der Anfragende bekommt nach 15 s einen 503 (upstream Issue #45, per A/B belegt). `TCPConnector(force_close=True)`
für die Upload-Sitzung (nicht für den Poll) behebt es; `test/provider.test.py` verlangt drei Uploads auf drei
Verbindungen, die CI wendet den Patch vor dem Test an (`DeploymentConfigTests` hält die Zeile fest). Weg damit,
sobald upstream es behebt.

**Wer den Pin anhebt, lässt `test/provider.test.py` laufen** (die CI tut es): die Regeln des Brokers ändern
sich, ohne dass ein laufender Provider davon erfährt. Gegen den alten Stand scheitert der Test mit genau den
gemeldeten vier bis fünf Sekunden. **Reihenfolge beim Ausrollen**: erst das Frontend mit dem Parser-Filter,
dann den Provider — ein älteres Frontend reicht jedes Keepalive als leeres Ergebnis an die Anzeige weiter.

Zwei Fallen, die dort
bewusst adressiert sind: der Provider-Token braucht `engine:read` **und `engine:write`** (er
REGISTRIERT die Engine; RookHub selbst genügt `engine:read`; ein RookHub-Token mit Scope `engine` meldet beides) — ohne Vorabprüfung endete das in einem
401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt; und die Registrierung
wird über den **Namen** identifiziert (gleicher Name = Aktualisierung, zwei Rechner brauchen zwei
Namen, sonst überschreiben sie sich). **`ENGINE_COUNT` (0.378.0)**: ein Container kann mehrere
Provider = mehrere registrierte Engines fahren (`ENGINE_<i>_NAME/_MAX_THREADS/_MAX_HASH` je Engine,
sonst `ENGINE_NAME <i>`) — gedacht als „Server Live" + „Server Hintergrund" für die Hintergrund-
Analyseaufträge; beide dürfen alle Kerne haben, weil RookHub den Hintergrund pausiert, sobald Live
rechnet. Stirbt ein Provider, endet der Container mit dessen Code (restart zieht alle neu). Der
Entrypoint ist deshalb bash (`wait -n`); `ENTRYPOINT_DRY_RUN=1` zeigt nur die Aufrufe —
`engine-provider/test/entrypoint.test.sh` prüft damit den Argument-Aufbau. **Die Provider starten
GESTAFFELT** (`PROVIDER_START_DELAY`, Vorgabe 3 s, seit 0.534.1): jeder registriert sich beim Start bei
lichess.org, und 13 Registrierungen im selben Augenblick hielt der DDoS-Schutz von Lichess für einen
Angriff (2026-09-11 auf der zweiten Maschine: 429, dann IP-Sperre, null Engines — jeder Neustart
wiederholte es). `test/supervisor.test.sh` misst den Abstand der echten Starts.
