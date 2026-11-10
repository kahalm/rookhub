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
| Liste voll (neue Liste > `MaxBackgroundEngines` = 64, bzw. passt nicht in die Spalte) | 400 `{ message: "At most 64 background engines", code: "background_list_full" }` |
| Engine gehört dem Konto nicht / nicht registriert (nur beim Aufnehmen) | 404 `code: "engine_not_found"` |
| Konto gibt es nicht | 404 `code: "user_not_found"` |
| Lichess-Engine ohne Token beim Konto | 400 „No Lichess token stored“ (Token unlesbar: „Stored token unreadable“) |
| Kennung leer / > 64 Zeichen | 400 „Invalid engine id“ |
| Lichess nicht erreichbar (Prüfung einer `eei_`) | 502 |

- **Aufnehmen** hängt hinten an, die Reihenfolge der übrigen bleibt; eine Zeile ohne Token wird angelegt, wenn das Konto
  keine hat. Nur Engines, die DIESEM Konto gehören (`rhe_` auf das Konto registriert, `eei_` über dessen Lichess-Token).
- **Entfernen** geht immer — auch bei einer Liste über dem Deckel, ohne Token und für fremde oder nicht mehr registrierte
  Kennungen.
- **Deckel** (0.952.0): 64, Spalte `varchar(1200)` (64 × 17 − 1 = 1 087 Zeichen + Reserve). EINE Regel für alle Wege, die
  die Liste vergrößern — Profil, dieser Schalter, Wiedereintragen nach dem Aufräumen (`RestorePrunedBackgroundAsync`):
  `LichessEngineCredential.CheckBackgroundList(next, currentCount)` lehnt ab, was über 64 liegt UND länger wird als bisher,
  und was nicht in die Spalte passt; `SetBackgroundEngines` wirft statt abzuschneiden. Ist die Liste beim Wiedereintragen
  voll, bleibt sie, wie sie ist (Warnung `… NICHT wieder eingetragen — Hintergrund-Liste voll …`).
- **Was mit der Listenlänge skaliert** (bei 64 geprüft): der `AnalysisJobWorker` startet je Engine EINEN Lauf ohne
  weiteren Deckel — 64 Hintergrund-Engines = bis zu 64 gleichzeitige Broker-Streams, jeder hält beim Fortschritt kurz einen
  DB-Scope (MySqlConnector-Pool Vorgabe 100); `MasterAnalysisScheduler` füllt so viele Meisterpartien vor, wie Engines
  nutzbar sind; `EngineAvailability.UsableAsync` macht je Aufruf zwei Abfragen mit IN-Liste ≤ 64 (Index UserId); der
  Rebalancer ist O(Engines × Wartende) im Speicher. Nichts davon ist bei 64 teuer; die Pool-Grenze ist die erste, die bei
  deutlich mehr Engines zählen würde.
- **Vorgeschichte 10.11.2026**: Prod hatte 33 Einträge bei Deckel 32 und im Profil ließ sich nichts mehr ankreuzen
  („can not save“). Nachgewiesen: KEIN Codepfad — die Liste wurde am 09.11. 20:12:39 UTC per SQL-UPDATE einer
  Claude-Sitzung um 17 PC-Engines ergänzt (16 → 33, geprüft wurde nur die Spaltenlänge 600); kein PUT, kein Log
  „wieder als Hintergrund-Engine eingetragen“. Danach von Hand auf 35 (594 Zeichen).
- **`PrunedBackgroundNames`**: der Name der Engine (wenn auf dieses Konto registriert) fällt in beiden Richtungen aus der
  Merkliste — eine von Hand entfernte Engine trägt die Wartung bei Neuanmeldung nicht wieder ein, eine von Hand aufgenommene
  nicht ein zweites Mal.
- **Wartende Aufträge** bleiben, wo sie sind — genau wie beim Bearbeiten der Liste im Profil: der Worker rechnet Aufträge
  über die Registrierung (`AnalysisJobEngineResolver`), nicht über die Liste, eine noch registrierte Engine arbeitet ihre
  Schlange also weiter ab. Der Rebalancer und der Reihum-Wechsel nach 503 fassen Aufträge auf einer Engine, die nicht in der
  Liste steht, nicht an (weder als Geber noch als Nehmer); automatisch verteilte neue Aufträge landen dort nicht mehr.
- Danach `EngineOverviewService.Invalidate()` (Cache-Schlüssel `admin-engine-overview`), Log
  `AdminEngines: Hintergrund {Action} engine={EngineId} konto={UserId} von admin={AdminId} ({AdminName})` mit `an`/`aus`.

## Übersichten (0.953.0): Tokens, Zeitpläne, Tempo normal/Schonzeit, Leerlauf-Grund, Hinweise, Maschinen

Wunsch 10.11.2026: „zeig auf der seite zusätzlich eine übersicht an: tokens (nur für engine) – registrierter name dazu.
letzter zugriff. einstellungen zu schedule. durchschnittliche kn/s normal/schonzeit. was dir sonst noch einfällt“. Alles
steckt in derselben Antwort (`GET /api/admin/engines/overview`, 10-s-Cache):

| Feld | Inhalt | Quelle |
|---|---|---|
| `tokens[]` | `{ id, name, prefix, ownerUserId, ownerName, createdAt, lastUsedAt, expiresAt, expired, engines[{ id, name, online }] }` — alle API-Tokens mit Bereich `engine`, NIE Token oder Hash, nur das Präfix wie im Profil | `UserApiTokens` (eine Handvoll Zeilen) |
| `engines[].apiTokenId` | Token, mit dem sich die Engine zuletzt angemeldet/geändert hat | neue Spalte `ExternalEngineRegistrations.ApiTokenId` |
| `engines[].schedule` | `{ slot, total, scope, rule, timeZone, reportedAt, openNow, percentNow }` | `EngineClientSchedules` (eine Zeile je gemeldeter Engine), `EngineAvailability.IsOpen` |
| `machines[]` | `{ key, name, ownerUserId, ownerName, source, apiTokenId, tokenName, engineIds, online, total, working, threads, doneDay, busyPercentDay, speed, schedule }`; `schedule` = `{ rule, timeZone, scope, total, reportedAt, reported, openNow, percentNow, nextChangeAt, nextPercent }` | Gruppierung im Speicher |
| `engines[].speed`, `machines[].speed` | `{ normal, reduced }` Ø kN/s der letzten 7 Tage | Durchsatz-Körbe |
| `engines[].idleReason` | nur im Zustand `idle`: `paused`, `explicitOnly`, `notInList`, `scheduleOff`, `quietHours`, `nothingWaiting`, `waitingElsewhere` | `EngineAvailability.UsableAsync` |
| `hints[]` | `{ kind, engineId, name, userId, count, max, since, detail }`, `kind` ∈ `stuckJobs`, `notInList`, `listCap`, `staleToken` | siehe unten |
| `quiet` | `{ enabled, quietNow, quietUntil, timeZone, windows[{ day (0 = So), from, to }] }` | `QuietHours` (`TextLlm:QuietHours`) |
| `queueEmptyInHours` | Wartende ÷ fertige Aufträge der letzten Stunde, nur ab 10 fertigen (`EstimateMinDonePerHour`) | — |
| `backgroundCap` | 64 | `LichessEngineCredential.MaxBackgroundEngines` |

- **Token ↔ Registrierung** war bisher NICHT verknüpft. Schlankste Lösung: `ExternalEngineRegistrations.ApiTokenId`
  (Migration `EngineRegistrationApiTokenId`, nullable, bewusst ohne Fremdschlüssel — ein gelöschter Token soll die
  Registrierung nicht anfassen). Gesetzt beim An-/Ummelden des Providers (`POST`/`PUT /api/external-engine`) aus dem
  Claim `api_token_id` des PAT-Handlers; ohne Claim bleibt der Wert stehen. Kein Schreiben je Poll. Registrierungen von
  vor 0.953.0 tragen `null`, bis der Provider sich neu meldet (Neustart).
- **„Letzter Zugriff“** = `UserApiTokens.LastUsedAt`, gedrosselt auf `ApiTokenService.LastUsedThrottle` (5 min) — auf 5
  Minuten genau (steht in der Hilfe). Ein pollender Provider hält ihn also aktuell.
- **Maschine** = gleicher Besitzer + gleicher Engine-Token + gleicher Namensstamm (`EngineOverviewSchedules.Stem`:
  „RookHub Gross Hintergrund 12“ → „RookHub Gross“; Nummer nur nach Leerzeichen/`#`, „Lc0“ bleibt). Lichess-Engines je
  Konto eine Gruppe. Kopf: online x/y, rechnet, Threads, 24 h fertig, Auslastung (Rechensekunden 24 h ÷ registrierte
  Engines × 86 400), Ø kN/s normal/Schonzeit, Zeitplan.
- **Zeitplan**: jüngste Meldung der Maschine gibt Regel/Zone vor; „jetzt offen x von y“ über `EngineAvailability.IsOpen`
  je Engine; nächstes Umschalten = erste Minute binnen 7 Tagen mit anderem Prozentsatz (`EngineScheduleRules.PercentAt`,
  höchstens 10 080 Schritte). Ohne Meldung: „kein Zeitplan — es gelten die RookHub-Sperrzeiten für Stapel-Aufträge“,
  dazu oben die Fenster und ob gerade Sperrzeit ist.
- **Normal/Schonzeit**: die Körbe der letzten 7 Tage werden EINMAL geladen (vorher drei Summen-Abfragen) und im Speicher
  in Stunde/Tag/Woche und normal/Schonzeit geteilt. Schonzeit = der Zeitplan der Engine sah zum Korb-Beginn weniger als
  100 % vor, ohne Zeitplan: RookHub-Sperrzeit. Eingeordnet wird nach der HEUTIGEN Regel (steht in der Hilfe). Bei
  ~60 Engines × 1 008 Körben/Woche höchstens ~60 000 schmale Zeilen je 10 s, nur solange ein Admin den Reiter offen hat.
- **kN/s geprüft**: bisher Knoten der letzten Zeile ÷ Wanduhr des Laufs. Die Knoten stimmen (jeder Lauf ist eine neue
  Suche, auch bei fortgesetzten Aufträgen; beim Knotenziel endet die Suche am Ziel), die Wanduhr aber zählt das Warten auf
  die Kopfzeilen, den Start der Engine und das Hochladen mit — das Tempo lag zu niedrig. Seit 0.953.0 geht die SUCHZEIT
  laut Engine ein (Knoten ÷ gemeldetes nps der letzten Zeile, `EngineThroughputRecorder.Record(searchSeconds)`), also
  zeitgewichtet über alle Läufe; ohne nps wie bisher die Wanduhr. Alte Körbe behalten ihre Wanduhr-Werte.
- **Leerlauf-Grund** (nur `idle`): Pause → nur auf Anforderung → nicht in der Liste des Besitzers → Zeitplan aus
  (`UsableAsync(batch: false)` nimmt sie nicht, Meldung vorhanden) → für den Besitzer wartet nichts → nur Stapel wartet
  und `UsableAsync(batch: true)` nimmt sie nicht (Sperrzeit bzw. Zeitplan) → sonst „Aufträge warten auf anderen Engines“
  (der Rebalancer verteilt im nächsten Takt). Je Besitzer zwei `UsableAsync`-Aufrufe (zwei kleine IN-Abfragen).
- **Hinweise**: `stuckJobs` — wartende Aufträge auf einer Engine, die offline ist oder nicht in der Liste ihres Besitzers
  steht (höchstens 10 Engines, je eine Abfrage nach dem jüngsten `LastError` über Index EngineId/Status; `detail` =
  `offline|…` bzw. `notInList|…`); `notInList` — registrierte Hintergrund-Engines einer Maschine (Zeitplan-Platz > 1 oder
  „Hintergrund/Background“ im Namen) in keiner Liste; `listCap` — Liste eines Kontos zur Hälfte voll oder mehr („35 von
  64“); `staleToken` — Engine-Token seit > 30 Tagen (`StaleTokenAfter`) ohne Zugriff (bzw. nie benutzt).

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
`tests/RookHub.Api.Tests/EngineBroker/AdminEngineBackgroundTests.cs`, Deckel `BackgroundListCapTests.cs`, Übersichten
`EngineOverviewInsightsTests.cs`, SQL-Übersetzung gegen echtes
MariaDB `tests/RookHub.Api.IntegrationTests/EngineOverviewSqlTests.cs` (Übersicht mit Daten in allen Tabellen, leerer
Stand, Schreiben/Addieren/Aufräumen der Körbe, Umschalten samt Merkliste, 64 Kennungen in der Spalte, Übersichten; `ROOKHUB_TEST_MYSQL` auf einen Wegwerf-Container).
