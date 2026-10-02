/**
 * Rücksprungziel (`?returnUrl=`) absichern: nur app-interne Pfade kommen durch, alles andere wird
 * zum Fallback. `//host/…` und `scheme://…` wären offene Weiterleitungen auf fremde Seiten — genau
 * das, was ein Phishing-Link an eine Anmeldemaske hängen würde.
 *
 * <p>EINE Fassung für Anmeldemaske, Registrierung, den `guestGuard` und das Verlassen der Maske nach einer
 * übernommenen Anmeldung (`HandoffService`, F1-016); vorher hatten die beiden Komponenten je eine Kopie, und die
 * dritte Stelle hätte die dritte Kopie gebraucht.</p>
 */
export function sanitizeReturnUrl(url: string | null | undefined, fallback = '/dashboard'): string {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url.includes('://')) return fallback;
  return url;
}

/** Anmelde-, Registrier- und Passwortseiten: dorthin zurückzuspringen ergäbe nach der Anmeldung keinen Sinn. */
const AUTH_PAGES = ['/login', '/register', '/forgot-password', '/reset-password'];

/** Steht `url` auf einer dieser Seiten? Query und Fragment zählen nicht. */
export function isAuthPage(url: string): boolean {
  return AUTH_PAGES.includes(url.split('#')[0].split('?')[0]);
}

/**
 * Query der Anmelde-/Registrier-Links in der Kopfzeile (UX-020): `returnUrl` ist die Seite, auf der man gerade steht —
 * auf den Auth-Seiten selbst deren eigenes `returnUrl`. Bisher verlinkten RookHub und die Turnierseite nackt, LeagueHub
 * fest mit `/`: wer über einen geteilten Link auf der Maske stand und oben „Registrieren“ tippte, landete danach auf
 * dem Dashboard statt auf der geteilten Seite; auf `/t/42` führte „Anmelden“ in den Kalender.
 *
 * <p>`home` = Rückfall, wenn es nichts Brauchbares gibt; ohne `home` bleibt die Query leer und die Maske nimmt ihren
 * eigenen (`/dashboard` — den es auf der Turnierseite und in LeagueHub nicht gibt, die geben deshalb `/` mit).</p>
 */
export function authLinkQuery(currentUrl: string, home?: string): { returnUrl?: string } {
  const url = currentUrl.split('#')[0];
  const q = url.indexOf('?');
  const path = q < 0 ? url : url.slice(0, q);
  const candidate = AUTH_PAGES.includes(path)
    ? new URLSearchParams(q < 0 ? '' : url.slice(q + 1)).get('returnUrl')
    : url;
  const safe = sanitizeReturnUrl(candidate, '');
  const returnUrl = safe || home;
  return returnUrl ? { returnUrl } : {};
}
