# Partie-Rückblick: Bewertungskurve, Genauigkeit, Zug-Klassen (0.512.0, Sonderklassen 0.514.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Unter dem Brett der geteilten Partie (`/g/:token`) und der eigenen Partie-Seite (`/games/:id`, seit 0.513.0 statt des Nachspiel-Dialogs; der Dialog `PgnViewerComponent` kann es weiterhin) —
Daten AUSSCHLIESSLICH aus RookHubs eigener Partie-Analyse (`GET /api/games/{id}/evals`,
`…/shared/{token}/evals`, alles in WEISS-Sicht; siehe „Gespeicherte Partien" im Haupt-CLAUDE.md).

- `features/games/game-review.util.ts` — reine Funktionen, Vektoren-Spec mit literalen Zahlen:
  - `winPercent`: `50 + 50 · (2 / (1 + e^(−0,00368208 · cp)) − 1)` (Lichess, lila `WinPercent`);
    Matt = 100/0 (Lichess nimmt ±1000 cp ≈ 97,5 % — hier bewusst der Rand), `mate 0` über die FEN.
  - `moveAccuracy`: `103,1668 · e^(−0,04354 · (vorher − nachher)) − 3,1669`, auf 0..100, kein Verlust = 100
    (Lichess, https://lichess.org/page/accuracy). Aus Sicht des ZIEHENDEN (Schwarz: 100 − Weiß).
  - `volatilityWeights` + `sideAccuracy`: Fenster `clamp(⌊Halbzüge/10⌋, 2, 8)`, die ersten (Breite − 2)
    Züge mit dem ersten Fenster, Gewicht = Standardabweichung (Grundgesamtheit) der Gewinnchance auf 0,5..12;
    Ergebnis = Mittel aus gewichtetem und harmonischem Mittel (lila `AccuracyPercent.gameAccuracy`).
    Lücken fallen aus ihrem Fenster, statt als 0 % zu zählen.
  - `classify`: chess.com-Bänder („How are moves classified?") in Prozentpunkten — best (Engine-Zug oder
    kein Verlust), ≤ 2 excellent, ≤ 5 good, ≤ 10 inaccuracy, ≤ 20 mistake, sonst blunder; Grenze gehört zur
    BESSEREN Klasse. Prozentpunkte statt 0,02 usw., weil Gleitkomma die Grenzwerte sonst verschiebt.
  - `reviewGame(evals, fens, ucis?)`: die Seite am Zug kommt aus der FEN, nie aus der Parität. Bewertbar ist
    ein Zug nur mit gerechneter Stellung davor; „danach" = nächste Stellung (tiefer), sonst der gespielte
    Kandidat. `ucis` (je Halbzug `von+nach+Umwandlung`) schaltet die Sonderklassen ein; ohne sie bleibt es
    bei der Grundklasse.
  - **Sonderklassen (0.514.0, `specialClass`)** — ETIKETTEN über der Grundklasse, Genauigkeit und Kurve
    ändern sich nicht. Bedingungen aus der chess.com-Hilfe „How are moves classified?", die ZAHLEN für
    Brilliant/Great aus WintrCat/freechess (`src/lib/analysis.ts`, `board.ts`; chess.com legt sie nicht offen),
    die für Miss gesetzt (freechess kennt kein Miss). Alles aus Sicht des
    Ziehenden; Bewertungen werden dafür in Centipawns verglichen, Matt in n = ±(1000 − n) Bauern wie
    `GuessScoring.Pawns` (ganzzahlig — „Abstand ≥ 1,5" als Kommazahl-Differenz kippte an der Grenze).
    „Fehler des Gegners" heißt: GRUNDklasse des vorigen Halbzugs mistake/blunder (auch wenn er selbst ein
    Miss wurde). Reihenfolge:
    1. **Miss** ersetzt inaccuracy/mistake/blunder: Gegner hat davor gepatzt, Gewinnchance vorher (= die des
       Bestzugs) ≥ `MISS_BEST_WIN` 70, danach ≤ `MISS_AFTER_WIN` 60 und ≥ `MISS_MIN_AFTER_WIN` 40 — von +5 auf
       −5 bleibt ein grober Fehler, das Etikett darf die Katastrophe nicht verdecken.
    2. **Brilliant** ersetzt best/excellent und seit 0.521.3 auch good (`BRILLIANT_BASES` — chess.com rechnet die
       Erwartungspunkte je Elo, dort ist 21.Ba6 der Prod-Partie „nahezu best", bei uns good): `sacrificedPiece` findet ein Opfer, der Zweitbeste liegt unter
       `BRILLIANT_WINNING_ANYWAY_PAWNS` +7 und ist kein Matt für den Ziehenden (fehlt er: kein Ausschluss),
       danach ≥ `BRILLIANT_MIN_AFTER_PAWNS` −1, keine Umwandlung, vorher nicht im Schach.
    3. **Great** ersetzt best (nie zusammen mit Brilliant): zweiter Kandidat da, Abstand ≥
       `GREAT_MIN_GAP_PAWNS` 1,5, der Zweitbeste gewinnt NICHT ohnehin (< +7 und kein Matt — dieselbe Grenze wie
       bei Brilliant; „#2 gegen #4" ist kein einziger guter Zug), Gegner hat davor gepatzt, danach ≥
       `GREAT_MIN_AFTER_WIN` 45 %, und KEIN Opfer (`sacrificedPiece` = null) — ein Zug mit hängender Figur ist
       Brilliants Sache. Erster Zug: kein Great.
    Fehlt eine Zutat (Lücke davor, kein zweiter Kandidat, keine UCI), entfällt die Sonderklasse. Book gibt es
    nicht (kein Eröffnungsbuch im Client). `MOVE_CLASS_COLORS` ist die EINE Farbtabelle (chess.com-nah) für
    Zähler, Abzeichen und Kurven-Punkte.
- `features/games/move-tactics.util.ts` — „hängt eine Figur?": die ANGREIFER sind die LEGALEN Schlagzüge des
  Gegners in der Stellung danach (`legalCapturersOf` über `moves({verbose})` — nach einem Abzugsschach darf
  der Bauer nicht schlagen, ein gefesselter Läufer auch nicht, der König nimmt keine gedeckte Dame; mit
  Pseudo-Angriffen „hing" jede dieser Figuren und der Zug wäre ein falsches Brilliant), die VERTEIDIGER
  Pseudo-Angriffe über chess.js `attackers(square, color)` (chess.js 1.4: ohne Legalität, ohne Röntgen; der
  König zählt mit, Wert ∞) — der Ziehende ist danach nicht am Zug, für ihn gibt es keine legalen Züge.
  `isPieceHanging(vorher, nachher, feld)` nach freechess: (a) vorher stand dort eine gegnerische Figur ≥ Wert
  → nein; (b) Turm schlug eine Leichtfigur und genau EIN Angreifer, eine Leichtfigur → nein; (c) ein
  billigerer Angreifer → ja; (d) mehr Angreifer als Verteidiger → ja, außer (Figur billiger als jeder
  Angreifer UND ein Verteidiger billiger als der billigste Angreifer) oder ein Bauer deckt; (e) sonst nein.
  `sacrificedPiece` = die TEUERSTE eigene Figur (N/B/R/Q, auch eine, die nicht gezogen hat — Legall), die hängt
  und bei der der Gegner im ABTAUSCH mehr gewinnt als das Geschlagene (`exchangeGain`, SEE ohne Röntgen; en
  passant = 1). **Seit 0.518.1 zwei Korrekturen**, beide an der Prod-Partie MYXN3hXqz1X2hm7Cx6V47Q gefunden
  (chess.com: kein Brilliant): (1) der Abtausch statt des vollen Figurenwerts — 22…gxf4 ließ einen von der
  Dame GEDECKTEN Turm vor einem Läufer stehen (Verlust die Qualität, 2 < geschlagener Läufer 3); (2) freechess'
  Schlag-Simulation (`capturable`) ist jetzt drin (seit 0.521.3 OHNE den Schläger selbst auf dem Opferfeld — sein
  Zurückschlagen rechnet schon `exchangeGain`; sonst wäre 21.Ba6 …Txa6 Dxa6 ein „Köder"): kostet das Schlagen den Schläger eine Figur mindestens vom
  Wert des Opfers (Abzugsangriff — 23.Lxd5 öffnet die b-Linie, exd5 verliert die Db8) oder erlaubt es bei
  einem Opfer unter Turmwert Matt in einem, ist es ein Köder. Die frühere Begründung „wir haben die Engine —
  ist der Zug best, ist das Opfer korrekt" war falsch: die Engine sagt „bester Zug", nicht „Opfer".
  Unlesbare FEN → kein Opfer, nie ein Wurf (läuft in
  einem `computed`). Die Erklärung (geopferte Figur, Abstand, Verpasstes) steht als TEXT neben dem Abzeichen,
  nicht als Tooltip — am Handy gibt es kein Hover.
- `shared/pgn-viewer/eval-graph.component.ts` — reines SVG, KEINE `clipPath`/`url(#…)` (mit `<base href>`
  finden Firefox/Safari die Verweise nicht), Flächen als eigene Polygone; feste Figurenfarben statt
  `currentColor` (sonst kehrt sich Weiß/Schwarz im Dunkelmodus um); Punkte als HTML (Kreise in einer
  verzerrten SVG wären Ellipsen) — die Farbe bringt die Marke mit (`EvalGraphMark.color`), die Kurve kennt
  keine Zug-Klassen. Punkte haben brilliant, great, miss, mistake, blunder. Klick → Halbzug-Index wie
  `currentMoveIndex` (−1 = Start). Höhe = `GameReview.curve` (seit 0.520.1, `graphHeight`: Bewertung
  LINEAR bis ±`GRAPH_CAP_PAWNS` 10, Matt am Rand — nicht die Gewinnchance, die ab +3 sättigt und jede gewonnene
  Phase zum Block am Rand machte; an der chess.com-Grafik derselben Partie nachgemessen). Fläche wie LICHESS:
  zwischen Kurve und Mittellinie hell (oben) bzw. dunkel (unten), sonst grau. **Die chess.com-Fläche (weiß vom
  unteren Rand, 0.520.1) gefiel nicht und ist in 0.521.1 zurückgenommen** — „das Kantige" war das Problem:
  seither ist die Kurve GEGLÄTTET (`EvalGraphComponent.smooth`, monotone kubische Interpolation nach
  Fritsch–Carlson, 8 Zwischenpunkte je Zug; geht durch jeden Punkt, schwingt nie über ihn hinaus). Die Kurve
  ist standardmäßig EINGEKLAPPT (`GameReviewComponent.graphOpen`, nicht gemerkt), Klick auf die Überschrift.
- `features/games/game-review.component.ts` — lädt, fragt alle 10 s nach, SOLANGE `pending`/`running`
  (nicht bei `none`/`done`/`failed`, nicht nach dem Schließen), zeigt bei `none` nichts; `statusChange` sagt
  der Seite, wann „Partie analysieren" gesperrt (läuft) oder ausgeblendet (fertig) wird, `reload()` nach dem
  Einwurf. Die Seiten halten Knopf-Zustand und Status in SIGNALEN (Angular 22 zeichnet sonst nach der
  HTTP-Antwort nicht neu). Im Dialog macht das Brett der Kurve Platz (`.with-review`, 90-vh-Dialog).
  Die Seiten reichen `game.moves` (chess.js `Move`) als `[moves]` herein — daraus werden die UCIs für die
  Sonderklassen. Das Abzeichen des aktuellen Zugs erklärt Brilliant (geopferte Figur + Feld), Great (Abstand
  zum Zweitbesten; mit Matt im Spiel ohne Zahl) und Miss per Tooltip. „!" gehört Great, Excellent trägt 👍.
  Läuft die Vertiefung (zweiter Durchgang, 0.523.0, `evals.refining`), steht dort „Vertiefung läuft — x von y"
  (`games.review.refining`) und die Komponente fragt nur noch alle `RefinePollMs` (60 s) nach — die Analyse ist schon
  `done` (Knopf weg), wird aber genauer; danach Ruhe.
  Neben dem Fortschritt steht seit 0.517.0 die Restdauer (`etaMinutes` vom Server, geschrieben mit
  `shared/eta.util.ts` `formatEta` — derselben Funktion wie auf „Partie-Analysen", Text-Key `gameAnalysis.eta`);
  nur solange die Analyse läuft, ohne Tempo gar nicht.
