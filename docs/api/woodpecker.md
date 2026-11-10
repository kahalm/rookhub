# Woodpecker-Training (auth, 0.773.0)

> Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Die Woodpecker-Methode (Axel Smith / Hans Tikkanen, „The Woodpecker Method“, Quality Chess 2018, Instructions
S. 26–29) über einen oder mehrere **Kurse** (`Book` mit Quiz-Linien). Menüpunkt `woodpecker` (Training), Seiten
`/woodpecker` (Übersicht) und `/woodpecker/:planId/solve` (Löser — seit 0.882.0 der Kurs-Löser `BookPuzzleComponent` mit der
Strategie `BookWoodpeckerMode`: dieselbe Oberfläche wie im Kurs, Plan-Kopf in der Kontext-Karte, Spielweise je Plan
`woodpecker:<id>` mit Kurs-Grundwahl, Kommentar-Sprache wie im Kurs). Der Nutzer stellt **nichts** ein außer den Kursen
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
| GET | `/api/woodpecker/{id}/next` | `{ puzzle: BookPuzzleDto \| null, plan }` — `null`, wenn der Zyklus erledigt ist; `?lang=` übersetzt die Kommentare wie `GET /api/courses/{id}/next` (0.882.0). Nur aus Kursen, auf die das Konto JETZT Zugriff hat (R3-003: ein Kurs ohne Zugang ruht, die übrigen laufen weiter); ein Kalkulationsbuch kommt ohne Lösung |
| POST | `/api/woodpecker/{id}/attempts` | `{ bookPuzzleId, solved, timeSeconds, hintsUsed? }` → `plan` (`hintsUsed` 0–3, geklemmt über `AttemptRecording`, fehlt = 0; 0.956.0); Aufgabe nicht im Plan (Zyklus 1: nicht in den Kursen, ab 2: nicht im Satz) oder Kurs ohne Zugang (R3-003) → 404; zweiter Versuch im selben Zyklus ändert nichts |

**Statistik je Zyklus** (`WoodpeckerCycleDto`): `attempted`, `solved`, `timeSeconds` und seit 0.956.0 `hinted` (Versuche
mit mindestens einem Tipp, gelöst oder nicht) und `solvedWithHint` (davon gelöst). **Entscheidung:** gelöst mit Tipp
bleibt GELÖST (zählt in `solved` und in der Quote), wird aber getrennt ausgewiesen — „sauber gelöst“ = `solved −
solvedWithHint`. Gewertet ist wie immer nur der erste Versuch je Aufgabe und Zyklus; ein späterer Versuch mit Tipp ändert
nichts. Versuche von vor 0.956 stehen auf `HintsUsed = 0`. Kurs-Versuche, die in Zyklus 1 übernommen werden, bringen
ihr `CourseAttempt.HintsUsed` mit.

**Statistik auf der Lösen-Seite** (0.956.0, Wunsch 10.11.: „wie viel gelöst, wie viel mit Hint, Hochrechnung“): die
Kontext-Karte des Lösers zeigt den laufenden Zyklus — gerechnet im Client aus `plan.cycles`
(`features/woodpecker/woodpecker-stats.util.ts`, rein und getestet):

| Kennzahl | Formel |
|---|---|
| Gelöst | `solved / attempted` + Quote (Prozentpunkte über die `pct`-Pipe); „ohne Tipp“ = `solved − solvedWithHint`, „mit Tipp“ = `hinted` (beides nur, wenn `hinted > 0`) |
| offen (ab Zyklus 2) | `setSize − attempted` |
| Ø | `timeSeconds / attempted` (Uhrform `formatSecondsClock`) |
| Restzeit (ab 2) | `offen × Ø` — reine Lösezeit |
| Tempo | `attempted / begonnene lokale Kalendertage seit Zyklusbeginn` (heute zählt ganz; „gerade begonnen“ mit 5 Aufgaben = 5/Tag) |
| nötig je Tag (ab 2) | `⌈offen / Resttage⌉`, Resttage aufgerundet wie „noch N Tage“; Frist vorbei → „Frist überschritten“ |
| voraussichtlich fertig (ab 2) | `jetzt + offen / Tempo` Tage; „rechtzeitig“, wenn ≤ Frist, sonst mit Frist daneben; ohne Versuch keins |
| Zyklus 1 | kein Ziel, stattdessen „bei diesem Tempo ≈ N Aufgaben im Satz bis zur Frist“ = `attempted + Tempo × Resttage (anteilig)`, höchstens `availableCount` |
| Vorzyklus | Quote und Ø des Zyklus davor (nur mit Versuchen) |

`WoodpeckerPlanDto.status`: `solving` (Zyklus läuft), `break` (Pause bis `nextCycleAt`), `ready`, `completed`.

**Dashboard-Kachel** (0.955.0): das Dashboard ruft `GET /api/woodpecker` getrennt vom übrigen Laden ab (nur mit
Menüpunkt `woodpecker`) und zeigt das laufende Training (`activeWoodpeckerPlan` in `woodpecker.service.ts`: nicht
abgeschlossen, jüngster Zeitpunkt aus Anlage, Beginn und Ende des aktuellen Zyklus). Der Plan liegt im Zwischenstand
des Dashboards (`woodpeckerPlan`); ohne laufendes Training bleibt die Kachel unsichtbar. Gestaltung: DESIGN 8.1.

**Client** (`book-woodpecker-mode.ts`): ein Versuch wird je Aufgabe einmal gemeldet; offline oder bei Netz-/408/429/5xx-
Fehler geht der POST in die Offline-Warteschlange (gleicher Body) und gilt weiter als gemeldet — „Nochmal“ oder „Ganze
Partie“ → „Zurück zum Puzzle“ melden nichts mehr. „Weiter“ wartet auf eine laufende Meldung, damit `GET next` nicht vor
dem POST ankommt. Liefert `next` (Zyklus 1: Ausschluss erst über den gespeicherten Versuch) eine Aufgabe, deren Versuch
noch in der Warteschlange liegt, gilt sie als gemeldet (Hinweis „schon gewertet“). Damit „Weiter“ nicht bis zum
nächsten App-Start dieselbe Aufgabe bringt, sendet die Warteschlange online Vorgemerktes selbst nach (5 s, dann 15 s,
60 s; GL-FE1-01).

**Daten** (`Models/WoodpeckerPlan.cs`, `Data/Configurations/WoodpeckerConfigurations.cs`): `WoodpeckerPlans` (UserId
Cascade), `WoodpeckerPlanBooks` (Position), `WoodpeckerCycles` (Number, StartedAt, FinishedAt), `WoodpeckerSetItems`
(Position) und `WoodpeckerAttempts` (Cycle, Solved, TimeSeconds, HintsUsed seit 0.956.0, AttemptedAt). Satz und Versuche tragen `BookId` (Cascade — Kurs löschen nimmt sie mit)
und einen Restrict-FK auf `BookPuzzle`, abgeräumt in `BookPuzzleDependents`. Die Kontolöschung lässt die Pläne stehen
(keine PII, `ProfileServiceTests.DeletionExempt`).
