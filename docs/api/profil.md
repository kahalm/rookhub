# Profil (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Profil (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/profile` | Eigenes Profil |
| PUT | `/api/profile` | Profil bearbeiten. E-Mail (= Reset-Anker) wechselt nur mit `currentPassword` (auch Erst-Setzen/Entfernen; sonst 403), danach Hinweis-Mail an die bisherige Adresse |
| DELETE | `/api/profile/account` | Konto löschen (DSGVO: anonymisiert Identität+PII, behält Statistik) |
| GET | `/api/profile/player-search?lastName=&firstName=` | Spielersuche (ChessResults + FIDE) |
| POST | `/api/profile/discord/link` | Discord verknüpfen via bot-signiertem Token `{ token }` (400 ungültig/abgelaufen, 409 Discord-ID schon vergeben) |
| DELETE | `/api/profile/discord` | Discord-Verknüpfung trennen |
| GET | `/api/profile/tokens` | Eigene API-Tokens (ohne Raw-Token) |
| POST | `/api/profile/tokens` | Neuen Token anlegen `{ name, expiresInDays?, scope? }` — `scope` ∈ `extension` (Vorgabe, nur `/api/extension/*`) / `engine` (nur `/api/external-engine/*`, der Engine-Provider, seit 0.537.0); Raw-Token nur einmalig im Response. Höchstens 20 Tokens je Konto — abgelaufene zählen nicht mit (Codereview S1-007) |
| DELETE | `/api/profile/tokens/{id}` | Token widerrufen |

**Nutzerverwaltung durch Admins** (Codereview 2026-09-29): `DELETE /api/admin/users/{id}` anonymisiert jetzt wie die
Selbstlöschung (`ProfileService.EraseUserAsync`, kein 409 durch Restrict-FKs mehr), `GET /api/admin/users` listet keine
gelöschten Konten (A9-004). **Sperren statt löschen** (F5-011, `users.manage`): `POST /api/admin/users/{id}/lock`
`{ until: ISO-UTC | null }` (null = unbefristet, `AppUser.LockedIndefinitely`) bzw. `DELETE …/lock` → 200 `AdminUserDto`
(mit `lockedUntil`). Die Sperre setzt `AppUser.LockedUntil`, rotiert den Security-Stamp (laufende Sitzungen enden sofort)
und verwirft den Auth-Cache; Konto, Daten und API-Tokens bleiben. `AuthUserValidation` zählt eine laufende Sperre als
inaktiv, API-Token- und Engine-Token-Prüfung weisen gesperrte Besitzer ab, Impersonation eines gesperrten Kontos → 400.
Guards: nicht sich selbst (400), Ende in der Zukunft (400), Admin-Konten nur durch Admins (403).
