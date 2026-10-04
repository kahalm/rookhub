/**
 * Zuordnungs-Schlüssel anonym hochgeladener Partien (0.656.0, Wunsch 2026-10-04: „wenn ein Spieler über den anonymen Link
 * hochlädt, merk dir das im Browser, damit du ihm — wenn er sich irgendwann einloggt — zum Bearbeiten zuordnen kannst").
 * Der Server gibt je Speicherung über einen Teilen-Link einen Schlüssel aus; hier liegen sie, bis nach dem Anmelden
 * JA oder NEIN gesagt ist. Speicher kann fehlen oder werfen (privates Fenster) — dann gibt es eben keine Zuordnung.
 */
const KEY = 'lh-claim-keys';
const MAX = 200;

export function claimKeys(): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(KEY) ?? '[]') as unknown;
    return Array.isArray(v) ? v.filter((k): k is string => typeof k === 'string' && /^[0-9a-f]{32}$/.test(k)) : [];
  } catch {
    return [];
  }
}

export function rememberClaimKey(key: string | null | undefined): void {
  if (!key || !/^[0-9a-f]{32}$/.test(key)) return;
  try {
    const keys = claimKeys().filter(k => k !== key);
    keys.push(key);
    localStorage.setItem(KEY, JSON.stringify(keys.slice(-MAX)));
  } catch { /* ohne Speicher keine spätere Zuordnung */ }
}

export function clearClaimKeys(): void {
  try { localStorage.removeItem(KEY); } catch { /* nichts zu tun */ }
}
