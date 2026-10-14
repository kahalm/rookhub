# Woodpecker-Training (auth, 0.773.0)

> Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Die Woodpecker-Methode (Axel Smith / Hans Tikkanen, „The Woodpecker Method“, Quality Chess 2018, Instructions
S. 26–29) über einen oder mehrere **Kurse** (`Book` mit Quiz-Linien). Menüpunkt `woodpecker` (Training), Seiten
`/woodpecker` (Übersicht) und `/woodpecker/:planId/solve` (Löser). Der Nutzer stellt **nichts** ein außer den Kursen
und dem (rückdatierbaren) Beginn — die Regeln stehen fest in `Services/Courses/WoodpeckerRules.cs`:

| Regel | Wert |
|---|---|
| Zyklus 1 | 28 Tage; jede bearbeitete Aufgabe kommt in den **Satz** |
| Pause zwischen Zyklen | mindestens 24 h nach Ende des vorigen (`next-cycle` vorher → 409) |
| Zieltage je Zyklus | halbiert, aufgerundet: 28, 14, 7, 4, 2, 1, 1 |
| Reihenfolge | Zyklus 1: Kurse in Plan-Reihenfolge, je Kurs Lesereihenfolge (`ChapterOrder.InReadingOrder`); ab Zyklus 2 zufällig aus dem Satz |
| Wertung | je Zyklus zählt nur der **erste** Versuch an einer Aufgabe (Unique `(PlanId, Cycle, BookPuzzleId)`) |
| Abgeschlossen | ein Zyklus ab 2 in ≤ 24 h erledigt, sonst nach Zyklus 7 |

**Zyklus 1 endet** an der Frist (beim nächsten Lesen nachgezogen, `FinishedAt` = Frist), auf Knopfdruck
(`finish-set`) oder sobald alle Aufgaben der Kurse im Satz sind. Ein leerer Satz endet nie von selbst. Ab Zyklus 2
endet ein Zyklus, wenn jede Aufgabe des Satzes bearbeitet ist; eine überzogene Frist wird nur angezeigt.

**Kurs-Versuche zählen in Zyklus 1 mit** (`WoodpeckerService.ImportCourseAttemptsAsync`): bei jedem Lesen werden die
`CourseAttempts` des Nutzers an den Plan-Kursen zwischen Beginn und Frist übernommen (je Aufgabe der erste, zeitlich
geordnet, ohne Erklärlinien). Wer schon im normalen Kurs-Löser gearbeitet hat, datiert den Beginn zurück und hat
diese Aufgaben im Satz. Umgekehrt schreibt der Woodpecker-Löser KEINE `CourseAttempts` (Kursfortschritt bleibt unberührt).

| Methode | Endpoint | Zweck |
|---|---|---|
| GET | `/api/woodpecker` | eigene Trainings, neueste zuerst (`WoodpeckerPlanDto[]`) |
| POST | `/api/woodpecker` | `{ bookIds[], startedAt? }` — Kurse in Reihenfolge, Beginn UTC (leer = jetzt, Zukunft → 400). Kurs ohne Zugriff (`CourseAccess.CanAccessAsync`) → 404, Kalkulationsbuch → 400 |
| GET | `/api/woodpecker/{id}` | ein Training (fremdes → 404) |
| DELETE | `/api/woodpecker/{id}` | löschen samt Zyklen, Satz, Versuchen |
| PUT | `/api/woodpecker/{id}/start` | `{ startedAt }` — Beginn des laufenden Zyklus verschieben: nicht in die Zukunft, nicht hinter dessen ersten Versuch, nicht vor das Ende des vorigen. Die Oberfläche bietet es nur in Zyklus 1 an |
| POST | `/api/woodpecker/{id}/finish-set` | Zyklus 1 vorzeitig beenden (Satz leer → 400) |
| POST | `/api/woodpecker/{id}/next-cycle` | nächsten Zyklus beginnen (Pause < 24 h oder Zyklus läuft → 409) |
| GET | `/api/woodpecker/{id}/next` | `{ puzzle: BookPuzzleDto \| null, plan }` — `null`, wenn der Zyklus erledigt ist |
| POST | `/api/woodpecker/{id}/attempts` | `{ bookPuzzleId, solved, timeSeconds }` → `plan`; Aufgabe nicht im Plan (Zyklus 1: nicht in den Kursen, ab 2: nicht im Satz) → 404; zweiter Versuch im selben Zyklus ändert nichts |

`WoodpeckerPlanDto.status`: `solving` (Zyklus läuft), `break` (Pause bis `nextCycleAt`), `ready`, `completed`.

**Daten** (`Models/WoodpeckerPlan.cs`, `Data/Configurations/WoodpeckerConfigurations.cs`): `WoodpeckerPlans` (UserId
Cascade), `WoodpeckerPlanBooks` (Position), `WoodpeckerCycles` (Number, StartedAt, FinishedAt), `WoodpeckerSetItems`
(Position) und `WoodpeckerAttempts` (Cycle). Satz und Versuche tragen `BookId` (Cascade — Kurs löschen nimmt sie mit)
und einen Restrict-FK auf `BookPuzzle`, abgeräumt in `BookPuzzleDependents`. Die Kontolöschung lässt die Pläne stehen
(keine PII, `ProfileServiceTests.DeletionExempt`).
