# Analyse-Verlauf + Sterne (0.603.0, auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Analyse-Verlauf + Sterne (0.603.0, auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-09-29: „merk dir eine History der letzten 20 Analysen von jedem User — diese sollen auch irgendwo ausgewählt
werden können" und „innerhalb einer Analyse will ich Stellungen mit einem Stern markieren, damit ich schnell zu diesen
springen kann". `Services/AnalysisHistoryService.cs`, Tabelle `AnalysisHistoryEntries`.
* Eine Analyse = Ausgangsstellung + Zugfolge (UCI, serverseitig nachgespielt — eine, die nicht geht, ist ein 400) + der
  Halbzug, an dem man stand + Sterne (Halbzüge, 0 = Ausgangsstellung) + Titel aus den PGN-Kopfdaten. Höchstens
  `MaxPerUser` 20 je Nutzer, die zuletzt angefassten bleiben; Konto löschen räumt ab.
* **Die Oberfläche führt die Kennung ihrer Analyse** (`AnalysisComponent.historyId`) und schickt sie bei jedem Speichern mit
  — sonst entstünde je Zug ein Eintrag. Ohne Kennung nimmt der Server einen Eintrag mit GLEICHER Stellung und GLEICHEN Zügen
  wieder (dieselbe Partie zweimal aus „Meine Partien" geöffnet). Eine neue Analyse beginnt bei Zurücksetzen, FEN laden,
  Stellung aufbauen, PGN laden und beim Öffnen eines Verlauf-Eintrags (dann mit dessen Kennung); der bisherige Stand geht
  dabei noch raus (`flushHistory`), eine verspätete Antwort gehört über `historySession` nicht mehr zur neuen.
* Gespeichert wird gedrosselt (`HISTORY_SAVE_MS` 1,5 s nach der letzten Änderung, beim Verlassen sofort), nur angemeldet und
  nicht die leere Grundstellung; nie zwei Anfragen gleichzeitig (die zweite hätte noch keine Kennung).
* Sterne: Knopf neben den Zugpfeilen bzw. Taste S, Sprungliste über der Zugliste („12...Nf6"), ★ in der Zugliste. Seit 0.604.0
  hängt ein Stern am KNOTEN des Zugbaums (auch in Varianten) und geht nur mit „ab hier löschen" weg. Abgemeldet gehen Sterne
  nur für die Sitzung.
* „Verlauf" neben der Überschrift → `analysis-history-dialog.component.ts`; `/analysis?history=<id>` öffnet einen Eintrag direkt.
* **Zugbaum** (0.604.0, Wunsch 2026-09-29 mit Screenshot des Lichess-Analysebretts: „so hätt ichs bei uns auch gern in der
  Analyse, inkl. der Variationen + Hauptlinie + Rechtsklick Variante hochstufen/löschen"). Reine Regeln in
  `features/analysis/analysis-tree.ts` (ohne Angular): `children[0]` ist IMMER die Fortsetzung, weitere Kinder sind Varianten;
  ein anderer Zug als die Fortsetzung wird eine Variante, derselbe Zug geht in die vorhandene (`addMove`). Das Brett hält
  `root` + `line` (= `lineThrough(aktueller Knoten)`: Weg dorthin und Fortsetzung) + `ply` — Pfeiltasten und die bisherigen
  Specs laufen unverändert auf `line`. Anzeige `analysis-move-tree.component.ts`: Hauptlinie als Tabelle Nr | Weiß | Schwarz mit
  der zuletzt gesehenen Bewertung je Zug (`node.evalText`, gesetzt, sobald die Leiste sie übernimmt — ab
  `EVAL_SETTLE_DEPTH`, Matt, Suchende), Varianten als Block, der die Tabelle unterbricht („1 | e4 | …", Block, „1 | … | c5"),
  Untervarianten in Klammern. Rechtsklick bzw. langer Druck (500 ms, eigene Uhr — iOS schickt kein `contextmenu`; Android
  schickt eins, die 800-ms-Sperre verhindert das doppelte Öffnen) öffnet ein `mat-menu` an einem fest positionierten Anker:
  Stern, Variante hochstufen (`promote`: EINE Stufe, an der tiefsten Verzweigung, an der der Weg nicht die Fortsetzung ist), zur
  Hauptvariante machen (`makeMainline`), ab hier löschen (`removeNode`; stand man darin, geht es beim Elternknoten weiter).
  `treeVersion` zählt jede Änderung am Baum, die Tabelle baut nur dann neu auf (nicht, wenn nur der aktuelle Zug wandert).
  PGN laden liest seit 0.604.0 Varianten mit (`parsePgnTree`: RAV, nur die erste Partie, `[FEN]`, Wörter ohne Zug wie „e.p."
  fallen weg, ein ungültiger Zug beendet nur seine Variante).
* **Gespeichert wird der Baum FLACH** (`AnalysisTreeDto`: `n[]` mit `p` = Index des Elternknotens davor, -1 = Wurzel, `u` UCI,
  `s` Stern, `e` Bewertung; `s` an der Wurzel = Ausgangsstellung markiert; `current` = Index des Knotens, an dem man stand).
  Verschachtelt bräuchte jeder Halbzug zwei JSON-Ebenen — System.Text.Json liest höchstens 64. `AnalysisHistoryService.CheckTree`
  spielt jeden Zug in der Stellung seines Elternknotens nach (Rochade → e1g1) und weist ab: Elternverweis nach vorn, derselbe
  Zug zweimal unter einem Knoten, mehr als `MaxNodes` 1500 Züge, 600 Halbzüge Tiefe, 60 Sterne. Unbrauchbare Bewertungen fallen
  still weg. `Moves` bleibt die HAUPTLINIE (Vorschau, Wiederfinden derselben Partie). Die Liste trägt den Baum nicht mit — das
  Brett holt einen gewählten Eintrag über `GET {id}`. Einträge von 0.603.0 (ohne `TreeJson`) kommen als Zugfolge zurück.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/analysis-history` | Die letzten 20 (zuletzt angefasste zuerst) `{ id, startFen, moves[] (Hauptlinie), ply, title, preview, moveCount, nodeCount, starCount, tree: null, current, createdAt, updatedAt }` |
| GET | `/api/analysis-history/{id}` | Ein eigener Eintrag MIT `tree` (404 fremd/unbekannt) |
| POST | `/api/analysis-history` | Speichern `{ id?, startFen, title?, tree: { s?, n: [{ p, u, s?, e? }] }, current }` → der Eintrag samt Kennung und Baum; 400 bei unlesbarer Stellung oder einem Baum, der nicht aufgeht |
| DELETE | `/api/analysis-history/{id}` | Eintrag löschen |
