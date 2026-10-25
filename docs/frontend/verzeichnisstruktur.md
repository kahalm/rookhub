# Verzeichnisstruktur

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


```
app/src/app/
  core/
    auth.service.ts          JWT-Management, Login/Register/Logout, localStorage
    auth.guard.ts            CanActivateFn, redirect zu /login
    auth.interceptor.ts      HttpInterceptorFn, Bearer-Token an Requests
  features/
    auth/
      login.component.ts     Login-Formular (username + password)
      register.component.ts  Registrierung (username + email + password)
    dashboard/
      dashboard.component.ts Uebersicht: Repertoire-Count, Subscription-Count, Freunde-Count, Abo-Liste
    profile/
      profile.component.ts   Profil bearbeiten (DisplayName, FideId, ChessResults, Chess.com, Lichess)
    friends/
      friends.component.ts   Freundesliste + Requests (Tabs), User-Suche, Request senden/akzeptieren/ablehnen
    repertoire/
      repertoire-list.component.ts          Liste aller Repertoires, Create-Dialog
      repertoire-detail.component.ts        Dateien-Liste, Drag&Drop Upload, Download, Delete
      create-repertoire-dialog.component.ts MatDialog: Name, Description, IsPublic
    tournaments/
      tournament-list.component.ts          Turnierliste vom Crawler, Subscribe-Button
      tournament-detail.component.ts        Tabs: Players (Table), Teams (Table), Pairings (Table + Round-Select)
    puzzles/
      book-puzzle.component.ts              Buch-Löser (Kurs/Wochenpost/Tagespuzzle/Buch): Container, siehe unten
  shared/
    navbar/
      navbar.component.ts   Material Toolbar, Navigation, User-Menu mit Logout
    loading-spinner/
      loading-spinner.component.ts  Zentrierter MatSpinner
    lines/
      chapter-groups.util.ts  groupByChapter: nach Kapitel gruppieren in Vorkommens-Reihenfolge
      mark-set.ts             MarkSet: Flashcard-Markierungen, optimistisch + Rollback
    chess/
      line-solver.ts          LineSolver + resolveExpectedUci/sameMove/judgeMove: „ist das der
                              erwartete Zug?" — EINE Regel fuer alle Loeser
```

- **Buch-Löser `features/puzzles/book-puzzle.component`** (Zerlegung Z18, Codereview 2026-09-29): Container
  (erbt `BasePuzzleSolver`, ist Wirt `BookModeHost`) + **Modus-Strategien** als reine TS-Klassen — `book-course-mode`
  (inkl. anonymer/öffentlicher und Offline-Kurs), `book-weekly-mode`, `book-daily-mode`, `book-standalone-mode`
  (Buch-Puzzle, Blättern, Versuch/Challenge/„Track solves“) — + Controller `book-review` (Durchsicht),
  `book-solve-mode` (Spielweise), `book-language` (Kommentar-Sprache), `book-last-solved` (letztes Puzzle/Teilen),
  `book-comment-view`, `book-hints`, `book-settings` + OnPush-Kinder `book-context-card` (mit `course-stats-panel`),
  `book-comment-block`, `book-board-hint`. DOM-Specs rendern die Kinder flach über `testing/book-puzzle-shallow.ts`.
- **`shared/lines/`** (seit 0.499.2): was sich die beiden LINIEN-LISTEN teilen — die Kurs-Durchsicht
  (`features/courses/course-browse.component.ts`) und die Repertoire-Linienliste
  (`features/repertoire/repertoire-lines.component.ts`). `groupByChapter(items, keyOf)` gruppiert in
  der Reihenfolge des ERSTEN Auftretens (= Lesereihenfolge; der Kapitel-SCHLÜSSEL ist generisch,
  weil „ohne Kapitel" im Kurs `null` und im Repertoire `''` heißt), `MarkSet<K>` hält die
  Flashcard-Markierungen und schaltet eine um: sofort in der Liste, Rollback bei Serverfehler. Die
  Klasse verhält sich nach außen wie ein `Set` (`has`/`size`/iterierbar), damit die Vorlagen
  unverändert `marked.has(…)`/`marked.size` benutzen.

- **`shared/chess/line-solver.ts`** (seit 0.499.8): der gemeinsame Kern der Löser — die Antwort auf
  „ist das der erwartete Zug?". Er legt die Semantik fest, und zwar auf **FELDER**: ein erwarteter
  Zug steht als `{ uci }` ODER `{ san }` da, wird über `resolveExpectedUci` auf der AKTUELLEN
  Stellung zu von/nach/Umwandlung aufgelöst (SAN per `chess.move(san)` auf einer KOPIE), und danach
  ist die SAN nur noch Anzeige — `Nbd2` = `Nb1d2` = `b1d2`, `0-0` = `O-O`, Suffixe `+#!?` zählen
  nicht. Verglichen wird mit `sameMove` nach der Präfix-Regel der Puzzles: **fehlt im Nutzerzug die
  Umwandlungsfigur, passt jede; steht eine ANDERE als erwartet, ist es falsch.** `judgeMove` urteilt
  über einen Nutzerzug OHNE ihn anzuwenden (`correct`/`alternative`/`wrong`/`illegal`/`not-your-turn`);
  angewendet wird getrennt. Zwei Fälle sind bewusst entschieden und mit Vektoren festgenagelt:
  **mehrdeutige SAN** (`Nd2`, wenn zwei Springer dorthin können) löst sich zu `null` auf statt zu
  einer Vermutung — für `judgeMove` heißt das, dass dann KEIN Zug `correct` sein kann, und wer das
  unterscheiden muss, fragt `resolveExpectedUci`/`expectedUci()` vorher; **lang-algebraische**
  Schreibweise braucht keine eigene Vorreinigung, weil chess.js sie in seiner (nicht-strikten)
  Vorgabe schon annimmt (an 1.4.0 nachgemessen, samt `0-0` und `e8Q`).
  Der Kern ist **synchron, ohne Timer, ohne Engine, ohne Angular** — Phasen, Verzögerungen, viz,
  Tipps und Eval bleiben bei den Aufrufern. Benutzbar auf zwei Arten: als Klasse `LineSolver`
  (eigenes Brett + Halbzug-Zählstand: `ply`/`expected`/`done`/`userToMove`/`expectedUci()`/`judge()`/
  `playExpected()`/`playFree()`/`opponentReply()`/`undo(n)`/`dests()`/`lastMove()`/`reset()`; `chess`
  ist ausdrücklich LESBAR) oder als reine Funktionen auf einer FREMDEN chess.js-Instanz.
- **Wer ihn benutzt**: `features/worksheets/worksheet-solve.component.ts`
  (`WorksheetTask` ist seither eine dünne Hülle um `LineSolver` — sie urteilt bewusst OHNE die
  Umwandlungsfigur des Dialogs, weil auf dem Blatt die Lösung die Figur setzt),
  `features/puzzles/base-puzzle-solver.ts` (seit 0.499.9: `onMoveMade` holt sein Urteil von
  `judgeMove` auf `this.chess`) und `features/repertoire/repertoire-trainer.component.ts` (seit
  0.499.10, siehe unten). Die letzten beiden benutzen die reinen FUNKTIONEN, nicht die Klasse: beide
  führen ihr Brett schon selbst und WEISEN es neu zu (`reviewGoToCore` im Puzzle-Löser,
  `startCurrentLine`/`showSolution` im Trainer) — ein zweites Brett daneben wäre die Quelle fürs
  Auseinanderlaufen. **Bewusst NICHT im Kern und weiterhin in Base**: moveLog samt `thinkMs`,
  `wrongMoveCount`, Off-Path-Zählung und -Warnung, der `ALT_HOLD_MS`-Timer des Alternativzugs, die
  Timer von `advanceAfterCorrectMove`, die Stockfish-Antwort (`opponentRespond`), `handleGameOver`,
  `mouseslip`, viz/Tipps/Eval. Der Kalkulations-Modus ist **kein Löser** und bleibt außen vor.
- **Repertoire-Trainer am Kern** (0.499.10): `onMove`, `onLearnMove` und `showSolution` urteilen
  über `judgeMove`/`resolveExpectedUci` statt über `normSan`-Textvergleich. Geurteilt wird gegen
  `this.fen` (NICHT `this.chess`): im Wiederhol-Fall nach einem Fehlzug ist `this.fen` bereits auf
  die Ausgangsstellung des Halbzugs zurückgesetzt. `altsAt(cardKey)` bringt die `[%alt]` des
  Repertoire-Graphs in die Form des Kerns und siebt den Hauptzug NICHT mehr aus — die alte Zeile
  `accepted.delete(expectedSan)` steckt im Kern, der `expected` zuerst prüft. Der Lern-Modus gibt
  bewusst eine LEERE Alternativen-Liste mit (er duldet keine). Unangetastet: `pendingWrong`/
  Mausrutscher/Streak, `lineHadWrong` als Urteil über die LINIE, der Eval-Vergleich und die
  Anzeige (`expectedDisplay`, `movesInLine`, `currentMovePrettyLabel`) — dort ist SAN Text für
  Menschen. `normSan` bleibt bestehen (es speist `lineKeyFromSans`, den handgespiegelten Gegenpart
  zu `ChessableTrainedLineService.LineKeyFromSans`) und hat im Trainer keine Verwendung mehr.
  **Gemessen und wichtig für die Erwartung**: `parsePgnText` liefert die Linie als
  chess.js-`Move`-Objekte (`loadPgn` → `history({verbose:true})`), deren `san` IMMER kanonisch ist —
  `Nbd2` im PGN kommt als `Nd2` an, `e2e4` als `e4`, `0-0` als `O-O`. Der Textvergleich war damit
  faktisch schon ein Feldvergleich; die EINE sichtbare Folge des Umbaus ist, dass eine Umwandlung
  ohne genannte Figur jetzt zählt und die Figur der LINIE aufs Brett kommt. Der Rest ist
  Robustheit: ein Tag, an dem die Linie nicht mehr über ein Brett kanonisiert ankommt, kostet
  kein „falsch" mehr. Tests, die das trennen, schreiben die SAN der Linie deshalb absichtlich auf
  eine nicht kanonische Form um — ein Test, der nur ein PGN hineingibt, prüft den PARSER.
- **Endlos-Modus `features/puzzles/endless-*`** (Zerlegung Z19, 2026-10-25): `endless-puzzle.component` ist der Container (Lauf, Kette, Löser-Hooks, Spielbildschirm); daneben `endless-config.component` (Start-Bildschirm, Themen-Auswahl), `endless-result.component` (Sieg/Game Over/History-Detail), `endless-help-dialog.component` (Hilfe) und `endless-session-recorder.ts` (reine Klasse: Fehler, gespielte Puzzles, Spielstand-Rumpf, einmalige Aufzeichnung); vorhanden waren schon `endless-chain.service`, `endless-storage.service`, `endless-fasttrack-state`, `endless-prefetch.util`, `endless-history.component`.
