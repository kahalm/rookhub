# Externe Engine (auth) — eigener Broker ODER Lichess-External-Engine-Protokoll als CLIENT

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Externe Engine (auth) — eigener Broker ODER Lichess-External-Engine-Protokoll als CLIENT“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Das Analysebrett kann statt der Browser-WASM-Engine eine **externe Engine** rechnen lassen: Stockfish auf
dem eigenen Rechner (offizieller Lichess-Provider, `lichess-org/external-engine`) oder eine gemietete
Cloud-Engine (stockfishcloud.com tritt selbst als Provider auf; Chessify lässt sich über sein
UCI-Tunnel-Binary vom Provider wrappen). **Zwei Wege** (seit 0.537.0): der Provider meldet sich DIREKT bei RookHub an (eigener Broker, empfohlen —
Abschnitt „Eigener Engine-Broker“ unten), oder RookHub spricht die offene Lichess-API als Client (bleibt für
Cloud-Engines, die sich nur bei Lichess registrieren können): der User hinterlegt einen Lichess-API-Token (Scope
`engine:read`, AES-verschlüsselt in `LichessEngineCredentials`), RookHub listet damit die auf DIESEM
Lichess-Konto registrierten External Engines und reicht Analyse-Anfragen an den Broker
(`engine.lichess.ovh`) durch — der ndjson-Stream geht 1:1 an den Browser.

**Warum als Server-Proxy und nicht direkt aus dem Browser**: das `clientSecret` einer Engine ist ein
Dauer-Geheimnis (wer es hat, kann fremde Rechenzeit verbrauchen) und bleibt deshalb serverseitig
(`LichessEngineService`, MemoryCache je `userId:engineId`, TTL 10 min). Nebeneffekt: die CSP
(`connect-src 'self'`) bleibt unangetastet und die eigene Engine ist auch vom Handy aus nutzbar.
Logik in `Services/LichessEngineService.cs`; URLs konfigurierbar (`Lichess:ApiUrl`/`Lichess:BrokerUrl`) —
zugleich die Vorbereitung auf den RookHub-EIGENEN Broker (seit 0.537.0, gleiche Endpoints, siehe unten).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/engine/credentials` | Status + maskierter Token (`{ hasCredentials, maskedToken }`) |
| POST | `/api/engine/credentials` | Lichess-Token setzen/überschreiben `{ token }` (max. 200 Zeichen) |
| DELETE | `/api/engine/credentials` | Token löschen |
| GET | `/api/engine/external` | Engines BEIDER Quellen — direkt angemeldete (`rhe_…`, `source: "rookhub"`, `online` = Provider hat in den letzten 30 s gepollt) zuerst, dann die des Lichess-Kontos (`eei_…`, `source: "lichess"`, `online: null`) — **ohne `clientSecret`** (`{ hasCredentials, tokenInvalid, lichessUnreachable, engines[] }`). Immer 200: `tokenInvalid` sagt, WARUM die Lichess-Liste leer ist (Lichess wies den Token ab), `lichessUnreachable`, dass Lichess nicht antwortete (die direkten Engines stehen trotzdem da) |
| POST | `/api/engine/external/{id}/analyse` | Analyse anfordern → **`application/x-ndjson`-Stream** (bei `rhe_…` vom eigenen Broker erzeugt, bei `eei_…` von Lichess durchgereicht — dieselben Zeilen). Body = `EngineAnalyseRequest` (`sessionId`, `initialFen`, `moves[]`, `multiPv`, GENAU EINES von `depth`/`movetime`/`nodes`, optional `threads`/`hash`); Threads/Hash werden serverseitig auf die von Lichess gemeldeten Engine-Maxima geklemmt, `variant` ist fest `chess`. Abbruch = Verbindung schließen (wandert über den Broker zum Provider) |

| PUT | `/api/engine/background` | Hintergrund-Engines für Analyseaufträge setzen `{ engineIds: [] }` (leere Liste = entfernen; jede muss registriert sein → sonst 404, höchstens 32 seit 0.669.2, davor 16). `GET /api/engine/external` liefert sie als `backgroundEngineIds` mit — der Live-Picker blendet sie aus. **MEHRERE sind der Sinn** (0.460.0): der Worker rechnet je ENGINE genau einen Auftrag, es laufen also so viele Aufträge nebeneinander, wie hier stehen. Mit einer einzigen ist die Warteschlange strikt seriell — auf Dev blockierte EIN zäher Auftrag (Tiefe 22, 5 Linien, 31 min) alle 49 wartenden. Ein neuer Auftrag geht auf die Engine mit der KÜRZESTEN Schlange (`AnalysisJobService.PickBackgroundEngineAsync`), nicht reihum: reihum trifft daneben, sobald eine Engine an einer zähen Stellung hängt |
