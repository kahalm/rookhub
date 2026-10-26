import { HttpErrorResponse } from '@angular/common/http';

/** Ursache eines Ladefehlers als i18n-Schlüssel (+ Platzhalter) — `null`, wenn sich nichts Genaueres sagen lässt. */
export interface LoadErrorCause {
  key: string;
  params?: Record<string, unknown>;
}

/**
 * Was ist schiefgegangen (UX-040/UX-074)? Gerät offline → „Du bist offline", Status 0 (Netz/Proxy/Server weg) →
 * „Server nicht erreichbar", sonst der HTTP-Status. Ohne Fehlerobjekt (`null`/`undefined`) keine Ursache.
 */
export function loadErrorCause(err: unknown, online: boolean = typeof navigator === 'undefined' || navigator.onLine !== false): LoadErrorCause | null {
  if (err === null || err === undefined) return null;
  if (!online) return { key: 'common.loadCause.offline' };
  if (err instanceof HttpErrorResponse) {
    if (err.status === 0) return { key: 'common.loadCause.unreachable' };
    return { key: 'common.loadCause.status', params: { status: err.status } };
  }
  return null;
}
