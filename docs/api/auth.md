# Auth (offen, kein JWT nötig)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Auth (offen, kein JWT nötig)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung `{ username, email?, password }` — E-Mail optional (`null` erlaubt, Unique-Index toleriert NULL-Duplikate) |
| POST | `/api/auth/login` | Login, gibt JWT zurück (gültig 30 Tage, mit `rememberMe` 90). Konto-Bremse: jeder Versuch wird vorab atomar gezählt (5 frei, dann 250 ms … 4 s Wartezeit); bei gebremstem Konto höchstens EINE Prüfung gleichzeitig, weitere sofort 429 (`Retry-After: 5`). Gesperrtes Konto (`AppUser.LockedUntil`) → 403 `{ message, lockedUntil }` (`null` = unbefristet) — erst NACH der Passwortprüfung, ein falsches Passwort bleibt 401 (kein Konto-Orakel) |
| POST | `/api/auth/forgot-password` | „Passwort vergessen" `{ email, site?, lang? }` — schickt (falls die Adresse zu einem aktiven Konto gehört) einen einmaligen Reset-Link (TTL 1 h) per Mail. Antwortet IMMER 200 (keine User-Enumeration). Versand via `PasswordResetService` + `IEmailSender` (SMTP/MailKit); ohne `Email:SmtpHost` wird die Mail nur geloggt. Link-Basis = `App:BaseUrl`; `site` (UX-031, FESTE Liste, nie eine URL) `kidhub`/`leaguehub`/`turnier` → `App:KidHubBaseUrl`/`App:LeagueHubBaseUrl`/`App:TurnierBaseUrl` (leer = `App:BaseUrl`), dazu Betreff, Absendername und Gruß der Seite („KidHub — …", „dasselbe Konto wie bei RookHub"); `lang` `de` oder ohne Angabe = Deutsch, jede andere = Englisch |
| POST | `/api/auth/reset-password` | Neues Passwort setzen `{ token, newPassword }` — 204 bei Erfolg, 400 bei ungültigem/abgelaufenem/verbrauchtem Token. Token ist einmalig (`UsedAt`) |
| POST | `/api/auth/rh-session` | Geteilte Anmeldung der Schwesterseite übernehmen — Nachweis ist das Cookie auf der gemeinsamen Elterndomäne (`SharedSessionService`, `Path=/api/auth/rh-session`). **204 = keine**, ohne Unterscheidung — bewusst kein 401: jeder anonyme App-Start fragt hier, und ein 401 zählte für die Überwachung als abgelehnter Anmeldeversuch (log-watcher `auth_bruteforce`, Fehlalarm 2026-09-15). Ein untaugliches Cookie wird dabei gelöscht |
| POST | `/api/auth/rh-session/end` | Geteilte Anmeldung beenden (Abmelden) — löscht das Cookie, immer 204 |

Die alten Pfade `POST /api/auth/session` und `session/end` (samt Merk-Cookie `<name>_moved` und Cookie-Umzug in `GET /api/auth/permissions`) sind seit 0.829.0 entfernt (N6-001). Jedes Schreiben/Löschen des Cookies löscht weiterhin ein altes mit `Path=/api/auth` (ging an JEDEN Host der Elterndomäne mit eigener `/api/auth`-Anmeldung), bis die letzten vor 0.624.0 ausgegebenen Cookies abgelaufen sind.

**Konten ohne E-Mail:** „Passwort vergessen“ erreicht sie nicht — ein Admin erzeugt mit `POST /api/admin/users/{id}/password-reset-link`
einen einmaligen Link (24 h), den er der Person weitergibt; eingelöst wird er wie ein Mail-Link über `reset-password`
(Details in `profil.md`).
