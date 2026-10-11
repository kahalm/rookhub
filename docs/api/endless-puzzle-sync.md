# Endless Puzzle Sync (auth + anon)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Endless Puzzle Sync (auth + anon)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/endless/progress` | Auth | Progress + Sessions laden (single call) |
| GET | `/api/endless/history?page=&pageSize=&archived=` | Auth | Paginierte Session-History (archived: bool-Filter) |
| GET | `/api/endless/sessions/{id}` | Auth | Lauf-Detail inkl. einzelner Puzzle-Versuche (History-Detailansicht) |
| PUT | `/api/endless/progress` | Auth | Config + Highscore + Active Game upsert |
| POST | `/api/endless/archive` | Auth | Sessions archivieren/unarchivieren |
| GET | `/api/endless/progress/anonymous?sessionId=` | Anon+RL | Anonymer Progress |
| PUT | `/api/endless/progress/anonymous` | Anon+RL | Anonymer Progress speichern. Eigener Deckel `activeGameState` ≤ 64 K Zeichen (`SaveAnonymousProgressDto.MaxActiveGameStateLength`; Konto: 1 Mio.). Gesamtdeckel `MaxAnonymousProgressRowsTotal` = 10 000 anonyme Zeilen: darüber keine NEUE Session-Id (400 `{ reason: "anonymousStorageFull" }` + Warnung `EndlessAnonymousStorageFull`), bestehende speichern weiter |
| POST | `/api/endless/sessions` | Auth | Session aufzeichnen. `puzzles` ≤ 500 (`RecordEndlessSessionDto.MaxPuzzles`), `lichessId` ≤ 20; je Puzzle ein `EndlessPuzzleAttempt`-Log + Summen-Event `EndlessSessionCompleted` |
| POST | `/api/endless/sessions/anonymous` | Anon+RL | Anonyme Session aufzeichnen (gleiche Deckel; geloggt NUR das Summen-Event, keine Einzel-Puzzle-Events — sonst ~1000 erfundene ES-Dokumente je Aufruf). Gesamtdeckel `MaxAnonymousSessionRowsTotal` = 50 000 anonyme Läufe (der Trim auf 50 gilt nur je Session-Id), darüber 400 `anonymousStorageFull` |
| POST | `/api/endless/sessions/bulk` | Auth | Bulk-Import (localStorage-Migration) |
| POST | `/api/endless/sessions/bulk/anonymous` | Anon+RL | Bulk-Import anonym (gleicher Gesamtdeckel; ein Paket, das nicht mehr passt, wird ganz abgewiesen) |
| POST | `/api/endless/claim-session` | Auth | Anonyme Daten auf User übertragen |
