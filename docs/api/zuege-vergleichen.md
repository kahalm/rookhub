# Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?"

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Züge vergleichen (0.602.0, auth) — „warum ist Zug 1 besser als Zug 2?"“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch 2026-09-29: „soll diese durchrechnen und schaun, warum Zug 1 besser ist als Zug 2 — vor allem im Vergleich: was
sind bei Zug 2 die besten Züge, und warum gehen die bei Zug 1 nicht (so gut)". `Services/Engines/MoveComparisonService.cs`
(Ablauf: Anlegen, Pumpe, Aufträge, Lesen; reine Teile daneben: `MoveComparisonBoard` Brett, `MoveComparisonEval` Bewertung
lesen/zeigen, `MoveComparisonSteps` Auswahl/Rangfolge/Antwort-Zeilen, `MoveComparisonPrompts` Fakten + Prompts,
`MoveComparisonExplainer` Begründungen, `MoveComparisonView` DTO, `MoveComparisonFromAnalysis` Übernahme aus der Analyse,
`MoveComparisonExplainJobs`/`MoveComparisonException` eigene Dateien; Zerlegung 2026-11-09),
Tabellen `MoveComparisons` + `MoveComparisonLines`, Pumpe `MoveComparisonPumpService` (`PeriodicWorker`, Start sofort, 5 s, `MoveComparison:PumpIntervalSeconds`).
* **Zwei Durchgänge über die Hintergrund-Aufträge** (NICHT die Live-Engine — rechnet auch, wenn die Seite zu ist):
  (1) je Kandidat (2–4) die Stellung DANACH mit `CandidateLines` (3) Linien = die besten Antworten des Gegners; daraus
  der beste Kandidat (`BestUci`, Sicht der Seite am Zug, Matt ± 100 000). (2) Je SCHWÄCHEREM Kandidaten dessen
  `RepliesTested` (3) beste Antworten, gespielt nach dem BESTEN: geht die Antwort dort nicht → `Illegal` (ohne Auftrag,
  selbst eine Auskunft), sonst ein Auftrag mit 1 Linie = die eigene Erwiderung und die Bewertung. Matt/Patt auf dem Brett
  braucht keinen Auftrag (`Terminal`).
* **Engine wie die Punktepartie** (`Services/Engines/EngineOwnerResolver.cs`, von `GameAnalysisService` mitbenutzt): eigene
  Hintergrund-Engine, sonst die Haus-Engine; auf der Haus-Engine höchstens `HouseMaxDepth` 24 (eigene bis 30, Vorgabe 22).
  Die Aufträge sind normale (nicht `Background`) — sie verdrängen Vertiefung/Meisterpartien. `remember: false`, und
  `AnalysisJobService.ListAsync` blendet Aufträge eines Vergleichs aus. **Fertige Aufträge werden eingesammelt**: Ergebnis
  in die Zeile (`ResultJson`, `ReachedDepth`), Auftrag GELÖSCHT — sonst stünden sie in der Liste und fielen irgendwann dem
  Trimmer (`MaxJobsPerUser`) zum Opfer. Ein verschwundener Auftrag macht die Zeile `Failed`, nicht den Vergleich.
* **Begründung** je schwächerem Kandidaten nur mit Modell auf eigener Hardware (`IsLocal`), im Hintergrund
  (`MoveComparisonExplainJobs` gegen Doppelstarts, ein Neustart lässt die Pumpe neu anstoßen). Fakten wie bei „Warum war das
  ein Fehler?" (`ExplanationFacts`: `FirstMoveAttacks`, `LineEvents` — die Bilanz der Antwort-Linie ab der Stellung VOR dem
  schwächeren Zug samt diesem, die der eigenen Erwiderung ab der Stellung nach dem besten Zug samt der Antwort), Stufen in
  Worten (`LevelName`), „fast gleich" unter 0,3 Bauern; Leser = die Seite am Zug. Genannte Züge nur aus den Linien
  (`MentionsOnly`, eine Nachfrage), gespeichert in englischer SAN, `PieceLetters` beim Lesen. Ohne Modell endet der
  Vergleich ohne Text; ein unbrauchbarer Text lässt ihn trotzdem `Done` werden (sonst hinge er auf „wird erklärt").
* Deckel: `MaxOpenPerUser` 3 laufende, `MaxKeptPerUser` 50 gespeicherte (älteste fertige gehen beim Anlegen).
* Frontend: ⋮-Menü der Stellung (`position-menu.component.ts`, angemeldet immer — ob eine Engine bereitsteht, sagt der
  Dialog) → `move-compare-dialog.component.ts` (Engine-Vorschläge des Analysebretts vorausgewählt, `[candidates]`, dazu
  jeder legale Zug) → Seite `/analysis/compare/:id` (`move-comparison.component.ts`, fragt alle 3 s nach, solange es
  läuft; Brett aus Sicht der Seite am Zug, Klick auf Zug/Antwort/Erwiderung stellt die Stellung auf).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/move-comparisons/status` | `{ engineAvailable, ownEngine, explanations, maxCandidates, defaultDepth, maxDepth, openComparisons, maxOpen }` (Literal vor `{id}`) |
| GET | `/api/move-comparisons` | Die letzten 20 eigenen (`moves` als SAN, `bestSan`) |
| GET | `/api/move-comparisons/{id}` | Stand: `status` (`candidates`/`replies`/`explaining`/`done`/`failed`), je Kandidat `evalText` (Sicht der Seite am Zug, wie die Begründung — auch Antworten/Erwiderungen; bis 0.807 Weiß-Sicht), `replies` (beste Antworten, Linie als SAN), `tests` (dieselben nach dem besten Zug: `state` `pending`/`done`/`failed`/`illegal`, `line` = eigene Erwiderung), `explanation`; beste zuerst, solange gerechnet wird Reihenfolge der Auswahl. Laufende Zeilen zeigen den Zwischenstand ihres Auftrags |
| POST | `/api/move-comparisons` | `{ fen, moves[] (UCI, Rochade auch König-schlägt-Turm), depth?, lang?, title? }`; 400 `reason` ∈ invalid-fen/game-over/invalid-move/too-few-moves/too-many-moves/too-many-open/no-engine/too-many-jobs |
| DELETE | `/api/move-comparisons/{id}` | Löschen samt offener Aufträge |

**Antwort nach dem besten Zug muss derselbe Zug sein (0.908.0)**: `MoveComparisonBoard.ReplyTransfers` — eine beste
Antwort auf den schwächeren Zug wird nach dem besten nur gerechnet, wenn auf ihrem Startfeld dieselbe Figur steht und sie,
falls sie dort etwas schlug, auch hier etwas schlägt (Befund an Vereinspartie 236: „Sxd7" mit +6,52, der Springer zog nach
dem besten Zug gratis auf ein leeres Feld). Sonst Zeilenstatus `Skipped`: kein Auftrag, nicht im DTO, nicht in den Fakten der
Begründung. Still dort und schlagend hier bleibt — genau das kann der Grund sein. Die Seite zeigt nur Antworten mit Test.
**Erklärdienst an EINER Stelle**: die Registrierung von `MoveComparisonService` in `Startup/DomainServices.Analysis.cs` reicht
den `IClaudeJsonClient` hinein — dort tauschen, wer die Begründungen schreibt (heute nur ein Modell auf eigener Hardware,
`ExplanationsAvailable`).

## Zugvergleiche zu den Fehlern der Vereinspartien (0.908.0)

Wunsch 2026-11-08: „bei Ligapartien lässt du ja Fehler nachspielen — kannst du für diese Fehler auch immer gleich den
Fehlzug mit dem besten Zug vergleichen? … Tiefe 35" und „Ligapartien dürfen an der Stelle alle mit 35 rechnen".
Seit 0.909.0 („vertiefe Ligapartien auf 30 und vergleiche auch 30"): Tiefe 30, und erst NACH der Vertiefung der Analyse
auf 30 (`docs/api/meisterpartien-im-hintergrund-analysieren.md`, „Vertiefung auf 30").
`Services/Engines/ClubMistakeComparisons.cs`, Auswahl `Services/Games/GameMistakeSelection.cs`, lesen
`Services/League/ClubGameComparisons.cs`.

* **Auswahl** = Spiegel von `collectMistakes` (`mistakes.util.ts`): Grundklasse Ungenauigkeit/Fehler/grober Fehler (Klassen aus
  `GameMoveClasses`, Codes I/M/P und Miss X), beide Seiten, ohne Bestzug / Bestzug = Partiezug / unspielbaren Bestzug. Ohne
  Buchzüge und ohne „nicht mehr zeigen" — beides hängt am Betrachter, der Server nimmt die Obermenge. Literal-Test auf
  beiden Seiten: `GameMistakeSelectionTests` ↔ `mistakes.util.spec.ts` („server mirror").
* **Wann**: am endgültigen Ende der HAUPTanalyse (Origin Club, ohne EngineId — Lc0 nie; `GameAnalysisWatchers.IsFinal`, seit
  0.909.0 also nach der Vertiefung auf 30 — Fehler und beste Züge stammen aus den Zahlen der Tiefe 30) ruft
  `GameAnalysisPump` an derselben Stelle wie die Glocke `ClubMistakeComparisons.EnqueueAsync`: je Fehler ein Vergleich
  [bester Zug der Analyse, Partiezug] mit Status `Waiting` (5), `GameAnalysisId` + `Ply` gesetzt (eindeutig je Analyse und
  Halbzug), Marke `GameAnalyses.MistakeComparisonsAt`. Doppelt aufgerufen passiert nichts.
* **Nachtrag** (Altbestand, auch Partie 236): die Pumpe (`MoveComparisonPumpService` → `TickAsync`) legt EINE Partie je Takt
  an, und nur, wenn kein Vergleich mehr wartet — die zuletzt fertig gewordene zuerst. Eine Analyse mit offener Vertiefung (der per
  `DeepenClubAnalyses` nachvertiefte Altbestand) ist nicht fertig und wird übersprungen, bis ihre Vertiefung durch ist —
  danach legt sie die Pumpe am Ende ohnehin selbst an. Neue Partien stehen so nie hinter dem
  Altbestand.
* **Rechnen**: höchstens `ClubMistakeComparisons:MaxActive` (1) gleichzeitig in `Candidates`/`Replies`, wartende nach
  `FinishedAt` der Analyse (neueste zuerst), dann Halbzug. Tiefe `ClubMistakeComparisons:Depth` (30 = `ClubRefineDepth`; 0.908.0: 35; NUR hier — eigene
  Vergleiche auf der Haus-Engine bleiben bei `HouseMaxDepth` 24). Aufträge als Stapel-Hintergrundarbeit (`Background` +
  `Batch`, wie die Analyse der Vereinspartien): nach allem, worauf jemand wartet, und in den Sperrzeiten nicht. **Vorrang
  (0.916.1)**: Partien mit angeforderter/laufender Zusammenfassung (`ClubGameSummaries` Requested/Running) starten zuerst, am
  `MaxActive`-Deckel vorbei, und ihre Aufträge sind nur `Background` (kein `Batch`). Ist die
  Haus-Engine nicht mehr freigegeben (nur bei gesetztem `EngineOwnerUserId`) oder der Auftragsdeckel voll, bleibt der Rest
  liegen. `ClubMistakeComparisons:Enabled=false` hält alles an. Begründung auf Deutsch (`Language` „de" — LeagueHub ist
  deutschsprachig).
* **Aus der vertieften Analyse statt neu gerechnet** (Wunsch 2026-11-09 an Partie 145: „warum musst du eigentlich, wenn du die
  Partie tief analysiert hast, die Fehler nochmals eigens analysieren? Sollten da nicht die bestehenden greifen?"):
  `Services/Engines/MoveComparisonFromAnalysis.cs`, Spalte `MoveComparisons.Source` (Migration `MoveComparisonSource`;
  `0` Engine, `1` Analysis = kein Auftrag, `2` Mixed = Rest gerechnet, `null` = Altbestand, noch nicht geprüft; im DTO `source`
  `engine`/`analysis`/`mixed`, Log-Zeilen „… ganz aus der Analyse, … teilweise“). Angewendet beim Anlegen (`EnqueueAsync`) und
  von der Pumpe einmal auf jeden ungeprüften Vergleich in `Waiting` oder `Candidates` (`ApplyAnalysisToUncheckedAsync`, am
  `MaxActive`-Deckel vorbei; Aufträge übernommener Zeilen werden gelöscht).
  * **Kandidat = Partiezug**: die Folgestellung (Halbzug + 1) IST die Stellung danach — ihre bis zu `CandidateLines` (3) besten
    Linien sind die besten Antworten wie bei einem eigenen Auftrag. **Sonst** (bester Zug, oder Partiezug ohne brauchbare
    Folgestellung): der Kandidat in der Stellung des Fehlers, seine Hauptvariante ab dem 2. Halbzug = EINE Linie der Stellung
    danach (der beste Zug zeigt dann nur seine beste Antwort — mehr verwendet die Begründung ohnehin nicht).
  * **Schwellen**: Stellung `Refined` UND `Depth` ≥ `ClubMistakeComparisons:Depth` (`Refined` steht auch bei aufgegebener
    Vertiefung, dann bliebe Tiefe 20), Stellung passt (Brett/Zugrecht/Rochade), Linie ≥ 2 Halbzüge (Antwort des Gegners und die
    eigene Erwiderung — beides braucht die Begründung) oder sie endet matt/patt; aus der Folgestellung muss deren BESTE Linie
    taugen (sonst stimmte die Bewertung nicht). `Depth` des Vergleichs = die kleinste Tiefe der übernommenen Daten; sie bleibt
    beim Start stehen (keine Angleichung an `:Depth`).
  * **Vorzeichen**: `CandidatesJson` speichert die Sicht der Seite am Zug JENER Stellung (bei der Folgestellung also des
    Gegners), die Zeilen des Vergleichs (`ResultJson`) die Weiß-Sicht wie eine Broker-Zeile (`{"depth":30,"pvs":[{"moves":[…],"cp":…}]}`)
    — umgerechnet beim Übernehmen, gelesen wie jede Broker-Zeile.
  * **Antwort-Tests** (beste Antworten auf den Fehlzug, gespielt nach dem besten Zug) sind NEUE Stellungen, die die Analyse nicht
    kennt. Ausnahme: ist die Antwort genau die beste Antwort auf den besten Zug (dessen Hauptvariante), steht die eigene
    Erwiderung schon dort (Fortsetzung, Bewertung des besten Zuges, Tiefe − 2) — `DeriveRepliesFromBest`, für alle Vergleiche
    mit `GameAnalysisId`, auch gerechnete. Unmögliche / „anderer Zug“ / Matt brauchen ohnehin keinen Auftrag.
  * **Rückfall**: nichts übernehmbar → wie bisher (`Engine`). Teilweise (ein Kandidat fehlt, oder offene Antwort-Tests) →
    `Mixed`: nur das Fehlende wird gerechnet; die Schrittstruktur bleibt (offene Kandidaten → Status `Candidates`, sonst gleich
    `Replies` mit je EINER Linie je offenem Test), gedeckelt wie gewohnt. Alles da → direkt `Explaining` (bzw. `Done`).
  * **Kosten neu**: der teure Schritt (2 Kandidaten-Aufträge × 3 Linien, Tiefe 30) entfällt bei vertieften Partien fast immer;
    übrig bleiben je Fehler 0–3 Antwort-Tests mit einer Linie, viele Vergleiche ganz ohne Auftrag (Schätzung, nicht gemessen:
    statt ~5 min je Vergleich etwa 0–3 min).
* **Begründung über Claude Fable** (0.911.0, Wunsch 2026-11-08: „mache dann (nur für Ligapartien ab dem 6.11.) auch die
  Analyse mit Fable über Token"): `Services/Engines/ClubFableExplanations.cs` (Singleton) + `Services/AnthropicJsonClient.cs`.
  Für Vergleiche mit `GameAnalysisId`, deren Vereinspartie am oder nach `ClubMistakeComparisons:FableFrom` (2026-11-06)
  gespielt wurde, schreibt `claude-fable-5-1` (`:AnthropicModel`) die Begründung statt des lokalen Modells; ältere bleiben lokal.
  **Spieldatum** (`GameDateAsync`): die Partie trägt nur das Jahr, deshalb das Datum der zugeordneten Ligarunde
  (`LeagueClubGame.LeagueTnr/LeagueRound` → `LeagueRounds.Date`), sonst `LeagueClubGame.CreatedAt` (auf Prod fast immer leer),
  sonst `CreatedAt` der Hauptanalyse (≈ Hochladen). **Prompt** (Stand 0.911.0; seit 0.971.0 gilt „Der taktische Grund“ weiter unten): Deutsch, Klubspieler 1500–2000, zwei Absätze + „Merksatz: …“,
  höchstens 170 Wörter — was der Fehlzug übersieht/zulässt mit der konkreten Widerlegungslinie (3–6 Halbzüge), was der beste
  Zug erreicht, nichts erfinden, kleine Unterschiede (< 0,5) ehrlich als klein, deutsche Figurenbuchstaben; als Fakten derselbe
  `UserPrompt` wie beim lokalen Modell (FEN, beide Züge, Bewertungen, Kandidatenlinien, gefilterte Antwort-Tests). Der Text wird
  wie beim lokalen Modell geprüft (nur Züge aus den Linien, `MentionsOnly` nach `EnglishSan`) und in englischer SAN gespeichert
  (die Anzeige setzt die Figurenbuchstaben der Sprache ein); `MoveComparison.Model` = `claude-fable-5-1`. Ein Vergleich darf
  auch ohne lokales Modell in „wird erklärt“ gehen, wenn Fable an ist (`CanExplain`).
  **Schlüssel und Schalter**: `:ExplainWithAnthropic` (Code-Vorgabe AUS — die Dev-Stacks tragen denselben Konto-Schlüssel;
  `compose.vpn.yml` setzt `${CLUB_MISTAKE_EXPLAIN_WITH_ANTHROPIC:-true}`, die Dev-Composes `:-false`), Schlüssel
  `:AnthropicApiKey`, sonst der Konto-Schlüssel `Anthropic:ApiKey` (ausdrücklich erlaubt, `.claude/rules/llm-text-clients.md`);
  leer/Platzhalter = aus. **Kostenbremse**: `:AnthropicDailyCap` (200 Aufrufe je UTC-Tag, auch Fehlschläge und die eine
  Wiederholung; nach einem Neustart zählen die heute mit Fable fertig gewordenen Vergleiche), `:AnthropicMaxTokens` (6000, Denken
  inklusive — Fable denkt immer), `:AnthropicEffort` (low). Fehler, Ablehnung (`refusal`), Deckel oder zweimal ein fremder Zug →
  das lokale Modell schreibt. Verbrauch je Aufruf im Log („Anthropic club-mistake-fable (…): n Eingabe-, m Ausgabe-Tokens“),
  Schlüssel und Antwort nie. Kosten: Fable $10/$50 je Mio. Tokens; je Begründung grob 2 000–3 000 Eingabe- und 1 000–3 000
  Ausgabe-Tokens (mit Denken) ≈ 0,07–0,18 $, Deckel 200/Tag ≈ höchstens ~60 $/Tag (6000 Ausgabe-Tokens je Aufruf).
* **Der taktische Grund statt der nacherzählten Engine-Linie** (0.971.0, Befund 2026-11-14 an Vereinspartie 245, Vergleich
  2569, 25...Lc8: „der Grund, warum Lc8 nicht funktioniert, ist ein taktischer. Wenn Txc8 Txc8 gehen würde, wäre alles gut,
  aber dann geht die Springergabel Sd7 — ebenso Txc8 Kxc8 Tc1 Kb8, gleiches Problem. Du ratterst nur die Computerlinie
  runter.“). Ursache: der Text darf nur Züge aus den gegebenen Linien nennen (`MentionsOnly`, bleibt so) — die Nebenwege des
  Verteidigers standen in keiner Linie. Dazu zwei falsche Stellungsbehauptungen („Läufer vor dem König in der c-Linie“,
  „ungedeckt“).
  * **Andere Verteidigungen als Zeilen** (`Services/Engines/MoveComparisonDefences.cs`, `MoveComparisonLineKind.Defence` = 2,
    KEINE Schemaänderung dafür): in der besten Antwort-Linie auf den Fehlzug an den ersten zwei Zügen des Verteidigers
    (Halbzug 1 und 3 der Linie) die naheliegenden anderen Züge — **im Schach alle anderen legalen Züge, sonst nur die, die die
    gerade gezogene Figur des Gegners schlagen** (Opfer annehmen, anders zurückschlagen). Rangfolge je Entscheidung: Schlagen
    der gezogenen Figur, Königszüge, Rest; darin die billigere Figur zuerst; Unterverwandlungen zählen nicht. Die beiden
    Entscheidungen kommen abwechselnd dran, bis `ClubMistakeComparisons:DefenceLines` (Vorgabe 4, 0–8, 0 = aus) erreicht ist.
    Zeile: `CandidateUci` = Fehlzug, `ReplyUci` = die andere Verteidigung, `Ordinal` = ihr Halbzug in der Linie (1/3), `Fen` =
    Stellung danach; der Weg dorthin ist der Anfang der Antwort-Linie und muss beim Lesen wieder auf `Fen` führen.
  * **Wann und für wen**: `MoveComparisonService.StartExtraLinesAsync` (bis 0.973 `StartDefencesAsync`), einmal je Vergleich beim Eintritt in „wird erklärt“
    (jeder Weg dorthin führt durch `PumpOneAsync`, auch der direkt aus der Analyse) — NUR wo Fable schreibt
    (`ClubFableExplanations.AppliesAsync`: Vereinspartie ab dem Stichtag bzw. eigene Partie mit angeforderter
    Zusammenfassung; auf Dev mit `ExplainWithAnthropic=false` also nie). Gibt es Zeilen, geht der Vergleich zurück auf
    `Replies`, je Zeile ein Auftrag mit EINER Linie in der Tiefe des Vergleichs (Hintergrund/Stapel bzw. Vorrang wie die
    Antwort-Tests), danach wieder „wird erklärt“. **Gescheitert = erledigt**: ein Auftrag, der sich nicht einreihen lässt oder
    verschwindet, macht nur seine Zeile `Failed`; der Text entsteht ohne diese Verteidigung, die Zusammenfassung
    (`ClubGameSummaries.ReadinessAsync` zählt `Done`/`Failed` des VERGLEICHS) hängt nie daran.
  * **Mehrarbeit** (Schätzung aus Prod-Daten vom 14.11., nicht live gemessen): die Regel, in Python nachgebaut und über die
    439 bisher von Fable erklärten Vergleiche gelegt, ergibt bei 49 % mindestens eine Zusatzlinie, im Mittel 0,93 je Fehler
    (0: 224, 1: 110, 2: 50, 3: 21, 4: 34); ein Antwort-Test (dieselbe Art Auftrag: eine Linie, Tiefe 30) dauerte im Bestand
    im Mittel 95 s (Median 44 s, 90 % unter 222 s) → grob 1,5 min mehr Engine-Zeit je Fehler, höchstens vier solche Aufträge.
  * **Fakten** (nur im Prompt für Fable, `MoveComparisonPrompts.UserPrompt(f, detailed: true)`; das lokale Modell bekommt den
    Block NICHT, sein Prompt ist Zeichen für Zeichen der alte): Figurenliste in Worten („White: king g2, queen e3, …“ — keine
    Kürzel wie „Kb8“, die läse die Zugprüfung als Zug); Block „Other defences in the refutation line … and why they fail“ je
    Entscheidung mit der Zahl der legalen Züge („exactly 3 legal moves: …“, wenn ALLE gerechnet sind, sonst „only the
    following were calculated — do not discuss the others“), je Verteidigung Bewertung, Engine-Linie mit Zugnummern und
    „Concretely“: `ExplanationFacts.CheckForks` (Schach + Angriff auf weitere Figuren durch eine Figur, die nicht geschlagen
    werden kann: „Nd7+ gives check and the knight on d7 also attacks the queen on b6 and the bishop on f6 (a fork); the knight
    on d7 cannot be captured“ — `FirstMoveAttacks` bleibt bei einem Schach leer), das Feld des widerlegenden Zuges („d7 is
    the square the bishop left with 25...Bc8“, „after 25...Bc8 the square d7 was covered only by the bishop on c8, which is
    no longer there“ — Brettgeometrie `Services/Games/BoardGeometry.cs`, ohne Fesselungen) und die Materialbilanz ab der
    Stellung VOR dem Fehlzug (`LineEvents` mit `leads`). Ist eine Verteidigung höchstens 0,3 Bauern schlechter als die
    Hauptlinie, steht „about as good as the main line, so this defence does NOT fail“ dabei. `CheckForks` steht auch an der
    Hauptlinie. Die Züge der Verteidigungen zählen zu `AllowedMoves`; nicht gerechnete bleiben verboten.
  * **Auftrag** (`ClubFableExplanations.SystemPrompt`): Absatz 1 = Motiv beim Namen, was der gespielte Zug aufgibt oder
    freigibt, Einstieg der Widerlegung, zu JEDER gegebenen Verteidigung ein Halbsatz, woran sie scheitert; Hauptlinie nur so
    weit wie nötig. Stellungsaussagen (Feld, gedeckt/ungedeckt, Linie) müssen aus Figurenliste oder Fakt folgen. 200 statt 170
    Wörter; `MoveComparisonLines.Explanation` deshalb 3000 statt 1500 Zeichen (Migration `MoveComparisonExplanationLength` —
    `MaxTextLength` 3000 stand schon über der Spalte), geprüft nach dem Einsetzen des Merksatzes. **Nicht maschinell
    geprüft** wird, ob eine Stellungsbehauptung im Text stimmt — das stützen nur Figurenliste, Fakten und Auftrag.
  * **Bestand bleibt**, nichts wird von selbst neu erklärt. EINEN fertigen Vergleich neu erklären (Admin):
    `POST /api/admin/move-comparisons/{id}/re-explain` (`AdminMoveComparisonsController`, `MoveComparisonService.ReexplainAsync`)
    → 202 `{ id, status: "explaining" }`, 404 unbekannt/kein Vergleich einer Partie-Analyse, 409 `not-done` solange er
    rechnet oder erklärt wird. Die Pumpe rechnet beim nächsten Takt die fehlenden Verteidigungen nach und stößt die
    Begründung an (ein Fable-Aufruf, zählt zum Tagesdeckel); die alte Begründung bleibt, bis die neue da ist, und bleibt
    auch, wenn Fable nichts Brauchbares liefert (das lokale Modell ersetzt eine vorhandene Begründung dann nicht). Währenddessen
    zählt der Vergleich für die Zusammenfassung als offen.
  * **Vergleichsseite**: die Zusatzzeilen stehen nicht im DTO (`MoveComparisonView` nimmt nur Kandidaten und Antwort-Tests),
    zählen aber in `pending`/`total` mit; der Status springt für sie von „explaining“ kurz zurück auf „replies“.
* **Warum die naheliegende Antwort auf den BESTEN Zug nicht geht** (0.974.0, Befund 2026-11-14 an Vereinspartie 251, Fehler
  20...dxc3 statt 20...e4, Stellung `r4r1k/1p4bp/p1nq2p1/4pnB1/2Bp4/N1P3P1/PP3P1P/R2QR1K1 b`: „die eigentliche Begründung,
  warum e4 geht, ist, dass Txe4 nicht geht“). Der Spiegelfall zu den anderen Verteidigungen: der Text erzählte 21.Lf1 nach,
  weil 21.Txe4 in keiner Linie stand — die Kandidatenlinie des besten Zugs trägt nur die beste Antwort der Engine, die
  Antwort-Tests nur die Antworten auf den Fehlzug.
  * **Nicht jeder Schlag** (Nutzer: „vermutlich viel zu häufig offensichtlich. Gibt es eine Möglichkeit zu sehen, dass der
    Schlagzug irgendwie menschlich ist, aber taktisch nicht geht? Stockfish Tiefe 1/2/3?“) — zwei Sichten
    (`Services/Engines/MoveComparisonBestMoveTries.cs`):
    1. **Verlockend = flache Sicht** (`MoveComparisonLineKind.BestMoveGlance` = 3): EIN Auftrag auf die Stellung nach dem
       besten Zug mit Tiefe `:BestMoveTryShallowDepth` (1) und fünf Linien (mehr lässt das Protokoll nicht zu,
       `EngineProtocol.MaxMultiPv`; für „unter den besten drei“ reicht es). Verlockend ist JEDER Zug — nicht nur ein
       Schlag —, der dort unter den besten `:BestMoveTryTop` (3) liegt UND höchstens `:BestMoveTryWithinPawns` (0,5)
       hinter dem Ersten. Die beste Antwort der tiefen Rechnung zählt nicht (sie ist schon die Kandidatenlinie).
    2. **Taktisch falsch = tiefe Sicht** (`MoveComparisonLineKind.BestMoveTry` = 4): nur die verlockenden Züge, höchstens
       `:BestMoveTries` (2, 0–4, **0 = aus**), je ein Auftrag mit EINER Linie in der Tiefe des Vergleichs. In den Prompt
       kommt ein Zug nur, wenn er dort für den Gegner mindestens `:BestMoveTryLossPawns` (1,0) schlechter ist als seine
       beste Antwort; sonst fällt er **still weg** (kein „geht ebenfalls“ — das wäre genau das Rauschen).
    Alle Schlüssel unter `ClubMistakeComparisons:`. Ohne eigene Rechnung: ein Zug, der schon fertiger Antwort-Test ist
    (seine Zahlen werden von dort gelesen), und einer, den eine weitere Linie des besten Kandidaten tief schon als „nicht
    widerlegt“ zeigt.
  * **Leerer Hash ist Pflicht** (`AnalysisJobEngineResolver.FreshSessionMaxDepth` = 4): alle Hintergrund-Aufträge eines
    Kontos teilen sich die Sitzung `rh-bg-{UserId}`, der Provider schickt `ucinewgame` nur bei einer NEUEN `sessionId` — eine
    Tiefe-1-Suche auf dem warmen Hash der tiefen Rechnung derselben Partie sieht deren Ergebnis. Gemessen mit dem Stockfish
    des Engine-Providers an der Stellung nach 20...e4 (Tiefe 1, 5 Linien): leer 1. Lf4 −1,94, **2. Txe4 −2,34** (0,40
    zurück), 3. Lf1 −2,49; nach einer Tiefe-20-Suche derselben Stellung ohne `ucinewgame` 1. Lf4 −2,49, 2. Lf1, 3. Lb3,
    **4. Txe4 −3,06** (0,57) — Txe4 wäre nicht ausgewählt worden; mit `ucinewgame` wieder Rang 2. Tiefenaufträge bis
    Zieltiefe 4 bekommen deshalb eine eigene Sitzung (`rh-bg-{UserId}-fresh-{JobId}`); der folgende gewöhnliche Auftrag
    wechselt zurück und beginnt ebenfalls auf leerem Hash (zwei `ucinewgame` je flacher Sicht). Die Zahl der Linien
    ändert auf leerem Hash nichts (gemessen mit 5, 40 und allen 51 legalen Zügen als Linien: jedes Mal Txe4 Rang 2, 0,40
    zurück) — fünf Linien genügen für „unter den besten drei“.
  * **Ablauf**: `MoveComparisonService.StartExtraLinesAsync`, nur wo Fable schreibt (wie die Verteidigungen). Erster
    Durchgang: die flache Sicht zusammen mit den Verteidigungen → zurück auf `Replies`. Zweiter Durchgang, sobald die
    flache Sicht da ist: die tiefen Rechnungen der verlockenden Züge → noch einmal `Replies`, dann „wird erklärt“. Die
    teure Rechnung läuft so nur für die null bis zwei Züge, die die flache Sicht bestehen. **Gescheitert = erledigt**:
    ohne flache Sicht keine verlockenden Antworten, eine gescheiterte tiefe Zeile fehlt im Prompt; die Zusammenfassung
    (`ClubGameSummaries.ReadinessAsync`) wartet nur, solange etwas offen ist. Re-Explain (`POST
    /api/admin/move-comparisons/{id}/re-explain`) holt beides für den Bestand nach und gibt einer gescheiterten flachen
    Sicht einen neuen Anlauf.
  * **Mehrarbeit** (Schätzung aus Prod-Daten vom 14.11., nicht live gemessen): die Regel, mit python-chess und dem
    Stockfish des Engine-Providers nachgebaut (Tiefe 1, 5 Linien, leerer Hash) und über die Stellung nach dem besten Zug der
    473 bisher von Fable erklärten Vergleiche gelegt, ergibt 0 tiefe Rechnungen bei 91 (19 %), 1 bei 131 (28 %), 2 bei 251
    (53 %) — im Mittel **1,34 je Fehler**, mehr als die anderen Verteidigungen (0,93). Ein Auftrag dieser Art (eine Linie,
    Tiefe 30) dauerte im Bestand im Mittel 95 s (Median 44 s) → grob 2 min mehr Engine-Zeit je Fehler, dazu der
    Tiefe-1-Auftrag (Millisekunden Rechenzeit, aber ein Platz in der Warteschlange, ein zweiter Durchgang und zweimal
    `ucinewgame`). Wie oft ein Block übrig bleibt, ist nur grob geschätzt (Ersatzrechnung mit Tiefe 14 statt 30): bei 119
    von 473 (25 %), zusammen 146 Züge, davon 33 Schläge — die meisten widerlegten verlockenden Züge sind also KEINE Schläge.
    Stellschrauben, wenn das zu viel Engine-Zeit ist: `:BestMoveTries` 1, `:BestMoveTryTop` 2, `:BestMoveTryWithinPawns` kleiner.
  * **Fakten** (Block „Obvious replies to the better move … and why they fail“, nur im Prompt für Fable, hinter „The
    opponent's best answer to … itself“): je Zug, was er schlägt, sein Platz in der flachen Sicht („looks good at first
    sight: the shallow search (depth 1) ranks it 2nd of 51 legal moves, 0.40 pawns behind its first choice“), die
    Bewertung gegen die nach der besten Antwort, „this reply fails“, die Erwiderung des Lesers mit Engine-Linie und
    „Concretely“. Dafür zwei neue geprüfte Fakten in `ExplanationFacts`: `DiscoveredAttacks` (Abzugsangriff: nach dem Zug
    bestreicht eine ANDERE eigene Langschrittfigur eine gegnerische Figur, die sie davor nicht bestrich — „dxc3 opens the
    d-file for the queen on d6, which now attacks the queen on d1 (a discovered attack)“; ohne Bauern/König als Ziel, nur
    Geometrie) und `HangingCaptures` (eine Figur wird geschlagen und kann auf dem Feld nicht zurückgeschlagen werden —
    „Qxa3 takes the knight on a3 and the queen on a3 cannot be recaptured (at the start of this line the knight was
    defended only by the pawn on b2, which is no longer there)“; kein Fakt beim bloßen Zurückschlagen, keine Bauern). Dazu
    wie bisher `CheckForks`, `FirstMoveAttacks` und die Materialbilanz ab der Stellung VOR dem besten Zug. Beide neuen
    Fakten stehen NUR in diesem Block (die Blöcke von 0.971.0 sind unverändert). Die Züge zählen zu `AllowedMoves`; ein
    verlockender, aber tief nicht widerlegter Zug bleibt verboten.
  * **Auftrag** (`ClubFableExplanations.SystemPrompt`, Absatz 2): zuerst in einem Satz, warum die naheliegende Antwort
    nicht geht (widerlegender Zug mit Zugnummer + Faktum, Beispiel „21.Txe4 scheitert an 21...dxc3 22.bxc3 Dxa3 mit
    Figurengewinn“), DANN die ruhige Hauptlinie. Wortgrenze unverändert 200.
  * **Bestand bleibt**; Vergleich neu erklären wie oben (Re-Explain).
* **Drohung des besten Zuges in EINEM Satz** (0.999.6, Befund 2026-11-22 an Vereinspartie 260, Nagy – Schwaz, Fehler 1
  15...Sb4 statt 15...Td8, Vergleich 2824, Stellung `r1b2rk1/1pq1bppp/pnn1p3/8/4N3/2N1B1P1/PP2PPBP/R2Q1RK1 b`): die
  Antwort-Tests rechnen die besten Antworten auf den Fehlzug auch gegen den besten Zug — gegen 15...Td8 stellen 16.Tc1 und
  16.a3 einfach die Dame auf d1 ein, und Fable schrieb „Auf 16.Tc1 oder 16.a3 geht einfach 16...Txd1 17.Tfxd1 mit
  Damengewinn“. Nutzer: „es reicht zu sagen, Weiß muss die Dame wegziehen“.
  * `MoveComparisonPrompts.ThreatsOf`: Figuren des Gegners (ohne Bauern/König), die die gezogene Figur nach dem besten Zug
    angreift und die mehr wert sind als sie oder ungedeckt stehen (kein Schachgebot). Faktum im Nutzer-Prompt (beide Modelle):
    „Concretely, 15...Rd8 attacks the queen on d1 — a direct threat: the opponent must react to it …“.
  * `IgnoresThreat`: eine Antwort (Antwort-Test oder „Obvious reply“), die weder schlägt noch Schach gibt, nach der eine
    bedrohte Figur noch auf ihrem Feld steht und deren Erwiderung sie sofort schlägt oder die ≥ 3 Bauern schlechter ausgeht
    als die beste Antwort → nur noch „- 16.a3: ignores the threat to the queen on d1 — not a real option, do not discuss.“,
    ohne Bewertung und Linie. Ihre Linien fallen aus `AllowedMoves` (der Befundtext würde verworfen); der Zug selbst bleibt
    erlaubt, wenn er woanders steht (z. B. als Antwort auf den Fehlzug).
  * Auftrag Fable (Absatz 2): für die Drohung EIN kurzer Satz („15...Td8 greift die Dame auf d1 an, Weiß muss sie
    wegziehen“), dann die echten Antworten; Wortgrenze 150 statt 200; Bewertungen ausdrücklich aus Sicht von Weiß
    (der Text schrieb „+7,01 aus seiner Sicht“), bei Schwarz am Zug sagt der Hinweis, dass das Vorzeichen umzudrehen ist.
    Lokales Modell: derselbe Halbsatz-Auftrag im `SystemPrompt`.
  * **Bestand bleibt**; Vergleich neu erklären wie oben (Re-Explain).
* **Linie bis zum Materialgewinn** (0.999.7, Befund 2026-11-22 an Vereinspartie 260, Fehler 4 20...Td8 statt 20...b6,
  Vergleich 2828): der Text schrieb „21...Sxb4 scheitert an 22.axb4 … Weiß bleibt nach 22...Txd1 23.Tfxd1 materiell vorn“
  — nach 23.Tfxd1 ist das Material nicht entschieden, erst 25.Td8+ Df8 26.Txf8+ gewinnt die Dame für den Turm.
  * `ExplanationFacts.LineEvents`: hinter der Materialbilanz, falls der Vorsprung erst ab dem vierten Halbzug der Linie
    dauerhaft besteht, „material is decided only at 26.Rxf8+ — the line must be shown up to there: 25.Rd8+ Qf8 26.Rxf8+
    Kxf8 (in this exchange White takes queen, Black takes rook); before that White is not ahead for good“ (gilt in allen
    Blöcken, die die Materialbilanz tragen).
  * Auftrag Fable (Absatz 1): Linie nie vor dem materialentscheidenden Zug abbrechen, die Grenze von zwei bis vier
    Halbzügen gilt dann nicht, Zwischenzüge raffen; Materialgewinn nur mit dem Zug, der ihn bringt. Lokales Modell: den
    Zug aus dem Faktum zusätzlich nennen.
  * **Bestand bleibt**; Vergleich neu erklären wie oben (Re-Explain).
* **Deckung geprüft, Material in Figuren, Motiv aus einer Sicht** (1.000.1, Befund 2026-11-22 an Vereinspartie 260,
  Fehler 5 21...Tb8 statt 21...b6, Vergleich 2830, Stellung `r1br2k1/1p3ppp/p3p3/q1Nn4/8/P5P1/1PQ1PPBP/2R2RK1 b`): der Text
  schrieb „Angriff auf das ungedeckte Pferd auf d5“ (d5 war durch e6 und Td8 gedeckt, auch nach 22.Db3), „Schwarz netto
  einen Bauern vorn (−6,43)“ (Schwarz war eine Figur vorn) und besprach 22.Db3/22.Tfd1, die nach 22...bxc5 den Springer
  einstellen.
  * Drohung (0.999.6) gilt auch für Bauernangriffe auf Leichtfiguren (21...b6 → „attacks the knight on c5“); eine Antwort,
    deren Erwiderung die bedrohte Figur schlägt, gilt erst ab 2 Bauern Verlust als Einsteller (sonst 3) — 22.e4 (Gegenangriff,
    nach 22...Dxc5 Ausgleich) bleibt eine Linie.
  * `ExplanationFacts.Coverage`: je Figur, die der erste Zug einer Linie angreift, „the knight on d5: attacked by the bishop on
    g2 and the queen on b3; defended by the pawn on e6 and the rook on d8“ (bzw. „defended by nothing — undefended“); überall,
    wo `FirstMoveAttacks` steht.
  * Materialbilanz in Figuren (`ExplanationFacts.InPieces`, angehängt an „… ends up ahead in material by N (…)“): „; in
    pieces: Black is a minor piece up for 2 pawns“ — gleiche Arten heben sich auf, Läufer/Springer als Leichtfiguren.
  * `MoveComparisonClaims.FalseUndefended`: nennt der Text eine Figur auf einem Feld „ungedeckt/ungeschützt/hängt“ (en
    „undefended/unprotected/hanging/loose“), wird in der Stellung vor dem Zug, nach beiden Kandidaten und in jeder Stellung
    der Linien nachgesehen; ist die Figur dort nirgends ungedeckt, fragt Fable (bzw. das lokale Modell) mit Hinweis neu an
    (zählt zum Tagesdeckel; zweiter Fehlschlag → wie bisher Rückfall).
  * Auftrag Fable: Motiv aus Sicht der Seite, die den Fehler spielte, ohne die Pläne beider Seiten zu mischen; „ungedeckt“
    nur mit „defended by nothing“; Materialaussagen nur aus „in pieces“, nie aus der Bewertung. Lokales Modell sinngemäß.
  * **Bestand bleibt**; Vergleich neu erklären wie oben (Re-Explain).
* **Nicht offensichtliche Verteidigungen statt Einsteller, „nur von X gedeckt“ geprüft** (1.001.1, Befund 2026-11-22 an
  Vereinspartie 261, Fehler 19...Le5 statt 19...f4, Vergleich 2854, Stellung `r4rk1/p5bp/2p1b1p1/5p2/2P1p3/1P2B3/P3BPPP/1R1R2K1 b`):
  die Widerlegung 20.f4 greift den Läufer an; der Text erklärte lang 20...Lxf4 21.Lxf4 („nutzlos, absolut offensichtlich“),
  nichts zu 20...exf3 e.p., und schrieb „das Feld f4 wurde nur vom Läufer selbst gedeckt“ (Le3 deckt f4).
  * `MoveComparisonDefences`: ein En-passant-Schlag zählt als Schlagen der gezogenen Figur (Feld der geschlagenen Figur =
    Linie des Ziels, Reihe des Ausgangsfelds) — vorher fiel 20...exf3 durchs Raster, gerechnet wurde nur 20...Lxf4.
  * Auswahl: Züge, die nach grober Brettrechnung (eigener Schlag minus bester einfacher Schlag des Gegners, gedeckte Figur
    nur bei Mehrwert) mindestens 2 Bauern schlechter stehen als der Zug der Linie und kein Schach geben, rücken nach hinten.
  * Fakten: eine gerechnete Verteidigung gilt als offensichtlicher Einsteller (`Defence.ObviousLoss`), wenn sie kein Schach
    gibt, der erste Zug der Engine-Linie schlägt, die Materialbilanz des Verteidigers nach einem UND zwei Halbzügen mindestens
    2 Bauern unter der Hauptlinie (Zug + zwei Halbzüge) liegt und die Bewertung mindestens 2 Bauern schlechter ist. Dann
    steht nur „- 20...Bxf4 instead of 20...Bc7: obviously loses material (21.Bxf4 simply recaptures) — do not discuss.“ da,
    ohne Linie und Fakten, sie steht nach den anderen und fehlt in `AllowedMoves`. Widerlegungen mit Schach/Gabel wie
    245/25...Lc8 (26...Txc8 27.Sd7+) bleiben erklärt.
  * Feld-Faktum „… was covered only by …, which is no longer there“ entfällt, wenn die deckende Figur selbst auf das Feld zog.
  * `MoveComparisonClaims.FalseSoleDefender`: „nur von/vom X (selbst) gedeckt“, „einzige Deckung/Verteidiger“, „defended
    only by“, „only/sole defender“ — geprüft in denselben Stellungen; die Aussage gilt, wenn dort genau EINE Figur der
    eigenen Farbe den Bauern/die Figur auf dem Feld deckt und zur genannten Art (und zum Feld) passt; „selbst“ gilt nie.
  * Auftrag Fable (lokal sinngemäß): Fokus auf der Frage, die sich ein Klubspieler wirklich stellt — nicht offensichtliche
    Verteidigungen erklären, offensichtliche Einsteller nicht.
  * **Re-Explain plant fehlende Verteidigungen nach**: die Pumpe vergleicht die heutige Auswahl mit den vorhandenen
    Zeilen (Kandidat, Zug, Halbzug) und reiht die fehlenden ein — vorher nur, wenn ein Vergleich GAR KEINE hatte. Für 2854
    nach dem Deploy: `POST /api/admin/move-comparisons/2854/re-explain` → 20...exf3 wird gerechnet, dann neu erklärt.
* **Bauernstruktur-Fakten** (1.001.1, Befund 2026-11-22 an Vereinspartie 261, Fehler 21...exf3 statt 21...h5, Stellung
  `r1r3k1/p6p/2p1b1p1/2B1bp2/2P1p3/1P3P2/P3B1PP/1R1R2K1 b`): nach 21...h5 … 26.Txd1 fxe4 hat Schwarz einen isolierten
  e-Bauern, der Text sagte nur „ausgeglichen“ — der Nutzer: „warum ist das kein Problem für Schwarz?“ (e4 ist Freibauer,
  Weiß hat die Damenflügel-Mehrheit).
  * `PawnStructure` (Games): isoliert, doppelt, rückständig, Freibauer („isolated but passed“), halboffene Linie und
    Blockade beim Isolani/rückständigen Bauern, Mehrheiten je Flügel (a–d / e–h) — nur, wenn am Ende der Linie eine dieser
    Eigenschaften gegenüber der Stellung vor dem Zug NEU ist. Steht am Ende der besten Antwort-Linie nach dem schwächeren
    Zug (in „Concretely …“) und nach der besten Antwort auf den besten Zug („Concretely, after 21...h5 and that line: pawn
    structure at the end of this line (after 26...fxe4): Black's pawn e4 is isolated but passed; White has a 3-vs-2
    queenside majority; Black has a 3-vs-2 kingside majority.“) — für Fable und das lokale Modell.
  * Auftrag: auffällige Schwäche bei etwa gleicher Bewertung → EIN Satz aus den Fakten, warum sie nicht zählt; keine
    Strukturaussage ohne Faktum. `MoveComparisonClaims.FalsePawnClaims` prüft „isoliert/Isolani/Freibauer/Doppelbauer/
    rückständig“ (en „isolated/passed pawn/doubled/backward“) am Brett, verneinte Aussagen ausgenommen.
* **Besitz und Sichtbarkeit**: Besitzer ist der Besitzer der Analyse (Haus-Engine-Konto). Alle Nutzerwege (`/api/move-comparisons`
  Liste/Lesen/Löschen, `MaxOpenPerUser`, `MaxKeptPerUser`) filtern `GameAnalysisId == null` — die Zeilen zählen nirgends mit.
  Lesen darf, wer die Vereinspartie sieht: `GET /api/league/club/games/{id}/comparisons` (alle, nach Halbzug: `{ ply,
  playedUci, analysisBestUci, comparison }`) und `GET /api/league/club/games/{id}/comparisons/{comparisonId}` (wie
  `GET /api/move-comparisons/{id}`), beide `league.view` im Verein der Partie; nicht über Teilen-Links.
* **Weg damit**: Partie gelöscht → Analyse gelöscht (`GameAnalysisDeletion` räumt Zeilen + offene Aufträge, die DB kaskadiert
  `FK_MoveComparisons_GameAnalyses_GameAnalysisId`); Züge korrigiert → `RebaseClubGameAsync` löscht sie und setzt die Marke
  zurück, das neue Ende legt sie neu an.
* **Kosten** (Stand 08.11., vor der Übernahme aus der Analyse — siehe oben): Prod hatte 230 Vereinspartien mit fertiger Hauptanalyse (Tiefe 20, ohne Vertiefung). Partie 236
  von Hand mit Tiefe 24: 11 Fehler, je 1,5–3 min. Tiefe 35 ist 11 Halbzüge tiefer — grob Faktor 20–80 → etwa 0,5–4 h je
  Vergleich auf einer Engine, also Wochen für den ganzen Altbestand. Deshalb einer nach dem anderen, nur im Leerlauf, neueste
  zuerst; Tiefe und Gleichzeitigkeit sind Schlüssel.
  Seit 0.909.0 (Tiefe 30): Prod-Messung der Tiefe-30-Vergleiche (3 Stück) im Schnitt 5 min je Vergleich → ~2 300 Fehler
  der 230 Altpartien ≈ 190 Engine-Stunden bei `MaxActive` 1, also ca. 8 Tage Dauerlauf, dazu die Vertiefung selbst (siehe dort).

**Vergleichsmodus (0.375.0)**: Der Waagen-Knopf startet eine ZWEITE Engine auf derselben
Stellung (`compareEngine`), beide Linienlisten stehen untereinander. Möglich ohne Umbau, weil
`AnalysisEngineService` weder Konstruktor noch `inject()` hat — er lässt sich schlicht per `new`
ein zweites Mal instanziieren (eigener Worker, eigener Zustand, eigene Generationszählung).
Drei Regeln, die dabei nicht kippen dürfen: (1) **nie dieselbe Engine beidseitig** — der Schutz
sitzt zentral in `startCompare`/`ensureDistinctCompareEngine` und wird auch beim Wechsel der
HAUPT-Engine und beim Wiederherstellen aus localStorage durchlaufen (sonst zwei WASM-Kerne bzw.
zwei Ströme auf dieselbe externe Engine); (2) **das Etikett muss die Wahrheit sagen** — fällt
eine Seite auf WASM zurück, nennt `compareEngineName`/`mainEngineName` „Browser" statt des
gewählten Namens; (3) **kein Block ohne Engine dahinter** — das Template hängt an
`compareRunning` (Schalter AN *und* Instanz vorhanden), sonst stünde nach einem Neuladen ohne
Engine-Liste ein ewiges „Berechne…" ohne erreichbaren Ausschalter.

**Frontend**: `features/analysis/external-engine.service.ts` (ndjson über den normalen `HttpClient` mit
`reportProgress` — `partialText` wächst, nur NEUE vollständige Zeilen werden geparst; so greifen die
Interceptors inkl. Auth). Der `AnalysisEngineService` bekam einen zweiten Analyse-Pfad
(`setRemoteEngine`/`analyzeRemote`): dieselbe `analysis$`-Schnittstelle, dieselbe `AnalysisLine`-Form,
also unverändertes Brett/Eval-Bar. **Die Bewertung kommt remote bereits aus Weiß-Sicht** (Spez.) — im
Gegensatz zum WASM-Pfad (`parseInfo`) darf `mapRemoteLine` das Vorzeichen deshalb NICHT drehen.
**Rochaden liefert der Broker als König-schlägt-Turm** (`e1h1`, lila-engine `emit.rs` nutzt
`CastlingMode::Chess960`) — chess.js kennt in der Standardvariante nur `e1g1` und bräche die Variante
dort ab. `castling-uci.util.ts` schreibt das um, aber NUR wenn auf dem Startfeld wirklich ein König
steht (`e1h1` ist ein legaler Turmzug, wenn der König woanders steht) — deshalb wird die Linie
mitgespielt statt Strings zu ersetzen. **Zwei Analyse-Pfade, EIN Zustand**: `searchGen` (beim Absetzen
des `go` festgehalten) hält Worker-Zeilen einer überholten Suche aus `state$` — der frühere Vergleich
`gen` gegen `gen` im selben Aufruf war wirkungslos, sodass Nachzügler des WASM-Kerns die Remote-Anzeige
überschrieben und sein spätes `bestmove` die laufende Suche für beendet erklärte. Beim Umschalten wird
der lokale Kern zusätzlich gestoppt (`stopLocalSearch`), und `analyze()` prüft die Auswahl NACH dem
`await init()` erneut (die Engine-Liste kann während des WASM-Handshakes eintreffen).
Scheitert die Remote-Suche VOR der ersten Datenzeile (Provider offline; auch: gar keine Antwort binnen
12 s), fällt der Service für den Rest der Sitzung still auf WASM zurück und die Seite sagt es
(`analysis.remoteFallback`); ein Abriss MITTEN im Stream gilt dagegen als beendete Suche (Ergebnis
bleibt stehen). Der angeforderte **Hash** ist auf `AnalysisEngineService.MaxRemoteHashMb` (4096 MB) gedeckelt — die von Lichess
gemeldete Registrierungs-Grenze (bis 1 TiB) ist keine Aussage darüber, was der Provider-Rechner je Analyse
allozieren soll; die Hintergrund-Aufträge nehmen dagegen das volle gemeldete Maximum (eigener Prozess, lange
Läufe). Token-Verwaltung: Profil-Karte `features/profile/engine-card.component.ts`.
**nginx**: eigene `location /api/engine/` mit `proxy_buffering off` + langen Timeouts — sonst sammelt
der Proxy die info-Zeilen bzw. kappt eine tiefe Suche nach 60 s. **Das genügt nicht allein**: nginx
VERBRAUCHT den `X-Accel-Buffering: no`-Header der API und reicht ihn NICHT weiter, ein davor
stehender Reverse-Proxy (hier Nginx Proxy Manager) sieht ihn also nie und puffert weiter. Deshalb
setzt der Frontend-nginx den Header für `/api/engine/` per map + server-weitem `add_header` SELBST.
**In Prod aufgetreten (0.373.0)**: kurze Suchen kamen am Stück an, lange (Tiefe 22 × 3 Linien > 12 s)
gar nicht — der Browser sah null Bytes, brach ab und meldete „Externe Engine nicht erreichbar",
während Provider und Broker fehlerfrei rechneten. Im NPM-Zugriffslog erkennbar an `499` mit
`Length 0` bei langen und `200` mit ~15 KB bei kurzen Suchen.
**Lebenszeichen + Fortsetzung (0.377.0)**: Der API-Proxy kopiert den Broker-Stream nicht mehr nackt
(`Services/Engines/NdjsonHeartbeatPump.cs`): schweigt der Broker 20 s, geht eine Leerzeile raus. Grund: bei
MultiPV 5 liegen ab Tiefe ~27 Minuten zwischen zwei Zeilen, und NPM (Default `proxy_read_timeout`
60 s) kappte den Stream, den der Browser dann als „fertig" wertete (Prod-Log: Streams mit exakt ~61 s,
Anzeige bleibt bei Tiefe 27 stehen). Der Client-Parser ignoriert Leerzeilen und Steuerzeilen ohne `pvs`
(das `{"keepalive":true}` des Providers — durchgelassen stünde die Anzeige alle 15 s bei Tiefe 0 ohne
Linien). Reißt ein Stream trotzdem
vor der Zieltiefe ab (Fehler ODER ≥ 5 s Funkstille vor dem Ende — ein Ende direkt nach der letzten
Zeile ist die Engine selbst, z. B. einzüge Stellungen), setzt `AnalysisEngineService.startRemoteStream`
bis zu dreimal ab der erreichten Tiefe fort (flache Wiederholungszeilen aus der warmen Hashtabelle werden
verschluckt, die Linien bleiben stehen) und meldet es über `remoteInterrupted$`
(`analysis.remoteCutResuming`/`remoteCutFinal`); ohne Tiefenfortschritt zwischen zwei Abrissen gilt der
letzte Stand als Ergebnis — bewusst KEIN Fallback auf WASM, das würde tiefe Linien durch flache ersetzen.
Die Karte zeigt zusätzlich „rechnet seit m:ss an Tiefe N" ab 5 s ohne neue Zeile (`showThinking`).

**Gegenstelle beim Nutzer**: `engine-provider/` ist ein fertiges Docker-Setup für den Rechner des
Users (Anleitung dort in der `README.md`). Es startet den OFFIZIELLEN Lichess-Provider — beim Bauen
auf einen Commit gepinnt + per Prüfsumme verifiziert statt ins Repo kopiert (eindeutige Herkunft,
Update = Zeilenwechsel im Dockerfile). Eigener Anteil: `entrypoint.sh` (Aufruf aus `.env`-Variablen)
und `preflight.py` (prüft den Token via `POST /api/token/test` VOR dem Start). **Seit 0.537.0 zeigt `ROOKHUB_URL`
beide Basen (`--lichess`, `--broker`) auf RookHub** und `ROOKHUB_API_TOKEN` ist der Token dafür (eigener
Broker, siehe oben; seit 0.538.0 hat er mit `ROOKHUB_URL` VORRANG vor einem daneben stehenden `LICHESS_API_TOKEN`,
und ohne `ROOKHUB_URL` ist er ein Abbruch mit Klartext — bei Lichess gilt er nicht); ohne `ROOKHUB_URL` läuft alles
wie bisher über Lichess. **EIN Container bedient EINEN Broker; beide Wege gleichzeitig sind ZWEI Container** (0.538.0,
Wunsch 2026-09-26 „lichess & eigener Broker", bewusst nicht ein Container, der beides kann): `compose.yml` hat den
zweiten Dienst `engine-provider-lichess` am Profil `lichess` mit eigener `.env.lichess` (Vorlage
`.env.lichess.example`, OHNE `ROOKHUB_URL`); `docker compose --profile lichess up -d` bzw. dauerhaft
`COMPOSE_PROFILES=lichess` in der `.env`. Compose braucht die `.env.lichess` nur bei aktivem Profil (geprüft). Über
Lichess gehört nur die Live-Engine, die Hintergrund-Engines in den direkten Container (IP-Drosselung). In RookHub
stehen Engines beider Quellen ohnehin in EINER Liste (`GET /api/engine/external`, `EngineRegistry`), die
Hintergrund-Liste darf `rhe_` und `eei_` mischen. **Das Lebenszeichen patcht das
Image seit 0.478.11 nicht mehr hinein** (bis dahin `patch_provider.py`): der gepinnte Stand (`d0eeb242`, 2026-09-06) erfüllt die zwei Regeln
des Brokers, an denen RookHub hängt, und `test/provider.test.py` prüft sie gegen einen nachgebauten Broker.

1. **Jede Suche endet mit `bestmove`.** Der Broker verlangt das seit lila-engine `0e1223b` (2026-09-06) und
   antwortet sonst `400 uci protocol error: expected bestmove before end of stream`. Der vorher gepinnte
   Provider (`a6ef15a8`) schickte nie eins, bekam die 400 bei JEDER Suche, schlief danach 5 s und holte erst
   dann den nächsten Auftrag. Gemeldet als „es dauert relativ lang, bis die Cloud-Engine anspringt": ein Zug
   eine Sekunde nach einer beendeten Suche wartete 4,1 s auf die erste Zeile (gegen den echten Broker
   gemessen, 2026-09-15). Die 400 stand schon vorher im Provider-Log, galt aber als Folge gekappter
   Verbindungen — so blieb die Verzögerung neun Tage unbemerkt.
2. **Lebenszeichen alle 15 s** (`{"keepalive":true}`). Der Provider reicht nur `info`-Zeilen MIT `score`
   weiter, zwischen zwei tiefen MultiPV-Iterationen vergehen Minuten, und der Broker kappt nach 60 s Stille —
   bei uns sichtbar als `HttpIOException: The response ended prematurely`; Aufträge kamen nie über Tiefe 29
   hinaus, 23 hingen auf Dev bei Tiefe 20/22. Bis 0.478.10 lieferte das ein eigener Eingriff
   (`patch_provider.py`: Wiederholung der letzten `info`-Zeile — eine Leerzeile verwirft der Broker). Dieselbe
   Klasse Fehler wie der `NdjsonHeartbeatPump` auf der Strecke API→Browser, nur einen Hop weiter vorne. Das
   Keepalive kommt beim Empfänger als eigene ndjson-Zeile an: der Worker zählt es als Lebenszeichen
   (`StreamTally.IsKeepalive`), der Browser verwirft es im Parser (`ExternalEngineService.analyse`).

**Ein Eingriff bleibt (0.535.1, `patch_force_close.py`)**: der asynchrone Provider lädt Suchen über eine
aiohttp-Sitzung mit Verbindungs-Pool hoch; eine inzwischen von der Gegenseite geschlossene Pool-Verbindung
fällt erst beim nächsten Upload auf — Auftrag schon abgeholt, Engine hat `go`, Upload stirbt im ersten Byte,
der Anfragende bekommt nach 15 s einen 503 (upstream Issue #45, per A/B belegt). `TCPConnector(force_close=True)`
für die Upload-Sitzung (nicht für den Poll) behebt es; `test/provider.test.py` verlangt drei Uploads auf drei
Verbindungen, die CI wendet den Patch vor dem Test an (`DeploymentConfigTests` hält die Zeile fest). Weg damit,
sobald upstream es behebt.

**Wer den Pin anhebt, lässt `test/provider.test.py` laufen** (die CI tut es): die Regeln des Brokers ändern
sich, ohne dass ein laufender Provider davon erfährt. Gegen den alten Stand scheitert der Test mit genau den
gemeldeten vier bis fünf Sekunden. **Reihenfolge beim Ausrollen**: erst das Frontend mit dem Parser-Filter,
dann den Provider — ein älteres Frontend reicht jedes Keepalive als leeres Ergebnis an die Anzeige weiter.

Zwei Fallen, die dort
bewusst adressiert sind: der Provider-Token braucht `engine:read` **und `engine:write`** (er
REGISTRIERT die Engine; RookHub selbst genügt `engine:read`; ein RookHub-Token mit Scope `engine` meldet beides) — ohne Vorabprüfung endete das in einem
401-Stacktrace, der sich unter `restart: unless-stopped` endlos wiederholt; und die Registrierung
wird über den **Namen** identifiziert (gleicher Name = Aktualisierung, zwei Rechner brauchen zwei
Namen, sonst überschreiben sie sich). **`ENGINE_COUNT` (0.378.0)**: ein Container kann mehrere
Provider = mehrere registrierte Engines fahren (`ENGINE_<i>_NAME/_MAX_THREADS/_MAX_HASH` je Engine,
sonst `ENGINE_NAME <i>`) — gedacht als „Server Live" + „Server Hintergrund" für die Hintergrund-
Analyseaufträge; beide dürfen alle Kerne haben, weil RookHub den Hintergrund pausiert, sobald Live
rechnet. Stirbt ein Provider, endet der Container mit dessen Code (restart zieht alle neu). Der
Entrypoint ist deshalb bash (`wait -n`); `ENTRYPOINT_DRY_RUN=1` zeigt nur die Aufrufe —
`engine-provider/test/entrypoint.test.sh` prüft damit den Argument-Aufbau. **Die Provider starten
GESTAFFELT** (`PROVIDER_START_DELAY`, Vorgabe 3 s, seit 0.534.1): jeder registriert sich beim Start bei
lichess.org, und 13 Registrierungen im selben Augenblick hielt der DDoS-Schutz von Lichess für einen
Angriff (2026-09-11 auf der zweiten Maschine: 429, dann IP-Sperre, null Engines — jeder Neustart
wiederholte es). `test/supervisor.test.sh` misst den Abstand der echten Starts.
