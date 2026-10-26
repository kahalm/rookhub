# Verzeichnisstruktur

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


```
app/src/app/
  core/
    auth.service.ts          Fassade: Login/Register/Logout, has(), Impersonation (Teile siehe docs/api/auth.md)
    session-store.ts         Token im localStorage, Ablauf, Aufräumen
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
  shell/
    navbar.component.ts     Material Toolbar, Navigation, User-Menu mit Logout (RookHub-Kopfleiste; App-Schicht,
                            darf Features kennen — seit Codereview F8-013 nicht mehr in shared/)
    offline-prefetch.service.ts  Offline-Pools (Standard-Puzzle + Endless) beim App-Start füllen (seit F8-013-Nachlauf
                            nicht mehr in core/, weil es Puzzle-/Endless-Dienste braucht)
  shared/
    navbar/
      nav-edge.ts           Kante der Kopfleiste messen (RookHub + Turnierseite)
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
  `judgeMove` auf `this.chess`) und der Repertoire-Trainer (`features/repertoire/repertoire-trainer-session.ts`, seit
  0.499.10, siehe unten). Die letzten beiden benutzen die reinen FUNKTIONEN, nicht die Klasse: beide
  führen ihr Brett schon selbst und WEISEN es neu zu (`reviewGoToCore` im Puzzle-Löser,
  `startCurrentLine`/`showSolution` in `TrainerSession`) — ein zweites Brett daneben wäre die Quelle fürs
  Auseinanderlaufen. **Bewusst NICHT im Kern und weiterhin in Base**: moveLog samt `thinkMs`,
  `wrongMoveCount`, Off-Path-Zählung und -Warnung (Regeln in `off-path-policy.ts`), der `ALT_HOLD_MS`-Timer des
  Alternativzugs, die Timer von `advanceAfterCorrectMove`, die Stockfish-Antwort (`opponentRespond`), `handleGameOver`,
  `mouseslip`, viz/Tipps/Eval (Bausteine siehe „Basis-Löser“ unten). Der Kalkulations-Modus ist **kein Löser** und bleibt außen vor.
- **Repertoire-Trainer am Kern** (0.499.10; seit Zerlegung Z21 in `TrainerSession`): `onMove`, `onLearnMove` und `showSolution` urteilen
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
- **Endlos-Modus `features/puzzles/endless-*`** (Zerlegung Z19, 2026-10-25): `endless-puzzle.component` ist der Container (Löser-Hooks, Brett, Dialoge, Spielbildschirm; Getter unter den alten Template-Namen). Lauf-Zustand und Übergänge (Start, Fortsetzen, Weiterrücken, Gelöst/Fehler, Leben, Kettenposition, Nachladen/Sieg, Sync, einmalige Aufzeichnung, Archivieren) liegen seit Zerlegung A4 im Signal-Dienst `endless-run.store.ts` (`EndlessRunStore`, je Seite in `providers`, Host-Rückrufe über `attach`); dazu reine Klassen `endless-profile.ts` (Konfiguration, Rekord, History, Themen, Fast-Track-Schwellen; `configOverridesFromQuery` für Deep-Links), `endless-run-timer.ts` (Session-/Puzzle-Uhr, Uhr über `ENDLESS_RUN_CLOCK` austauschbar), `endless-view-settings.ts` (Brett/Figuren/Visualisierung, Einstellungs-Dialog, Spielweise Training/Einfach) und `endless-last-solved.ts` (zuletzt gelöstes Puzzle für Analyse/♥/Aufgabenblatt). Store-Spec = Übergangstabelle mit Handuhr (`testing/manual-clock.ts`, Fabrik `testing/endless-run-store.ts`). Daneben `endless-config.component` (Start-Bildschirm, Themen-Auswahl), `endless-result.component` (Sieg/Game Over/History-Detail), `endless-help-dialog.component` (Hilfe) und `endless-session-recorder.ts` (reine Klasse: Fehler, gespielte Puzzles, Spielstand-Rumpf, einmalige Aufzeichnung); vorhanden waren schon `endless-chain.service`, `endless-storage.service`, `endless-fasttrack-state`, `endless-prefetch.util`, `endless-history.component`.
- **Kurse (Zerlegung 2026-10, Z20/Z29/Z33/F3-023):** `features/courses/` — HTTP nach Thema in `course-api.ts`/`course-authoring-api.ts`/`course-share-api.ts`/`course-translation-api.ts` + `course.types.ts` (`course.service.ts` = Fassade), Kursdetail mit `chapter-row.component` + `calc-series.controller`, Kalkulations-Modus `calc/` als Container + reine Klassen (`calc-chapter-model`, `calc-tree-cursor`, `calc-tree-saver`, `calc-review.controller`, `keyed-single-flight-queue`, `calc-training-timer`, `calc-page-mode`; seit W7a `calc-page-loader`, `calc-training-session`, `calc-local-mode`, `calc-labels`, `calc-dialogs`) + OnPush-Kinder (`calc-result-card`, `calc-annotate-card`, `calc-status-panel`); Aufstellung in `docs/api/kalkulations-modus.md`.
- **Repertoire-Trainer + Stellungssuche zerlegt** (Codereview Z21/Z32): `repertoire-trainer.component.ts` lädt und verdrahtet nur; das Abspielen (8 Phasen, Timer über `TrainerClock`, Zugurteil) ist `repertoire-trainer-session.ts` (seit W7a in Schichten: `repertoire-trainer-state.ts` = Felder, Anzeige-Getter, Timer, Typen → `repertoire-trainer-learn.ts` = Lern-Modus → `TrainerSession` = Ablauf/Urteil; Exporte weiter über `-session`), dazu `-sr-sync` (SR-Abgleich + Offline-Queue), `-frequency-loader` („Häufigste zuerst“), `-eval-compare` (Stockfish-Vergleich), `-queue.util` (Warteschlange/Gegner-Reihung); in `position-repertoires.component.ts` liegt die Sicht „Ähnlich“ als `similar-positions-panel.component.ts` + `similar-positions-search.ts`, der Linien-Link in `repertoire-line-link.ts`.
- **Partie-Seiten `features/guess`, `features/reconstruct`, `features/games/shared-game*`** (Zerlegung Z24–Z26, 2026-10-25): Punktepartie = `guess-board.component` (.html/.scss) + `guess-playback.ts` (reine Abspielsteuerung, Uhr injizierbar) + `guess-history-panel.component` (Zugliste) + `guess-settings-menu.component` + `guess-history.util.ts`; Rekonstruktion = `reconstruct-detail.component` + `part-editor.component` (Zug-Editor) + `gap-proposals.component` (Lückenzeile) + `reconstruct-share-menu.component` + `reconstruct-head-form.component` + `reconstruct-moves.util.ts` + `reconstruct-moves-draft.ts` (Zugfolge im Teil-Editor: Text, Ausgangsstellung, Blätter-Stand, nachgespieltes Brett; Template liest `draft.*`, W7a); Partieseite = `shared-game.component` + `game-actions.service` (Aktionen an der Partie, je Seite bereitgestellt, V33; auch die Partienliste `games-list.component` über die Varianten `copyPgnOf`/`downloadPgnOf`/`openInAnalysisOf`, die das PGN nachladen) + `game-engine-controller.ts` (Live-Engine, Training-Analyse) + `game-training-controller.ts` (Fehler-Training).
- **Basis-Löser `features/puzzles/base-puzzle-solver.ts`** (Zerlegung Z23, 2026-10-25): `BasePuzzleSolver<S>` ist der
  Lös-Automat aller vier Löser (Standard, Endless, Buch/Kurs, Woodpecker); `S` = die Endzustände der Komponente
  (`PuzzleEndState`, `EndlessEndState`, `BookEndState` in `book-mode-host.ts`, `WoodpeckerEndState`), die Basis setzt
  `SolverCoreState`. Gemeinsam in der Basis: Eval-Anzeige (`toggleEval`/`refreshEval`, Tiefe über `depth`), Blättern
  der Durchsicht (`reviewNext/Prev`, `exitReview`, `showOriginalSolution`, `reviewArrowKey`), `releaseSolver()` fürs
  ngOnDestroy. Bausteine (rein bzw. mit austauschbarer `SolverClock`, je eigene Spec): `solver-timer.ts`
  (`SolverTimer` Lösezeit, `SecondsCountdown` Auto-Weiter/Viz-Countdown), `hint-ladder.ts` (`HintLadder`),
  `off-path-policy.ts` (`OffPathPolicy`, `whiteEvalToPlayerPawns`, `playerEvalText`), `viz-controller.ts`
  (`VizController`, `opponentArrowModeFor`), `solver-setup.util.ts` (Vorspiel klemmen, Blickrichtung, Vorschau),
  `solver-texts.ts` (Anarchy-/Off-Path-Texte); Test-Uhr `testing/manual-clock.ts`. **Fehlversuch (P-1, 2026-10-25)**:
  der erste Fehlzug eines Lösungsgangs ruft den Hook `onFirstWrongMove()` (nicht bei geduldeten Alternativen und nicht,
  wenn der abweichende Zug selbst mattsetzt); jede Komponente meldet dort sofort (Standard `recordAttempt(false)`, Endless
  über `registerLoss` = Leben, Buch an Kurs/Wochenpost/Tagespuzzle/Einzel-Buch/„Track solves“, Woodpecker). Danach meldet
  derselbe Durchgang nichts mehr — auch nicht nach Zurücksetzen, „Nochmal“ (Buch: `setupPuzzle(p, true)`) oder
  Mausrutscher, und ein späteres Lösen zählt nicht als gelöst; erst das nächste Puzzle (bzw. Neuladen) ist wieder meldbar.
  **Schichten (W7a, 2026-10-26)**: `solver-board-base.ts` (`SolverBoardBase`: Brett-Präsentation, chess.js, Lösungszüge,
  Visualisierung, `updateBoard`) → `solver-assist-base.ts` (`SolverAssistBase`: Tipps, Eval, Off-Path, Stoppuhr,
  Stockfish-Epoche) → `solver-review-base.ts` (`SolverReviewBase`: Durchsicht, Einstellungs-Panel, Countdown,
  `abortSolver`/`releaseSolver`) → `base-puzzle-solver.ts` (Setup, Zug-Bewertung, Gegnerzug, Alternativzug,
  Mausrutscher). `SolverCoreState`/`MoveLogEntry` weiter aus `base-puzzle-solver`. Standard-Löser `puzzle.component`
  daneben: `standard-puzzle-source.ts` (Deep-Link/Vorab/Offline-Pool/Zufall, Epochen, schwächste Themen),
  `standard-view-settings.ts` (Ansicht, Filter-Dialog, Spielweise — Muster `EndlessViewSettings`),
  `puzzle-revenge-round.ts` (Challenge/Revanche je einmal, Feuerwerk-Stand), `standard-attempt.ts` (Versuchsmeldung).
- **Analysebrett `features/analysis/analysis.component`** (Zerlegung Z22, 2026-10-25): Container; daneben rein
  `analysis-settings.ts` (`AnalysisSettings`, `DEPTH_OPTIONS`, Schlüssel), `analysis-route.ts` (`readAnalysisParams`),
  `analysis-readout.ts` (Partie-Ende, Zugziele, Bewertungsleiste, Pfeile, `SearchClock`), `analysis-engine-choice.ts`
  (`pickEngineAfterLoad`, `EngineChoiceList`, `speedHintText`), `analysis-engine-picker.ts` (`AnalysisEnginePicker`: External
  Engines laden, Wahl, Verdrahtung; W7a), `analysis-line-model.ts` (`AnalysisLineModel`,
  `playLine`, `boardMove`), `analysis-title.util.ts` (`pgnTitle`); vorhanden waren `analysis-tree.ts`,
  `engine-lines.util.ts`, `analysis-compare.ts` (Vergleichs-Engine), `analysis-history-saver.ts`, `maia/maia-sparring.ts`.
- **Turnierkalender der Turnierseite `src-turnier/app/features/tournament-directory/`** (Zerlegung Z27/Z28, 2026-10-25): die Karte `tournament-map.component` behält Leaflet-Lebenszyklus, Ausschnitt und Marker; daneben `map-pin-groups.ts` (rein: Bündelung je Koordinate, Pin-Stil), `map-popup-builder.ts` (rein: Hover-Hinweis, Bündel-Liste), `map-popup-content.ts` (Popup-Inhalt samt Kurzansicht über den ViewContainerRef), `map-gestures.ts` (Mausrad/Touch) und `map-legend.component` (Einfärben + Legende, OnPush). Der Kalender `tournament-directory.component` ist Container für Filterzustand, Reiter und Deep-Links; daneben `directory-view-state-sync.ts` (gemerkter Zustand lokal + beim Nutzer), `directory-loaders.ts` (Liste/Karte/Monat mit Zähler gegen überholte Antworten), `place-search.ts` + `place-field.component` (Ortsfeld) + `directory-place.ts` (`DirectoryPlaceController`: Ort/Umkreis/Ortung gegen Suchprofil, W7a), `search-profile-menu.component` + `search-profile-actions.ts` (Suchprofile), `directory-filter-panel.component` (ausgeklapptes Filterfeld, OnPush mit einem Eingang je Filterfeld, weil der Kalender sein Filterobjekt in place ändert). Turnierverlauf `tournament-history`: Aufteilung des offenen Reiters als `computed()` (F6-025). Crawl-Aufträge verfolgen Turnierseite („Aktualisieren“) und Verzeichnis-Detail („Holen“) gemeinsam über `src-turnier/app/core/watch-crawl-job.ts` (`watchCrawlJob`, rein RxJS: timer + switchMap + takeWhile + take, mit Deckel `maxMs`, F6-012).
- **LeagueHub-Vereinsseiten `src-leaguehub/app/features/club/`** (Zerlegung Z31, 2026-10-25): `club-add-page.component` (.html) ist Container für `club-draft-store.ts` (reine Klasse: PGN-Liste, Online-Entwurf, Pakete, offene Listen) + `pgn-source-panel.component` (PGN-Quelle) und `scoresheet-upload-store.ts` (reine Klasse: Fotos, Hochladen, Nachfragen) + `club-scoresheet-panel.component` (.html); `club-scan-page.component` (.html) nutzt `club-side-form.component` (Kopfdaten zu `scan-header-form.ts`), `scan-export.ts` + `scan-export-actions.component` und `scan-merge.ts`; Teilen-Link-Schlüssel in `club-anon-keys.ts`. Die Korrektur selbst bleibt die geteilte `sheet-workbench`/`SheetEditSession`.
- **Admin-Seite, Auth-Fassade, shared-Schicht** (Zerlegung Z30/Z34, F8-013, 2026-10-25): `features/admin/admin.component` = Kopf + Tabs je Recht; Nutzer/Bücher/Gruppen in `features/admin/tabs/admin-{users,books,groups}-tab.component` (+ `admin-book-filter.ts`), Gruppenliste in `admin-groups.store.ts`; `core/auth.service.ts` = Fassade über `session-store`, `permission.service`, `impersonation.service`, `login-follow-up.service` (siehe `docs/api/auth.md`); RookHub-Kopfleiste in `shell/navbar.component.ts`, `LEGAL_SITE` in `core/legal-site.ts` (auch die Turnierseite über `@rh/core/legal-site`), `ThemeMode` in `core/theme-mode.ts`, Kapitelfarben in `shared/repertoire/repertoire-color.util.ts`; Wache `shared/no-feature-imports.spec.ts` (prüft auch `core/`). Nachlauf: Partie-Betrachter-Dialog `PgnViewerComponent` in `features/games/pgn-viewer.component.ts` (die Bausteine Brett/Zugliste/Kurve/Parser bleiben in `shared/pgn-viewer/`), Zug-Helfer in `shared/chess/keyboard-move.util.ts` und `shared/chess/puzzle-move.util.ts` (Weiterleitungen in `features/puzzles/` nur noch für die Löser, `courses/calc` und `reconstruct`), Offline-Vorabruf in `shell/`, die `?lang=`-Regel als `langParams` in `shared/lang-params.ts`, der Typ `BoardBadge` in `shared/pgn-viewer/board-badge.ts`; Ausnahmeliste der Wache: nur noch `game-replay` → `GameReviewComponent` (begründet in der Spec).
