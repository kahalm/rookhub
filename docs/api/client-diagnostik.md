# Client-Diagnostik (offen)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Client-Diagnostik (offen)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| POST | `/api/client-log` | AllowAnonymous + RL | Client-seitiges Diagnose-Event `{ kind, detail?, url? }` (v. a. Browser-Engine-Crash/Hänger) — wird strukturiert mit Marker „ClientLog" geloggt (→ ES/Kibana), nichts in der DB. `heartbeat*`-Kinds auf Information, sonst Warning. `kind=heartbeat_bot` ist nur noch der Altpfad des Bots ohne Secret — der signierte Heartbeat läuft über `POST /api/bot/heartbeat`. Frontend: `ClientLogService` (gedrosselt), Engine-Services melden via `reportEngineEvent`-Hook |
