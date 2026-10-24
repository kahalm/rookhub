# Hintergrund-Analyseaufträge (auth) — „diese Stellung rechnen, sobald die Hintergrund-Engine frei ist"

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Hintergrund-Analyseaufträge (auth) — „diese Stellung rechnen, sobald die Hintergrund-Engine frei ist"“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

**Aufbau (seit Zerlegung Z11, 2026-10-24):** `AnalysisJobWorker` = Schleife, Takt und die Schritte eines Laufs
(`RunJobAsync`: Engine auflösen → Broker anfragen → Stream → einordnen); der Stream-Lauf selbst (Stand, Sichern,
Stillstands-Wächter, Knotenziel) ist `JobStreamRun`; WELCHER Status am Ende gilt, entscheiden reine Funktionen —
`AnalysisJobRunEnd.ClassifyRunEnd(RunFacts)` / `ClassifyStall` und `BrokerFailurePolicy.OnStatus` (Golden-Tabelle
`AnalysisJobRunEndTests`); geschrieben wird er über `AnalysisJobTransitions`, der Engine-Wechsel reihum ist
`BackgroundEngineRotation`. Zeilen-Bausteine: `AnalysisJobStream`, `StreamTally` (je eigene Datei).

`AnalysisJobs`: eine Stellung mit Zieltiefe + Linienzahl, abgearbeitet vom `AnalysisJobWorker` (Hosted
Service, Singleton) über denselben Broker-Pfad wie live — **je ENGINE ein laufender Auftrag** (`_running`
ist nach `EngineId` verschlüsselt: ein Stockfish-Prozess kann nur EINE Suche; Aufträge auf verschiedenen
Engines laufen deshalb parallel), ohne den 10-Minuten-Deckel des Live-Proxys. **Vorrang der Live-Analyse**:
der `EngineActivityTracker` (Singleton; zählt Live-Streams je Nutzer FÜR DEN DECKEL und je Engine FÜR DEN
VORRANG, ersetzt das frühere statische `ActiveStreams`) feuert `LiveStarted(engineId)` beim Übergang 0→1
auf einer Engine → der Worker bricht genau den Auftrag auf DIESER Engine ab (Status `Paused`; der Provider
stoppt Stockfish, die Hashtabelle bleibt warm) und setzt erst nach `AnalysisJobs:IdleGraceSeconds` (20 s)
Ruhe dort fort; andere Engines bleiben unberührt. **Laufender Stand**: `CurrentDepth`/`CurrentNps` werden
bei JEDER empfangenen Zeile fortgeschrieben — auch bei den flacheren, die das Ergebnis nicht ersetzen.
Ohne das stand die Anzeige nach einer Fortsetzung minutenlang still (die Engine rechnet erst von Tiefe 1
wieder hoch, und ohne Persist lief nicht einmal die Zeit weiter). Für die SEKÜNDLICHE Anzeige kommt
`Services/Engines/AnalysisJobLive.cs` (Singleton) dazu: derselbe Stand ohne Datenbank, `Seconds` wächst aus der
Startzeit des Laufs (läuft also auch, während die Engine schweigt), abrufbar über `GET
/api/analysis-jobs/live`. Bewusst NICHT über häufigeres Persistieren gelöst — das hieße, für eine reine
Anzeige jede Sekunde eine Zeile zu schreiben. **Fortsetzungs-Regel**: der Broker liefert nach
jedem Neustart die flachen Iterationen erneut — übernommen wird nur eine Zeile mit `depth ≥ ReachedDepth`
(`AnalysisJobStream.ShouldPersist`); dieselbe Regel lässt bei „mehr Linien" das alte Ergebnis stehen, bis
die neue Suche es überholt. **Sticky hash**: `PickNextAsync` bevorzugt den zuletzt gelaufenen Auftrag
(typisch „weiter bis Tiefe 50" direkt nach Abschluss), sonst FIFO; `NextAttemptAt` = Backoff nach
Broker-Fehlern. Beim API-Start werden `Running`-Aufträge auf `Paused` gesetzt. Anpassungs-Regeln im
`AnalysisJobService.UpdateAsync`: Tiefe ↑ über das Erreichte → fertiger Auftrag zurück in die Queue;
Tiefe ↓ auf/unter das Erreichte → `Done`; Linien ↓ → gespeicherte `pvs` kürzen, kein Neustart; Linien ↑ →
laufende Suche abbrechen (`IAnalysisJobControl.Interrupt`), zurück in die Queue, Ergebnis bleibt Anzeige.
Ergebnis = letzte Broker-Zeile als opakes JSON (`ResultJson`), das Frontend mappt es wie den Live-Stream; die
Bewertung der Hauptvariante steht zusätzlich als `EvalText` in der Zeile, damit Listen die (großen) Roh-Zeilen
nicht laden müssen. **Eine gescheiterte Stellung ist nicht verloren** (0.460.0): ein gescheiterter Auftrag heißt fast
immer „die Engine war gerade nicht zu gebrauchen" und nicht „diese Stellung geht nicht" — eine
Stellung der Partie hat immer einen legalen Zug, Matt oder Patt kann sie gar nicht sein. Früher
schloss `IngestFinishedAsync` sie sofort mit LEERER Kandidatenliste ab; eine tote Engine löschte
damit stillschweigend Stellungen aus der Partie, die nie wieder gerechnet wurden (am 2026-09-10 an
25 Stück passiert, alle von Hand zurückgesetzt). Jetzt zählt `GameAnalysisPosition.FailedAttempts`
mit und die Stellung wird erneut eingereiht; erst nach `GameAnalysisDefaults.MaxPositionAttempts`
(3) ist Schluss, damit eine wirklich unlösbare Stellung nicht ewig im Kreis läuft.

**Grenzen und Terminalzustände (0.383.0, aus dem Review)**: `MultiPv` ist auf **1..5**
gedeckelt — das Protokoll-Maximum von `work.multiPv`, im Worker ein zweites Mal geklemmt (ein größerer Wert
wird vom Broker abgewiesen und der Auftrag liefe endlos in die Wiederholung). Der Worker bricht **nicht mehr
selbst** ab, wenn die Hauptvariante die Zieltiefe erreicht: die Engine bekommt `depth` als Limit und beendet den
Stream selbst — sonst blieben die Linien 2..K eine Iteration flacher (jede `pv` traegt ihre eigene Tiefe). Ein KURZER Lauf
ohne Tiefenfortschritt (< `AnalysisJobs:FruitlessMinRuntimeSeconds`, 60 s) erhoeht `FruitlessAttempts`; ab
`MaxFruitlessAttempts` (3) gilt der Auftrag als `Failed` (Matt-/Pattstellungen liefen sonst ewig im 30-s-Takt).
Ein LANGER Lauf ohne Fortschritt zaehlt bewusst NICHT: das ist eine gekappte Verbindung, kein Sackgassen-Beweis.
**Live aufgetreten**: der Wachhund des offiziellen Providers setzt seinen „zuletzt benutzt"-Stempel erst am
Stream-ENDE und terminiert jede Suche, die laenger als `--keep-alive` (Vorgabe 300 s) dauert — ab Tiefe 29 mit
5 Linien jede Iteration. Der Auftrag kam nie tiefer und galt nach drei Runden als gescheitert; Abhilfe beidseitig:
`KEEP_ALIVE` im Provider hoch (siehe `engine-provider/README.md`) UND diese Laufzeit-Unterscheidung hier. (Das betraf den bis
0.478.10 gepinnten Provider; der heutige erneuert den Stempel waehrend der Suche — die Unterscheidung bleibt fuer fremde
Provider.) Bleibt die erste Datenzeile binnen
`AnalysisJobs:FirstLineTimeoutSeconds` (300) aus, wird pausiert statt den Slot des Users unbegrenzt zu halten.
**Ein 503/504 des Brokers wechselt die ENGINE, statt zu warten** (0.475.6). Diese Antwort heisst
beim Broker: fuer diese Engine ist gerade kein Provider verbunden — eine Aussage ueber die Engine
und nicht ueber den Auftrag. Vorher stellte `BackoffAsync` den Auftrag zwei Minuten zurueck und
klopfte dann an dieselbe Tuer; am 2026-09-12 auf Prod pendelten damit 24 von 32 Auftraegen gegen
die zwoelf Engines der zweiten Maschine (309 solcher Antworten in 25 Minuten Log), waehrend die
vier der ersten die ganze Arbeit trugen. Gewechselt wird REIHUM (`NextEngineAfter`, dieselbe Regel
wie beim Stillstand) mit `AnalysisJobs:EngineSwitchBackoffSeconds` (5..600, Vorgabe 15) statt der
120 s. Die kurze Frist ist unbedenklich, weil der Worker je ENGINE nur einen Auftrag rechnet: es
sind hoechstens so viele Versuche gleichzeitig unterwegs wie Engines hinterlegt sind, nicht so
viele wie Auftraege offen sind — bei einem Totalausfall des Brokers also rund ein Abruf je
Sekunde. Steht die Engine nicht in der Hintergrund-Liste (von Hand gewaehlt), bleibt sie und es
gilt weiter der lange Backoff.

**Eine WIEDERHOLTE Bewertungszeile ist ein Lebenszeichen, kein Fortschritt** (0.475.1). Das
Lebenszeichen des RookHub-Providers bis 0.478.10 war die erneut gesendete letzte `info`-Zeile —
fuer den Worker war sie damit von echter Arbeit nicht zu unterscheiden. Der Waechter der ersten Zeile
war nach ihr entschaerft, einen zweiten gab es nicht: eine Engine, die NACH der ersten Zeile stehen
blieb, hielt ihren Auftrag unbegrenzt auf `Running`. Am 2026-09-12 auf Prod: Auftrag 14240 stand VIER
STUNDEN auf Tiefe 11, die wiederholte Zeile bis auf das Byte dieselbe (`time` 27 ms, `nodes` 24006,
Tempo eingefroren auf 889 111 Knoten/s), Rechenzeit lief weiter mit. Weil die Pumpe je Nutzer nur EINE
Partie fuettert (`IsOwnersTurnAsync`), standen dahinter 434 Partien und elf freie Engines — seit 01:51
keine einzige gerechnete Stellung.

`StreamTally` zaehlt eine zeichengleiche Wiederholung deshalb als Lebenszeichen (eine rechnende Engine
KANN sich nicht wiederholen, `time` und `nodes` wandern mit jeder Zeile), und der Waechter laeuft
weiter: er wird bei jeder FRISCHEN Zeile neu gestellt, mit `AnalysisJobs:StallTimeoutSeconds`
(300..86400, Vorgabe 1800). Die Frist ist bewusst weit — sie faengt nicht die langsame, sondern die
haengende Engine; eine tiefe Iteration mit fuenf Linien darf Minuten dauern. Faellt sie, wandert der
Auftrag REIHUM auf die naechste hinterlegte Hintergrund-Engine (`BackgroundEngineRotation.NextEngineAfter`):
ueber die kuerzeste Schlange gewaenne ausgerechnet die haengende, sie hat ja gerade nichts zu tun. Eine
Engine, die NICHT in der Hintergrund-Liste steht, wurde von Hand gewaehlt und bleibt. Ein Stillstand
zaehlt als Fehlversuch — anders als ein abgerissener Stream ist er eine Aussage ueber die Engine; der
Zaehler faellt bei jedem Lauf mit Tiefenfortschritt auf 0 zurueck, eine bloss langsame tiefe Suche kann
also nicht daran scheitern.

**Seit 0.478.11 kommt das Lebenszeichen als EIGENE Zeile** (`{"keepalive":true}`, vom offiziellen
Provider alle 15 s geschickt, vom Broker durchgereicht). `StreamTally.IsKeepalive` zaehlt es als
Lebenszeichen; es traegt keine Tiefe und stellt den Waechter deshalb nicht neu. Die
Wiederholungs-Regel bleibt stehen: auf fremden Rechnern laeuft der alte Provider weiter.

Unerwartete Ausnahmen setzen den Auftrag in einem EIGENEN Scope auf `Paused` zurueck (sonst stuende er bis zum
naechsten API-Start auf `Running` und wuerde nie wieder aufgegriffen). `TryCancel` faengt `ObjectDisposedException`
— zwischen Dictionary-Griff und `Cancel()` kann der Lauf selbst geendet haben, und der Wurf lief bis in den
Request-Thread des Live-Streams (dessen Zaehler waere fuer immer stehen geblieben). `EngineActivityTracker.Begin`
kapselt den `LiveStarted`-Aufruf entsprechend. `MaxJobsPerUser` (200) trimmt die aeltesten fertigen Auftraege.
**Frontend (0.380.0)**: Hintergrund-Engine in der Profil-Engine-Karte wählen (`PUT /api/engine/background`);
das Analysebrett filtert sie aus Live- und Vergleichs-Picker (`backgroundEngineId` aus `GET /api/engine/external`)
und bietet ein Uhr-Symbol „Im Hintergrund analysieren" (`AnalysisJobDialogComponent`: Tiefe/Linien vorbelegt aus
den Live-Einstellungen, legt den Auftrag direkt an) sowie den Sprung zur Seite `/analysis/jobs`
(`AnalysisJobsComponent`: Liste, 10-s-Poll solange Aufträge offen, aufklappen = Brett + gespeicherte Linien
ohne laufende Engine, Tiefe/Linien nachträglich ändern). Die Abbildung Broker-Zeile → Anzeige teilt sich der
Live-Pfad mit der Auftragsseite über `features/analysis/engine-lines.util.ts` (`mapBrokerLine`, `uciLineToSan`,
`toDisplayLines`, `formatElapsed`).
**Verbindung zu „Gemerkte Stellungen" (0.381.0)**: `AnalysisJobService.CreateAsync` legt je Stellung EINMAL eine
`RememberedPosition` an (Match über die ersten 4 FEN-Felder; ein Chessable-Eintrag bleibt unangetastet;
`SourceUrl = "/analysis/jobs"`, `CourseName = Titel`), und `RememberedPositionService.ListAsync` hängt jeder
gemerkten Stellung den jüngsten passenden Auftrag als `Analysis` an (Status, Tiefen, Linien, `EvalText` der
Hauptvariante via `AnalysisJobService.EvalTextOf`). Die Remembered-Seite rendert interne `sourceUrl`s (führender
`/`) als `routerLink`, zeigt die Analyse-Info als Chip-Zeile und bietet ohne Auftrag das Uhr-Symbol (gleicher Dialog);
sie frischt sich alle 10 s auf, SOLANGE ein Auftrag offen ist (`hasOpenJob`) — sonst ruht der Poll ganz.
**„Brett + aktueller Stand" (0.599.0, Wunsch 2026-09-29: „zeige Brett und aktuellen Stand der Analyse, und lass Job
weiterlaufen")**: auf der Remembered-Seite öffnen der Name „Analyse-Auftrag" und die Analyse-Zeile
`AnalysisJobViewDialogComponent` (statt auf `/analysis/jobs` zu springen) — Brett mit Pfeil je Linie (bester Zug grün),
Linien, erreichte/laufende Tiefe, Tempo, Zeit; fertig die endgültigen Linien. Er fragt `GET /api/analysis-jobs/{id}`
alle 5 s und, solange der Auftrag rechnet, `/live` im Sekundentakt; fertig/gescheitert ruht beides. **Bewusst ohne
Engine**: „Im Analysebrett öffnen" mit der Auftrags-Engine würde den Auftrag über den Live-Vorrang pausieren.
**„Im Analysebrett öffnen" (0.384.0)** hängt `engine`/`depth`/`lines` des Auftrags an die URL: das Brett wählt genau
diese Engine (auch die sonst ausgeblendete Hintergrund-Engine, einmalig und NICHT als Dauerwahl gespeichert) und
setzt die Suche fort, statt bei Tiefe 0 zu beginnen — der Provider hat die Stellung noch im Hash. Zahlen im
Engine-Kontext laufen einheitlich über `formatKiloNps`/`formatKiloNodes` (kN, Tausendertrennung, keine
Nachkommastellen) — vorher sprang die Einheit je nach Tempo zwischen N/s, kN/s und MN/s.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/analysis-jobs/live` | NUR der laufende Stand der gerade rechnenden Aufträge (`{ id, depth, nps, seconds }`) — aus dem Arbeitsspeicher, ohne DB; die Auftragsliste holt ihn im Sekundentakt (Literal-Route vor `{id:int}`) |
| GET | `/api/analysis-jobs/{id}` | EIN eigener Auftrag (gleiche Form wie in der Liste; 404 fremd/unbekannt) — für „Brett + aktueller Stand" auf den gemerkten Stellungen (0.599.0) |
| GET | `/api/analysis-jobs` | Eigene Aufträge (neueste zuerst) inkl. Status (`queued/running/paused/done/failed`), `reachedDepth`, `resultJson`, `secondsSpent`, `lastError`, `houseEngine` (rechnet auf fremder Rechenzeit — Tiefe/Linien/Engine nur für Admins änderbar) |
| POST | `/api/analysis-jobs` | Anlegen `{ fen, targetDepth (1–60), multiPv (1–5), engineId?, title? }` — `engineId` fehlend = Hintergrund-Engine aus dem Profil (keine → 400); ein GENANNTES `engineId` muss eine Engine des Engine-Besitzers sein (Hintergrund-Liste, eigene `rhe_`-Registrierung oder Lichess-Engine des Tokens), sonst 400 — erst danach verdrängt der Auftrag einen Hintergrund-Lauf auf dieser Engine (Codereview 2026-09-29, A4-003; ist Lichess gerade nicht erreichbar, wird angelegt, aber nichts verdrängt); FEN muss legal sein; max. 150 offene je User (`MaxOpenJobsPerUser`) |
| POST | `/api/analysis-jobs/batch` | Mehrfachauswahl `{ fens[] (1–200), targetDepth, multiPv, engineId? }` → `{ created[], skipped[{fen, reason}] }` mit `invalid` (keine legale FEN) / `duplicate` (nicht gescheiterter Auftrag zur Stellung existiert — auch innerhalb des Batches) / `limit` (Deckel offener Aufträge); nie 4xx wegen einzelner Stellungen — ein genanntes `engineId`, das nicht dem Nutzer gehört, ist dagegen ein 400 für den ganzen Batch (A4-003) |
| PUT | `/api/analysis-jobs/{id}` | Anpassen `{ targetDepth?, multiPv?, title?, engineId? }` nach den Regeln oben; ein Engine-Wechsel bricht den Lauf ab und reiht neu ein (Ergebnis bleibt, neue Engine startet mit kaltem Hash bei `ReachedDepth`); 404 wenn nicht eigener. **Auftrag auf der Haus-Engine** (`EngineOwnerUserId` gesetzt: Punktepartie, „Partie analysieren“, Vertiefung): Tiefe/Linien/Engine ändert nur ein Admin, sonst 400 — derselbe Riegel wie `POST /api/game-analyses` (Codereview 2026-09-29, A4-001); Titel, Neustart, Löschen bleiben erlaubt, unveränderte Werte zählen nicht als Änderung. Ein Engine-Wechsel nur auf eine Engine des Engine-Besitzers (sonst 400, A4-003) |
| POST | `/api/game-analyses/{id}/restart` | Die ganze Partie neu anstossen: alles Ungerechnete kommt frisch in die Warteschlange, aufgegebene Stellungen (`CandidatesJson = "[]"`) kommen zurueck. Die alten Auftraege werden GELOESCHT und neu angelegt — erst dadurch waehlt `PickBackgroundEngineAsync` erneut. Der Ausweg aus „Auftrag haengt an einer Engine, die aus ist": ein Auftrag wechselt von sich aus nie die Engine (am 2026-09-11 hingen fuenf Partien an ihren letzten Halbzuegen fest, waehrend fuenf freie Engines danebenstanden). Eine gepinnte `EngineId` bleibt |
| POST | `/api/analysis-jobs/{id}/restart` | Wieder einreihen (Fehlversuchs-Zähler + Backoff gelöscht, laufender Lauf abgebrochen); das ERGEBNIS bleibt — die Suche setzt bei `ReachedDepth` an. Ein Auftrag mit erreichtem Ziel bleibt `Done` |
| DELETE | `/api/analysis-jobs/{id}` | Löschen (laufende Suche wird abgebrochen) |

**Reihenfolge je Engine (0.817.1)**: `PickNextForEngineAsync` zieht in drei Stufen — erst normale Aufträge, dann die
Hintergrundarbeit eigener Partien (Vertiefung), zuletzt die Hintergrundarbeit des Stapels (Meister-, Vereins- und
Liga-Partien, `AnalysisJob.Batch`); innerhalb einer Stufe der zuletzt gelaufene (warme Hashtabelle), sonst der älteste.
`Batch` setzt die Pumpe beim Anlegen (`GameAnalysisOrigins.IsBatch`), die Migration `AnalysisJobBatch` hat die offenen
Aufträge einmal nachgetragen. Anlass 21.10.2026: die Vertiefung von Partie 117 stand hinter früher angelegten Liga-Stellungen.
