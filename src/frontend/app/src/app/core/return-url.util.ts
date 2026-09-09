/**
 * Rücksprungziel (`?returnUrl=`) absichern: nur app-interne Pfade kommen durch, alles andere wird
 * zum Fallback. `//host/…` und `scheme://…` wären offene Weiterleitungen auf fremde Seiten — genau
 * das, was ein Phishing-Link an eine Anmeldemaske hängen würde.
 *
 * <p>EINE Fassung für Anmeldemaske, Registrierung und den `guestGuard`; vorher hatten die beiden
 * Komponenten je eine Kopie, und die dritte Stelle hätte die dritte Kopie gebraucht.</p>
 */
export function sanitizeReturnUrl(url: string | null | undefined, fallback = '/dashboard'): string {
  if (!url || !url.startsWith('/') || url.startsWith('//') || url.includes('://')) return fallback;
  return url;
}
