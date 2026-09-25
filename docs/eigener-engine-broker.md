# Eigener Engine-Broker: RookHub ohne Lichess

Stand 2026-09-25. Umsetzungsplan; nach der Umsetzung wandert das Wesentliche nach `CLAUDE.md`
(Kapitel „Externe Engine") und in `engine-provider/README.md`, diese Datei bleibt als Entwurfs-Protokoll.

## 0. Ausgangslage und Ziel

Heute spricht RookHub das Lichess-External-Engine-Protokoll als **Client**: der Nutzer hinterlegt
einen Lichess-Token, RookHub listet die auf seinem Lichess-Konto registrierten Engines und schickt
Analyse-Anfragen an den Lichess-Broker `engine.lichess.ovh`, der sie an den Provider auf dem Rechner
des Nutzers vermittelt (`engine-provider/`, offizieller `example-provider.py`). Lichess erfüllt dabei
drei Rollen:

1. **Registrierung** — `lichess.org/api/external-engine` (GET/POST/PUT) mit dem Lichess-Token; die
   Engine gehört zum Lichess-Konto.
2. **Vermittlung (Broker)** — `engine.lichess.ovh`: Warteschlange je `providerSecret`, Long-Poll des
   Providers (`/api/external-engine/work`, 10 s), Upload der Suche (`/work/{id}`, Chunked-POST mit
   UCI-Zeilen), Auslieferung an den Anfragenden (`/{id}/analyse`, ndjson).
3. **Protokoll-Umsetzer** — der Broker macht aus UCI-`info`-Zeilen JSON-Zeilen (`pvs`, Weiß-Sicht,
   `bestmove`), reicht `{"keepalive":true}` durch, erzwingt `bestmove` am Ende.

Was daran weh tut (alles in dieser Sitzung belegt):

- **Drosselung je IP durch den Lichess-DDoS-Schutz**: 8 Provider auf einer Maschine gehen, 12 nicht
  (13 684 × 503 am 12./13.9., 422 × 503 in einem 10-Minuten-Lasttest am 25.9. — auch mit dem neuen
  Provider). Die Grenzen sind laut Lichess „varied and ever changing" und nicht dokumentiert; eine
  Verletzung endete am 11.9. in einer IP-Sperre.
- **Protokolländerungen ohne Vorwarnung**: seit 6.9. verlangt der Broker `bestmove`; der gepinnte
  Provider wusste das nicht, jede Suche endete mit 400 + 5 s Schlaf (4 s Wartezeit je Zug).
- **Upstream-Fehler im Provider** (Issue #45, 25.9.): wir patchen den Provider inzwischen selbst.
- **Feste 15 s** vom Broker, bis ein Provider einen Auftrag übernommen haben muss, sonst 503.
- Ein Lichess-Konto mit Token (`engine:read` + `engine:write`) ist Voraussetzung; Ausfälle und
  Deploys von Lichess (nginx-503-Schübe) treffen uns mit.

**Ziel:** Eine Engine auf dem Rechner des Nutzers verbindet sich **direkt mit RookHub**. Lichess ist
dafür nicht mehr nötig. Der bestehende Lichess-Pfad bleibt für Cloud-Anbieter (stockfishcloud,
Chessify), die selbst als Lichess-Provider auftreten — beide Quellen erscheinen in derselben Auswahl.

## 1. Entscheidung: Welcher Weg

| | A1 — Broker in der RookHub-API (C#), offizieller Provider unverändert | A2 — `lila-engine` (Rust) als Container + MongoDB | B — eigener Provider (WebSocket) |
|---|---|---|---|
| Neuer Code | Hub, UCI→JSON, Registrierung, Endpunkte (~1500 Zeilen C# inkl. Tests) | Registrierung nach Mongo schreiben; Betrieb | Provider (Python) + Server-Seite (C#), Windows-Skripte, Doku |
| Provider-Seite | **nur Konfiguration** (`ROOKHUB_URL`, Token) | nur Konfiguration | neues Programm beim Nutzer |
| Fidelity zum Lichess-Verhalten | Port von `emit.rs` (klein, klar) | exakt | egal, eigenes Protokoll |
| Streaming durch Proxys | Chunked-Upload braucht `proxy_request_buffering off` (nginx + NPM) | dito, plus eigener Host | WebSocket (NPM-Schalter) |
| Abhängigkeiten | keine neuen | Mongo, Rust-Image, zweite Konfig | keine |
| Beobachtbarkeit | Kibana/ECS wie alles andere | separates Log | gut |
| Eigene Regeln (Zeitschranken, `bestmove` locker) | ja | nur per Fork | ja |

**Gewählt: A1.** Der Provider bleibt der offizielle (gepinnt, geprüft, von uns getestet), auf dem
Rechner des Nutzers ändert sich eine `.env`-Zeile. Der Broker liegt dort, wo schon der Anfragende
sitzt (Analysebrett-Proxy und Auftrags-Worker laufen in derselben API) — die Analyse-Anfrage
braucht dann keinen HTTP-Hop mehr. B bleibt als späterer Ausbau (ein Prozess bedient mehrere
Engines) möglich; das Protokoll gibt es her, weil der Broker nur nach `providerSecret` verteilt und
die Antwort die angefragte Engine nennt.

## 2. Zielarchitektur

```
Rechner des Nutzers                          RookHub-Server
┌──────────────────────┐   HTTPS (NPM → nginx → API)   ┌────────────────────────────────────┐
│ engine-provider      │ ── GET/PUT /api/external-engine ──▶ Registrierung (DB)             │
│ example-provider.py  │ ── POST /api/external-engine/work ─▶ Hub.Acquire (Long-Poll 10 s)  │
│ (unverändert, pro    │ ◀─ 200 {id, engine, work} ────────  Warteschlange je Selector     │
│  Engine ein Prozess) │ ── POST /work/{id} (chunked, UCI) ─▶ Submit → UCI→JSON → Channel  │
└──────────────────────┘                                │        ▲                          │
                                                        │  Anfragende (IN-PROZESS):         │
                                                        │  EngineController.Analyse (Browser)│
                                                        │  AnalysisJobWorker (Aufträge)     │
                                                        └────────────────────────────────────┘
Lichess-Pfad (bleibt): LichessEngineService → engine.lichess.ovh → Cloud-Provider
```

**Was gleich bleibt:** das ndjson-Format zum Browser und zum Worker (`pvs`, `depth`, `nodes`,
`time`, `bestmove`, `{"keepalive":true}`), `mapBrokerLine`/`AnalysisJobStream`/`StreamTally`,
`EngineActivityTracker` (Vorrang Live vor Hintergrund, je Engine-Id), Hintergrund-Engine-Liste und
Haus-Engine, `NdjsonHeartbeatPump` zum Browser, der Lichess-Pfad samt Token-Karte.

**Was neu ist:** eine zweite Engine-Quelle („RookHub direkt", Ids `rhe_…`), der Hub, drei
Provider-Endpunkte, die Registrierungs-API, `POST /api/token/test`, ein API-Token-Scope `engine`,
nginx-Regeln für den Upload, ein Online-Status je Engine, Profil-Karte und Auswahl in zwei Quellen.

## 3. Protokoll der Provider-Seite — exakt das, was `example-provider.py` (d0eeb242) erwartet

Quelle der Wahrheit: `engine-provider`-Pin (Dockerfile `PROVIDER_SHA`) und der Broker-Quelltext
`lila-engine` (Klon unter
`/tmp/claude-1000/-home-kahalm-claude/0549c162-79a1-46fe-b244-a6fe28a6da81/scratchpad/lila-engine/src/`:
`main.rs`, `emit.rs`, `uci.rs`, `api.rs`, `hub.rs`, `ongoing.rs`, `model/`). Der Provider liegt als
`prov-new.py` daneben.

### 3.1 Registrierung (`--lichess`-Basis, Bearer = RookHub-API-Token `rkh_…` mit Scope `engine`)

| | |
|---|---|
| `GET /api/external-engine` | Liste der Engines des Kontos: `[{ id, name, clientSecret, userId, maxThreads, maxHash, variants, providerData }]`. Der Provider sucht darin per `name`. |
| `POST /api/external-engine` | Rumpf `{ name, maxThreads, maxHash, variants: ["chess", …], providerSecret }` → legt an; Antwort 200 mit dem Engine-Objekt (der Provider liest sie nicht). |
| `PUT /api/external-engine/{id}` | derselbe Rumpf → aktualisiert (Name = Identität; `providerSecret` wechselt bei jedem Provider-Start, sofern `PROVIDER_SECRET` nicht gesetzt ist). |
| `DELETE /api/external-engine/{id}` | für die Profil-Karte (Lichess hat das auch). |
| `POST /api/token/test` | Rumpf `text/plain` = der Token (Lichess erlaubt kommagetrennt mehrere). Antwort `{ "<token>": { "userId": "<Benutzername>", "scopes": "engine:read,engine:write", "expires": <ms-Epoche oder null> } }`, unbekannt/fremder Scope → `{ "<token>": null }`. Immer 200. Das erwartet `preflight.py` (`REQUIRED_SCOPES`). Anonym; nur `rkh_`-Tokens mit Scope `engine` gelten. |

Regeln: Name je Nutzer eindeutig (≤ 200 Zeichen), höchstens 32 Engines je Nutzer, `maxThreads`
1..1024, `maxHash` 1..1 048 576 MiB, `variants` ⊇ `chess` (andere werden gespeichert, aber nur
`chess` angeboten). Gespeichert wird **nicht** das `providerSecret`, sondern der Selector
`sha256("providerSecret:" + secret)` als Hex (wie lila-engine `ProviderSecret::selector`). `id` =
`rhe_` + 12 Zeichen `[A-Za-z0-9]` (so lang wie `eei_…` — die CSV-Spalte `BackgroundEngineIds` fasst
16 davon). `clientSecret` = 32 Zufallsbytes (base64url), im Klartext gespeichert (die API ist der
einzige Anfragende; es reist nur zum Besitzer). Mit JWT (Browser) sind GET und DELETE ebenfalls
erlaubt (Profil-Karte); POST/PUT nur mit API-Token — der Provider ist der Einzige, der registriert.

### 3.2 Arbeit holen — `POST /api/external-engine/work` (`--broker`-Basis)

Rumpf `{ "providerSecret": "…" }`. Kein anderer Auth. Antwort:

- `200 { id, engine: {…wie oben…}, work }` sobald ein Auftrag für den Selector wartet;
- `204` ohne Rumpf nach `Engine:LocalBroker:AcquireWaitSeconds` (10 s) — **auch bei unbekanntem
  Selector** (kein 401/404: das wäre Rauschen für den log-watcher und Information nach außen).

`work` = `{ sessionId, threads, hash, multiPv, variant: "chess", initialFen, moves: [uci…] }` plus
GENAU EINES von `depth` | `movetime` | `nodes` (der Provider nimmt das erste, das er findet, in der
Reihenfolge movetime, depth, nodes). Vor der Vergabe: `threads = min(threads, maxThreads)`,
`hash = min(hash, maxHash)`, `multiPv` 1..5, `initialFen` legal (Gera.Chess), Züge legal und
höchstens 600, Rochaden als König-schlägt-Turm normalisiert (der Provider setzt `UCI_Chess960
true`; siehe `api.rs::Work::sanitize` und `castling-uci.util.ts`).

Wartende Aufträge, deren Anfragender inzwischen weg ist, werden beim Holen übersprungen
(`is_valid`). Höchstens `Engine:LocalBroker:MaxQueuedPerEngine` (64) Aufträge je Selector, darüber
503 an den Anfragenden. Jeder erfolgreiche Poll setzt `LastSeenAt` der Engine (im Speicher; alle
60 s in die DB) — daraus der Online-Status.

### 3.3 Ergebnis hochladen — `POST /api/external-engine/work/{id}` (chunked, `Transfer-Encoding: chunked`, kein Content-Length)

Zeilenweise (`\n`), jede Zeile eine von:

- `info …` — UCI; nur Zeilen mit `score` schickt der Provider überhaupt;
- `bestmove <uci> [ponder <uci>]` oder `bestmove (none)` — Ende der Suche;
- `{"keepalive":true}` — alle 15 s bei schweigender Engine; **verbatim** als eigene Zeile an den
  Anfragenden weiterreichen (Browser-Parser und `StreamTally` kennen sie);
- unbekannte JSON-Steuerzeile → ignorieren (nicht 400 wie lila-engine).

Antwort an den Provider: `200` am Ende (auch bei EOF **ohne** `bestmove` — dann Warnung im Log
`EngineBroker: Upload ohne bestmove`, Stream zum Anfragenden regulär beenden; die 400 von lila-engine
war die Ursache des 5-s-Schlaf-Dramas, wir bauen sie nicht nach). `404` wenn `{id}` unbekannt oder
schon eingelöst. Bricht der Anfragende ab (Browser wechselt die Stellung, Worker pausiert), wird die
Antwort an den Provider **sofort** mit 200 beendet (er stoppt dann die Engine) — nicht erst am Ende
der Suche.

**Kestrel-Fallen (Pflicht):** `[DisableRequestSizeLimit]` und
`HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>()!.MinDataRate = null` in der Aktion —
Kestrels Vorgabe (240 Bytes/s nach 5 s Gnade) killte einen Upload, der bei tiefer MultiPV-Suche
minutenlang nur alle 15 s 19 Bytes Lebenszeichen schickt. Rumpf mit `PipeReader`/`StreamReader`
zeilenweise lesen, **nicht** puffern (`Request.EnableBuffering` nie aufrufen).

### 3.4 Auslieferung an den Anfragenden — in-Prozess

`IEngineBroker.AnalyseAsync(EngineRef engine, EngineWork work, CancellationToken ct)` liefert eine
`EngineAnalysisSession` mit `Stream Ndjson` (Lesestrom aus einem `Channel<string>`/`Pipe`) — damit
bleiben `NdjsonHeartbeatPump.PumpAsync(stream, Response.Body, …)` im Controller und
`AnalysisJobStream.ConsumeAsync(stream, …)` im Worker **unverändert**. Wartet länger als
`Engine:LocalBroker:ProviderTimeoutSeconds` (15) kein Provider-Upload → Ergebnis `503`
(`ProviderTimeout`), damit der Worker wie bisher reihum wechselt (`NextEngineAfter`) und das
Analysebrett nach 12 s auf WASM zurückfällt.

### 3.5 UCI → JSON (Port von `emit.rs`, mit Vektor-Tests)

Ausgabezeile `{ "time": ms, "depth": n, "nodes": n, "pvs": [ { "moves": [uci…], "cp": n | "mate": n, "depth": n }, … ], "bestmove"?: uci, "ponder"?: uci }`.

- `multipv` fehlt ⇒ 1. Eine `info`-Zeile mit `multipv 1` (oder ohne) setzt `time`/`depth`/`nodes`
  des Emits neu und **leert alle pvs**; Zeilen mit `multipv > 1` setzen `depth = min(depth, ihre depth)`.
- Eine Linie entsteht nur aus `info` mit `depth` **und** `score` **und** `pv`; bei `multipv 1` nur,
  wenn der Score **weder** `lowerbound` **noch** `upperbound` trägt (bei `multipv > 1` immer).
- `pvs` wird auf `multipv` Einträge verlängert; Linie an Position `multipv-1`. **Gesendet** wird nur,
  wenn alle Plätze belegt sind (`should_emit`) — deshalb keine halben MultiPV-Sätze.
- Score aus **Weiß-Sicht**: ist Schwarz am Zug (in der Stellung nach `initialFen`+`moves`), Vorzeichen
  drehen (`cp` und `mate`; `mate 0` bleibt 0).
- PV-Züge auf der Stellung nachspielen, beim ersten illegalen Zug abschneiden, höchstens 30; Rochade
  als König-schlägt-Turm ausgeben (`e1h1`) — genau wie lila-engine, damit `castling-uci.util.ts` weiter passt.
- `bestmove`: letzter Emit zusätzlich mit `bestmove` (und `ponder`), leere pvs-Plätze entfernt;
  `(none)` ⇒ `bestmove` weglassen.
- Unparsbare `info`-Zeile ⇒ Warnung + überspringen (lila-engine bricht mit 400 — wir nicht).

## 4. Backend (C#) — Bauteile

| Bauteil | Inhalt |
|---|---|
| `Models/ExternalEngineRegistration` + Migration | Id (`rhe_…`, PK, ≤ 20), UserId (Cascade), Name (≤ 200), ClientSecret (≤ 64), ProviderSelector (64 Hex, Index), MaxThreads, MaxHash, Variants (CSV ≤ 200), ProviderData? (≤ 500), CreatedAt, UpdatedAt, LastSeenAt?; **UNIQUE (UserId, Name)**. Wird beim Kontolöschen mit abgeräumt (`ProfileService`). |
| `Services/EngineBroker/` | `EngineHub` (Singleton; `ConcurrentDictionary<selector, EngineQueue>`, `Channel<PendingJob>`; `AcquireAsync(selector, ct)` mit Wartezeit; `Ongoing` (jobId → PendingJob, Verfall 30 s ohne Upload); GC). `UciLineParser` (Port `uci.rs`, nur `info`/`bestmove`, tolerant). `EmitBuilder` (Port `emit.rs`). `WorkSanitizer` (FEN/Züge/Klemmen). `LocalEngineBroker : IEngineBroker`. `LichessEngineBroker : IEngineBroker` (Hülle um `LichessEngineService`). `EngineRegistry` (löst `eei_` → Lichess (Token nötig), `rhe_` → DB; liefert `EngineRef { Id, Name, MaxThreads, MaxHash, Source, Online? }`). |
| `Controllers/ExternalEngineController` (`api/external-engine`) | 3.1–3.3. `[DisableRateLimiting]` für `work` und `work/{id}` (siehe 6.). Registrierung mit `[Authorize]` + Scope-Prüfung: API-Token ⇒ `scope == engine`, JWT ⇒ nur GET/DELETE. |
| `Controllers/TokenController` oder in `ProfileController` | `POST /api/token/test` (AllowAnonymous, RL-Policy `anonymous-puzzle`). |
| `ApiTokenService.AllowedScopes` | `{ "extension", "engine" }`; Extension-Endpunkte prüfen weiter `extension`, Engine-Endpunkte `engine`. |
| `EngineController` | `ListExternalEngines`: beide Quellen zusammenführen (`source`, `online`); `Analyse`: über `EngineRegistry` + `IEngineBroker` statt direkt `_lichess`; Threads/Hash-Klemmung bleibt. `SetBackgroundEngine`/`SetHouseEngine`: Ids beider Quellen zulassen. |
| `LichessEngineCredential` | Zeile auch **ohne** Token erlaubt (`EncryptedToken` leer): sie trägt `BackgroundEngineIds`/`ShareAsHouseEngine`, die ein Nutzer ohne Lichess-Konto braucht. `hasCredentials` = Token vorhanden. **Alle** Leser prüfen (`grep LichessEngineCredentials`): `AnalysisJobService.TokenAsync/PickBackgroundEngineAsync`, `GameAnalysisService.ResolveGuessEngineOwnerAsync`, `AnalysisJobWorker`, `EngineController`. |
| `AnalysisJobWorker` | `ResolveEngineAsync`/`AnalyseAsync` über Registry + Broker; Fehlertexte quellenneutral („Engine nicht (mehr) registriert"). `sessionId` bleibt `rh-bg-{UserId}`. |
| `EngineActivityTracker` | unverändert (Engine-Id ist der Schlüssel). |
| Konfiguration | `Engine:LocalBroker:Enabled` (true), `AcquireWaitSeconds` (10), `ProviderTimeoutSeconds` (15), `MaxQueuedPerEngine` (64), `OngoingExpirySeconds` (30), `OnlineWindowSeconds` (30). Compose-Beispiele: keine Pflichtvariable. |
| Logging | Serilog-Domain-Tags `engine,broker`; Zähler je Engine: Aufträge, Uploads ohne bestmove, ProviderTimeouts, Uploads mit Abbruch durch den Anfragenden. Kein Token, kein Secret im Log. |

## 5. Provider-Seite (`engine-provider/`)

- `.env`: neue Variable `ROOKHUB_URL=https://rookhub.oberschmid.homes`. Setzt der Entrypoint
  `LICHESS_URL` **und** `BROKER_URL` darauf (ausdrücklich gesetzte Werte gewinnen) und reicht sie als
  `--lichess`/`--broker` durch. `ROOKHUB_API_TOKEN=rkh_…` wird zu `LICHESS_API_TOKEN`, wenn dieses
  leer ist (der Provider kennt nur den Lichess-Namen). `preflight.py` prüft dann gegen
  `{ROOKHUB_URL}/api/token/test` (der `LICHESS_URL`-Zweig tut das schon) und erwartet
  `engine:read`+`engine:write` — genau das liefert RookHub für Scope `engine`.
- `entrypoint.test.sh`: Dry-Run zeigt `--lichess`/`--broker`; Fälle: nur `ROOKHUB_URL`; `ROOKHUB_URL`
  plus ausdrückliches `BROKER_URL`; Token-Alias; Token-Alias verliert gegen `LICHESS_API_TOKEN`.
- README: neuer Abschnitt **„Direkt mit RookHub (ohne Lichess)"** ganz vorne als empfohlener Weg
  (API-Token im Profil mit Scope „Engine" anlegen; zwei `.env`-Zeilen; Windows: `--lichess`/`--broker`
  in `run_provider.ps1`); Lichess-Weg bleibt dokumentiert für Cloud-Anbieter. `.env.example` und
  `windows/run_provider.ps1` (Variablen `$rookhubUrl`) nachziehen. Die 429/IP-Sperre-Zeile in der
  Fehlertabelle bekommt den Zusatz „betrifft nur den Lichess-Weg".

## 6. Proxys und Grenzen — die Stellen, an denen es sonst still scheitert

1. **Frontend-nginx** (`src/frontend/nginx.conf`): neue `location ^~ /api/external-engine/` mit
   `proxy_http_version 1.1; proxy_request_buffering off; proxy_buffering off; proxy_cache off;
   client_max_body_size 0; proxy_read_timeout 3600s; proxy_send_timeout 3600s;` — ohne
   `proxy_request_buffering off` sammelt nginx den Chunked-Upload bis zum Ende, der Anfragende sähe
   die erste Zeile erst nach der Suche. `DeploymentConfigTests` prüft die Direktiven.
2. **Nginx Proxy Manager** (Prod/Dev, `location /` mit Standard-`proxy.conf`, also
   `proxy_request_buffering on`): eine **Custom Location** `/api/external-engine/` mit denselben
   Direktiven je Host. Das ist ein Deploy-Schritt außerhalb des Repos (NPM-Oberfläche oder
   `/yacht/AppData/Config/Nginx-Proxy/nginx/proxy_host/30.conf`), nur auf Zuruf; bis dahin erkennt die
   Abnahme-Messung (7.) es, weil die erste Zeile erst mit der letzten käme.
3. **Globaler Rate-Limiter der API** (`Program.cs`: 100 Anfragen/min je IP): 13 Provider = 78
   Polls/min plus Uploads — die Provider-Endpunkte müssen davon **ausgenommen** sein
   (`[DisableRateLimiting]`), sonst bauen wir die Lichess-Drosselung nach. Die Registrierung
   (selten) und `token/test` bleiben limitiert.
4. **log-watcher**: Polls antworten 204, ungültiger Selector 204, `token/test` 200 — kein 401/404 im
   Takt, kein Fehlalarm `api_scan`/`auth_bruteforce`.
5. **`MaxConcurrentStreamsPerUser` (4)** und `MaxStreamDuration` (10 min) im Live-Proxy bleiben.

## 7. Tests und Abnahme

**Unit (xUnit):** `UciLineParser` (Vektoren: `info depth 20 seldepth 30 multipv 2 score cp -35
lowerbound nodes … pv e2e4 e7e5`, `score mate -3`, `bestmove (none)`, `bestmove e1g1 ponder …`,
Zeilen mit `string`/`currmove`, kaputte Zeilen); `EmitBuilder` (MultiPV-Sätze vollständig/unvollständig,
Reset bei multipv 1, Weiß-Sicht bei Schwarz am Zug, lowerbound-Regel, PV-Abschneiden, Rochade
`e1h1`, bestmove/ponder, 30-Züge-Deckel); `WorkSanitizer`; `EngineHub` (Acquire wartet/liefert/204,
ungültige Aufträge übersprungen, Ongoing-Verfall, Anfragender weg ⇒ Upload sofort beendet,
ProviderTimeout, Deckel je Selector); Registrierung (Name-Identität, Secret-Wechsel, Limits, Scope);
`token/test`-Format; `EngineRegistry` (Mischliste, Online-Status). Bestehende Tests des Lichess-Pfads
bleiben grün.

**Integration (`RookHub.Api.IntegrationTests`, `ApiFactory`):** ein **C#-Fake-Provider**, der sich
genau wie `example-provider.py` verhält (Registrierung, Long-Poll, Chunked-Upload mit `info`-Zeilen,
15-s-Keepalive, `bestmove`): Analyse über `POST /api/engine/external/rhe_…/analyse` liefert die
ndjson-Zeilen **während** des Uploads (Zeitstempel!), Keepalive kommt durch, Abbruch des Browsers
beendet den Upload; Auftrags-Worker rechnet einen Hintergrund-Auftrag über eine `rhe_`-Engine zu Ende.

**Vertrag mit dem echten Provider (Pflicht, lokal):** `engine-provider/test/rookhub-broker.e2e.sh`:
E2E-Stack (`compose.e2e.yml`, Ports 15099/18099 — siehe `scripts/e2e.sh`), Nutzer + API-Token per API
anlegen, das Provider-Image bauen (`engine-provider/Dockerfile`, Stockfish enthalten) und mit
`ROOKHUB_URL=http://host.docker.internal:18099` (Frontend-nginx-Hop!) starten, dann als der Nutzer
eine Analyse anfordern: erste Zeile < 1 s, Zeilen mit `pvs`, letzte mit `bestmove`, danach ein
zweiter Auftrag < 1 s nach dem Ende; Provider-Log ohne ERROR/Traceback. Danach dieselbe Messung mit
`ENGINE_BACKGROUND_COUNT=12` für 3 Minuten Auftrag an Auftrag (Portierung von `last12.py`): **0 × 503**.

**Frontend (Karma, `ng test app`):** Engine-Karte mit beiden Quellen, Online-Punkt, Löschen;
Picker zeigt Quelle; i18n-Parität en/de/hr/hu (`i18n-parity.spec.ts`).

**Abnahme (Definition of Done):** alle oben genannten Tests grün; `ng build app` + `ng build turnier`
grün; die Vertrags-Messung mit 12 Engines ohne 503; im Direkt-Modus **kein** Aufruf nach
`lichess.org`/`engine.lichess.ovh` (Log-Prüfung); Doku (CLAUDE.md-Kapitel „Externe Engine",
`engine-provider/README.md`, `.env.example`, `src/frontend/CLAUDE.md`-Route/Karte), Version + Changelog
zweisprachig; die Lichess-Nutzung funktioniert unverändert (Tests + ein Klick-Test auf Dev).

## 8. Rollout

1. Merge auf `master` (Dev-Images nachts); NPM-Custom-Location auf **Dev** setzen (Zuruf), Provider
   auf dem Server mit `ROOKHUB_URL` auf Dev zeigen lassen und die 12-Engine-Messung gegen Dev wiederholen.
2. Tag (Zuruf) → Prod; NPM-Custom-Location auf Prod; Provider-Stacks `/opt/stacks/rookhub-schach-engine`
   und die Gross-Maschine auf `ROOKHUB_URL` umstellen (die Lichess-Registrierungen bleiben liegen,
   stören nicht; Hintergrund-Liste im Profil auf die `rhe_`-Engines umstellen).
3. Lichess-Token bleibt optional für Cloud-Engines.

## 9. Arbeitspakete (Reihenfolge für die Umsetzung)

1. Modell + Migration + `EngineRegistry` + Registrierungs-API + `token/test` + Scope `engine` (+ Tests).
2. `UciLineParser` + `EmitBuilder` + `WorkSanitizer` (+ Vektor-Tests) — **zuerst gegen `emit.rs`/`uci.rs` lesen**.
3. `EngineHub` + `LocalEngineBroker` + Provider-Endpunkte (+ Tests, Kestrel-Fallen, `[DisableRateLimiting]`).
4. `IEngineBroker`-Abstraktion; `EngineController`, `AnalysisJobWorker`, `AnalysisJobService`,
   `GameAnalysisService` umhängen; Credential ohne Token; Mischliste (+ Tests des Lichess-Pfads grün).
5. nginx-Location + `DeploymentConfigTests`.
6. Integrationstest mit C#-Fake-Provider.
7. Provider-Seite: `ROOKHUB_URL`/`ROOKHUB_API_TOKEN`, Tests, README, `.env.example`, Windows-Skript.
8. Frontend: DTO (`source`, `online`), Engine-Karte, Picker/Vergleich/Jobs/Position-Menü, i18n, Specs.
9. `rookhub-broker.e2e.sh` gegen den E2E-Stack + 12-Engine-Messung; Log-Prüfung „kein Lichess".
10. Doku, Version, Changelog.

**Nicht tun:** Prod- oder Dev-Stacks anfassen (`/opt/stacks/**`), NPM ändern, pushen, taggen,
Lichess-Pfad-Semantik ändern, Secrets/Token ausgeben, Kopie 1 (`rookhubstack/`) berühren.
