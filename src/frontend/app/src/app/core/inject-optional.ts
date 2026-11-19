import { inject } from '@angular/core';

/**
 * `inject(…, { optional: true })`, das auch AUSSERHALB eines Injektors nicht wirft, sondern `null` liefert — für
 * Komponenten, die Specs mit `new` bauen (dort gibt es keinen Injektions-Kontext). Im Betrieb wie `inject`.
 */
export function injectOptional<T>(token: abstract new (...args: never[]) => T): T | null {
  try { return inject(token, { optional: true }); } catch { return null; }
}
