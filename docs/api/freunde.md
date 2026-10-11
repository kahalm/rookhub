# Freunde (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Freunde (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/friends` | Freundesliste |
| GET | `/api/friends/requests` | Offene (eingehende) Anfragen |
| GET | `/api/friends/requests/sent` | Von mir gesendete, noch nicht angenommene (Pending) Anfragen — für „wartet auf Bestätigung" in der Freundesliste. Literal-Route vor `{...}` |
| POST | `/api/friends/request/{userId}` | Anfrage senden. Rate-Limit `user-social` (10/min je Konto, gemeinsam mit `POST /api/challenges`, Codereview N9-003). Die Glocke `friend_request_received` klingelt je (Absender, Empfänger) nicht erneut, solange die letzte ungelesen oder jünger als 24 h ist (`FriendService.RequestBellQuietPeriod`, Absender = `data.username`) — Senden–Zurückziehen–Senden legte sonst jedes Mal Glocke + Web-Push an; die Anfrage selbst bleibt erlaubt (PD-080) |
| POST | `/api/friends/accept/{friendshipId}` | Annehmen |
| POST | `/api/friends/decline/{friendshipId}` | Ablehnen |
| DELETE | `/api/friends/{friendshipId}` | Entfernen |
| GET | `/api/friends/search?q={query}` | User suchen (min. 2 Zeichen) |
| GET | `/api/friends/{userId}/stats` | Puzzle-Statistik eines Freundes (Vergleich „Du vs. Freund": Elo/Gelöst/Versuche/Genauigkeit/Serien + Themen-Aufschlüsselung). Nur zwischen akzeptierten Freunden (sonst 403); reused `PuzzleService.GetStatsAsync`/`GetBreakdownAsync` |
| GET | `/api/friends/{userId}/revenge` | „Revenge a Friend": Standard-Puzzles, an denen der Freund gescheitert ist und die er nie gelöst hat (`PuzzleService.GetUnsolvedFailuresAsync(targetId, viewerId)`, sortiert nach jüngstem Fehlversuch). Pro Puzzle `solvedByViewer` (hat der Aufrufer es schon gelöst → erledigte Revanche). Nur zwischen akzeptierten Freunden (sonst 403) |
