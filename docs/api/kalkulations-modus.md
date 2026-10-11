# Kalkulations-Modus (auth) — Stellungen ohne Lösung

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Kalkulations-Modus (auth) — Stellungen ohne Lösung“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Ein Buch mit `Book.IsCalculation` (Schalter auf der KURS-Detailseite, Besitzer/Admin — `PUT
/api/courses/{bookId}/calculation`; **nicht** im Admin-Bücher-Tab) ist ein **Kalkulationsbuch**: seine Linien
werden nicht abgefragt, sondern als reine Stellungen (FEN + optionaler Kommentar) zum Durchrechnen serviert.
Die Kursübersicht bietet dafür statt sequenziell/zufällig den Kalkulations-Modus an
(`/courses/:bookId/calc`); Fortschritt = Stellungen mit eigenem Analysebaum (`PuzzleCount`/`SolvedCount` in
`CourseListItemDto` zählen bei diesen Büchern ALLE Linien bzw. die bearbeiteten). `calcPoints`/`calcMaxPoints`
im selben DTO (und in `CourseDetailDto`) = Punktestand des Kurses aus der Selbstbewertung, IMMER als
„x / y" (Maximum = 4 × alle Stellungen) — nur bei Kalkulationsbüchern gefüllt, sonst beide `null`.

**Es gibt keine Lösung — und sie verlässt den Server nicht.** Die Endpoints liefern `BookPuzzle.Moves`
bewusst NICHT aus; bei einer normalen Puzzle-Linie höchstens den Vorlauf bis zum Trainingsstart
(`CalcPositionDto.SetupMoves` = Halbzüge `0..StartPly`), nie die Züge ab dem Schlüsselzug. Der Baum selbst
ist für den Server **opak** (JSON-String, nur auf Größe + Gültigkeit geprüft) — Struktur/Semantik liegen im
Frontend (`features/courses/calc/calc-tree.util.ts`), damit Formatänderungen keine Migration brauchen.
Zugriff je Buch über `Services/CourseAccess.cs` (dieselbe Regel wie die Kurs-Endpoints, aus
`CourseService.CanAccessAsync` herausgezogen); kein Zugriff → 404. Der Modus ist nicht auf Bücher mit dem
Flag beschränkt — es steuert nur den Einstieg in der Übersicht.

**Kein SOLVER-Pfad liefert ein Kalkulationsbuch aus** (`CourseAccess.IsCalculationBookAsync`, seit der
Freigabe öffentlicher Kalkulations-Kurse). Die Solver-/Kurs-Wege reichen die Linien über
`BookPuzzleService.MapToDto` samt `Moves` durch — in einem Kalkulationsbuch ist das die Lösung. Sie
antworten dort deshalb wie auf ein nicht vorhandenes Buch (404): `GET /api/courses/{bookId}/public`
(anonym), `GET /api/courses/{bookId}/puzzles` (Offline-Export), `GET /api/book-puzzles/{id}/next` und
`{id}/random`, seit dem Codereview 2026-09-29 (A7-001) auch `GET /api/courses/{bookId}/pgn`,
`POST /api/courses/{bookId}/convert-to-repertoire`, `GET /api/courses/{bookId}/next` und
`GET /api/book-puzzles/random?bookId=`; die Favoriten (A9-001): `POST /api/favorites` lehnt Linien aus
Kalkulationsbüchern mit 404 ab, `GET /api/favorites` liefert bei solchen Linien `moves` leer. Alle PGN-Exporte
(Buch/Kapitel/Linie) liegen im `CoursePgnExportService` hinter EINER Pforte (`EnsureExportAllowedAsync`, A7-010). **Warum das nötig ist**: ein öffentlicher Kalkulations-Kurs BRAUCHT `Book.IsPublic` für
seine Kurz-URL `/{slug}` — und genau dieses Flag ist auch das einzige Tor dieser Nachbar-Endpoints;
ohne die Sperre erzwänge das Freischalten die Preisgabe der Lösung. Wer die Zugfolgen wieder über die
Kurs-Pfade braucht, schaltet den Kalkulations-Modus aus (Besitzer/Admin, `PUT
/api/courses/{bookId}/calculation`). Unberührt bleibt `GET /api/book-puzzles/{id}` (Einzel-Puzzle per Id) —
bewusst ungegatet für Teilen-Links/Tagespuzzle/OG-Vorschau/Bot-Lookup (siehe `BookAccess`).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/calculations/books/{bookId}` | Auth | Buchkopf + leichte Stellungsliste (`id`/`round`/`title`/`chapter`/`hasTree` + `chosenSan`/`chosenUci`/`secondsSpent`/`grade`/`points`), Reihenfolge Round→Id — ohne FEN/Kommentar/Züge. Dazu **serverseitig** gerechnete Kapitelsummen (`chapters[]`: `positionCount`/`treeCount`/`chosenCount`/`ratedCount`/`points`/`maxPoints`/`secondsSum`) + Buchsummen `points`/`maxPoints`/`secondsSum` |
| GET | `/api/calculations/books/{bookId}/public` | **AllowAnonymous** | Kopf + VOLLSTÄNDIGE Stellungen (`id`/`round`/`title`/`chapter`/`fen`/`setupMoves`/`comment`) eines ÖFFENTLICH freigegebenen Buchs — die EINZIGE (lesende) Öffnung des Modus, gegatet auf **`Book.IsPublic`** (dieselbe Bedingung wie die Slug-Auflösung, damit Einstieg und Inhalt zusammenpassen); nicht freigegeben → 404. Bewusst **nicht** `BookAccess.PubliclyExposed`: die Pool-Flags (`ForDaily`/`ForRandom`/`ForBlind`) öffnen einzelne Puzzles/Zufallsziehungen, nicht den strukturierten Kurs — ein persönlicher Import im Tagespuzzle-Pool ginge sonst anonym als vollständiger Kurs heraus. Bewusst ein eigener Endpoint mit eigenem DTO statt `books/{bookId}` zu öffnen: hier gibt es überhaupt keine Nutzer-Felder (Baum/Zeit/Stufe/Festlegung), also auch keinen „kein Nutzer"-Sonderfall in einem Pfad, der sonst Nutzerdaten liefert. Anonyme Arbeit bleibt LOKAL im Browser |
| GET | `/api/calculations/positions/{bookPuzzleId}` | Auth | Eine Stellung (FEN, `setupMoves`, Kommentar) + eigener Analysebaum (`treeJson`, `treeUpdatedAt`) + die drei Trainings-Werte |
| PUT | `/api/calculations/positions/{bookPuzzleId}` | Auth | Baum speichern (Upsert) `{ treeJson, addSeconds?, secondsToken?, grade?, clearGrade?, chosenSan?, chosenUci?, clearChoice? }` — 400 bei leerem/ungültigem JSON, > `CalculationService.MaxTreeJsonLength` (256 KB), `grade` außerhalb 0–4 oder `secondsToken` > 64 Zeichen; Antwort = `CalcPositionStateDto` (`bookPuzzleId`/`updatedAt`/`hasTree`/`chosenSan`/`chosenUci`/`secondsSpent`/`grade`/`points`) |
| PATCH | `/api/calculations/positions/{bookPuzzleId}` | Auth | **Nur** die drei Trainings-Werte, ohne den Baum erneut zu schicken (festlegen/Zeit/bewerten) — gleiches Feld-Set wie oben ohne `treeJson`, gleiche Antwort. Legt die Zeile bei Bedarf mit LEEREM Baum an (zählt dann nirgends als „bearbeitet"); ein PATCH ohne Wirkung legt gar nichts an |
| DELETE | `/api/calculations/positions/{bookPuzzleId}` | Auth | Eigenen Baum verwerfen (idempotent) — Zeit/Festlegung/Bewertung bleiben stehen, die Zeile wird nur bei komplett leeren Werten entfernt |
| GET | `/api/calc-editions/{bookId}` | AllowAnonymous | Kalkulations-Serie: bereits FREIGEGEBENE Ausgaben eines Buchs inkl. Video (keine Entwürfe) — für die Betrachter-Serienseite |
| GET | `/api/calc-editions/{bookId}/manage` | Besitzer/Admin | ALLE Ausgaben inkl. Entwürfe (Verwaltung); 403 sonst |
| PUT | `/api/calc-editions/{bookId}` | Besitzer/Admin | Ausgabe anlegen/ändern (Upsert je Kapitel) `{ chapter, title?, videoUrl?, publishAt, testerPreviewAt? }`; 400 ab 120 Ausgaben je Buch (`MaxEditionsPerBook`, Codereview A7-004) |
| DELETE | `/api/calc-editions/{bookId}/{editionId}` | Besitzer/Admin | Ausgabe löschen |
| GET | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Kalkulations-Serie Phase 2: Verteiler-Mitglieder inkl. Tester-Häkchen (`{ userId, username, isTester, createdAt }`) |
| PUT | `/api/calc-editions/{bookId}/members` | Besitzer/Admin | Mitglied hinzufügen/ändern per Benutzername `{ username, isTester }`. NEU nur Freunde des Besitzers (Admin: jeder); 404 gleich für unbekannt und „kein Freund" (kein Benutzernamen-Orakel); 400 bei Nicht-Kalkulationsbuch oder mehr als 200 Mitgliedern (`MaxMembersPerBook`). Bestehende Mitglieder bleiben änderbar (Codereview A7-004) |
| DELETE | `/api/calc-editions/{bookId}/members/{userId}` | Besitzer/Admin ODER das Mitglied selbst | Mitglied aus dem Verteiler entfernen bzw. sich selbst austragen |
| GET | `/api/calc-editions/{bookId}/views` | Besitzer/Admin | Kalkulations-Serie Phase 3: „Gesehen"-Übersicht — welches Verteiler-Mitglied welche Ausgabe wann geöffnet hat (`{ editionId, chapter, userId, username, viewedAt }`) |

**Serien-Freigabe-Benachrichtigung (Phase 3b):** `CalcSeriesAnnounceScheduler` (HostedService, Standard alle 5 min, Config `CalcSeries:AnnounceIntervalSeconds` 60..3600) ruft `CalcSeriesAnnounceService.RunOnceAsync`: fällige Ausgaben (nur solche, deren Kapitel noch Stellungen hat — eine Ausgabe ohne Stellungen bleibt unmarkiert und wird erst angekündigt, wenn das Kapitel wieder Stellungen hat; Codereview A7-003; Verteiler nur lebende Konten, A9-004) → In-App-Benachrichtigung `calc_series_edition_released` (Daten `book`/`chapter`, Link `/courses/{bookId}`) an den Verteiler. Tester werden zum früheren `TesterPreviewAt` informiert, alle übrigen Mitglieder zur öffentlichen `PublishAt`. Idempotent über `CalcEdition.TesterAnnouncedAt`/`PublishAnnouncedAt`; die öffentliche Runde schließt die Tester-Runden-Empfänger über die GESPEICHERTE Liste `CalcEdition.TesterAnnouncedUserIds` (CSV) aus — NICHT über das veränderliche `IsTester`-Flag (sonst würde ein spät hinzugefügter Tester verloren gehen bzw. ein ent-Tester-tes Mitglied doppelt benachrichtigt). **Kein Mail-Kanal** (es gibt kein Mail-Opt-out-Modell — bewusst nur In-App).

**Rechnen → festlegen → prüfen → bewerten**: je Stellung hält `CalculationTrees` drei eigene SPALTEN
(nicht im opaken `TreeJson` vergraben — dort wären sie für Auswertungen für immer unerreichbar):
`ChosenSan`/`ChosenUci` (die EINE Festlegung auf einen ersten Zug — derselbe Zug erneut = Toggle
zurück, ein anderer verschiebt sie), `SecondsSpent` (der Client schickt **Deltas**, der Server
ADDIERT; Deckel `MaxSecondsPerFlush` = 1 h je Übertragung, `MaxSecondsSpent` gesamt) und `Grade`
(Selbstbewertung als benannte STUFE). `null` vs. löschen unterscheiden die Schalter
`clearGrade`/`clearChoice` — ein fehlendes Feld heißt immer „unverändert". Die Bewertung ist
**reine Selbsteinschätzung**: der Server liefert weiterhin keine Lösung aus.

**Zeit ist der einzige ADDIERENDE Wert — und braucht deshalb eine Idempotenz-Marke.** Stufe und
Festlegung SETZEN (Wiederholung schadet nicht), `addSeconds` addiert: kam eine Anfrage an und ging
nur die ANTWORT verloren (Timeout/502), würde der Wiederholversuch die Zeit still ein zweites Mal
buchen. Der Client vergibt darum je gemessenem Delta eine Marke (`secondsToken`, ≤64 Zeichen,
Inhalt für den Server opak) und **wiederholt mit DERSELBEN Marke**; die Zeile merkt sich
`SecondsToken` + `SecondsTokenApplied` und rechnet nur an, was unter dieser Marke noch nicht
verbucht war (identischer Retry ⇒ 0 s; ein beim Wiedereinreihen um neue Messungen gewachsener Patch
behält seine Marke ⇒ nur die Differenz). Stufe/Festlegung laufen dabei weiter durch. Ohne
`secondsToken` gilt das alte Verhalten (bedingungslos addieren). Frontend-Gegenstück:
`newSecondsToken()` + `mergeReviewPatch` in `calc-review.util.ts` (die Marke des ÄLTEREN Patches
gewinnt beim Zusammenlegen — genau der kann schon beim Server sein).

**Bewertung = AUSWAHL, keine freie Zahl** (`Models/CalculationGrade.cs`): fünf benannte Stufen
`notSolved` (0) · `someIdeas` (1) · `moveNoMainLine` (2) · `moveNoSideLines` (3) · `solved` (4) —
„Hauptfolge nicht gesehen" wiegt bewusst schwerer als „Nebenfolgen nicht gesehen". Eine benannte
Stufe ist reproduzierbar, „7 von 10" bedeutet nächste Woche etwas anderes. **Gespeichert wird die
STUFE**, nicht die Punktzahl; die Punkte entstehen ausschließlich in `CalculationGrades.PointsFor`
(heute linear 0..4) und werden zusätzlich mit ausgeliefert — eine spätere Neugewichtung passiert nur
dort und schreibt die Vergangenheit nicht um. `null` = noch nicht bewertet und ausdrücklich etwas
anderes als Stufe 0 („nicht gelöst"). Eine Stufe außerhalb 0–4 ist ein **Client-Fehler → 400** und
wird NICHT still auf 0 geklemmt. Kapitel-/Kurssummen nennen IMMER auch ihr Maximum
(`points` + `maxPoints` = 4 × Stellungen), weil eine nackte Summe ohne die Zahl der Stellungen
nicht lesbar ist („14 / 24" statt „14").

Buch↔Gruppe-Freigabe verwaltet der Admin:
| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/books/{id}/groups` | Admin | Gruppen-Ids mit Kurs-Zugriff auf das Buch |
| PUT | `/api/admin/books/{id}/groups` | Admin | Vollständige Gruppen-Freigabe setzen (ersetzt; ungültige Ids ignoriert) |
