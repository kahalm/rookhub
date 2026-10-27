# „Ohne Engine analysieren“ — eigene Linien, Bewertung erst auf Zuruf (0.855.0)

Wunsch 2026-10-27: „wenn ich auf *Partie analysieren* klicke (egal von wo), soll ein Popup kommen: mit/ohne Engine. Ohne soll
keine Engine-Bewertung zeigen, sondern mich Linien durchklicken lassen. Diese Linien werden nur für mich gespeichert und im
Hintergrund analysiert … am Ende einschaltbar, dann zeigt er für alle Linien die Bewertungen (+ weitere Züge mit Live-Engine).“
Dazu: „Entscheidung merken“ im Fenster, Vorgabe im Profil umstellbar. Entscheidungen des Users (Rückfrage 2026-10-27): die
Partie-Analyse läuft im Hintergrund und bleibt dabei verdeckt; EIN Baum je Partie; beim Aufdecken ein Vergleich.

## Ablauf

1. **Auswahl** — `AnalyzeGameService.analyze(url, status, pgn)` (Partienliste, Partieseite, Nachspiel-Dialog) fragt über
   `AnalyzeChoiceDialogComponent` „Mit Engine“ / „Ohne Engine“ mit Kästchen „Entscheidung merken“. Die Vorgabe
   (`ask` | `engine` | `blind`) liegt im Anzeige-Zustand je Nutzer (`/api/view-state/analyze.choice`, erlaubter Schlüssel in
   `ViewStateService.AllowedKeys`) und gilt auf allen Geräten; umstellbar im Profil (`AnalyzeChoiceCardComponent`).
   Mit Engine = wie bisher. Ohne Engine = die Partie wird trotzdem still eingeworfen (Kurve/Klassen sind dann später auf der
   Partieseite da) und das Analysebrett öffnet `/analysis?blind=1` mit dem PGN im Router-State. Zusätzlich im ⋮ der Partieseite
   „Ohne Engine analysieren“ (Engine-Gruppe) — auch für schon analysierte Partien und Vereinspartien.
2. **Analysebrett ohne Engine** (`LineStudySession`, `line-study-session.ts`): Engine aus (nur für diese Seite, nicht als
   Einstellung), ausgeblendet sind Bewertungsleiste, Engine-Karte samt Tablebase, Maia, Explorer, ⋮ der Stellung (tiefe
   Analyse/Aufträge), FEN/PGN-Karte und „Zurücksetzen“. Gespeichert wird in den Linien-Baum statt in den Analyse-Verlauf
   (`scheduleSave`). Kennung der Partie: `lineStudyKey(startFen, Partiezüge)` (FNV-Hash, Zugzähler egal) — dieselbe Partie aus
   jeder Quelle landet im selben Baum; beim Wiederöffnen wird der gespeicherte Baum aufs Brett gelegt.
3. **Hintergrund** (`LineStudyWorker`, alle 20 s `LineStudyService.StepAsync`): jede Stellung des Baums ist eine
   `LineStudyPosition` (Schlüssel wie die „+“-Markierung). Bis zu 20 offene Aufträge je Nutzer (`MaxOpenJobsPerUser`), Tiefe 20,
   zwei Linien, Hintergrund-Engine des Nutzers bzw. die Haus-Engine (`EngineOwnerResolver`), NICHT in „Gemerkte Stellungen“.
   Fertige Aufträge wandern als Kandidaten in die Stellung und werden gelöscht (die Auftragsliste hält nur 200 je Nutzer);
   gescheiterte bis zu 3-mal neu. Matt/Patt ist sofort „gerechnet“ (ohne Kandidaten). Ohne Engine bleibt alles „ausstehend“.
4. **Engine einschalten** (`POST …/reveal`): erst jetzt liefert `GET …/evals` Zahlen — je KNOTENINDEX des gespeicherten Baums
   (Client und Server bilden den Stellungsschlüssel nicht zeichengleich, die flache Reihenfolge schon; die Sitzung hält die Knoten
   des zuletzt gespeicherten Baums fest). Bewertungen stehen an jedem Zug der Zugliste, die Live-Engine läuft wieder für weitere
   Züge, und die Karte zeigt den **Vergleich** (`compareLines`, `line-study.util.ts`): je EIGENEM Zug (alles außer den
   Partiezügen) die Klasse wie im Rückblick (`classify` über die Gewinnchance), Zahl der Bestzüge, und für jede Ungenauigkeit,
   jeden Fehler und groben Fehler der bessere Zug — Klick springt hin. Solange noch gerechnet wird, fragt die Sitzung alle 15 s.

## Endpunkte (auth)

| Methode | Route | Zweck |
|---|---|---|
| GET | `/api/line-studies?key=` | der Baum zur Partie (404 = keiner) |
| PUT | `/api/line-studies` | `{ gameKey, startFen, title, tree, current }` anlegen/ersetzen (Baum geprüft wie im Analyse-Verlauf, `CheckTree`; 400 bei ungültigem) |
| POST | `/api/line-studies/{id}/reveal` | Engine einschalten |
| GET | `/api/line-studies/{id}/evals` | `{ revealed, analyzed, total, nodes[{ index, cp, mate, bestUci, depth }] }` — `nodes` erst nach dem Einschalten |

Höchstens 300 Bäume je Nutzer (älteste fliegen). Kontolöschung räumt ab (`AnalysisDataEraser`). Tests: `LineStudyServiceTests`,
Specs `line-study.util`, `line-study-session`, `analyze-choice`.

## Nachträge 0.856.0 (Rückmeldungen 2026-10-27 nach dem ersten Einsatz)

* **Frage auch bei schon analysierten Partien** („club-games/226 — keine Frage wegen Analysieren“): die „Analyse“-Knöpfe in
  LeagueHub (Vereinsliste, Aufstellung, Brett) springen auf `club-games/{id}?ask=1`; die Partieseite fragt dann nach dem Laden
  (`askAnalyzeMode`) bzw. wendet die Vorgabe an — „ohne Engine“ geht aufs Analysebrett, „mit Engine“ bleibt. Der Parameter wird
  danach aus der Adresse genommen. Über einen Teilen-Link nicht (ohne Konto kein „ohne Engine“).
* **Schmale Leiste statt großer Karte** („der große Block rechts oben stört“): eine Zeile — Symbol, „Ohne Engine“, „12/68
  gerechnet“, kleiner Knopf „Engine einschalten“; die Erklärung steht im Tooltip. Nach dem Aufdecken die Zähler des Vergleichs
  („7/12 beste · ?! 2 · ? 1“), ein Tipp klappt die Liste auf (höchstens 220 px, scrollt).
* **Eigene Bewertungen und Kommentare** („Bewertungsbuttons analog zum Kalk-Modus fehlen, auch Kommentare zu Stellungen“): unter
  der Zugliste die Karte des Kalkulations-Modus (`CalcAnnotateCardComponent`) für den aktuellen Zug — Zug-Symbol (!!, !, !?, ?!,
  ?, ??), Stellungsbewertung (=, ⩲, ±, +−, ∞ …) und Kommentar (≤ 500 Zeichen). Sie hängen am Knoten (`glyph`, `evaluation`,
  `comment`; flach `g`/`v`/`c`), der Server behält bekannte Symbole und kürzt den Kommentar (`AnalysisHistoryService.CheckTree`,
  gilt damit auch für den Analyse-Verlauf). Die Zugliste zeigt Symbol und Bewertung am Zug, den Kommentar in Varianten kursiv,
  in der Hauptlinie als Sprechblasen-Symbol mit Tooltip.

