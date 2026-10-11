# Eigener Engine-Broker (0.537.0) — der Provider spricht direkt mit RookHub

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Eigener Engine-Broker (0.537.0) — der Provider spricht direkt mit RookHub“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


**Was**: RookHub bietet die PROVIDER-Seite des Lichess-External-Engine-Protokolls selbst an
(`Controllers/EngineBroker/ExternalEngineController.cs`, `Controllers/Accounts/TokenController.cs`, `Services/EngineBroker/`). Der
offizielle Provider (`example-provider.py`, Pin `d0eeb242`) läuft UNVERÄNDERT — nur `--lichess`/`--broker` zeigen
auf RookHub (`ROOKHUB_URL`), und der Token ist ein RookHub-API-Token mit Scope `engine` (`ROOKHUB_API_TOKEN`,
im Profil unter „API-Tokens“ anzulegen). Plan, Protokoll und Begründung: `docs/eigener-engine-broker.md`.

**Warum**: jede Suche lief über lichess.org/engine.lichess.ovh. Dreizehn Engines einer Maschine liefen in 429 und
IP-Sperren (2026-09-11), der Lichess-Broker antwortete 503, sobald gerade kein Provider pollte, und ohne
Lichess-Konto ging gar nichts. Direkt gibt es keine fremde Drossel, keinen Lichess-Token und keinen Hop übers Internet.

**Aufbau**:
* **Registrierung** `ExternalEngineRegistrations`: Kennung `rhe_` + 12 Zeichen, `ClientSecret` (32 Byte base64url,
  verlässt den Server nie Richtung Browser), `ProviderSelector` = sha256(`"providerSecret:" + secret`) hex — das
  Geheimnis des Providers selbst wird NIE gespeichert. Identität = der NAME je Konto: der Provider fragt `GET`, dann
  `PUT` auf den gleichnamigen Eintrag bzw. `POST`; ein `POST` mit exakt vorhandenem Namen aktualisiert ebenfalls
  (zwei Provider, die gleichzeitig starten). Ein Name, der sich nur in Groß/Klein unterscheidet → 409; höchstens 32
  je Konto. Anlegen/Ändern NUR mit Engine-Token (der Provider registriert, der Browser nicht → 403), Löschen auch
  aus dem Profil.
* **`EngineHub`** (Singleton, Arbeitsspeicher): Schlange je Selector, Übergabe direkt an einen wartenden Poll.
  `POST /api/external-engine/work` wartet `AcquireWaitSeconds` (10) → 200 `{ id, work, engine }` oder 204. Der
  Upload `POST /api/external-engine/work/{id}` wird ZEILENWEISE gelesen (`EngineUploadPump`), über `UciLineParser` +
  `EmitBuilder` (Port von lila-engine `emit.rs`, Commit 60ea115c) in genau die ndjson-Zeilen verwandelt, die der
  Lichess-Broker schickt, und sofort an den Anfragenden gereicht. `{"keepalive":true}` geht unverändert durch.
* **`EngineRegistry`** löst eine Kennung auf (lokal/Lichess), **`IEngineBroker`** (`EngineBrokerRouter` →
  `LocalEngineBroker` bzw. `LichessEngineBroker`) liefert beiden Aufrufern denselben Strom. `EngineController`
  (live) und `AnalysisJobWorker` (Hintergrund, damit auch `GameAnalysisService`) kennen nur noch diese zwei.
* Nimmt kein Provider einen Auftrag binnen `ProviderTimeoutSeconds` (15) an, ist das ein **503** wie beim
  Lichess-Broker (der Worker wechselt dann die Engine); ebenso eine Schlange über `MaxQueuedPerEngine` (64).
  „online“ = gepollt in den letzten `OnlineWindowSeconds` (30) (`EngineSelectorDirectory`; `LastSeenAt` schreibt
  `EngineBrokerMaintenanceService` alle 60 s, dazu alle 10 min eine Statistikzeile). Schalter
  `Engine:LocalBroker:Enabled` (Vorgabe an; aus = `/api/external-engine/*` 404).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/external-engine` | JWT oder Token `engine` | Registrierte Engines im Lichess-Format; `clientSecret` NUR für den Engine-Token |
| POST | `/api/external-engine` | Token `engine` | Registrieren `{ name, maxThreads, maxHash, variants, providerSecret, providerData? }` (gleicher Name = Aktualisierung) |
| PUT | `/api/external-engine/{id}` | Token `engine` | Registrierung ändern (neues `providerSecret` = neuer Selector) |
| DELETE | `/api/external-engine/{id}` | JWT oder Token `engine` | Engine entfernen (auch aus der Hintergrund-Liste); ein laufender Provider meldet sie beim nächsten Start neu an |
| POST | `/api/external-engine/work` | **anonym** (Selector ist der Nachweis) | Long-Poll `{ providerSecret }` → 200/204. Unbekannter Selector = 204 nach der Wartezeit, je Client-Adresse höchstens 30 je Minute (`UnknownSelectorThrottle`), darüber sofort 429 ohne Halten; Rumpf ≤ 4 KB (`[RequestSizeLimit]`, sonst 413) |
| POST | `/api/external-engine/work/{id}` | **anonym** (Auftragskennung) | Chunked-Upload der UCI-Ausgabe bis `bestmove`; 404 = Auftrag weg/abgelaufen |
| POST | `/api/token/test` | **anonym** + RL | Lichess-kompatible Token-Prüfung (Rumpf `text/plain`, Tokens mit Komma, max. 20) → `{ token: { userId, scopes, expires } \| null }`; Scopes `engine:read,engine:write` NUR für Scope `engine`. Der Provider fragt hier vor dem Start (`preflight.py`) |

**Abweichungen von lila-engine — alle bewusst, alle mit Test:**
* `EmitBuilder` ist ein TREUER Port. `tools/emit-reference/run.sh` baut das Original (Rust, im Container) und lässt es
  gegen dieselben Fälle laufen; `EmitBuilderVectorTests` enthält dessen Ausgaben LITERAL. Wer am Emit dreht, lässt das
  Skript laufen und übernimmt die neuen Ausgaben nicht ungeprüft.
* Eine unlesbare `info`-Zeile, eine Zeile über 16 KiB und eine unbekannte JSON-Steuerzeile werden ÜBERSPRUNGEN (lila:
  400), ein Upload-Ende ohne `bestmove` beendet den Strom regulär (lila: 400 — genau diese 400 kostete bis d0eeb242
  fünf Sekunden je Suche). Warnungen je Upload auf drei gedeckelt. `bestmove (none)` erscheint nicht als Zug.
* Der `WorkSanitizer` weist VOR der Schlange ab, was keine legale Standardstellung ist (Gegner im Schach, Bauer auf der
  Grundreihe, Rochaderechte ohne König/Turm, Chess960-Rochaderechte, falsches e.p.-Feld) → **400**. Stockfish 19
  BEENDET sich bei so einer Stellung samt Provider (siehe Dockerfile); eine krumme Stellung kostete sonst den ganzen
  Engine-Pool einen Neustart. Der Worker setzt so einen Auftrag bei einer `rhe_`-Engine sofort auf `Failed`
  („Stellung abgewiesen: …“) statt ihn zwei Minuten zurückzustellen — Warten ändert daran nichts.

**Fallen, an denen es STILL scheitert** (alle mit Test festgenagelt):
1. **Kestrels Mindest-Datenrate**: ein Upload schweigt zwischen zwei tiefen Iterationen minutenlang (nur das Keepalive
   alle 15 s); Kestrel bricht Rümpfe unter 240 B/s ab („Reading the request body timed out due to data arriving too
   slowly“). `Submit` setzt `IHttpMinRequestBodyDataRateFeature.MinDataRate = null` und `[DisableRequestSizeLimit]` —
   `EngineBrokerTests` (Integration, echter Kestrel) fällt ohne beides um.
2. **Rate-Limiter**: 13 Provider sind 78 Polls je Minute plus Uploads, der globale Deckel 100/min je IP →
   `[DisableRateLimiting]` an Poll UND Upload (schaltet auch den globalen Limiter ab — per Test belegt: ohne es 429).
   Registrierung und `token/test` bleiben limitiert.
3. **Puffernde Proxys**: der Frontend-nginx hat `location ^~ /api/external-engine/` mit `proxy_request_buffering off`
   (sonst sammelt nginx den Chunked-Upload bis zum ENDE der Suche und die erste Zeile käme mit der letzten),
   `proxy_buffering off`, `client_max_body_size 0` und 3600-s-Timeouts (`DeploymentConfigTests`); nur der Poll
   `= /api/external-engine/work` hat eine eigene exakte location mit `client_max_body_size 8k`. **Der Nginx Proxy
   Manager davor puffert Anfragen per Vorgabe genauso** — Dev und Prod brauchen je eine Custom Location
   `/api/external-engine/` mit denselben Direktiven, sonst läuft der direkte Weg nur in Zeitlupe. Deploy-Schritt
   außerhalb des Repos, nur auf Zuruf (TODO.md). **Und die Registrierung heißt `/api/external-engine` OHNE
   Schrägstrich**: für genau diese URI antwortet nginx bei einer Präfix-location mit Schrägstrich und `proxy_pass`
   SELBST mit 301 auf „…/“ (an den Container-Port). Die exakte `location = /api/external-engine` nimmt ihr die URI
   weg (`DeploymentConfigTests`). Gefunden erst mit dem echten Provider — der Kestrel-Integrationstest geht an nginx
   vorbei.
4. **Die frühe Antwort**: ist der Anfragende weg (Browser zu, Worker bricht ab), antwortet der Broker dem Upload
   SOFORT 200 — so stoppt der Provider die Engine und holt den nächsten Auftrag. Kestrel meldet den dabei
   abgebrochenen Rumpf als `BadHttpRequestException` („Unexpected end of request content“), nicht als Abbruch: ob der
   Anfragende oder der Provider weg ist, entscheidet deshalb der Zustand des Auftrags, nicht der Ausnahmetyp.
5. **Unbekannter Selector = 204 nach der Wartezeit**, nicht 401/404: ein veralteter Provider pollte sonst im
   Sekundentakt Fehler (log-watcher `api_scan`), und ein fremder Selector verrät nichts. Polls loggen auf Debug,
   `/api/external-engine/work` zählt als Systemaufruf (`SystemCallClassifier`). **Das Halten ist je Client-Adresse
   gedeckelt** (Codereview 2026-09-29, A4-005, `UnknownSelectorThrottle`, 30 je Minute × `RateLimitScale`): darüber
   sofort 429 ohne Halten — der Provider wartet nach einer 4xx-Antwort selbst (Backoff bis 10 s), nach einem 204 dagegen
   nicht, deshalb bleibt das Halten innerhalb des Fensters. Bekannte Selectors laufen ungedrosselt. Der Poll-Rumpf ist
   auf 4 KB gedeckelt (`ExternalEngineController.MaxAcquireBodyBytes`), vorher band MVC bis zu 30 MB in einen String.
6. **Scope-Zaun je Scope** (`PatScopeFenceMiddleware.AllowedPrefixesByScope`): ein Token `engine` erreicht NUR
   `/api/external-engine/*`, einer `extension` nur `/api/extension/*`. Ein neuer Scope braucht dort einen Eintrag,
   sonst erreicht er gar nichts.
7. **Die Selector-Menge liegt im Speicher** (`EngineSelectorDirectory`, alle 60 s bzw. bei Registrierung neu geladen):
   ein Poll kostet keine Datenbankabfrage. Ein Neustart der API verliert die Schlangen — laufende Uploads enden, der
   Browser setzt fort (Abriss-Regel des Live-Pfads), der Worker pausiert und startet neu.
8. `LichessEngineCredentials` trägt die Hintergrund-Liste auch OHNE Lichess-Token (`EncryptedToken` leer):
   `DELETE /api/engine/credentials` löscht dann nur den Token, die Zeile mit `rhe_`-Hintergrund-Engines bleibt.

**Vertragstest mit dem echten Provider**: `engine-provider/test/rookhub-broker.e2e.sh` (kein CI-Test — baut den
E2E-Stack und das Provider-Image, läuft rund zehn Minuten). Provider gegen `http://host.docker.internal:18099`
(Frontend-nginx-Hop), lichess.org/engine.lichess.ovh im Container auf 127.0.0.1 gesperrt; misst erste Zeile,
`pvs`, `bestmove`, zweiten Auftrag direkt danach und drei Minuten Last mit 12 Hintergrund-Engines; prüft die Logs
auf 503/ProviderTimeout, abgerissene Uploads und Lichess-Aufrufe. **Gemessen 2026-09-26** (Provider auf
`--cpus 6`, Live 2 Threads, Hintergrund je 1 Thread/64 MB): Live-Analyse Tiefe 18 × 3 Linien erste Zeile nach
0,55 s (kalte Engine), der zweite Auftrag direkt danach nach 0,05 s; drei Minuten Last mit 12 Hintergrund-Engines
(Tiefe 20, 2 Linien): 206 Aufträge angelegt, 182 fertig, 0 gescheitert, 11–18 je Engine; 22 Live-Proben daneben
erste Zeile im Mittel 0,11 s, höchstens 0,18 s; **0 × 503**, 0 abgerissene Uploads, keine Lichess-Erwähnung.

**Rollout** (nur auf Zuruf): Merge → NPM-Custom-Location auf Dev → Provider mit `ROOKHUB_URL` gegen Dev, Messung
wiederholen → Tag → NPM-Custom-Location auf Prod → in den Provider-Stacks einen ZWEITEN Dienst für den direkten Weg
DAZU (`.env` mit `ROOKHUB_URL`/`ROOKHUB_API_TOKEN`, 1 Live + Hintergrund-Engines) und den bestehenden Lichess-Dienst
auf die eine Live-Engine zurückfahren (0.538.0: beide Wege = zwei Container) und die Hintergrund-Liste im Profil auf
die `rhe_`-Engines. Die Lichess-Registrierungen bleiben liegen und stören nicht; der Lichess-Token bleibt für
Cloud-Engines und für das Analysebrett von lichess.org.
