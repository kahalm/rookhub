# Kurs-Detailseite + Inhaltspflege (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Kurs-Detailseite + Inhaltspflege (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

`/courses/:bookId` (Frontend) zeigt Metadaten, eigenen Fortschritt und die **Kapitel-Verwaltung**.
Logik in `Services/CourseAuthoringService.cs`. Zwei Rechte-Ebenen: **lesen** = Kurs-Zugriff
(`CourseAccess`, kein Zugriff → 404); **Inhalte ändern** = Besitzer des persönlichen Buchs ODER Admin
(sonst 403); den **eigenen Fortschritt** (Kapitel-Reset) darf jeder mit Lese-Zugriff.

Kapitel werden hier über den **Namen** adressiert, nicht über einen Index (der verschiebt sich beim
Anlegen) — und die Verwaltungssicht listet **ALLE** Kapitel, auch rein aus Stellungs-/Info-Linien
bestehende (die Solver-Kapitelliste `GET /chapters` bleibt unverändert: nur Quiz-Linien, Index-Kontrakt
mit `?chapterIndex=`). `CourseManageChapterDto.SolverIndex` verbindet beides (`null` = im Solver nicht
startbar). **Manuell angelegte Linien sind `IsInfoOnly=true`** (keine Lösung ⇒ nie abgefragt, nicht in
Daily-/Random-Pools) — genau das sind die Stellungen des Kalkulations-Modus; Einfügen setzt zusätzlich
`Book.ImportVersion = ImportPipeline.CurrentVersion` (handgepflegte Bücher haben kein Quell-PGN und
sollen nicht im „Aktualisieren"-Banner hängen).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/courses/{bookId:int}` | Auth | Detailbild: Metadaten, Fortschritt, `canManage`, Kapitel-Verwaltungssicht (`LineCount`/`QuizCount`/`SolverIndex`/`FirstLineId`). Terminierte, für den Betrachter noch gesperrte Wochen einer Kalkulations-Serie fehlen in Kapitelliste UND Zählern (`Services/CalcVisibility.cs`; Besitzer/Admin sehen alles — Codereview A7-002) |
| GET | `/api/courses/{bookId:int}/lines?chapter=` | Auth | Linien EINES Kapitels (leer = „ohne Kapitel") — mit `MoveCount`, aber **ohne Zugfolge**. Gesperrte Woche → leere Liste wie ein unbekanntes Kapitel (kein Orakel) |
| GET | `/api/courses/{bookId:int}/chapter-pgn?chapter=` | Auth | EIN Kapitel als PGN (leer = „ohne Kapitel"): je Linie das Spiel mit derselben `Round` verbatim aus `Book.Source.SourcePgn` (`PgnParser.SplitGameBlocks`), sonst aus der gespeicherten Linie rekonstruiert (`CoursePgnExporter`). **Kalkulationsbücher → 404** (die Züge wären die Lösung) |
| GET | `/api/courses/{bookId:int}/lines/{lineId:int}/pgn` | Auth | EINE Linie als PGN, gleiche Regeln wie beim Kapitel. Alle drei Kurs-Downloads (auch `/{bookId}/pgn`) entfernen die internen Marker `[%alt]`/`[%info]` (`PgnParser.StripInternalMarkers`); „Kurs → Repertoire" und der Repertoire-Endpunkt `/api/repertoires/{id}/pgn` behalten sie (Viewer/Trainer brauchen `[%alt]`), dort entfernt das Frontend sie erst beim Speichern (`shared/pgn-export.util.ts`) |
| POST | `/api/courses/{bookId:int}/lines` | Besitzer/Admin | Stellungen als Text einfügen `{ chapter?, text }` → `{ added, chapter, issues[], totalLines }`. Parser = `Services/FenListParser.cs` (eine FEN je Zeile, führende Nummer „1:"/„2." wird ignoriert, Kommentar nach `\|` oder in `{…}`; **keine** Legalitätsprüfung, nur Struktur). Bereits im Buch vorhandene FENs → `issues` mit Grund `duplicate`; max. 500 Zeilen (`too_many`) |
| DELETE | `/api/courses/{bookId:int}/lines/{lineId:int}` | Besitzer/Admin | Einzelne Linie löschen (räumt Restrict-Abhängige ab: CoursePuzzleResults/CourseAttempts/CourseInfoViews/BookPuzzleAttempts/DailyPuzzles/CalculationTrees) |
| PUT | `/api/courses/{bookId:int}/calculation` | Besitzer/Admin | Kalkulations-Modus des Kurses ein-/ausschalten `{ isCalculation }` → `{ isCalculation }`. Ändert KEINE Linien (Analysebäume/Lösungen bleiben), nur Einstieg + Fortschritts-Zählung. Bewusst hier statt im Admin-Bücher-Tab: wer die Stellungen einfügt, entscheidet auch, wie sie serviert werden |
| PUT | `/api/courses/{bookId:int}/chapters/rename` | Besitzer/Admin | Kapitel umbenennen `{ chapter, newName }` (leerer Name = „ohne Kapitel"); 400 wenn Zielname existiert. Eine Ausgabe der Kalkulations-Serie wird in derselben Speicherung mit umbenannt; Ziel „ohne Kapitel" bzw. ein Ziel mit schon vorhandener Ausgabe → 400, bevor etwas geändert wird (Codereview A7-003) |
| POST | `/api/courses/{bookId:int}/chapters/delete` | Besitzer/Admin | Ganzes Kapitel = alle seine Linien löschen `{ chapter }` — löscht die Ausgabe der Kalkulations-Serie mit (A7-003) |
| POST | `/api/courses/{bookId:int}/chapters/reset` | Auth | **Einzel-Kapitel-Reset des EIGENEN Fortschritts** `{ chapter }` — leert CoursePuzzleResults/CourseInfoViews dieses Kapitels; die `CourseAttempts` bleiben als Zeit-Log stehen, statt dessen merkt sich `CourseLineResets` je Linie den Reset-Zeitpunkt (0.672.3). Pool, Statistik und ✓/✗ zählen nur Versuche nach BEIDEN Zeitpunkten (`CourseService.AttemptsSinceReset`) — vorher fielen die zurückgesetzten Linien weiter aus dem Pool und der Kurs meldete „abgeschlossen". Buchweites `CourseProgress.ResetAt` bleibt (ist buchweit, löscht beim Kurs-Reset die `CourseLineResets` des Buchs), eigene `CalculationTrees` bleiben ebenfalls (Nutzerarbeit) |
