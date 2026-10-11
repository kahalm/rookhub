# Benachrichtigungen / Glocke (auth) — generischer In-App-Strom

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Benachrichtigungen / Glocke (auth) — generischer In-App-Strom“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Eine zentrale Navbar-Glocke mit „!"-Indikator. `Notifications`-Tabelle (`UserId`, `Type`, `DataJson` = i18n-Parameter, `Link`, `SeenAt?`), Text wird im Frontend über `notifications.type.<type>` lokalisiert. `NotificationService.CreateAsync` wird per fire-and-forget von den Domänen-Services aufgerufen. Trigger-Typen: `chessable_import_completed`/`_failed` (ChessableImportService), `friend_request_received`/`friend_request_accepted` (FriendService), `challenge_received`/`challenge_resolved` (ChallengeService), `revenge_performed` (RevengeNotificationService, Dual-Write). Frontend: `InAppNotificationService` + Glocke in der Navbar (löste den Freunde-Badge ab); 60-s-Poll für den Zähler; Browser-`NotificationService` (Web-Notification-API) bleibt separat für späteres Push. Mail/Push sind Phase 2/3.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/notifications?take=20` | Letzte Benachrichtigungen (neueste zuerst) |
| GET | `/api/notifications/history?page=&pageSize=` | Vollständige History (paginiert, neueste zuerst) + Gesamtzahl — für die `/notifications`-Seite |
| GET | `/api/notifications/count` | Anzahl ungelesener (Glocken-Badge) |
| POST | `/api/notifications/seen` | Alle als gelesen markieren (beim Öffnen der Glocke) |

**Aufbewahrung** (Codereview 2026-09-29, A9-003): gelesene Benachrichtigungen verfallen nach 180 Tagen
(`NotificationRetentionScheduler`, täglich 05:00 UTC). Die Kontolöschung ersetzt den eigenen Namen in FREMDEN
Benachrichtigungen (z. B. Freundschaftsanfrage, Herausforderung) durch `deleted_<id>`.
