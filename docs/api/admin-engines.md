# Admin-Seite „Engines“ (0.946.0) — Übersicht, Warteschlange, wer rechnet was, Durchsatz

Wunsch 10.11.2026: „bau für admins eine page für die registrierten engines: übersicht / welche ist für background / was
wartet momentan / welche engine arbeitet momentan was ab. durchsatz und so“.

**Oberfläche:** Admin → Reiter „Engines“ (`/admin?tab=engines`, `features/admin/tabs/admin-engines.component.*`), nur
Admins (Tab-Recht `null` in `admin-tabs.ts`). Seit 0.951.0 pflegt der Admin hier die Hintergrund-Zuordnung je Engine und
Konto (unten); sonst lesend — der Pause-Schalter bleibt im Reiter „Hintergrundjobs“ und ist von hier verlinkt. Frischt sich alle 15 s auf, nur bei sichtbarem Tab (beim Zurückkehren sofort); ein gescheitertes
Auffrischen lässt den letzten Stand mit Hinweis stehen. Offline-Engines sind eingeklappt (Knopf „N offline einblenden“).
Im UI-Sweep als Admin-Reiter `admin-engines` (`tools/ui-sweep/routes.mjs`, `ADMIN_TABS`).

## Endpunkt (nur Admins, `[HasPermission(Permissions.AdminOnly)]`)

| Methode | Route | Beschreibung |
|---|---|---|
| GET | `/api/admin/engines/overview` | Übersicht, 10 s zwischengespeichert (`EngineOverviewService.CacheFor`) |
| PUT | `/api/admin/engines/{engineId}/background` | Rumpf `{ userId, enabled }`: Engine in die Hintergrund-Liste dieses Kontos aufnehmen / daraus entfernen (0.951.0) |

`Controllers/EngineBroker/AdminEnginesController.cs` → `Services/EngineBroker/EngineOverviewService.cs`, DTOs in
`DTOs/EngineBroker/EngineOverviewDtos.cs`. Antwort:

- `generatedAt`, `serverStartedAt` (ab da zählen die Broker-Zähler), `bucketMinutes` (10)
- `pause { paused, forcedByConfig, since, by }` — aus `BackgroundPause`
- `queue { queued, paused, running, oldestWaitingSince, byKind[{ kind, queued, paused, running, oldestWaitingSince }],
  gameAnalysesPending, gameAnalysesRunning }` — `kind` = `JobPriority` (`interactive`, `waiting`, `clubNew`,
  `clubBackfill`, `league`, `master`)
- `totals { hour, day, week }` — je Fenster `{ runs, done, failed, interrupted, retried, unavailable, timeouts, errors,
  busySeconds, avgJobSeconds, kiloNodesPerSecond }`
- `engines[]` — `{ id, name, source (rookhub|lichess|other), registered, ownerUserId, ownerName, online, lastSeenAt,
  registeredAt, maxThreads, maxHash, variants, backgroundFor[{ userId, name }], houseEngine, explicitOnly,
  state (working|live|idle|offline|unknown), liveStreams, providerPolling, brokerQueued, current, waitingQueued,
  waitingPaused, oldestWaitingSince, throughput { hour, day, week }, broker }`; sortiert rechnet → live → Leerlauf →
  unbekannt → offline. `current` = laufender Auftrag `{ jobId, ownerName, title, fen, kind, background, batch,
  targetDepth, targetNodes, multiPv, reachedDepth, depth, nps, nodes, seconds, runningSince, gameAnalysisId, ply,
  gameTitle }`. `broker` = Zähler des eigenen Brokers seit API-Start (`jobs, completed, withoutBestmove, requesterGone,
  providerGone, providerTimeouts, queueFull, ongoingExpired`; `providerTimeouts` + `queueFull` sind die 503).

## Hintergrund-Zuordnung pflegen (0.951.0)

Wunsch 10.11.2026: „lass mich … hier auch warten ob hintergrund oder nicht“. `PUT /api/admin/engines/{engineId}/background`
mit `{ userId, enabled }` ändert die Hintergrund-Liste (`LichessEngineCredentials.BackgroundEngineIds`) des Kontos
`userId` — gedacht für den BESITZER der Engine (`ExternalEngineRegistration.UserId`), nicht die Liste des Admins. Die Logik
liegt in `EngineSettingsService.SetBackgroundMembershipAsync` und teilt Bereinigung (`NormalizeBackgroundIds`), Deckel/Länge
(`CheckBackgroundList`), Token-Prüfung (`CheckLichessToken`), Registrierungs-Prüfung (`AllRegisteredAsync`) und Speichern
(`SaveBackgroundAsync`) mit `PUT /api/engine/background` aus dem Profil.

| Fall | Antwort |
|---|---|
| geändert | 200 `{ engineId, userId, enabled, changed: true, backgroundEngineIds }` |
| schon drin / schon draußen | 200 mit `changed: false`, nichts geschrieben, kein Log |
| Liste voll (neue Liste > `MaxBackgroundEngines` = 32) | 400 `{ message: "At most 32 background engines", code: "background_list_full" }` |
| Engine gehört dem Konto nicht / nicht registriert (nur beim Aufnehmen) | 404 `code: "engine_not_found"` |
| Konto gibt es nicht | 404 `code: "user_not_found"` |
| Lichess-Engine ohne Token beim Konto | 400 „No Lichess token stored“ (Token unlesbar: „Stored token unreadable“) |
| Kennung leer / > 64 Zeichen | 400 „Invalid engine id“ |
| Lichess nicht erreichbar (Prüfung einer `eei_`) | 502 |

- **Aufnehmen** hängt hinten an, die Reihenfolge der übrigen bleibt; eine Zeile ohne Token wird angelegt, wenn das Konto
  keine hat. Nur Engines, die DIESEM Konto gehören (`rhe_` auf das Konto registriert, `eei_` über dessen Lichess-Token).
- **Entfernen** geht immer — auch bei einer Liste über dem Deckel (Prod 10.11.2026: 33 Einträge bei Deckel 32 — kein
  Codepfad schreibt mehr als 32, die Liste kam von außen), ohne Token und für fremde oder nicht mehr registrierte Kennungen.
- **Deckel**: 32 wie im Profil und bei der Wiederaufnahme nach dem Aufräumen (`RestorePrunedBackgroundAsync`), begrenzt
  durch die 600-Zeichen-Spalte. Gemessen wird die Liste NACH dem Aufnehmen.
- **`PrunedBackgroundNames`**: der Name der Engine (wenn auf dieses Konto registriert) fällt in beiden Richtungen aus der
  Merkliste — eine von Hand entfernte Engine trägt die Wartung bei Neuanmeldung nicht wieder ein, eine von Hand aufgenommene
  nicht ein zweites Mal.
- **Wartende Aufträge** bleiben, wo sie sind — genau wie beim Bearbeiten der Liste im Profil: der Worker rechnet Aufträge
  über die Registrierung (`AnalysisJobEngineResolver`), nicht über die Liste, eine noch registrierte Engine arbeitet ihre
  Schlange also weiter ab. Der Rebalancer und der Reihum-Wechsel nach 503 fassen Aufträge auf einer Engine, die nicht in der
  Liste steht, nicht an (weder als Geber noch als Nehmer); automatisch verteilte neue Aufträge landen dort nicht mehr.
- Danach `EngineOverviewService.Invalidate()` (Cache-Schlüssel `admin-engine-overview`), Log
  `AdminEngines: Hintergrund {Action} engine={EngineId} konto={UserId} von admin={AdminId} ({AdminName})` mit `an`/`aus`.

## Woher die Zahlen kommen

| Kennzahl | Quelle | Abfrage |
|---|---|---|
| Engines, Besitzer, Threads/Hash, registriert seit | `ExternalEngineRegistrations` (klein, ≤ 64 je Konto) | ganze Tabelle, ohne `ClientSecret` |
| online / zuletzt da | `EngineSelectorDirectory` (Speicher), sonst `LastSeenAt` | — |
| Hintergrund-Zuordnung, Haus-Engine | `LichessEngineCredentials.BackgroundEngineIds` / `ShareAsHouseEngine` (eine Zeile je Konto) | `BackgroundEngineIds IS NOT NULL` |
| nur auf Anforderung | `AnalysisJobs:ExplicitOnlyEngineIds` | Konfiguration |
| wartend/pausiert/laufend je Engine und Art, älteste Wartezeit | `AnalysisJobs` | `Status IN (0,1,2)` gruppiert nach (EngineId, Status, Priority), Index (EngineId, Status, Priority, Background, Batch, CreatedAt) bzw. (Status, EngineId) |
| laufender Auftrag | `AnalysisJobs` `Status = 1` + `AnalysisJobLive` (Tiefe/Tempo/Knoten/Sekunden im Speicher) | Index (Status, EngineId) |
| Partie zum laufenden Auftrag | `GameAnalysisPositions.AnalysisJobId` → `GameAnalyses` | Index (AnalysisJobId, GameAnalysisId), dann PK |
| offene Partie-Analysen | `GameAnalyses.Status` Pending/Running | Index (Status) |
| Live-Streams | `EngineActivityTracker.LiveEngines()` | Speicher |
| wartende Polls (Provider da, nichts zu tun), Schlange im Broker | `EngineHub.QueueSnapshot()` | Speicher |
| Broker-Zähler seit Start | `EngineHub.Stats` (`BrokerStats`) | Speicher |
| Durchsatz 1 h / 24 h / 7 T | `EngineThroughputBuckets` + ungeschriebene Zuwächse aus `EngineThroughputRecorder` | `BucketStart >= …` gruppiert nach Engine, Index (BucketStart) |

Lichess-Engines (`eei_`) erscheinen nur mit Kennung, ohne Namen, Threads und Online-Status — die stehen bei Lichess, und
eine Abfrage je Konto mit dessen Token wäre für eine Übersicht zu teuer. Engines, die nur noch in Listen oder Aufträgen
stehen (Registrierung gelöscht), tragen „Registrierung entfernt“.

## Durchsatz-Zählung (`EngineThroughputBuckets`, neu in 0.946.0)

Fertige Aufträge verschwinden beim Einsammeln (`GameAnalysisIngest` löscht sie), die Stellungen tragen weder Engine noch
einen Index auf den Zeitpunkt — aus den vorhandenen Zeitstempeln lässt sich der Durchsatz je Engine nicht indexgestützt
ableiten. Deshalb zählt der `AnalysisJobWorker` jedes Laufende (`RunJobAsync`, nur wenn der Auftrag als laufend markiert
war) in `EngineThroughputRecorder` (Singleton, Speicher): Korb = (Engine, 10 Minuten). `EngineBrokerMaintenanceService`
schreibt die Zuwächse minütlich in die Tabelle (Schlüssel EngineId + BucketStart, Upsert per Addition; scheitert das
Schreiben, gehen sie beim nächsten Mal mit) und räumt stündlich Körbe älter als 8 Tage weg. Ein Neustart verliert
höchstens die letzte Minute. Höchstens 144 Zeilen je Engine und Tag.

| Ausgang | gezählt als |
|---|---|
| Ziel erreicht | `done` (+ `DoneJobSeconds` = `SecondsSpent` des Auftrags über alle Läufe → `avgJobSeconds`) |
| Auftrag endgültig gescheitert | `failed` |
| Pause ohne Fehlertext (Live-Vorrang, Admin-Pause, Ändern/Löschen, Abbruch vor den Kopfzeilen) | `interrupted` |
| Pause mit Fehlertext (Stream vor dem Ziel zu Ende, abgerissen) | `retried` |
| Broker 503/504 | `unavailable` |
| keine Kopfzeilen / keine erste Zeile / Stillstand | `timeouts` |
| Broker nicht erreichbar, andere Statuscodes, Ausnahmen | `errors` |

`busySeconds` = Wanduhr-Sekunden aller Läufe (Auslastung), `kiloNodesPerSecond` = Knoten der letzten Zeile je Lauf durch
die Laufzeit der Läufe mit Knotenangabe. Live-Analysen im Analysebrett zählen NICHT mit (nur der Hintergrund-Worker).

**Tests:** Unit `tests/RookHub.Api.Tests/EngineBroker/EngineOverviewTests.cs` (InMemory), Umschalten
`tests/RookHub.Api.Tests/EngineBroker/AdminEngineBackgroundTests.cs`, SQL-Übersetzung gegen echtes
MariaDB `tests/RookHub.Api.IntegrationTests/EngineOverviewSqlTests.cs` (Übersicht mit Daten in allen Tabellen, leerer
Stand, Schreiben/Addieren/Aufräumen der Körbe, Umschalten samt Merkliste; `ROOKHUB_TEST_MYSQL` auf einen Wegwerf-Container).
