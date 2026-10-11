# Gruppen (Admin + auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Gruppen (Admin + auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/groups` | Admin | Alle Gruppen inkl. MemberCount |
| POST | `/api/admin/groups` | Admin | Gruppe anlegen (name, description) |
| PUT | `/api/admin/groups/{id}` | Admin | Gruppe umbenennen / Beschreibung |
| DELETE | `/api/admin/groups/{id}` | Admin | Gruppe + Mitgliedschaften löschen |
| GET | `/api/admin/groups/{id}/members` | Admin | Mitglieder einer Gruppe |
| POST | `/api/admin/groups/{id}/members/{userId}` | Admin | User zur Gruppe hinzufügen (idempotent) |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | User aus Gruppe entfernen |
| GET | `/api/admin/groups/{id}/training-goal` | Admin | Trainingsziel-Vorlage der Gruppe (Source "none" wenn keine) |
| PUT | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage setzen/aktualisieren (PuzzleMinutes/BookMinutes 0–600, PlayGames 0–200 Partien/Woche, WeeklyDaysTarget 0–7) |
| DELETE | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage entfernen |
| GET | `/api/my-groups` | Auth | Gruppen-Namen des eingeloggten Users (gruppenabhängige Anzeige) |
| GET/PUT | `/api/admin/groups/{id}/roles` | `roles.manage` | Rollen einer Gruppe `{ roleIds }` (0.589.0) — jedes Mitglied hat sie, live. Die admin-Rolle geht nie an eine Gruppe (still verworfen), „Everyone" nimmt keine (400) |

Admin-Oberfläche „Gruppen“: Mitglieder hinzufügen über ein Suchfeld mit Liste statt Dropdown (0.591.0, Wunsch „die Dropdown ist dafür nicht geeignet“) — ohne Suche die neuesten Konten zuerst (aus den vorab geladenen 500), ab zwei Zeichen die Server-Suche (`GET /api/admin/users?search=`, auch jenseits der 500), je Zeile ein „Hinzufügen“-Knopf; Mitglieder stehen nicht in der Liste.

**Rechte gelten LIVE, nicht ab dem nächsten Anmelden** (0.589.0, Wunsch 2026-09-28: „das ist doch scheiße — sollte
immer wieder neue Infos holen"). Vorher standen die Rechte als `perm`-Claims im Token (bis 30 Tage gültig): eine neue
Rolle wirkte erst nach dem nächsten Anmelden, eine entzogene galt so lange weiter. Jetzt:
* **Server**: `Services/Accounts/PermissionResolver.cs` löst eigene Rollen (`UserRoles`) + Rollen der Gruppen (`GroupRoles` über
  `UserGroups`) auf, 60 s gespeichert. `PermissionAuthorizationHandler` (`[HasPermission]`) und `LeagueClubController`
  (Verwalter) fragen ihn statt des Claims; die Admin-Rolle des Tokens erfüllt weiter alles. **Jede Änderung** an Rollen,
  Rollen-Rechten, Gruppenrollen, Mitgliedschaften und dem Admin-Flag ruft `PermissionResolver.InvalidateAll()` — wer eine
  neue Schreibstelle dafür baut, ruft es mit, sonst gilt die Änderung bis zu einer Minute später. **Der Admin-ENTZUG**
  (`AdminService.ToggleAdminAsync`) rotiert deshalb zusätzlich den Security-Stamp (die Token-Rolle gälte sonst bis zu
  90 Tage weiter; alle Sitzungen des Kontos enden sofort) und nimmt die System-Rolle „admin" weg, die der `RoleSeeder`
  beim Start nur ANLEGT; das Hochstufen hängt sie an.
* **Oberfläche**: `GET /api/auth/permissions` → `{ isAdmin, permissions }` (live). `AuthService.has` liest diesen Stand
  (Signal, sonst die Claims des Tokens als Rückfall), `AuthService.isAdminLive` ebenso für die reine Admin-Frage (0.727.5;
  `isAdmin` ist die Token-Rolle); `core/permission-refresher.service.ts` holt ihn vor dem ersten
  Seitenaufbau (`provideAppInitializer` in RookHub, Turnierseite, LeagueHub, höchstens 3 s gewartet), bei jeder
  An-/Abmeldung, beim Zurückkehren in den Tab (höchstens alle 2 min, seit Codereview F1-019 — vorher ≥ 30 s) und alle
  2 min, solange der Tab sichtbar ist.
* Die `perm`-Claims beim Anmelden bleiben (jetzt inkl. Gruppenrollen) — nur noch Startwert für ältere Oberflächen.
