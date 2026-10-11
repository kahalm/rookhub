# Knotenziel + „nur auf Anforderung"-Engines (2026-10-05, Phase 1 der Lc0-Zweitprüfung, UNGETAGGT)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Knotenziel + „nur auf Anforderung"-Engines (2026-10-05, Phase 1 der Lc0-Zweitprüfung, UNGETAGGT)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

- `AnalysisJob.TargetNodes` (long?, Migration `AnalysisJobTargetNodes`), `CreateAnalysisJobRequest.TargetNodes` 1 000..50 000 000.
  Gesetzt: Worker schickt `nodes` statt `depth` (genau EIN Limit), setzt `ReachedDepth` je Lauf auf 0, bricht selbst ab und gilt
  als fertig bei Ziel (`AnalysisJobStream.NodeGoalMet`, 5 % Spielraum wenn die Engine selbst endet). `TargetDepth` zählt dann nicht;
  `UpdateAsync`/`RestartAsync` fassen fertige Knotenaufträge nicht an.
- Config `AnalysisJobs:ExplicitOnlyEngineIds` (Liste von Engine-Ids; leer = wie bisher, NICHT in Prod gesetzt): solche Engines fallen
  aus der automatischen Wahl (`PickBackgroundEngineAsync`), aus dem Failover (`NextEngineAfter`, weder hinein noch heraus) und aus der
  Platzzahl der Meisterpartien-Analyse; mit ausdrücklicher `EngineId` rechnen sie. Helfer `Services/Engines/ExplicitOnlyEngines.cs`.
- Offen: `EngineOwnerResolver` zählt sie noch als Hintergrund-Engine (nur Lc0 eingetragen → Besitzer gilt als „hat Engine", automatische Wahl wirft).
- **Konvergenz-Stufen einer Knotenanalyse** (2026-10-05, UNGETAGGT): bei Aufträgen mit `TargetNodes` hält der Worker aus denselben Stream-Zeilen
  (keine Mehrrechnung) je Schwelle `AnalysisJobs:SnapshotStepNodes` (Vorgabe 10 000, 0 = aus) den Stand fest — `NodeStepRecorder`
  (`Services/Engines/NodeSteps.cs`, rein): Schwelle T bekommt die LETZTE Zeile mit `nodes ≤ T`, aber nur, wenn sie höchstens eine Schrittweite alt ist
  (sonst fehlt die Stufe, nichts wird erfunden); am Ende steht die letzte Zeile zusätzlich unter dem Knotenziel selbst (mit der tatsächlich
  erreichten Knotenzahl). Eine Wiederaufnahme ersetzt eine Stufe nur mit MEHR Knoten. Gespeichert als JSON `AnalysisJobs.NodeStepsJson`
  (`[{"t","n","m","cp"|"mate"}]`, Bewertung aus Sicht der Seite am Zug, mit dem Persist-Intervall gebündelt) und beim Ingest nach
  `GameAnalysisPositions.NodeStepsJson` mitgenommen, bevor der Auftrag gelöscht wird. Auswertung `Convergence.Evaluate` (rein): Bezug je Stellung
  = ihre letzte Stufe, Stellungen mit nur einer Stufe fehlen; je Schwelle Anteil gleicher Zug, Median und 90. Perzentil des Abstands in cp (Matt ±1000)
  und in Gewinnchance-Prozentpunkten, Zugwechsel gegenüber der Vorstufe, Matt-gegen-kein-Matt getrennt gezählt. `GET /api/game-analyses/{id}/convergence`
  (nur eigene Analyse, sonst 404); Tabelle „Konvergenz" in `game-analysis-detail.component.ts` (nur bei Knotenanalysen). **Dichte der Stufen hängt an der Engine**:
  der Broker (`EmitBuilder`) gibt je VOLLSTÄNDIGEM MultiPV-Satz einer `info`-Folge eine Zeile weiter — wie oft Lc0 so einen Satz meldet, steht nirgends im Code.
- **Taktik-Ernte, Zweitprüfung (Phase 2, Plan `TACTIC_LC0_PLAN_2026-10-05.md`)**: Config `TacticHarvest:SecondEngineId` (leer = aus, alles wie
  vorher), `TacticHarvest:SecondEngineNodes` (Standard 50 000), MultiPV 3, Aufträge mit `EngineId` + `TargetNodes`. Läuft NACH der
  Erstprüfung (Status `Done`, `SecondAgrees == null`): Stufe 0 Aufgabenstellung (bester Zug gleich + `IsUnique`, `TacticHarvest.Agree`),
  Stufe 1 Stellung vor dem Fehler (`Detect` mit den Zweitwerten), ab Stufe 2 spätere Löserzüge (uneinig → Lösung endet davor, bei
  Matt-Aufgaben `Disputed`). Uneinig → `TacticCandidateStatus.Disputed` + `RejectReason` (`lc0Move`/`lc0NoBlunder`/`lc0Line`/`lc0Failed`/`lc0Fen`).
  `PublishAsync` nimmt bei eingeschalteter Zweitprüfung nur `SecondAgrees == true`. Steht die Engine nicht in der Hintergrund-Liste des
  Besitzers, wartet die Zweitprüfung (Warnung im Log). Zähler: `SELECT Status, COUNT(*) FROM TacticCandidates GROUP BY Status`.
