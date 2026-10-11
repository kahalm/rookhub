# Book-Puzzles (offen + Admin)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Book-Puzzles (offen + Admin)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/book-puzzles/{id}` | AllowAnonymous | Puzzle by ID |
| GET | `/api/book-puzzles/{id}/next` | AllowAnonymous | Nächstes Puzzle im selben Buch (Loop am Ende) — **buch-gegatet** (`BookAccess`); **Kalkulationsbücher → 404** (Solver-Weg, liefert `Moves`) |
| GET | `/api/book-puzzles/{id}/random` | AllowAnonymous | Zufälliges Puzzle aus demselben Buch — **buch-gegatet** (`BookAccess`); **Kalkulationsbücher → 404** |
| POST | `/api/book-puzzles/{id}/attempt` | Auth | Lösungsversuch erfassen `{ solved, timeSeconds }` (Tagespuzzle) |
| POST | `/api/book-puzzles/{id}/flag-hints` | Auth | Tipps als „dumm/schlecht" markieren/aufheben `{ flagged }` — jeder eingeloggte User (Review-Flag `BookPuzzle.HintsFlagged`; 404 wenn Puzzle fehlt) |
| POST | `/api/book-puzzles/{id}/attempt/anonymous` | Anon (`anonymous-write`) | Anonymer Versuch (Session-ID, je Session/Puzzle dedupliziert). Nur für Puzzles aus anonym lesbaren Büchern (`BookAccess`) oder je zugeordnete Tagespuzzles, sonst **404**. Der Bot-Webhook anonymer Solves ist entprellt (`AnonymousSolveNotifyThrottle`: höchstens einer je Puzzle und 30 s, der Rest als EINE Nachmeldung am Fensterende — sie liest die Löser frisch) — sonst trieb ein Skript mit frischen Session-Ids Zähler, Aggregationen und Discord-Edits hoch (A2-003) |
| GET | `/api/book-puzzles/{id}/results?since=` | AllowAnonymous | Solver-Liste (je User; Discord-ID/-Name NUR für den signierten Bot und Eingeloggte, siehe „Discord-Verknüpfung in den Ergebnis-Endpunkten“) + Versuchs-/Lösungszähler + `anonymousSolvedCount`. Löser-Status: nur wer im **ersten** Versuch löste, gilt als Löser. Ohne Bot-Signatur nur für Puzzles aus lesbaren Büchern (`BookAccess`) oder je zugeordnete Tagespuzzles, sonst **404** (auch unbekannte Id) — sonst waren die Löser privater Buchlinien per Id-Aufzählung abrufbar (A2-002) |
| POST | `/api/book-puzzles/{id}/track` | AllowAnonymous (`anonymous-write`) | „Track solves" eines per Link geteilten Puzzles: erfasst den **Erstversuch** des Besuchers (eingeloggt via Token, sonst `{ solved, sessionId }`) in `SharedPuzzleAttempts` (Unique `(BookPuzzleId, IdentityKey)` → nur 1. Versuch zählt; `solved=false` = Fehlzug/Aufgeben/Reset) und liefert `{ solved, failed }` |
| GET | `/api/book-puzzles/{id}/track-counts` | AllowAnonymous | Aktuelle „Track solves"-Zähler `{ solved, failed }` |
| GET | `/api/book-puzzles/daily/leaderboard?month=yyyy-MM` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | Monats-Wertung des Tagespuzzles (für den Bot): je User Punkte (10 je Erstversuch-Lösung + Tages-Rang-Bonus 5/3/1), `solved`, `golds`; absteigend nach Punkten. Default = laufender UTC-Monat. Literal-Route **vor** `daily/{date}` |
| GET | `/api/book-puzzles/daily/hall-of-fame?top=5` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | All-time-Bestenlisten: meiste gelöste Dailies, meiste 🥇 (Tage als schnellster Erstversuch-Löser), schnellste je gelöste Lösung. `top` 1–25 |
| GET | `/api/book-puzzles/daily/{date}` | AllowAnonymous | Tagespuzzle für UTC-Datum (`yyyyMMdd` oder `today`); legt on-demand eine persistierte Zuordnung in `DailyPuzzles` an — aber NUR für heute/gestern (ältere Daten: gespeicherte Zuordnung oder 404; verhindert anonyme Write-Amplification per Datums-Enumeration) |
| GET | `/api/book-puzzles/by-line-id?lineId=xxx` | AllowAnonymous | Lookup für schach-bot |
| GET | `/api/book-puzzles/books` | AllowAnonymous | Buch-Liste mit Counts — nur **lesbare** Bücher (`BookAccess`) |
| POST | `/api/admin/book-puzzles/import` | Admin | Bulk-Import aus JSON |
| POST | `/api/admin/book-puzzles/daily/{date}/regenerate` | Admin | Tagespuzzle eines UTC-Datums neu generieren: Datum/Link bleibt, bisheriges Puzzle wird `Retired=true` gesetzt (nie wieder in Daily/Random/Blind), neues aus dem forDaily-Pool zugeordnet |
| POST | `/api/admin/book-puzzles/{id}/regenerate-hints` | Admin | Tipps eines einzelnen Buch-Puzzles synchron (neu) generieren (force). 400 ohne `Anthropic:TextApiKey`, 404 wenn Puzzle/keine Tipps; sonst die generierten Tipps |
| POST | `/api/admin/books/{bookId}/generate-hints?force=` | Admin | Tipps für ein ganzes Buch im Hintergrund erzeugen (eigene Tipp-Queue `HintTaskQueue`, ungedeckelt); `force` regeneriert auch vorhandene, sonst nur fehlende/veraltete. Antwort `{ queued }` |

**Discord-Verknüpfung in den Ergebnis-Endpunkten (S4-001, `Authorization/BotRequestSignature.cs`)**: die vier
anonym erreichbaren Ergebnis-Endpunkte (`/api/book-puzzles/{id}/results`, `daily/leaderboard`, `daily/hall-of-fame`,
`/api/weekly-posts/{id}/results`) liefern `discordId`/`discordUsername` nur noch an den **signierten Bot** und an
**eingeloggte** Nutzer (die Wochenpost-Bestenliste der App zeigt `discordUsername || name`); anonym stehen sie auf
`null` — sonst war die Zuordnung RookHub-Konto ↔ Discord-Konto samt Lösezeiten für jeden Unangemeldeten per
Aufzählung abrufbar (aus demselben Grund gibt es keine anonyme Profil-Sicht mehr, A2-008). Bot-Vertrag (== schach-bot `puzzle/rookhub.py` `_bot_auth_headers`):
`X-Bot-Timestamp` = Unix-Sekunden, `X-Bot-Signature: sha256=<hex(HMAC_SHA256(SchachBot:StatsSecret, "<ts>.<path>"))>`,
`path` = `Request.Path` OHNE Query (eine Signatur taugt nicht für eine andere Puzzle-ID), ±300 s — dieselbe
Prüfung wie `player-progress` (dort ist die Discord-ID das signierte Objekt). Header mitgeschickt, aber falsch
(oder Secret serverseitig leer) → **401**; der Bot holt dann einmal unsigniert nach und warnt. Kein Header →
gewöhnlicher Aufruf. Die DTOs sind je Abruf frisch gebaut (`RemoveDiscordLinks()` mutiert sie) — wer hier einmal
eine Antwort cacht, muss vorher kopieren.

**Zugriff auf die offenen Buch-Endpoints (`Services/Courses/BookAccess.cs`, seit 0.317.1)**: EINE Regel für
`{id}/next`, `{id}/random`, `/random?bookId=`, `/books` und (ohne Bot-Signatur, Tagespuzzles ausgenommen) `{id}/results`. Anonym sichtbar ist ein Buch nur, wenn ein Admin
es bewusst geöffnet hat — `Book.IsPublic` (öffentlicher Kurs) oder Mitgliedschaft in einem offenen Pool
(`ForDaily`/`ForRandom`/`ForBlind`); eingeloggte sehen zusätzlich eigene (`OwnerUserId`), per `CourseShare`
geteilte und über `BookGroupAccess` (inkl. „Everyone") freigegebene Bücher; Admins alles. Altbestand ohne
`Book`-Zeile bleibt ungegatet (dort kann keine Freigabe hängen). **Bewusst weiter offen**: `GET
/api/book-puzzles/{id}` (Einzel-Puzzle per Id) — Basis für Teilen-Links, Tagespuzzle, OG-Vorschau und den
Bot-Lookup per LineId. Bewusst NICHT identisch mit `CourseService.CanAccessAsync`: die Pool-Flags öffnen nur
Einzel-Puzzles/Zufallsziehungen, nicht den strukturierten Kurs (Kapitel/Fortschritt/Offline-Export). Folge für
den schach-bot: sein `/kurs`-Katalog (`/books` + `?bookId=`) enthält nur noch Pool-/öffentliche Bücher.
