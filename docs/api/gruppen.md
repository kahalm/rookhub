# Gruppen (Admin + auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Gruppen (Admin + auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/admin/groups` | Admin | Alle Gruppen inkl. MemberCount (direkt + geerbt, jedes Konto einmal) und `includedGroupIds` |
| POST | `/api/admin/groups` | Admin | Gruppe anlegen (name, description) |
| PUT | `/api/admin/groups/{id}` | Admin | Gruppe umbenennen / Beschreibung |
| DELETE | `/api/admin/groups/{id}` | Admin | Gruppe + Mitgliedschaften löschen |
| GET | `/api/admin/groups/{id}/members` | Admin | Mitglieder einer Gruppe, auch geerbte: `{ userId, username, direct, via[] }` (`direct=false` = nur über die Gruppen in `via`) |
| POST | `/api/admin/groups/{id}/includes/{includedId}` | Admin | Gruppe `includedId` in Gruppe `id` aufnehmen (idempotent); 400 bei „Everyone“ oder Kreis (`code: group_inclusion_cycle`) |
| DELETE | `/api/admin/groups/{id}/includes/{includedId}` | Admin | Aufnahme wieder lösen (404, wenn keine) |
| POST | `/api/admin/groups/{id}/members/{userId}` | Admin | User zur Gruppe hinzufügen (idempotent) |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | User aus Gruppe entfernen |
| GET | `/api/admin/groups/{id}/training-goal` | Admin | Trainingsziel-Vorlage der Gruppe (Source "none" wenn keine) |
| PUT | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage setzen/aktualisieren (PuzzleMinutes/BookMinutes 0–600, PlayGames 0–200 Partien/Woche, WeeklyDaysTarget 0–7) |
| DELETE | `/api/admin/groups/{id}/training-goal` | Admin | Vorlage entfernen |
| GET/PUT | `/api/admin/groups/{id}/roles` | `roles.manage` | Rollen einer Gruppe `{ roleIds }` (0.589.0) — jedes Mitglied hat sie, live. Die admin-Rolle geht nie an eine Gruppe (still verworfen), „Everyone" nimmt keine (400) |

Admin-Oberfläche „Gruppen“: Mitglieder hinzufügen über ein Suchfeld mit Liste statt Dropdown (0.591.0, Wunsch „die Dropdown ist dafür nicht geeignet“) — ohne Suche die neuesten Konten zuerst (aus den vorab geladenen 500), ab zwei Zeichen die Server-Suche (`GET /api/admin/users?search=`, auch jenseits der 500), je Zeile ein „Hinzufügen“-Knopf; Mitglieder stehen nicht in der Liste.

**Rechte gelten LIVE, nicht ab dem nächsten Anmelden** (0.589.0, Wunsch 2026-09-28: „das ist doch scheiße — sollte
immer wieder neue Infos holen"). Vorher standen die Rechte als `perm`-Claims im Token (bis 30 Tage gültig): eine neue
Rolle wirkte erst nach dem nächsten Anmelden, eine entzogene galt so lange weiter. Jetzt:
* **Server**: `Services/Accounts/PermissionResolver.cs` löst eigene Rollen (`UserRoles`) + Rollen der Gruppen (`GroupRoles` über
  `GroupMembership`, auch geerbte — siehe „Gruppe in Gruppe“) auf, 60 s gespeichert. `PermissionAuthorizationHandler` (`[HasPermission]`) und `LeagueClubController`
  (Verwalter) fragen ihn statt des Claims; die Admin-Rolle des Tokens erfüllt weiter alles. **Jede Änderung** an Rollen,
  Rollen-Rechten, Gruppenrollen, Mitgliedschaften und dem Admin-Flag ruft `PermissionResolver.InvalidateAll()` — wer eine
  neue Schreibstelle dafür baut, ruft es mit, sonst gilt die Änderung bis zu einer Minute später. **Der Admin-ENTZUG**
  (`AdminService.SetAdminAsync`, alt `ToggleAdminAsync`) rotiert deshalb zusätzlich den Security-Stamp (die Token-Rolle gälte sonst bis zu
  90 Tage weiter; alle Sitzungen des Kontos enden sofort) und nimmt die System-Rolle „admin" weg, die der `RoleSeeder`
  beim Start nur ANLEGT; das Hochstufen hängt sie an.
* **Oberfläche**: `GET /api/auth/permissions` → `{ isAdmin, permissions }` (live). `AuthService.has` liest diesen Stand
  (Signal, sonst die Claims des Tokens als Rückfall), `AuthService.isAdminLive` ebenso für die reine Admin-Frage (0.727.5;
  `isAdmin` ist die Token-Rolle); `core/permission-refresher.service.ts` holt ihn vor dem ersten
  Seitenaufbau (`provideAppInitializer` in RookHub, Turnierseite, LeagueHub, höchstens 3 s gewartet), bei jeder
  An-/Abmeldung, beim Zurückkehren in den Tab (höchstens alle 2 min, seit Codereview F1-019 — vorher ≥ 30 s) und alle
  2 min, solange der Tab sichtbar ist.
* Die `perm`-Claims beim Anmelden bleiben (jetzt inkl. Gruppenrollen) — nur noch Startwert für ältere Oberflächen.
* **Rechte statt Admin-Flag** (Codereview F5-005): Glocken „an die Admins“ gehen über
  `PermissionResolver.UserIdsWithPermissionAsync` an alle mit dem Recht (`messages.admin` / `users.manage` /
  `chessable.admin`); Katalog-Besitz = `catalog.manage`; unveröffentlichte Wochenposts sieht `weeklyposts.manage`;
  Push-Bereich „admin“ = `users.manage`. Teil-Freischaltungen im Action-Rumpf über `BaseApiController.HasPermissionAsync`.
* **Admin-Bereich je Recht** (Codereview F5-005, Frontend-Rest): `/admin` öffnet sich für jeden, der mindestens einen
  Admin-Tab sehen darf. Zuordnung Tab → Recht als Daten in `features/admin/admin-tabs.ts` (`ADMIN_TAB_PERMISSIONS`,
  gleich den `[HasPermission]` der Controller): Nutzer `users.manage`, Bücher `books.manage`, Tagespuzzle `daily.manage`,
  Puzzles `puzzles.manage`, Gruppen `groups.manage`, Menü `menu.manage`, Nachrichten `messages.admin`, Kurs-Download
  `chessable.admin`, CI `ci.view`, Rollen `roles.manage`, Uploads `messages.admin` (wie `AdminLeagueUploadController`),
  Hintergrundjobs nur Admin. Admin-Flag setzen und Admin-Konten anfassen (Einsteigen, Reset-Link, Löschen) bietet die
  Nutzerliste nur Admins an (Server: 403). Die Dashboard-Kachel „Chessable-Queue" hängt an `chessable.admin`. Wer einen
  Controller auf ein anderes Recht umstellt, zieht die Tabelle mit.

## Gruppe in Gruppe (2026-10-14)

Wunsch: „bei Gruppen auch Gruppen Gruppen zuweisen — dann sind alle User der ersten Gruppe auch in der 2.“ Nimmt man
Gruppe A in Gruppe B auf (`GroupInclusions`: `GroupId` = B, `IncludedGroupId` = A), gilt jedes Mitglied von A auch als
Mitglied von B — mit B's Rollen, Kurs-/Buch-/E-Book-Freigaben, LeagueHub-Verein, Menüpunkten, Katalog-Freigaben und
Trainingsziel-Vorlage. Das wirkt über beliebig viele Stufen (A ⊂ B ⊂ C ⇒ A-Mitglieder sind in C).

* **Eine Stelle für die Mitgliedschaft**: `Services/Accounts/GroupMembership.cs`. `GroupIdsOf(db, userId)` (direkt +
  geerbt, als Abfrage — in anderen Abfragen als VARIABLE einsetzen, EF macht daraus ein `IN (…)`) und `UserIdsOf(db,
  groupId)`. Wer neu „ist das Konto in Gruppe X?“ fragt, nimmt das — NIE direkt `UserGroups`, sonst fehlen die geerbten.
* **Hülle statt Rekursion**: `GroupClosures (AncestorId, DescendantId)` hält die transitive Hülle ohne Selbst-Zeilen;
  `GroupMembership.RebuildClosureAsync` rechnet sie nach jeder Änderung an Aufnahmen und nach dem Löschen einer Gruppe
  komplett neu (die Tabellen sind klein) und ruft `PermissionResolver.InvalidateAll()`. Nie von Hand schreiben.
* **Ausgeschlossen**: „Everyone“ (hat keine Mitgliedszeilen) weder als umfassende noch als enthaltene Gruppe; Kreise
  (A in B, B in A — auch über Zwischenstufen) → 400 `group_inclusion_cycle`.
* **Löschen** einer Gruppe entfernt ihre Aufnahmen in beide Richtungen — die Kette über sie reißt ab.
* **Oberfläche** (Admin → Gruppen): Tabelle zeigt „enthält: …“; im Mitglieder-Kasten stehen geerbte Mitglieder gedämpft
  mit „· Gruppe“ und ohne ✕ (entfernen nur in ihrer eigenen Gruppe), darunter „Enthaltene Gruppen“ (Chips mit ✕,
  Auswahl „Gruppe aufnehmen“) und „Diese Gruppe steckt selbst in: …“.
* `AdminUserDto.Groups` (Benutzerliste) zeigt weiter nur die DIREKTEN Gruppen.
