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

**Zeitzone des Betrachters (`?utcOffsetMinutes=`, 0.993.0):** JEDER Endpunkt, der einen Plan zurückgibt (Liste,
Einzelplan, Anlegen, `archive`, `unarchive`, `start`, `finish-set`, `next-cycle`, `next`, `attempts`), nimmt wie `stats`
`?utcOffsetMinutes=` — Minuten östlich von UTC (`-getTimezoneOffset()`), auf ±14 h geklemmt
(`WoodpeckerStatsCalculator.ViewerOffset`). Damit zählt **`WoodpeckerPlanDto.todayAttempted`** die gewerteten Versuche des
LAUFENDEN Zyklus seit der lokalen Mitternacht (`LocalDayStartUtc`; eine Zählabfrage in `BuildDtoAsync`, keine Migration);
ohne Angabe gilt der UTC-Tag. Die Oberfläche schickt den Wert bei allen diesen Aufrufen mit (`WoodpeckerService.tz()`,
literal geprüft in `woodpecker.service.spec.ts`). Nur der nachgesendete Versuch der Offline-Warteschlange trägt ihn
nicht — dessen Antwort zeigt niemand an. Am Tag einer Zeitumstellung kann die Grenze um eine Stunde danebenliegen
(gerechnet wird mit dem Abstand von jetzt).

**Tagesziel ohne Statistik-Fenster (0.993.0,** Wunsch 18.11.2026: „oben auch ohne Statistik visualisieren, wenn ich heute
das notwendige Minimum erledigt habe (sowohl im Dashboard als auch wenn ich drinnen bin)“, „grüne Checkmark oder so“):
`woodpeckerDailyGoal(plan)` in `features/woodpecker/woodpecker-stats.util.ts` (rein, getestet), gezeigt vom geteilten
Baustein `features/woodpecker/woodpecker-daily-goal.component` an DREI Stellen — Dashboard-Kachel (eigene Zeile unter
der Frist), Löser-Kopf (am Ende der Zyklus-Zeile, dahinter seit 0.994.0 der Zeitplan) und Statistik-Fenster (Je-Tag-Zeile).

| Größe | Formel |
|---|---|
| Tage bis zur Frist | Kalendertage von heute bis zum Fristtag, beide mitgezählt (≥ 1) = `⌈(Frist − lokale Mitternacht heute) / 24 h⌉`, über Kalendertage gerechnet (`calendarDaysUntil`: eine Zeitumstellung zählt keinen Tag dazu); endet die Frist genau um Mitternacht, zählt ihr Tag nicht |
| **Soll heute** | `⌈(offen jetzt + heute bearbeitet) / Tage bis zur Frist⌉`, offen = `setSize − attempted`. „offen + heute“ ist der Stand von heute früh — das Soll bleibt deshalb über den Tag FEST |
| erreicht | `heute ≥ Soll` (oder nichts mehr offen) → grüner Haken `check_circle` + „Tagesziel erreicht“ + „n von Soll“; sonst leise „Heute: n von Soll“ |
| nur mit Soll | Status `solving`, Zyklus ≥ 2, Frist nicht vorbei. Zyklus 1 (der Satz wächst, kein Soll): nur „Heute: n“. Pause, bereit, abgeschlossen, archiviert, Frist überschritten: nichts |
| „heute“ | `plan.todayAttempted`; im Fenster der `days[]`-Eintrag von `/stats` (derselbe Zeitzonen-Parameter). Fehlt das Feld (älterer Server hinter neuem Frontend): KEINE Anzeige statt „0 von n“ |

**Entscheidung (18.11.2026, löst die Formel aus 0.992.0 ab):** „nötig je Tag = ⌈offen / Resttage⌉“ sank mit jeder gelösten
Aufgabe und konnte nie „erreicht“ sein; das Fenster zeigt seither dasselbe feste Tagessoll wie Kachel und Löser-Kopf —
es gibt nur noch EINE Soll-Zahl.

**Zeitplan — Anteil des Satzes passend zum Tag des Zyklus (0.994.0,** Wunsch 18.11.2026: „neben x pro Tag für fertig auch
noch ein Checkmark, wenn ich % der Gesamtpuzzles passend zum aktuellen Tag hab (und in Statistik oben ausgewiesen: noch
x Puzzle bis z. B. 55 % erfüllt)“): `woodpeckerSchedule(plan)` in `features/woodpecker/woodpecker-stats.util.ts` (rein,
getestet) — NUR im Frontend gerechnet, der Plan liefert seit jeher alles Nötige (`setSize`, `attempted`, `startedAt`,
`deadline` des laufenden Zyklus); keine API-Änderung. Gezeigt vom geteilten Baustein
`features/woodpecker/woodpecker-schedule.component` an denselben drei Stellen wie das Tagesziel, jeweils direkt
dahinter bzw. darunter. Vom Tagesziel unabhängig: das eine fragt „heute genug?“, das andere „insgesamt dort, wo ich
heute stehen soll?“.

| Größe | Formel |
|---|---|
| Tag k | begonnene lokale Kalendertage seit Zyklusbeginn, der Tag des Beginns ist Tag 1 (`calendarDaysSince`), höchstens D |
| Tage D | Kalendertage vom Tag des Beginns bis zum Fristtag, beide mitgezählt (`calendarDaysUntil` ab dem Beginn; endet die Frist genau um Mitternacht, zählt ihr Tag nicht). Dieselbe Zählung wie beim Tagesziel: an jedem Tag vor der Frist gilt `D = k − 1 + Tage bis zur Frist`. Ein Zyklus über 14 × 24 h ab Mitternacht (rückdatierter Beginn) hat 14 Tage, einer ab mittags berührt 15 (erster und letzter Tag sind halbe) |
| **Soll bis Ende heute** | `⌈setSize × k / D⌉`; Soll-Anteil `k / D` (höchstens 1), angezeigt gerundet in Prozentpunkten |
| im Zeitplan | `attempted ≥ Soll` → grüner Haken `check_circle` + „Im Zeitplan“ + Soll-Anteil; sonst neutral „Zeitplan: noch x bis n %“ mit `x = Soll − attempted` |
| aktueller Stand | `attempted / setSize`, gerundet wie der Soll-Anteil (100 % erst, wenn nichts mehr offen ist) — nur im Fenster, nur wenn erreicht: „57 % erreicht (aktuell 61 %)“ |
| letzter Tag / nach der Frist | Soll = der ganze Satz (100 %), „noch x“ = offene Aufgaben; nach der Frist ohne Tagesangabe. Bleibt — anders als das Tagesziel — nach der Frist stehen |
| nur ab Zyklus 2 | Status `solving`, Zyklus ≥ 2. Zyklus 1 (der Satz wächst, es gibt keinen Anteil), Pause, bereit, abgeschlossen, archiviert oder unbrauchbare Daten (Beginn, Frist, Satz): nichts |
| im Fenster | mit dem Stand aus `/stats` (`planWithStats`), wie alle Zahlen der Übersicht |

**Statistik je Zyklus** (`WoodpeckerCycleDto`): `attempted`, `solved`, `timeSeconds` und seit 0.956.0 `hinted` (Versuche
mit mindestens einem Tipp, gelöst oder nicht) und `solvedWithHint` (davon gelöst). **Entscheidung:** gelöst mit Tipp
bleibt GELÖST (zählt in `solved` und in der Quote), wird aber getrennt ausgewiesen — „sauber gelöst“ = `solved −
solvedWithHint`. Gewertet ist wie immer nur der erste Versuch je Aufgabe und Zyklus; ein späterer Versuch mit Tipp ändert
nichts. Versuche von vor 0.956 stehen auf `HintsUsed = 0`. Kurs-Versuche, die in Zyklus 1 übernommen werden, bringen
ihr `CourseAttempt.HintsUsed` mit.

**Statistik auf der Lösen-Seite = Fenster** (0.992.0, Wunsch 18.11.2026: „ganz oben bisherige Zeit, Zeit für
die gleichen Puzzles in den vorherigen Zyklen, bisherige % richtig verglichen mit vorher, Puzzles/Tag nötig (offene
Puzzle / Resttage), Anzahl heute“ + „als modales Overlay, das aktuelle Puzzle währenddessen pausieren“). Vorher
(0.956.0–0.958.1) ein aufklappbarer Block in der Kontext-Karte mit gemerktem Zustand `rh.woodpecker.statsOpen` — beides
entfällt. Die Kontext-Karte hat nur noch den Knopf „Statistik“ (Ausgabe `woodpeckerStats`); der Buch-Löser öffnet
`features/woodpecker/woodpecker-stats-dialog.component` (`BookPuzzleComponent.openWoodpeckerStats`).

**Pause:** solange das Fenster offen ist, steht die Lösezeit der Aufgabe — in der angezeigten Uhr und im gemeldeten
`timeSeconds` (`POST …/attempts`). Technik: `VisibilityStopwatch.hold()/release()` (zählt nur „Tab sichtbar UND nicht
angehalten“), `SolverTimer.pause()/resume()`, `SolverAssistBase.pauseTimer()/resumeTimer()` + `timerPaused`. Der Halt
gehört der Uhr, nicht dem Lauf: lädt hinter dem Fenster die nächste Aufgabe (Auto-Weiter), beginnt deren Uhr angehalten.
Das Brett ist währenddessen unkenntlich (`.board-paused` → Blur) — Pausieren ist keine Gratis-Denkzeit. Mit angehalten
werden der Countdown bis zum Verdecken im Visualisierungsmodus (`VizController.setCountdownPaused`) und der
2-s-Countdown zur nächsten Aufgabe (`SecondsCountdown.paused`): die nächste Aufgabe lädt erst nach dem Schließen.

**Übersicht ganz oben** (`woodpeckerOverview` in `features/woodpecker/woodpecker-stats.util.ts`, rein und getestet;
Quelle `GET …/stats` + Frist aus dem Plan):

| Zeile | Formel |
|---|---|
| Zeit — „Bisher“ | `timeSeconds` des laufenden Zyklus |
| Zeit — „Zyklus k“ (jeder frühere, jüngster zuerst) | `cycles[k].sameAsNow.secondsThen` = dieselben Aufgaben damals (Schnittmenge, siehe unten); Zyklen ohne gemeinsame Aufgabe fehlen |
| Richtig — „Bisher“ / „Zyklus k“ | `solved / attempted` bzw. `sameAsNow.ratePctThen` (Prozentpunkte, `pct`-Pipe) |
| Schnittmenge kleiner als jetzt bearbeitet | zusätzlich ein Satz je Zyklus mit `count`, `secondsNow`, `ratePctNow` — die Jetzt-Werte auf genau diesen Aufgaben, damit nie Gesamtzeit gegen eine Teilmenge steht (normal ist `count` = bearbeitet: der Satz ist ab Zyklus 2 fest) |
| Je Tag (ab Zyklus 2, seit 0.993.0) | das **Tagesziel** (siehe oben, Baustein `app-woodpecker-daily-goal`): „Heute: n von Soll“ bzw. grüner Haken + „Tagesziel erreicht“, dahinter klein die Herleitung „(offen + heute, verteilt auf d Tage)“ bzw. am letzten Tag „(…, heute ist der letzte Tag)“. Bis 0.992.0 stand hier „Nötig je Tag: ⌈offen / Resttage⌉ · Heute: n“. Frist vorbei → „Frist überschritten“ + offen + „Heute: n“ |
| Zeitplan (ab Zyklus 2, seit 0.994.0) | eigene Zeile unter „Je Tag“ (Baustein `app-woodpecker-schedule`, ausführlich): „Zeitplan (Tag k von D): noch x Aufgaben bis n %“ bzw. Haken + „n % erreicht (aktuell m %)“; nach der Frist „Zeitplan: noch x Aufgaben bis 100 %“. Formeln siehe oben |
| Heute | `days[]`-Eintrag mit dem lokalen Datum des Geräts (`attempted`); kein Eintrag = heute noch nichts = 0 |
| Zyklus 1 | kein Soll (der Satz wächst): das bisherige Tempo `attempted / begonnene Kalendertage` + „Heute: n“; keine Vergleichsspalten |

Alle Zahlen des Fensters kommen aus EINEM Serverstand: `planWithStats` legt die Werte des laufenden Zyklus aus `/stats`
über den Plan aus dem Löser (der kann eine Meldung hinterher sein); nur Frist und `availableCount` stammen aus dem Plan.

**Abschnitt „Dieser Zyklus“** (bis 0.992.0 die Zeilen in der Kontext-Karte; `woodpeckerCycleStats`):

| Kennzahl | Formel |
|---|---|
| Gelöst | `solved` von `attempted` (ohne Quote — die steht oben); „ohne Tipp“ = `solved − solvedWithHint`, „mit Tipp“ = `hinted` (beides nur, wenn `hinted > 0`) |
| Ø | `timeSeconds / attempted` (Uhrform `formatSecondsClock`) |
| Restzeit (ab 2) | `offen × Ø` — reine Lösezeit |
| Tempo | `attempted / begonnene lokale Kalendertage seit Zyklusbeginn` (heute zählt ganz; „gerade begonnen“ mit 5 Aufgaben = 5/Tag) |
| voraussichtlich fertig (ab 2) | `jetzt + offen / Tempo` Tage; „rechtzeitig“, wenn ≤ Frist, sonst mit Frist daneben; ohne Versuch keins |
| Zyklus 1 | „bei diesem Tempo ≈ N Aufgaben im Satz bis zur Frist“ = `attempted + Tempo × Resttage (anteilig)`, höchstens `availableCount` |

Der Vorzyklus als eigene Zeile („Zyklus 1: 75 % gelöst, Ø 1:30“) ist entfallen: die Übersicht vergleicht auf denselben
Aufgaben, die Verlaufstabelle zeigt die ganzen Zyklen.

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

**Entscheidung „Rest ≈“:** die Zeile „Dieser Zyklus“ bleibt bei `offen × Ø`; die genauere Hochrechnung steht im
Abschnitt „Hochrechnung“ mit ihrer Grundlage („aus dem Vergleich mit Zyklus N−1“) — zwei klar benannte Zahlen.

**Oberfläche** (`features/woodpecker/woodpecker-stats-panel.component`, Inhalt des Statistik-Fensters): entsteht mit dem
Fenster, lädt dann sofort und bei einem neuen Planstand (`planId:Zyklus:bearbeitet`) entprellt 800 ms neu. Reihenfolge:
Übersicht (siehe oben) → Dieser Zyklus → Dieselben Aufgaben (gemeinsame Zahl · Anteil der Zeit · Faktor · gelöst
jetzt/damals; die Zeiten und Quoten selbst stehen in der Übersicht) → Wechsel je Aufgabe (+ Zeitstufen jetzt/damals) →
Hochrechnung → Tipps → Verlauf (Tabelle je ganzem Zyklus; die Spalte „dieselben wie jetzt“ ist in die Übersicht
gewandert) bzw. in Zyklus 1 „Je Tag“ (jüngste 14) + Zeitstufen → Sorgenkinder (Link „ansehen“ = `/puzzles/book/:id`).
Ohne Versuch im Zyklus: Übersicht (mit „–“, dem Tagesziel „Heute: 0 von Soll“ und dem Zeitplan) + ein Leersatz.

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
