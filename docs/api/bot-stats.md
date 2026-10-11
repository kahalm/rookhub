# Bot-Stats (Bot-intern, HMAC-signiert)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Bot-Stats (Bot-intern, HMAC-signiert)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/bot/player-progress/{discordId}` | AllowAnonymous + HMAC | Heutiger Trainingsziel-Fortschritt + Puzzle-Stats + jüngster Wochenpost-Status für eine verknüpfte Discord-ID. Signaturheader `X-Bot-Signature: sha256=…` mit `SchachBot:StatsSecret` (== Bot-`ROOKHUB_STATS_SECRET`); 401 bei falscher Signatur, 404 `{ reason: "not-linked" }` bei nicht verknüpfter Discord-ID, 503 `{ reason: "not-configured" }` wenn `SchachBot:StatsSecret` leer oder Platzhalter ist (vorher ebenfalls 404 — der Bot hielt dann JEDEN Abonnenten für unverknüpft; leeres Secret meldet der Start einmal als Warnung) |
| POST | `/api/bot/heartbeat` | AllowAnonymous + HMAC + RL | Signiertes Lebenszeichen des Schach-Bots (S5-008, `BotHeartbeatController`): `X-Bot-Timestamp` + `X-Bot-Signature: sha256=<hex(HMAC_SHA256(SchachBot:StatsSecret, "<ts>./api/bot/heartbeat"))>`, ±300 s. Gültig → 204 und die Zeile `Heartbeat: schach-bot healthy` (Kopf `HeartbeatService.LogTemplatePrefix`, Feld `HeartbeatService = schach-bot` → `labels.HeartbeatService`, danach zählt der log-watcher); ohne/falsche/alte Signatur → 401 und KEINE Zeile; Secret leer/Platzhalter → 503 `{ reason: "not-configured" }`. `kind=heartbeat_bot` über `/api/client-log` ist nur noch der anonyme Altpfad (fälschbar) |
