# KidHub — Kinderseite (offen, 0.554.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → KidHub — Kinderseite (offen, 0.554.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Eigene Oberfläche (`kidhub(-dev).oberschmid.homes`, drittes Angular-Projekt, siehe „Drei Oberflächen"). Spielen geht
**ohne Anmeldung** — der Fortschritt liegt dann nur auf dem Gerät; angemeldet gleicht KidHub ihn mit dem Konto ab
(0.563.0, `PUT /api/kids/progress`). `KidsController` + `KidsPuzzleService` + `KidsProgressService`.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/kids/levels` | AllowAnonymous (`kids-read`) | Stufen der Leiter in Reihenfolge `[{ level, theme, puzzleCount }]` — leer, solange die Leiter nicht aufgebaut ist. Wie `levels/{level}` und `courses` mit `Cache-Control: public, max-age=300` + ETag (304 bei `If-None-Match`); eine leere Leiter wird nicht zwischengespeichert |
| GET | `/api/kids/levels/{level}` | AllowAnonymous | Eine Stufe am Stück `{ level, theme, puzzles[{ id, fen, moves }] }` (Lichess-Form: `moves[0]` stellt die Aufgabe), leichteste zuerst; 404 unbekannt |
| GET | `/api/kids/courses?lang=` | AllowAnonymous | Kinderkurse, die GERADE gezeigt werden `[{ bookId, title, description, puzzleCount }]` — freigegeben (`Book.ForKids`), ohne Kalkulationsbücher, ohne leere, und seit 0.565.0 erst, wenn sie in jeder Sprache aus `Kids:RequiredCourseLanguages` (Vorgabe `de`) vorliegen: Quelle in der Sprache ODER ein fertiger Übersetzungsauftrag (`Done`, auch „nichts zu tun"). `title` = Kindertitel in `lang` → `en` → `de` → Buchname (`KidsTitles.Pick`), sortiert nach diesem Titel; `puzzleCount` ohne Info-Linien
| GET | `/api/kids/courses/{bookId}/puzzles?lang=` | AllowAnonymous | Aufgaben eines Kinderkurses in Lesereihenfolge (`BookPuzzleDto`, OHNE `IsInfoOnly`); `lang` wie bei den Kursen. 404 wenn nicht `ForKids`/Kalkulationsbuch/noch nicht in der geforderten Sprache; `bookTitle` = Kindertitel wie oben |
| PUT | `/api/kids/progress` | Auth | Den GANZEN Stand des Browsers schicken → zusammengeführt gespeichert, Antwort = gemeinsamer Stand (`KidsProgressMerge`, Regeln unten). 400 über den Deckeln (`MaxLevels` 1000, `MaxCourses` 500, `MaxLinesPerCourse` 10 000); Zeiten über jetzt + 1 Tag werden gekappt. Je KONTO gedeckelt (Codereview F7-001): der eingehende Stand behält nur Kinderkurse (`Book.ForKids`, kein Kalkulationsbuch) und nur deren eigene Linien-Ids, Stufen nur 1..1000 (Gespeichertes bleibt); Rumpf höchstens 1 MB (`RequestSizeLimit`) |
| GET | `/api/kids/language-hint` | AllowAnonymous | Land der Besucher-IP und passende Kindersprache `{ country, language }` (0.560.0) — lokal nachgeschlagen; ein bekanntes Land ohne eigene Kindersprache → `en` (0.560.1); beides `null` bei LAN-Adresse, unbekanntem Land oder ohne Länderliste |
| POST | `/api/kids/endless/batch` | AllowAnonymous (`anonymous-read`) | Endlos-Modus (0.566.0): `{ windows[{ minRating, maxRating }] (≤ 40), exclude[] (≤ 1000) }` → je Fenster ein kindgerechtes Lichess-Puzzle `[{ id, fen, moves, rating }]` in Fensterreihenfolge, im Lauf keins doppelt; Fenster ohne Treffer fehlen. 400 über den Deckeln |
| POST | `/api/admin/kids/rebuild` | `puzzles.manage` | Leiter sofort neu rechnen → `{ levels, puzzles }` (nach einem Neuimport der Standard-Puzzles, der sie per Cascade leert) |

**Rate-Limit nach Zweck** (Codereview 2026-09-29, A10-003): die Lese-Endpunkte (`language-hint`, `levels`, `levels/{level}`,
`courses`, `courses/{id}/puzzles`) hängen an `kids-read` — 240/min je IP und AUS dem globalen 100/min-Topf genommen
(`RateLimitPartitions.Global`: die Policy ist dort selbst die Obergrenze je Adresse). Vorher teilten sie sich mit 15 anderen
offenen Endpunkten `anonymous-puzzle` (30/min je IP), und eine Schulklasse hinter einer NAT-Adresse sah ab dem 16. Kind das
Fehlerbild. KidHub schickt bewusst keine `X-Visitor-Id`, deshalb zählt hier die Adresse. Die übrigen offenen Endpunkte:
`anonymous-read` (60/min) und `anonymous-write` (30/min) zählen je Konto, sonst je IP + gültiger `X-Visitor-Id` (die
RookHub-App schickt sie) — die Obergrenze je Adresse ist dort weiter der globale Limiter; `anonymous-puzzle` (30/min, je
Konto bzw. je IP) bleibt für Client-Log, Bot-Statistik, Token-Test, Extension-Senke und Bestandssuche. Schlüssel und Deckel
stehen in `Services/RateLimitPartitions.cs`, die Zuordnung je Endpunkt hält `AnonymousRateLimitTests` fest.

**Die Leiter** (`KidsPuzzles`, `Services/Kids/KidsCurriculum.cs`) = als „besonders einfach" markierte Lichess-Puzzles.
Vorgabe des Nutzers (2026-09-27): die mit dem niedrigsten Rating, deren Lösung 1 (höchstens 2) eigene Züge lang ist
und bei denen wenig Figuren auf dem Brett stehen. Vorfilter in der DB (Rating ≤ 900, RD ≤ 90, Popularity ≥ 80,
NbPlays ≥ 50, `Moves` ≤ 23 Zeichen), dann in C#: 2 oder 4 Züge, ≤ 12 Figuren, ohne en passant/Rochade/
Unterverwandlung. Thema je Puzzle: Ein-Züger `promote` (Lösungszug macht eine Dame) vor `mate1`; Zwei-Züger `mate2` >
`fork` > `skewer` > `pin` > `discovered` > `capture` (hangingPiece UND der erste eigene Zug schlägt). Innerhalb eines
Themas zählt die **Figurenzahl vor dem Rating** (`Score = Figuren·40 + Rating`). 40 Stufen à 10 im Themen-Wechsel
(`KidsCurriculum.Levels`), jedes Thema teilt sich die `PoolFactor`·10·(Stufen) leichtesten Aufgaben der Reihe nach,
gleichmäßig verteilt — die Stufen eines Themas werden also merklich schwerer. Zu dünne Themen (< 3) fallen weg, die
Nummern bleiben lückenlos; Gleichstand → `LichessId`, damit Dev und Prod dieselbe Leiter bauen.
Zwei Befunde, die den Lehrplan geformt haben (Dev-Bestand): unter Rating 700 ist praktisch ALLES „Matt in 1" (eine
reine Rangliste wäre 30 Stufen lang dasselbe), und „freie Figur schlagen" gibt es als Ein-Züger ohne Matt so gut wie
nicht (1 Stück unter 900) — Lichess-Puzzles ohne Matt brauchen fast immer zwei eigene Züge, deshalb ist `capture`
ein Zwei-Züger (250 Kandidaten ≤ 12 Figuren).
**Aufbau ohne Handgriff**: `KidsPuzzleSeeder` (BackgroundService, 20 s nach dem Start) baut die Leiter, wenn sie
fehlt oder `CurriculumVersion` ≠ `KidsCurriculum.Version`. **Wer Auswahl oder Stufenfolge ändert, erhöht
`KidsCurriculum.Version`** — sonst bleibt überall die alte Leiter stehen. Ersetzt wird in EINER Transaktion
(Execution-Strategy-Muster).

**Fortschritt im Konto** (0.563.0, Wunsch 2026-09-27): drei Tabellen (`KidsLevelProgresses`, `KidsCourseProgresses`,
`KidsCourseLines`), abgeglichen per „ganzer Stand hinauf, zusammengeführter zurück" statt einzelner Ereignisse — so
übernimmt der erste Abgleich nach dem Anmelden auch, was das Kind vorher ohne Konto gespielt hat, und ein zweites Gerät
bekommt denselben Stand. Die EINE Regel (`Services/Kids/KidsProgressMerge.cs`, SPIEGEL `mergeProgress` in
`src-kidhub/app/core/kids-progress.store.ts`, LITERALE Fälle A–E auf beiden Seiten): Sterne nach Höhe; der laufende
Durchgang der JÜNGERE (`runAt`, Gleichstand: der gespeicherte); Kurs-Linien vereinigt (je Linie die jüngste Zeit), aber
nur NACH dem jüngsten „Von vorn" (`resetAt`) — ohne die Marke brächte ein anderes Gerät verworfene Linien zurück. Eine
Linie ohne Zeit (im Browser vor 0.563.0 gelöst) gilt als 1 ms, nicht 0 (sonst fiele sie durch `at > resetAt`). Zwei
Geräte gleichzeitig → eindeutiger Index schlägt an → neu laden, einmal wiederholen. `KidsCourseLine.BookPuzzleId` hat
bewusst keinen FK; Buch löschen räumt beides ausdrücklich ab (`BookAdminService`, InMemory kaskadiert nicht).
Browser: `KidsProgressSync` (hinauf beim Anmelden, 800 ms nach jeder Änderung, bei sichtbarem Tab und zurückkehrendem
Netz). Abmelden LEERT den Stand im Browser (er liegt im Konto; auf einem geteilten Tablet soll das nächste Kind nicht
mit fremden Sternen anfangen), und ein Stand mit fremdem `owner` wird beim Anmelden verworfen statt übernommen.

**Kindertitel + „erst, wenn Deutsch"** (0.565.0, Wunsch 2026-09-27: „benenne alle Kurse für die Kinder um, blende sie
vorerst aus, bis sie deutsch sind"): `Book.KidsTitles` (JSON `{"de":"…","en":"…","hr":"…","hu":"…"}`, `Services/Kids/KidsTitles.cs`)
gibt einem Kinderkurs eigene Titel je KidHub-Sprache, OHNE den Buchnamen in RookHub anzufassen; gepflegt in der
Bücherverwaltung (Stift neben „Kinder", fragt die vier Sprachen nacheinander). Sichtbar ist ein Kinderkurs erst, wenn er
in jeder Sprache aus `Kids:RequiredCourseLanguages` vorliegt (Vorgabe `de`, leer = sofort) — auch der direkte Link ist
bis dahin 404; die Regel steht EINMAL in `KidsPuzzleService.VisibleKidsBooks`.

**Kinderkurse**: `Book.ForKids` setzt nur ein Admin (Bücherverwaltung, Spalte „Kinder"). Das Flag öffnet die Aufgaben
auf der Kinderseite bewusst OHNE `IsPublic` — wie die Pool-Flags eine absichtliche Freigabe; Kalkulationsbücher bleiben
trotz Flag draußen (ihre Zugfolge ist die Lösung, die der Kalkulations-Modus zurückhält).
