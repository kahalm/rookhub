# Eigene Fehler nachspielen (0.516.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


- **Seit 0.526.2**: der Fehlversuch nennt seine Bewertung (`MistakesSession.triedEval` — aus `Mistake.candidates` der
  Analyse, sonst `MistakeJudgeService.evaluate` im Browser; `undefined` = rechnet noch), und es gibt Tipps WIE BEIM
  PUZZLE (`classifyMoveFromFen` + `buildStagedHints`, Keys `puzzles.hints.*`: Zugart → Figur → Zug). Nach der dritten
  Stufe zählt die Aufgabe nicht mehr als selbst gefunden (wie „Lösung zeigen").

Der Trainer zur Partie, wie Lichess' „Aus deinen Fehlern lernen": Stellung VOR dem eigenen Fehler,
der gespielte Zug steht daneben, gesucht ist der bessere. Knopf in der Kopfzeile von `/games/:id`
und `/g/:token`, sobald es Aufgaben gibt. **Gespielt wird seit 0.519.0 auf dem BRETT DER SEITE**
(bis dahin ein Dialog mit eigenem, kleinerem Brett — gewünscht 2026-09-24): den Zustand hält
`features/games/mistakes-session.ts` (reine Klasse mit Signalen), die Seite bindet ihr Brett daran
(`boardFen`/`lastMove`/`flipped`/`playable`, Züge über `onTrainingMove`), und unter dem Brett steht statt
der Navigationsleiste die Trainer-Leiste (`mistakes-trainer.component.ts`, Eingabe `session`, Ausgabe
`closed`). Drei Dinge, die dabei nicht kippen dürfen: die Tippzonen neben dem Brett fallen im Training
weg (sie lägen sonst über dem Brett und schluckten jeden Zug), die Pfeiltasten blättern nicht in der
Partie darunter, und Zugliste/Kurve springen je Aufgabe auf die Stellung VOR dem Fehler — nach dem
Beenden steht man dort.
- **Gleichwertige Züge zählen** (0.519.0, `acceptedMoves` in `mistakes.util.ts`): der Bestzug UND jeder
  Kandidat derselben Suche, der höchstens `EQUIVALENT_LIMIT` (= `CLASS_LIMITS.excellent`, 2 Punkte
  Gewinnchance) dahinter liegt — die Grenze, bis zu der der Rückblick einen Zug „Exzellent" nennt. Dafür
  liefert `GameEvalPlyDto.Candidates` seit 0.519.0 alle (bis zu fünf) Kandidaten in Weiß-Sicht.
- **Ein Zug AUSSERHALB der Kandidaten** (0.520.0): liegt schon der SCHWÄCHSTE Kandidat innerhalb der
  Grenze (`unlistedMayBeEquivalent` → `Mistake.checkUnlisted`), kann auch ein nicht gelisteter Zug
  gleichwertig sein — dann rechnet die Browser-Engine nach (`mistake-judge.service.ts`, Phase `checking`,
  Brett gesperrt). Sonst ist er schlechter als der fünfte und sicher daneben, gerechnet wird nichts.
  Verglichen wird Browser gegen Browser (`isEquivalentAfter`): Bestzug der Analyse UND eigener Zug in
  derselben Tiefe (`MistakeJudgeService.DEPTH` = 16) — gegen die Zahl der tieferen Server-Analyse zu
  messen hieße, eine flache mit einer tiefen Suche zu vergleichen. Matt/Remis nach dem Zug stehen ohne
  Engine fest (sie hätte dort keinen Zug). Ein Urteil, das nach einem Aufgabenwechsel eintrifft, verwirft
  die Session (`epoch`); kann die Engine nicht prüfen (`null`), gilt der Zug als daneben und die Leiste
  sagt es. `StockfishResult.score` liefert dafür die Bewertung als Zahl (Weiß-Sicht).

- **Die Auswahl ist rein und getestet** (`features/games/mistakes.util.ts`): `collectMistakes` nimmt
  die Ungenauigkeiten, Fehler und groben Fehler BEIDER Seiten in Partie-Reihenfolge, je mit
  Ausgangsstellung, gespieltem Zug und dem Bestzug der Engine (`GameEvalPly.bestUci`) als SAN.
- **Entschieden wird über die GRUNDklasse** (`ReviewedMove.base`, neu in 0.516.0), nicht über `cls`:
  ein als **Miss** ausgewiesener Zug ist ein Fehler, dem zusätzlich eine Gelegenheit entgangen ist —
  Lichess kennt dieses Etikett gar nicht, und ohne die Unterscheidung fiele genau der Zug aus dem
  Training, der auf den Patzer des Gegners folgte. Dasselbe gilt für Brilliant/Great, die nie zu den
  drei Stufen gehören.
- **Übersprungen wird, was sich nicht abfragen lässt**: kein Bestzug (Lücke in der Analyse), Bestzug
  = gespielter Zug, und ein Bestzug, der in der Stellung gar nicht geht. Eine unspielbare „Lösung"
  vorzuführen wäre schlimmer als eine ausgelassene Aufgabe.
- **Wer wird trainiert**: `SharedGameDetail.ownerSide`; fehlt die Zuordnung (fremde geteilte Partie),
  die Seite mit den meisten Fehlern (`sideWithMoreMistakes`) — EINE Regel, `trainingSide`. Haben beide
  Seiten welche, schaltet der Dialog um — Brett dreht mit, Zähler beginnt von vorn. **Der Knopf zählt NUR
  diese Seite** (0.517.1): er zählte beide zusammen, der Dialog öffnete aber auf der des Besitzers — mit
  einem einzigen Fehler des Gegners stand „(1)" am Knopf, und der Dialog hatte nichts abzufragen (und ohne
  Fehler auf der zweiten Seite auch keinen Umschalter).
- **Die Aufgaben kommen aus dem Rückblick**, nicht aus einem zweiten Abruf: `GameReviewComponent`
  hat die Analyse ohnehin und meldet sie über die Ausgabe `mistakesChange`.
- **Geurteilt wird mit `sameMove`** aus `shared/chess/line-solver` — der gemeinsame Kern. Das Brett
  wandelt ohne Rückfrage in eine Dame um, und die Regel dort lässt eine fehlende Umwandlungsfigur
  gelten; eine Unterverwandlung als Lösung ließe sich dort ohnehin nicht eingeben. Als richtig zählt
  der Zug der Engine, ein anderer ähnlich guter Zug NICHT (dafür ist der Analysieren-Knopf da).
- **Nach einem Fehlversuch muss die FEN neu gebunden werden** (`retry()`): `app-chess-board` führt
  den Nutzerzug selbst aus, und ein Signal mit demselben Wert löst kein `ngOnChanges` aus — deshalb
  steht nach einem falschen Zug dessen Stellung im Signal und „Nochmal" schreibt die Ausgangsstellung
  zurück. Wer erst danebengreift oder die Lösung zeigen lässt, bekommt die Aufgabe nicht als selbst
  gefunden gutgeschrieben.
