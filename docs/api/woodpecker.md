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
| POST | `/api/woodpecker` | `{ bookIds[], startedAt?, archiveCurrent? }` — Kurse in Reihenfolge, Beginn UTC (leer = jetzt, Zukunft → 400). Kurs ohne Zugriff (`CourseAccess.CanAccessAsync`) → 404, Kalkulationsbuch → 400. Schon ein nicht archiviertes Training → 409 `woodpecker_active_exists`, außer `archiveCurrent: true` (0.958.0, siehe „Ein Training, Archiv“) |
| GET | `/api/woodpecker/{id}` | ein Training (fremdes → 404) |
| DELETE | `/api/woodpecker/{id}` | löschen samt Zyklen, Satz, Versuchen (auch archivierte) |
| POST | `/api/woodpecker/{id}/archive` | ins Archiv legen (jederzeit, auch mitten im Zyklus; schon archiviert = unverändert) → `plan` (0.958.0) |
| POST | `/api/woodpecker/{id}/unarchive` | wiederherstellen — nur ohne anderes nicht archiviertes Training, sonst 409 `woodpecker_active_exists` (0.958.0) |
| PUT | `/api/woodpecker/{id}/start` | `{ startedAt }` — Beginn des laufenden Zyklus verschieben: nicht in die Zukunft, nicht hinter dessen ersten Versuch, nicht vor das Ende des vorigen. Die Oberfläche bietet es nur in Zyklus 1 an |
| POST | `/api/woodpecker/{id}/finish-set` | Zyklus 1 vorzeitig beenden (Satz leer → 400) |
| POST | `/api/woodpecker/{id}/next-cycle` | nächsten Zyklus beginnen (Pause < 24 h oder Zyklus läuft → 409) |
| GET | `/api/woodpecker/{id}/next` | `{ puzzle: BookPuzzleDto \| null, plan }` — `null`, wenn der Zyklus erledigt ist; `?lang=` übersetzt die Kommentare wie `GET /api/courses/{id}/next` (0.882.0). Nur aus Kursen, auf die das Konto JETZT Zugriff hat (R3-003: ein Kurs ohne Zugang ruht, die übrigen laufen weiter); ein Kalkulationsbuch kommt ohne Lösung |
| GET | `/api/woodpecker/{id}/stats` | ausführliche Statistik (`WoodpeckerStatsDto`, 0.957.0; `?utcOffsetMinutes=`), fremdes → 404 — siehe unten |
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

**Seit 0.957.0 ist die ganze Statistik im Löser standardmäßig ZUGEKLAPPT** (Nutzerentscheidung 10.11.2026): sichtbar
bleiben Zyklus-Kopf, Balken und Frist plus der Textknopf „Statistik“ am Ende der Kopfzeile (`aria-expanded`, Zustand je
Gerät `rh.woodpecker.statsOpen`). Aufgeklappt stehen zuerst die drei Zeilen oben, darunter die ausführliche Statistik.

### Ausführliche Statistik (`GET /api/woodpecker/{id}/stats`, 0.957.0)

Wunsch 10.11.: „momentan hast du 1 h für 50 Puzzle gebraucht, die gleichen Puzzle haben letztes Mal 2 h gebraucht“ +
Tipp-Auswertung. Eigener Abruf (NICHT in `BuildDtoAsync`, das bei jedem Versuch und in Liste/Dashboard läuft); fremder Plan
→ 404. `?utcOffsetMinutes=` (Minuten östlich von UTC, ±14 h geklemmt) legt die Tageszeilen fest. Der Dienst
(`WoodpeckerService.GetStatsAsync`) zieht wie `GET` erst nach (Frist, Kurs-Import), lädt dann ALLE Versuche des Plans in
einer Abfrage und die Satz-Ids; gerechnet wird rein und statisch in `Services/Courses/WoodpeckerStatsCalculator.cs`
(Tests mit literalen Zahlen: `WoodpeckerStatsCalculatorTests`, MariaDB: `WoodpeckerSqlTests.Stats_…`). Keine Migration.

| Feld | Formel |
|---|---|
| `cycles[]` | je Zyklus `attempted`, `solved`, `solvedClean` (gelöst ohne Tipp), `hinted`, `timeSeconds`, `ratePct` (Prozentpunkte), `avgSeconds`, `daysUsed` = (Ende bzw. jetzt − Beginn) in Tagen, `hintsTrackedCount` (Versuche ab Tipp-Stichtag) |
| `cycles[k].sameAsNow` (k < N) | **Schnittmenge** über `BookPuzzleId`: Aufgaben, die im laufenden Zyklus N bearbeitet sind UND in k einen Versuch haben — `count`, `secondsNow`/`secondsThen`, `solvedNow`/`solvedThen`, `hintedNow`/`hintedThen`, Quoten, `timeRatio` = secondsNow / secondsThen (0,5 = halbe Zeit), `speedFactor` = secondsThen / secondsNow. Aufgaben nur in einem der beiden zählen nicht |
| `changes` (gegen N−1) | je gemeinsamer Aufgabe: `improved` (damals verfehlt → jetzt gelöst), `worsened`, `bothSolved`, `bothMissed`; `faster`/`slower`/`sameTime`; `muchFaster`/`muchSlower` (≥ 60 s Unterschied); `savedSeconds`/`lostSeconds` (Summen der Differenzen); `bucketsNow`/`bucketsThen` (Zeitstufen auf denselben Aufgaben) |
| `forecast` (gegen N−1, ab 10 gemeinsamen Aufgaben) | r = `timeRatio` der Schnittmenge; Rest = Σ über die OFFENEN Aufgaben des Satzes von r × Zeit in N−1 (offen ohne Versuch in N−1: Ø jetzt); gesamt = bisher + Rest (≈ r × Gesamtzeit N−1); `goalReached` = r ≤ 0,5 (Buchziel „halbe Zeit“). Genauer als „offen × Ø“, weil die offenen Aufgaben die schweren sein können |
| `days[]` | laufender Zyklus je lokalem Tag: Aufgaben, gelöst, Zeit |
| `timeBuckets` | laufender Zyklus: schnell < 30 s, mittel 30–120 s, lang > 120 s |
| `hints` | laufender Zyklus, NUR Versuche ab Stichtag (`tracked`; ältere = `untracked`): gelöst mit 0/1/2/3 Tipps, verfehlt ohne/mit Tipp |
| `hintChanges` (gegen N−1) | nur gemeinsame Aufgaben, deren BEIDE Versuche ab Stichtag liegen: `more`/`fewer`/`same` Tipps, `sumNow`/`sumThen` (Summe der Stufen); sonst `null` |
| `slowest[]` | die 10 langsamsten Aufgaben von N (Zeit jetzt + in N−1), nur Bezeichnung (Titel, sonst Kapitel) — nie Stellung/Lösung |
| `alwaysMissedCount` / `alwaysMissed[]` | Aufgaben mit Versuchen in ≥ 2 Zyklen, in keinem gelöst (Liste höchstens 10) |

**Tipp-Stichtag** (`hintsTrackedSince`, Konstante `WoodpeckerStatsCalculator.HintsTrackedSince` = 10.11.2026 17:34 UTC,
Prod-Deploy von 0.956.0): `HintsUsed` gibt es erst seitdem; ältere Versuche stehen auf 0, auch die in Zyklus 1 aus
Kurs-Versuchen übernommenen. Sie zählen in keiner Tipp-Auswertung; die Oberfläche sagt „Tipps werden erst seit … gezählt“
und zeigt in der Verlaufstabelle „ohne Tipp“ nur für Zyklen, deren Versuche ALLE Tipp-Daten haben (sonst „–“).

**Entscheidung „Rest ≈“:** die obere Zeile bleibt `offen × Ø` (aus `plan`, ohne zusätzlichen Abruf); die genauere
Hochrechnung steht im ausführlichen Teil mit ihrer Grundlage („aus dem Vergleich mit Zyklus N−1“) — eine Zahl, die sich
beim Aufklappen ändert, wäre verwirrender als zwei klar benannte.

**Oberfläche** (`features/woodpecker/woodpecker-stats-panel.component`): entsteht erst beim Aufklappen, lädt dann sofort
und nach jeder gemeldeten Aufgabe (neuer Stand `planId:Zyklus:bearbeitet`) entprellt 800 ms neu. Abschnitte: Dieselben
Aufgaben → Wechsel je Aufgabe (+ Zeitstufen jetzt/damals) → Hochrechnung → Tipps → Verlauf (Tabelle je Zyklus mit Spalte
„dieselben wie jetzt“) bzw. in Zyklus 1 „Je Tag“ (jüngste 14) + Zeitstufen → Sorgenkinder (Link „ansehen“ =
`/puzzles/book/:id`).

`WoodpeckerPlanDto.status`: `solving` (Zyklus läuft), `break` (Pause bis `nextCycleAt`), `ready`, `completed`, seit 0.958.0
`archived` (geht allem vor; `completedAt` bleibt daneben erhalten) und `archivedAt`.

**Ein Training, Archiv** (0.958.0, Nutzerentscheidung 10.11.: „soll nur ein Woodpecker geben – für einen neuen muss ein
alter archiviert werden“): je Nutzer höchstens EIN nicht archiviertes Training (`WoodpeckerPlans.ArchivedAt IS NULL`) —
egal ob laufend, in Pause, bereit oder abgeschlossen. Erzwungen im `WoodpeckerService`, nicht nur in der Oberfläche:

| Fall | Antwort |
|---|---|
| `POST /api/woodpecker` mit vorhandenem nicht archiviertem Training | 409 `woodpecker_active_exists` |
| dasselbe mit `archiveCurrent: true` | ALLE bisherigen nicht archivierten bekommen `ArchivedAt = jetzt`, das neue wird angelegt — Sperre auf die Nutzerzeile (`SELECT … FOR UPDATE`), Prüfen, Archivieren und Anlegen in EINER Transaktion in der Execution-Strategy |
| `unarchive` neben einem aktiven | 409 `woodpecker_active_exists` |
| `next`, `attempts`, `next-cycle`, `finish-set`, `start` auf ein archiviertes | 409 `woodpecker_archived` |
| `GET`, Liste, `DELETE`, `archive` auf ein archiviertes | wie bisher |

Ein archiviertes Training ist eingefroren: `RefreshAsync` lässt es aus (keine Frist von Zyklus 1 wird nachgezogen, kein
Kurs-Versuch mehr übernommen). Beim Archivieren wird vorher einmal nachgezogen — eingefroren ist der Stand von da.
Wiederherstellen macht es wieder aktiv; danach gelten die Fristen wie zuvor (eine inzwischen abgelaufene Frist von
Zyklus 1 beendet den Zyklus beim nächsten Lesen). **Altbestand** mit mehreren nicht archivierten je Nutzer (vor 0.958.0
möglich; Prod 10.11.: 3 Pläne bei 3 Nutzern, also keiner) bleibt lauffähig: die Liste zeigt alle, Anlegen ist bis zum
Archivieren gesperrt (der Dialog archiviert alle mit), die Dashboard-Kachel nimmt `activeWoodpeckerPlan`. Die Migration
`WoodpeckerPlanArchivedAt` legt nur die Spalte an und archiviert nichts.

**Dashboard-Kachel** (0.955.0): das Dashboard ruft `GET /api/woodpecker` getrennt vom übrigen Laden ab (nur mit
Menüpunkt `woodpecker`) und zeigt das laufende Training (`activeWoodpeckerPlan` in `woodpecker.service.ts`: nicht
abgeschlossen und nicht archiviert, jüngster Zeitpunkt aus Anlage, Beginn und Ende des aktuellen Zyklus). Seit 0.958.0
startet der Knopf „Zyklus N starten“ im Zustand `ready` den Zyklus direkt (`next-cycle`) und springt in den Löser; ein
409 (Pause läuft doch noch, archiviert) kommt als Snackbar, danach lädt die Kachel die Liste neu (`planChange` ans
Dashboard). Der Plan liegt im Zwischenstand
des Dashboards (`woodpeckerPlan`); ohne laufendes Training bleibt die Kachel unsichtbar. Gestaltung: DESIGN 8.1.

**Client** (`book-woodpecker-mode.ts`): ein Versuch wird je Aufgabe einmal gemeldet; offline oder bei Netz-/408/429/5xx-
Fehler geht der POST in die Offline-Warteschlange (gleicher Body) und gilt weiter als gemeldet — „Nochmal“ oder „Ganze
Partie“ → „Zurück zum Puzzle“ melden nichts mehr. „Weiter“ wartet auf eine laufende Meldung, damit `GET next` nicht vor
dem POST ankommt. Liefert `next` (Zyklus 1: Ausschluss erst über den gespeicherten Versuch) eine Aufgabe, deren Versuch
noch in der Warteschlange liegt, gilt sie als gemeldet (Hinweis „schon gewertet“). Damit „Weiter“ nicht bis zum
nächsten App-Start dieselbe Aufgabe bringt, sendet die Warteschlange online Vorgemerktes selbst nach (5 s, dann 15 s,
60 s; GL-FE1-01).

**Daten** (`Models/WoodpeckerPlan.cs`, `Data/Configurations/WoodpeckerConfigurations.cs`): `WoodpeckerPlans` (UserId
Cascade, `ArchivedAt` seit 0.958.0), `WoodpeckerPlanBooks` (Position), `WoodpeckerCycles` (Number, StartedAt, FinishedAt), `WoodpeckerSetItems`
(Position) und `WoodpeckerAttempts` (Cycle, Solved, TimeSeconds, HintsUsed seit 0.956.0, AttemptedAt). Satz und Versuche tragen `BookId` (Cascade — Kurs löschen nimmt sie mit)
und einen Restrict-FK auf `BookPuzzle`, abgeräumt in `BookPuzzleDependents`. Die Kontolöschung lässt die Pläne stehen
(keine PII, `ProfileServiceTests.DeletionExempt`).
