/**
 * Reihenfolge der Tabs in `admin.component.html` (mat-tab-group) als EINZIGE Quelle für die
 * Deep-Link-Indizes (`/admin?tab=<key>`). Wird ein Tab im HTML umsortiert/hinzugefügt, NUR diese
 * Liste mitziehen — sonst öffnet ein Deep-Link den falschen Tab. Der Guard-Test in
 * `admin-tabs.spec.ts` hält die Liste mit der HTML-Reihenfolge konsistent.
 * Der Server verlinkt einzelne Keys in Glocken (`Services/Accounts/AdminLinks.cs`, Spiegeltest `AdminLinksTests`) — Keys nicht umbenennen, ohne dort mitzuziehen.
 */
export const ADMIN_TAB_KEYS = [
  'users',     // 0
  'books',     // 1
  'daily',     // 2
  'puzzles',   // 3
  'groups',    // 4
  'menu',      // 5
  'messages',  // 6
  'courseDl',  // 7
  'ci',        // 8
  'roles',     // 9
  'uploads',   // 10 — LeagueHub-Stapel-Uploads (0.651.0)
  'jobs',      // 11 — Hintergrundjobs pausieren
] as const;

export type AdminTabKey = typeof ADMIN_TAB_KEYS[number];

/**
 * Welches Recht ein Tab braucht (F5-005) — dasselbe, das der Backend-Controller des Tabs verlangt; `null` = nur Admins
 * (Controller mit `[Authorize(Roles = "Admin")]`). Admins dürfen jeden Tab (`AuthService.has` gibt Admins jedes Recht).
 * Auffällig: „Uploads" hängt am Server an `messages.admin` (dieselben Leute bekommen die Upload-Meldung), nicht an
 * `league.manage`. Ändert der Server das Recht eines Controllers, HIER mitziehen.
 */
export const ADMIN_TAB_PERMISSIONS: Readonly<Record<AdminTabKey, string | null>> = {
  users: 'users.manage',        // AdminController (users/*, config)
  books: 'books.manage',        // AdminController (books/*)
  daily: 'daily.manage',        // BookPuzzleController (book-puzzles/daily/*/regenerate)
  puzzles: 'puzzles.manage',    // AdminController (puzzles/*)
  groups: 'groups.manage',      // GroupController
  menu: 'menu.manage',          // MenuAdminController
  messages: 'messages.admin',   // AdminMessageController
  courseDl: 'chessable.admin',  // ChessableAdminController
  ci: 'ci.view',                // CiController
  roles: 'roles.manage',        // RolesAdminController
  uploads: 'messages.admin',    // AdminLeagueUploadController
  jobs: null,                   // BackgroundJobsController — nur Admins
};

/** Was die Zugriffsprüfung vom {@link AuthService} braucht (in Tests leicht nachzubauen). */
export interface AdminTabAccess {
  has(permission: string): boolean;
  readonly isAdminLive: boolean;
}

/** Darf der Nutzer diesen Tab sehen? */
export function canSeeAdminTab(auth: AdminTabAccess, key: AdminTabKey): boolean {
  const perm = ADMIN_TAB_PERMISSIONS[key];
  return perm === null ? auth.isAdminLive : auth.has(perm);
}

/** Die Tabs, die der Nutzer sieht — in der kanonischen Reihenfolge von {@link ADMIN_TAB_KEYS}. */
export function visibleAdminTabs(auth: AdminTabAccess): AdminTabKey[] {
  return ADMIN_TAB_KEYS.filter(k => canSeeAdminTab(auth, k));
}

/** Öffnet sich der Admin-Bereich für den Nutzer (mindestens ein Tab)? EINZIGE Regel für Guard und Menüeintrag. */
export function canOpenAdmin(auth: AdminTabAccess): boolean {
  return ADMIN_TAB_KEYS.some(k => canSeeAdminTab(auth, k));
}

/**
 * 0-basierter Tab-Index für einen Key, oder −1 wenn unbekannt. Ersetzt hartcodierte Indizes. `tabs` = die tatsächlich
 * gerenderten Tabs (die Admin-Seite rendert nur die erlaubten, F5-005) — ohne Angabe die volle Liste.
 */
export function adminTabIndex(key: string | null | undefined, tabs: readonly AdminTabKey[] = ADMIN_TAB_KEYS): number {
  return key ? tabs.indexOf(key as AdminTabKey) : -1;
}
