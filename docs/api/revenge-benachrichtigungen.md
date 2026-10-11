# Revenge-Benachrichtigungen (auth) — Ziel-User über Revanche informieren

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Revenge-Benachrichtigungen (auth) — Ziel-User über Revanche informieren“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Geht ein Freund (Avenger) eines gescheiterten Puzzles eines Users (Target) im Revenge-Modus an, wird der Target informiert (gelöst ODER gescheitert). Frontend: `/puzzles/:id?revengeUserId=…` meldet das Ergebnis nach dem Versuch (fire-and-forget). `RevengeNotificationService` legt nur an, wenn die beiden befreundet sind UND der Target an dem Puzzle tatsächlich gescheitert ist. Robuster (N9-001): `revengeUserId` im Rumpf von `POST /api/puzzles/{id}/attempt` — dann legt der Server die Glocke mit dem gespeicherten Versuch selbst an (gleiche Prüfungen, Dedupe je Avenger/Target/Puzzle); ein `/revenge/result`, das parallel zum Versuch ankommt, findet noch keinen Versuch und legt nichts an.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/revenge/result` | Revanche-Ergebnis melden `{ targetUserId, puzzleId, solved }` — legt Benachrichtigung an (still ignoriert, wenn keine Freunde / Target nie gescheitert) |
| GET | `/api/revenge/notifications` | Eigene Revanche-Benachrichtigungen (neueste zuerst) |
| GET | `/api/revenge/notifications/count` | Anzahl ungelesener (Navbar-Badge, kombiniert mit Challenges) |
| POST | `/api/revenge/notifications/seen` | Alle als gelesen markieren |
