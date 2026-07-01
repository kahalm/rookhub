/**
 * Reihenfolge der Tabs in `admin.component.html` (mat-tab-group) als EINZIGE Quelle für die
 * Deep-Link-Indizes (`/admin?tab=<key>`). Wird ein Tab im HTML umsortiert/hinzugefügt, NUR diese
 * Liste mitziehen — sonst öffnet ein Deep-Link den falschen Tab. Der Guard-Test in
 * `admin-tabs.spec.ts` hält die Liste mit der HTML-Reihenfolge konsistent.
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
] as const;

export type AdminTabKey = typeof ADMIN_TAB_KEYS[number];

/** 0-basierter Tab-Index für einen Key, oder −1 wenn unbekannt. Ersetzt hartcodierte Indizes. */
export function adminTabIndex(key: string | null | undefined): number {
  return key ? ADMIN_TAB_KEYS.indexOf(key as AdminTabKey) : -1;
}
