# Puzzle-Challenges (auth) — „schick dieses Puzzle an Freunde"

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Puzzle-Challenges (auth) — „schick dieses Puzzle an Freunde"“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Nach dem Lösen kann ein User ein konkretes Puzzle an **einen oder mehrere** Freunde schicken (Multi-Select im Solver-Menü, alle Modi außer Wochenpost). Die Challenge ist **polymorph**: `Source` (`Standard` = `Puzzles`-Tabelle, Standard/Endless; `Book` = `BookPuzzles`-Tabelle, Buch/Kurs/Tagespuzzle). Der Empfänger löst sie über den quellen-passenden Deep-Link (`/puzzles/:id?challengeId=…` bzw. `/puzzles/book/:id?challengeId=…`, meldet das Ergebnis nach dem Versuch via Resolve zurück), der Status (Pending→Solved/Failed) erscheint beim Absender. Logik in `ChallengeService` (nutzt `FriendService.AreFriendsAsync`); Existenz wird je Quelle geprüft (kein FK). Frontend: wiederverwendbare `ChallengeFriendsComponent`.

**Der gespeicherte Versuch schließt die Challenge** (Codereview N9-001): `POST /api/puzzles/{id}/attempt` bzw. `/api/book-puzzles/{id}/attempt` stellen nach dem Speichern jede offene Challenge an den Nutzer für dieses Puzzle (gleiche `Source`) auf Solved/Failed und benachrichtigen den Absender (`ChallengeService.ResolveFromAttemptAsync`; Fehler dort werden nur geloggt, `ChallengeAfterAttempt`, der Versuch bleibt 200). Vorher schickte der Client Versuch und Resolve gleichzeitig, der Buch-Solver Resolve sogar zuerst — die Prüfung lief vor dem gespeicherten Versuch und buchte echte Lösungen endgültig als „nicht gelöst“. Abgeschlossen wird ATOMAR (`UPDATE … WHERE Status = Pending`), damit Versuch und Resolve nicht beide benachrichtigen.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/challenges` | Batch-Challenge anlegen `{ toUserIds[], puzzleId, source }` — antwortet `{ sent, skipped[] }` (übersprungene Empfänger mit Grund `self`/`not_friends`/`duplicate`); 404 nur wenn das Puzzle in der zur `source` passenden Tabelle fehlt. Rate-Limit `user-social` (10/min je Konto, gemeinsam mit der Freundschaftsanfrage, sonst 429) |
| GET | `/api/challenges/incoming` | Offene eingehende Challenges (Posteingang) inkl. Absender + Puzzle-Rating |
| GET | `/api/challenges/outgoing` | Gesendete Challenges inkl. Ergebnis-Status + Lösezeit |
| GET | `/api/challenges/incoming/count` | Anzahl offener eingehender Challenges (Navbar-Badge) |
| GET | `/api/challenges/outgoing/pending-counts` | Pro Freund (Map `toUserId`→Count) die von mir geschickten, noch OFFENEN (Pending) Challenges — für die „Freund (n)"-Klammer im „An Freund schicken"-Menü. Nur Freunde mit n > 0. Literal-Route vor `{id}` |
| POST | `/api/challenges/{id}/resolve` | Ergebnis melden `{ solved, timeSpentSeconds }` — nur der Empfänger (403), 409 wenn schon aufgelöst (auch: schon vom Versuch geschlossen). Nötig nur noch für „nicht gelöst/aufgegeben“; ein „gelöst“ ohne gespeicherten gelösten Versuch lässt die Challenge offen (200, der Versuch schließt sie) statt sie als „nicht gelöst“ zu buchen |
