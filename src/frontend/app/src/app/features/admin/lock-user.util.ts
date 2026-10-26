/**
 * Reine Logik des Sperrknopfs (Codereview P-4, Entscheidung 2026-10-25) — ohne Angular.
 * Die Sperre selbst liegt im Server (`POST/DELETE /api/admin/users/{id}/lock`, F5-011).
 */

/** Server-Wert einer unbefristeten Sperre: 9999-12-31 (`AppUser.LockedIndefinitely`). */
export function isIndefiniteLock(lockedUntil: string | null | undefined): boolean {
  return !!lockedUntil && new Date(lockedUntil).getUTCFullYear() >= 9999;
}

/** „Gesperrt bis einschließlich <Tag>“ (Datumsfeld, `YYYY-MM-DD`, Ortszeit) → Sperrende = Beginn des Folgetags in
 *  Ortszeit, als ISO-UTC. Ungültiges Datum → `null`. */
export function lockEndFromDate(day: string): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!m) return null;
  const end = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]) + 1, 0, 0, 0, 0);
  return Number.isNaN(end.getTime()) ? null : end.toISOString();
}

/** Heute in Ortszeit als `YYYY-MM-DD` — frühester wählbarer Tag (Sperre bis einschließlich heute). */
export function todayIso(now = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
}

/** Vorschlag im Datumsfeld: in sieben Tagen. */
export function defaultLockDay(now = new Date()): string {
  return todayIso(new Date(now.getFullYear(), now.getMonth(), now.getDate() + 7));
}
