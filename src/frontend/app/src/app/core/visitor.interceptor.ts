import { HttpInterceptorFn } from '@angular/common/http';

const SESSION_KEY = 'rookhub_puzzle_session';

/** Stabile Besucher-/Anon-Session-Id aus dem localStorage (gleiche Id wie die anonymen
 *  Puzzle-/Endless-Calls). Wird angelegt, falls noch keine existiert. */
export function getOrCreateVisitorId(): string | null {
  try {
    let id = localStorage.getItem(SESSION_KEY);
    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(SESSION_KEY, id);
    }
    return id;
  } catch {
    return null;
  }
}

/**
 * Setzt `X-Visitor-Id` (stabile Anon-Session-Id) auf jedem /api-Request. Damit kann das
 * Backend „Unique Visits" auch fuer anonyme Besucher loggen (VisitorId = Username wenn
 * eingeloggt, sonst diese Session-Id). Nur fuer /api — statische Assets/i18n bleiben unberuehrt.
 */
export const visitorInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api')) return next(req);
  const id = getOrCreateVisitorId();
  return next(id ? req.clone({ setHeaders: { 'X-Visitor-Id': id } }) : req);
};
