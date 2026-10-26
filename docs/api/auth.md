# Auth (offen, kein JWT nötig)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Auth (offen, kein JWT nötig)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| POST | `/api/auth/register` | Registrierung `{ username, email?, password }` — E-Mail optional (`null` erlaubt, Unique-Index toleriert NULL-Duplikate). Benutzername und E-Mail werden über Kreuz geprüft (Name mit `@` ≠ fremde E-Mail, E-Mail ≠ fremder Name) → 409 — der Login gibt dem Benutzernamen Vorrang, ein Konto namens „opfer@…“ fing sonst die E-Mail-Anmeldung ab (Codereview A1-010) |
| POST | `/api/auth/login` | Login, gibt JWT zurück (gültig 30 Tage, mit `rememberMe` 90). Konto-Bremse: jeder Versuch wird vorab atomar gezählt (5 frei, dann 250 ms … 4 s Wartezeit); bei gebremstem Konto höchstens EINE Prüfung gleichzeitig, weitere sofort 429 (`Retry-After: 5`). Gesperrtes Konto (`AppUser.LockedUntil`) → 403 `{ message, lockedUntil }` (`null` = unbefristet) — erst NACH der Passwortprüfung, ein falsches Passwort bleibt 401 (kein Konto-Orakel) |
| POST | `/api/auth/forgot-password` | „Passwort vergessen" `{ email, site?, lang? }` — schickt (falls die Adresse zu einem aktiven Konto gehört) einen einmaligen Reset-Link (TTL 1 h) per Mail. Antwortet IMMER 200 (keine User-Enumeration). Suche, Token und Versand laufen als Hintergrundarbeit (`IWebhookTaskQueue`, eigener DI-Scope), die Antwort wartet nicht darauf — die Antwortzeit verrät kein Konto (Codereview A1-009). Versand via `PasswordResetService` + `IEmailSender` (SMTP/MailKit); ohne `Email:SmtpHost` wird die Mail nur geloggt. Link-Basis = `App:BaseUrl`; `site` (UX-031, FESTE Liste, nie eine URL) `kidhub`/`leaguehub`/`turnier` → `App:KidHubBaseUrl`/`App:LeagueHubBaseUrl`/`App:TurnierBaseUrl` (leer = `App:BaseUrl`), dazu Betreff, Absendername und Gruß der Seite („KidHub — …", „dasselbe Konto wie bei RookHub"); `lang` `de` oder ohne Angabe = Deutsch, jede andere = Englisch |
| POST | `/api/auth/reset-password` | Neues Passwort setzen `{ token, newPassword }` — 204 bei Erfolg, 400 bei ungültigem/abgelaufenem/verbrauchtem Token. Token ist einmalig (`UsedAt`) |
| POST | `/api/auth/rh-session` | Geteilte Anmeldung der Schwesterseite übernehmen — Nachweis ist das Cookie auf der gemeinsamen Elterndomäne (`SharedSessionService`, `Path=/api/auth/rh-session`). **204 = keine**, ohne Unterscheidung — bewusst kein 401: jeder anonyme App-Start fragt hier, und ein 401 zählte für die Überwachung als abgelehnter Anmeldeversuch (log-watcher `auth_bruteforce`, Fehlalarm 2026-09-15). Ein untaugliches Cookie wird dabei gelöscht |
| POST | `/api/auth/rh-session/end` | Geteilte Anmeldung beenden (Abmelden) — löscht das Cookie, immer 204 |

**Absolute Obergrenze der Sitzung** (Codereview A1-007/P-14, entschieden 25.10.2026): jede Anmeldung trägt ihren
Zeitpunkt als `auth_time`-Claim (Unix-Sekunden) im JWT UND im Cookie `rh_session`. Login, Registrierung und Passwortwechsel
setzen ihn auf jetzt; das Einlösen des Cookies (`rh-session`), die Übergabe (`handoff` → `rh-session/handoff`) und das
dabei neu geschriebene Cookie ERBEN ihn. Kein Token und kein Cookie läuft über `auth_time` + `Auth:SessionMaxDays`
(Vorgabe 90) hinaus, ein älteres Cookie wird nicht mehr eingelöst (204, Cookie gelöscht) — danach läuft das JWT ab, die
API antwortet 401 `invalid_token`, und die Oberfläche meldet ab (`SessionStore`-Ablauf bzw. `authInterceptor`). Vorher
schrieb jedes Einlösen ein frisches 30-Tage-Cookie samt neuem Token: eine abgegriffene Sitzung verlängerte sich endlos.
Der Passwortwechsel (`PUT /api/auth/change-password`) schreibt das Cookie wie der Login neu (A1-020) — der rotierte
Stempel entwertet das alte, sonst bräuchten die Nebenoberflächen danach eine neue Anmeldung.
Altbestand ohne Claim gilt beim nächsten Einlösen als „jetzt angemeldet" (kein Massen-Abmelden beim Deploy). Code:
`Services/Accounts/SessionLifetime.cs`; Impersonations-Tokens (2 h) tragen ihren eigenen Zeitpunkt. Eine gesenkte
Konfiguration wirkt auf bereits ausgegebene JWTs erst mit deren Ablauf.

**API-Tokens (PATs, `rkh_…`) laufen ab** (Codereview S1-007/P-14, entschieden 25.10.2026): `POST /api/profile/tokens`
befristet jeden neuen Token auf höchstens `Auth:PatMaxDays` (Vorgabe 365) — `expiresInDays` fehlt/`null` oder ist länger →
genau die Höchstdauer (`ApiTokenService.CreateAsync`); die Oberfläche bietet „Nie" nicht mehr an. Abgelaufene Tokens lehnt
die Prüfung ab (`ApiTokenService.FindValidAsync`, 401) und sie zählen nicht ins Limit von 20. **Altbestand** ohne Ablauf
(25.10.: 30 von 35 auf Prod) bekommt KEINE Migration, sondern den Datenlauf `LibraryImport pat-cap [--dry-run] [--days n]`
(`ExpiresAt = heute + 365` für Zeilen ohne Ablauf, wiederholbar) — auf Prod nur auf Zuruf, nach einem Dump, zuerst `--dry-run`.
RepCheck verbindet sich nach Ablauf neu (Ein-Klick-Verbindung).

Die alten Pfade `POST /api/auth/session` und `session/end` (samt Merk-Cookie `<name>_moved` und Cookie-Umzug in `GET /api/auth/permissions`) sind seit 0.828.1 entfernt (N6-001). Jedes Schreiben/Löschen des Cookies löscht weiterhin ein altes mit `Path=/api/auth` (ging an JEDEN Host der Elterndomäne mit eigener `/api/auth`-Anmeldung), bis die letzten vor 0.624.0 ausgegebenen Cookies abgelaufen sind.

Der alte Tauschpfad des Übergabe-Codes `POST /api/auth/handoff/exchange` ist seit 0.828.3 entfernt (F1-008); eingelöst wird nur noch über `POST /api/auth/rh-session/handoff`.
**Einmal-Codes atomar** (A1-008): Übergabe-Code und Reset-Link werden per bedingtem `UPDATE … WHERE UsedAt IS NULL` entwertet
(`OneTimeCodes.TryConsumeAsync`, InMemory-Rückfall wie `ChessableFetchLane.TryClaimAsync`) — von zwei gleichzeitigen Einlösungen
gelingt nur eine; der Reset entwertet VOR dem Setzen des Passworts. MariaDB-Beleg: `OneTimeCodeSqlTests`.

**Konten ohne E-Mail:** „Passwort vergessen“ erreicht sie nicht — ein Admin erzeugt mit `POST /api/admin/users/{id}/password-reset-link`
einen einmaligen Link (24 h), den er der Person weitergibt; eingelöst wird er wie ein Mail-Link über `reset-password`
(Details in `profil.md`).

## Frontend (alle fünf Oberflächen)

`core/auth.service.ts` ist die EINE Fassade (`AuthService`: `login`/`register`/`logout`, `currentUser$`, `has`,
`isAdminLive`, `impersonate`/`stopImpersonation`, `adoptSession`, `flushSessionEnd` …), dahinter seit der Zerlegung Z34:

| Datei | Aufgabe |
|---|---|
| `core/session-store.ts` | Token im Speicher (`rookhub_user`), Ablauf (`validUser`), `persist` bei vollem Speicher, lokales Aufräumen beim Nutzerwechsel, Merker `rookhub_session_end_pending` |
| `core/permission.service.ts` | Rechte: `perm`-Claims des Tokens, Live-Stand aus `GET /api/auth/permissions` (`PermissionRefresher`), `has`/`isAdminLive` |
| `core/impersonation.service.ts` | „Als Nutzer einsteigen": Admin-Sicherung `rookhub_admin_user`, Ein- und Ausstieg (ohne Sicherung meldet die Fassade ab) |
| `core/login-follow-up.service.ts` | Nachlauf nach dem Anmelden: anonyme Sitzungen (Punktepartie, Puzzles, Endless, E-Books), Discord-Link, Präferenzen — per dynamischem `import()` (Zyklus-Brecher, hält die Feature-Dienste aus dem Start-Bündel) |
| `core/auth-response.ts` | `AuthResponse`, JWT-Payload/Ablauf |

Aufrufer nutzen nur `AuthService`; die Teile sind `providedIn: 'root'` und ohne Rückbezug auf die Fassade.
