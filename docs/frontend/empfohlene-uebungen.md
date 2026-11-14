# Empfohlene Übungen in der Zusammenfassung (0.960.0)

Backend, Auswahl und Mattbild: `docs/api/partie-zusammenfassung.md`, Abschnitt „Empfohlene Übungen“.

- `ClubGameSummaryComponent` lädt die Übungen nur zu einem fertigen Text (`ClubGameSummaryService.exercises(source)`, dieselben fünf
  Wege wie die Zusammenfassung: Vereinspartie, eigene Partie, Liga-Teilen-Link, `/cg/{token}`, `/g/{token}`). Keine Stellen oder ein
  Fehler → kein Abschnitt, kein Platzhalter.
- `SummaryExercisesComponent` (`features/games/summary/summary-exercises.component.ts`): `h2` „Empfohlene Übungen“, Einleitungssatz,
  je Stelle eine `h3` mit der Zugnummer als Textknopf (springt wie ein Zug im Text aufs Brett bzw. öffnet am Handy das Brett-Blatt) und
  dem Grund („verpasstes Matt in 4“, seit 0.961.0 auch „verpasste Taktik: Gabel“ / „dem Gegner eine Taktik erlaubt: Spieß“ —
  Themenname über `PuzzleThemePipe`); darunter Karten (`auto-fill`, ≥ 140 px) mit kleinem Brett — Stellung NACH dem Gegnerzug, aus Sicht
  des Lösers, letzter Zug markiert —, Art der Übereinstimmung und „Lichess · Wertung“. Drei Stellen offen, der Rest hinter „Weitere
  Stellen (N)“. Gelöst/nicht gelöst als Häkchen/Kreuz an der Karte (nur im Speicher der Seite).
- Lösen: ab 900 px im Abschnitt über den Karten (`SummaryExerciseSolverComponent` mit `PuzzleBoardComponent` + gemeinsamem
  `LineSolver`), darunter als Blatt `SummaryExerciseSheetComponent` (Muster wie `SummaryBoardSheetComponent`: Griff, Titel, X, Wischen,
  Zurück-Taste). Richtig → der Gegner antwortet nach 450 ms; falsch → der Zug steht 700 ms und wird zurückgenommen; beim letzten Zug
  zählt jedes Matt; „Lösung zeigen“ spielt den Rest vor; „Weiter“ = nächste Aufgabe der Stelle.
- Angemeldet wird der ERSTE Ausgang je Aufgabe über `PuzzleService.recordAttempt` verbucht (wie der normale Löser, also mit
  Puzzle-Wertung); anonym nur lösen.
- **Buchbeispiele** (0.962.0, `source: 'book'`): Karte mit der Diagrammstellung (keine Zugmarke, Blick aus Sicht der Seite am Zug), darunter
  die Quellzeile (`a.book`: Titel · Autor · Kapitel bzw. „S. N“/„Abschnitt N“, Link in den Leser über `exerciseReaderQuery`). Lösbar
  (`exerciseSolvable`) → wie eine Lichess-Karte, der `LineSolver` startet ohne Vorspiel (`startPly: 0`) und verbucht nichts; sonst ist die
  Karte selbst ein Link in den Leser mit „Beispiel im Buch“ und fehlt im Löser (der blättert nur durch die lösbaren Aufgaben der Stelle).
  Ergebnisse je `exerciseKey` (`lichess:<id>` / `book:<id>` — Ids können gleich sein).
- **„Taktiken trainieren“** (0.968.0): `mat-stroked-button` (`school`) mit leiser Hinweiszeile unter den Stellen, nur wenn die Seite ihn
  freigibt (`canTrain`: angemeldet UND Menüpunkt „Kurse“ sichtbar — `MenuService.visible$`, sonst wiese `coursePlayGuard` ab) und es
  mindestens eine lösbare Aufgabe gibt. Klick → `ClubGameSummaryService.trainTactics(source, lang)` (dieselben fünf Wege), danach
  `/courses/{bookId}/chapter/{chapterIndex}/sequential`; `chapterIndex === null` → `/courses/{bookId}`; Fehler → Snackbar, Knopf wieder
  frei. Ohne Anmeldung (Teilen-Links) kein Knopf, wie „Nicht mehr vorschlagen“ bei der Literatur. Server: `docs/api/kurse.md`.
