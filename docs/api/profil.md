# Profil (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Profil (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/profile` | Eigenes Profil |
| PUT | `/api/profile` | Profil bearbeiten. E-Mail (= Reset-Anker) wechselt nur mit `currentPassword` (auch Erst-Setzen/Entfernen; sonst 403), danach Hinweis-Mail an die bisherige Adresse. Eine neue E-Mail darf weder fremde E-Mail noch fremder Benutzername sein → 409 (nur bei echtem Wechsel, Bestandskollisionen blockieren das Speichern nicht; Codereview A1-010) |
| DELETE | `/api/profile/account` | Konto löschen (DSGVO: anonymisiert Identität+PII, behält Statistik) |
| GET | `/api/profile/player-search?lastName=&firstName=` | Spielersuche (ChessResults + FIDE) |
| POST | `/api/profile/discord/link` | Discord verknüpfen via bot-signiertem Token `{ token }` (400 ungültig/abgelaufen oder vor dem Freiwerden der Discord-ID für ein anderes Konto ausgestellt: Vermerk je ID in Tabelle `DiscordLinkReleases` bis frei + `Discord:LinkTokenMaxAgeDays` (Standard 30 = Bot-TTL), gesetzt bei Trennen/ID-Wechsel/Kontolöschung im selben Speichern, überlebt Neustarts — `DiscordLinkService`, Codereview A1-011; 409 Discord-ID schon vergeben) |
| DELETE | `/api/profile/discord` | Discord-Verknüpfung trennen — alte Bot-Links dieser ID löst danach nur noch dieses Konto ein (A1-011) |
| GET | `/api/profile/tokens` | Eigene API-Tokens (ohne Raw-Token) |
| POST | `/api/profile/tokens` | Neuen Token anlegen `{ name, expiresInDays?, scope? }` — `scope` ∈ `extension` (Vorgabe, nur `/api/extension/*`) / `engine` (nur `/api/external-engine/*`, der Engine-Provider, seit 0.537.0); Raw-Token nur einmalig im Response. Höchstens 20 Tokens je Konto — abgelaufene zählen nicht mit (Codereview S1-007) |
| DELETE | `/api/profile/tokens/{id}` | Token widerrufen |

**Nutzerverwaltung durch Admins** (Codereview 2026-09-29): `DELETE /api/admin/users/{id}` anonymisiert jetzt wie die
Selbstlöschung (`ProfileService.EraseUserAsync`, kein 409 durch Restrict-FKs mehr), `GET /api/admin/users` listet keine
gelöschten Konten (A9-004). **Admin-Recht setzen** (F5-013, `users.manage`): `PUT /api/admin/users/{id}/admin`
`{ isAdmin }` setzt einen Soll-Wert (idempotent; nur Admins, nie das eigene Konto; fehlender Wert → 400);
`POST …/toggle-admin` ist veraltet und entfällt in einer späteren Version. **Sperren statt löschen** (F5-011, `users.manage`): `POST /api/admin/users/{id}/lock`
`{ until: ISO-UTC | null }` (null = unbefristet, `AppUser.LockedIndefinitely`) bzw. `DELETE …/lock` → 200 `AdminUserDto`
(mit `lockedUntil`). Die Sperre setzt `AppUser.LockedUntil`, rotiert den Security-Stamp (laufende Sitzungen enden sofort)
und verwirft den Auth-Cache; Konto, Daten und API-Tokens bleiben. `AuthUserValidation` zählt eine laufende Sperre als
inaktiv, API-Token- und Engine-Token-Prüfung weisen gesperrte Besitzer ab, Impersonation eines gesperrten Kontos → 400.
Guards: nicht sich selbst (400), Ende in der Zukunft (400), Admin-Konten nur durch Admins (403).
**Reset-Link für Konten ohne E-Mail** (UX-002 / P-2, `users.manage`, 0.828.0): `POST /api/admin/users/{id}/password-reset-link`
→ 200 `{ link, expiresAt }` (ISO-UTC). `PasswordResetService.IssueAdminResetLinkAsync` legt wie „Passwort vergessen“ einen
einmaligen Token an (nur SHA-256 in `PasswordResetTokens`, offene ältere Tokens entwertet), schickt aber keine Mail — der
Admin gibt den Link über den Kanal der Person weiter, sie setzt selbst ein neues Passwort (`/api/auth/reset-password`,
entwertet wie jeder Reset Sitzungen und API-Tokens). Gültig 24 h (`AdminLinkTtl`; Mail-Link 1 h). Der Admin setzt und sieht
kein Passwort. 404 unbekannt/gelöscht; Admin-Konten nur durch Admins (403, sonst übernähme eine delegierte Rolle mit
`users.manage` ein Admin-Konto). Oberfläche: Schlüssel-Knopf bzw. ⋮-Eintrag „Reset-Link erzeugen“ in der Nutzerliste,
Dialog mit Kopieren-Knopf (`reset-link-dialog.component.ts`).
