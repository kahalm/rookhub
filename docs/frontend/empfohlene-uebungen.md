# „Taktiken trainieren“ am Ende der Zusammenfassung (Übersicht „Empfohlene Übungen“ entfallen, 0.973.0)

Backend, Auswahl der Aufgaben und Mattbild: `docs/api/partie-zusammenfassung.md`, Abschnitt „Empfohlene Übungen“.

**Stand seit 0.973.0** (Wunsch 2026-11-14: „train am ende der summary gefällt mir, aber übersicht darüber kann weg“): Die Seite zeigt
die Aufgaben der Partie NICHT mehr — keine Überschrift „Empfohlene Übungen“, keine Stellen, Karten, Buchangaben, kein Löser im
Abschnitt, kein Blatt am Handy. Entfernt: `SummaryExercisesComponent`, `SummaryExerciseSheetComponent`,
`SummaryExerciseSolverComponent`, `exerciseKey`, `exerciseReaderQuery`, `exerciseMoveLabel` und alle Texte unter
`games.summary.exercises.*` außer `train`, `trainHint`, `trainFailed`. Geblieben ist nur der Knopf:

- **„Taktiken trainieren“** (0.968.0) steht direkt in `ClubGameSummaryComponent` (`.train`), hinter dem Text und vor dem Modellvermerk:
  `mat-stroked-button` (`school`) mit leiser Hinweiszeile, nach oben mit Trennlinie (`--mat-sys-outline-variant`) und 24 px Abstand
  abgesetzt, ohne eigene Überschrift. Er erscheint nur, wenn `canTrain()` (angemeldet UND Menüpunkt „Kurse“ sichtbar —
  `MenuService.visible$`, sonst wiese `coursePlayGuard` ab) und `trainable()` (die Partie hat mindestens eine lösbare Aufgabe,
  `exerciseSolvable`: jede Lichess-Aufgabe, ein Buchdiagramm nur mit bestätigter Lösung).
- Klick → `ClubGameSummaryService.trainTactics(source, lang)` (dieselben fünf Wege wie die Zusammenfassung: Vereinspartie, eigene
  Partie, Liga-Teilen-Link, `/cg/{token}`, `/g/{token}`), danach `/courses/{bookId}/chapter/{chapterIndex}/sequential`;
  `chapterIndex === null` → `/courses/{bookId}`; Fehler → Snackbar, Knopf wieder frei; während des Anlegens gesperrt. Server:
  `docs/api/kurse.md`.
- **Abruf der Aufgaben** (`ClubGameSummaryService.exercises(source)`, `GET …/summary/exercises`): nur noch, um `trainable` zu
  bestimmen, und nur, wenn es einen fertigen Text gibt UND `canTrain()` gilt (ein `effect`). Ohne Anmeldung (Teilen-Links) oder ohne
  Kurs-Zugang fragt die Seite die Aufgaben gar nicht ab und zeigt von ihnen nichts. Keine Aufgaben oder ein Fehler → kein Knopf, kein
  Platzhalter. Die Antwort trägt weiterhin die vollen Aufgaben (Stellungen, Züge, Buchquelle), gebraucht wird davon nur „gibt es eine
  lösbare“ — ein eigener schlanker Endpunkt wäre möglich, ist aber nicht gebaut.
- Aufgaben einer Partie lösen: im Kurs „Persönliche Taktiken“ (Kurs-Löser). Das Verbuchen als Puzzle-Versuch
  (`PuzzleService.recordAttempt`) aus dem früheren Löser der Seite gibt es damit nicht mehr.
