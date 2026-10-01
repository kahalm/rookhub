import { HttpInterceptorFn } from '@angular/common/http';
import { ANON_PUZZLE_SESSION_KEY, getOrCreateAnonSessionId } from './anon-session';

/** Stabile Besucher-/Anon-Session-Id aus dem localStorage (gleiche Id wie die anonymen
 *  Puzzle-/Endless-Calls). Wird angelegt, falls noch keine existiert — über denselben Erzeuger wie
 *  die Puzzle-Versuche ({@link getOrCreateAnonSessionId}): ohne `crypto.randomUUID` (HTTP-Dev-Stack)
 *  und bei gesperrtem Speicher gibt es trotzdem eine Kennung, und es ist dieselbe. */
export function getOrCreateVisitorId(): string {
  return getOrCreateAnonSessionId(ANON_PUZZLE_SESSION_KEY);
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
