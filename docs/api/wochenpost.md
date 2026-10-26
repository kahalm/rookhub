# Wochenpost (öffentlich lesbar, durchspielbar mit Login, Admin verwaltet)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Wochenpost (öffentlich lesbar, durchspielbar mit Login, Admin verwaltet)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Bildet die wöchentlichen schach-bot-Posts auf RookHub ab: ein PGN + Termin (Datum + Uhrzeit; `WeeklyPost.ScheduledAt` ist UTC — das Frontend schickt ISO mit `Z` und zeigt Ortszeit, Termin-Helfer in `weekly.service.ts`, Codereview F5-009). PGN-Validierung via `RepertoireService.LooksLikePgn`. Puzzles werden on-the-fly aus dem PGN geparst (`PgnImportService.ParsePgn`) — Progress ist index-basiert.

**Per-User-Fortschritt**: idempotenter erster Versuch je `(WeeklyPostId, UserId, PuzzleIndex)`. „Erledigt" = **alle Puzzles gespielt** (gelöst egal). Aufgeben und Reset nach mindestens einem Zug zählen als ✗; seit P-1 meldet schon der erste Fehlzug das ✗ (der Löser meldet danach nichts mehr, der Server wertet ohnehin nur den ersten Versuch). Der Server glaubt einem „gelöst“ mit `wrongAttempts > 0` nicht (zählt als ✗, F2-010). Die gemeldete Spielweise (`mode`) ist die WIRKSAME des Versuchs: Stufe 0 (Figuren ziehbar, auch über ⋮ → Einstellungen umgestellt) oder das Auge nach dem ersten Zug → `easy`; ein Stufenwechsel im Wochenpost gilt nur fürs laufende Puzzle und schreibt die eigene Visualisierungs-Einstellung nicht (F2-010). Nach jedem **neuen** Versuch fire-and-forget Webhook (`SchachBotWebhookService.NotifyWeeklyAsync`, HMAC-signiert) an den Bot → Discord-Embed mit Live-Bestenliste.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/weekly-posts` | AllowAnonymous | Liste (ohne PGN), nach Termin absteigend |
| GET | `/api/weekly-posts/progress` | Authorize | Batch-Fortschritt für die Übersicht (`List<WeeklyPostProgressDto>`, nur Posts mit Versuchen) — literal-Route MUSS vor `{id}` stehen |
| GET | `/api/weekly-posts/{id}` | AllowAnonymous | Detail inkl. PGN |
| GET | `/api/weekly-posts/{id}/puzzles` | AllowAnonymous | Puzzle-Sequenz zum Durchspielen |
| POST | `/api/weekly-posts/{id}/attempt` | Authorize | Versuch erfassen `{ puzzleIndex, solved, timeSeconds }` (idempotent je Index) |
| GET | `/api/weekly-posts/{id}/progress` | Authorize | Eigener Fortschritt `{ total, playedCount, solvedCount, totalSeconds, playedIndices[], completed }` |
| GET | `/api/weekly-posts/{id}/results` | AllowAnonymous (Discord-Felder nur Bot-signiert/eingeloggt) | Bestenliste (alle Spieler mit ≥1 Versuch): `playedCount`, `solvedCount`, `totalSeconds`, `completed`; Sortierung erledigt→gelöst→Name |
| POST | `/api/admin/weekly-posts` | Admin | Upload (multipart: file + scheduledAt + optional title) |
| PUT | `/api/admin/weekly-posts/{id}` | Admin | Termin/Titel ändern |
| DELETE | `/api/admin/weekly-posts/{id}` | Admin | Löschen |
| POST | `/api/admin/weekly-posts/from-chapter` | `weeklyposts.manage` | Wochenpost aus EINEM Kapitel eines Buchs `{ bookId, chapterIndex, scheduledAt, title?, description? }` — Puzzles live aus den BookPuzzles |

**Kapitel-Quelle** (A2-017): `from-chapter` verlangt Kurszugriff auf das Buch (`CourseAccess.CanAccessAsync`, sonst 403) und lehnt
Kalkulationskurse ab (400) — `GET …/{id}/puzzles` ist anonym und gäbe sonst Lösungen preis. Wird ein Quellbuch nachträglich zum
Kalkulationskurs, liefert `…/puzzles` die Puzzles ohne Lösung (`BookPuzzleService.WithholdSolution`, wie `GET /api/book-puzzles/{id}`).
