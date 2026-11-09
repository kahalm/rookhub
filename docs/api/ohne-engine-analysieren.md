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
| PUT | `/api/line-studies` | `{ gameKey, startFen, title, tree, current, createOnly? }` anlegen/ersetzen (Baum geprüft wie im Analyse-Verlauf, `CheckTree`; 400 bei ungültigem); `createOnly: true` = nur anlegen, 409 wenn es den Baum schon gibt (F2-002) |
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


## Nachtrag 0.860.1 — Bewertung am Zug statt Liste oben

Wunsch 2026-10-27 mit Screenshot: „die Auflistung oben bringt gar nichts — besser vermutlich unten hinter jeden Zug die Eval
schreiben“. Die Leiste zeigt nach dem Aufdecken nur noch die Zähler („84/135 beste · ?! 7 · ? 7 · ?? 16“); das Urteil über
jeden eigenen Zug ab Ungenauigkeit steht als farbiges ?!/?/?? direkt am Zug in der Zugliste (`AnalysisNode.verdict`, gesetzt von
`LineStudySession.applyEvals`, Tooltip „→ besserer Zug“), die Bewertung steht jetzt auch in Varianten hinter dem Zug. Mit
„Engine ausschalten“ (0.859.0) blendet die Zugliste beides wieder aus (`hideEngine`).

## Nachtrag 0.895.0 — freies Brett ohne Engine, oben umschaltbar

Wunsch 2026-11-07: „einfacher Weg zwischen `/analysis?blind=1` und `/analysis` zu wechseln“. Bisher wirkte `?blind=1` nur
mit einer Partie (und Konto) — ohne blieb das Brett normal. Jetzt: `?blind=1` ohne Linien-Baum = `noEngine` im
`AnalysisComponent` (dasselbe Ausblenden wie oben, `blind`-Getter; Engine aus, die gespeicherte Einstellung bleibt
unberührt und kommt beim Zurückschalten wieder). Knopf „Ohne Engine“ / „Mit Engine“ in der Kopfzeile (`toggleNoEngine`)
schaltet um und ersetzt die Adresse (`?blind=1` dazu bzw. weg, `replaceUrl`). Mit Linien-Baum ist der Knopf weg — dort
gelten „Engine einschalten/ausschalten“ der Karte. Kein Linien-Baum, kein Hintergrund: das freie Brett speichert wie sonst
in den Analyse-Verlauf.

## Nachtrag 0.898.0 — eine Partie, ein Baum, Modus jederzeit umschaltbar

Wunsch 2026-11-08 (Screenshot des Auswahl-Dialogs): „zwischen den beiden Modi einfach wechseln können; Linien, die ich ohne
Engine analysiert habe, müssen bei mir gespeichert bleiben (jeweils nur beim User) und im Hintergrund mitbewertet werden —
fließen nicht in die !! und in die Genauigkeit ein, sollen mir aber schnell zeigen, was der richtige Zug/die Bewertung ist.“

- Jede Partie im Analysebrett (PGN aus Router-State oder `?pgn=`, angemeldet) läuft jetzt über den Linien-Baum
  (`startStudy(blind)`), auch „Mit Engine“. Gespeichert wird also immer in `/api/line-studies` (je Nutzer), nicht mehr in den
  Analyse-Verlauf; der Hintergrund rechnet jede Stellung wie bisher (`LineStudyPosition`, eigene Aufträge — keine Wirkung auf
  Bewertungskurve, Zug-Klassen oder Genauigkeit der Partie).
- Der Dialog bestimmt nur noch den Start: „Mit Engine“ deckt den Baum gleich auf (`LineStudySession.revealWhenReady()` —
  wartet, bis der Baum eine Kennung hat), „Ohne Engine“ startet verdeckt.
- Umschalten: Kopf-Knopf „Ohne Engine“/„Mit Engine“ (`toggleNoEngine`, führt `?blind=1` in der Adresse mit, Router-State
  bleibt fürs Neuladen) und die Knöpfe der Leiste. `studyEngineHidden` ist der Modus (`blind`-Getter); das erste „Mit Engine“
  deckt auf dem Server auf, danach nur noch Anzeige. Ein schon aufgedeckter Baum öffnet „Ohne Engine“ jetzt verdeckt (vorher
  sprang er beim Wiederöffnen auf).

## Nachtrag 0.900.0 — Partieseite ⇄ Analysebrett ohne Engine

Klarstellung des Users 2026-11-08: „wenn ich oben auf *Mit Engine* klicke, will ich auf `club-games/236` landen, bei *Ohne
Engine* auf `analysis?blind=1` — vermutlich besser `games/236?blind=1`“. Also zwei Seiten, ein Umschalter:

- **Partieseite** (`SharedGameComponent`: `/games/:id`, `/club-games/:id`, Teilen-Links) = „Mit Engine“ (Kurve, Klassen,
  Genauigkeit). Oben in der Kopfzeile „Ohne Engine“ (`analyzeWithoutEngine`). `?blind=1` an der Partieseite springt gleich
  dorthin (Adresse ersetzt) — so ist `/club-games/236?blind=1` der Einsprung.
- **Analysebrett** bekommt die Partieseite als `?game=<Pfad>` (`AnalyzeGameService.startWithoutEngine(url, pgn, gamePage)`,
  nur seiteneigene Pfade, `readAnalysisParams.gamePage`). Ohne Engine führt „Mit Engine“ oben dann per `navigateByUrl` zurück;
  die Linien speichert die Sitzung beim Verlassen. Die Leiste „Ohne Engine“ deckt weiterhin HIER auf (Bewertungen der eigenen
  Linien, `toggleNoEngine(true)`). Ohne `?game=` (z. B. „Im Analysebrett öffnen“ aus der Liste) schaltet der Kopf-Knopf wie in
  0.898.0 auf der Seite um.
- Ohne Konto geht der Weg auch, nur ohne gespeicherte Linien (freies Brett ohne Engine, 0.895.0).


## Nachtrag Fixwelle B8 — Laden/Speichern abgesichert (F2-002/003/004)

- `LineStudySession.open()` legt den Partie-Baum nur nach **404** an, und zwar mit `createOnly` (Server: 409, wenn es den
  Baum inzwischen gibt → die Sitzung lädt neu). Jeder andere Ladefehler (500, 429, Netz) sperrt das Speichern
  (`schedule`/`flush`/`destroy`) und zeigt über der Leiste `app-load-error` mit Neuversuch (`open()` erneut) — vorher ersetzte
  ein kurzer Ladefehler den gespeicherten Baum durch die nackte Partie.
- Gescheitertes Speichern bleibt offen (dirty) und meldet sich per Snackbar (`analysis.lineStudy.saveFailed`); der nächste
  Zug bzw. das Verlassen versucht es erneut.
- „Engine einschalten“ während eines laufenden Speicherns wird vorgemerkt und nach dem Speichern ausgeführt.
