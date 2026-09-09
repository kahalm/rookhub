import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Hat der SERVER das Token abgelehnt? Nur dann ist ein 401 ein Grund, die Sitzung wegzuwerfen.
 *
 * <p>Der JWT-Handler antwortet auf ein abgelaufenes, falsch signiertes oder per Security-Stamp
 * entwertetes Token mit <c>WWW-Authenticate: Bearer error="invalid_token"</c>. Ein Controller, der
 * selbst 401 zurückgibt — falsches aktuelles Passwort bei „Passwort ändern" oder „Konto löschen",
 * falsches Passwort bei einem erneuten Login, „keine geteilte Anmeldung" — setzt diesen Header
 * NICHT. Vorher galt jeder 401 als Rauswurf: am 2026-09-09 tippte ein Nutzer beim Passwortwechsel
 * das alte Passwort falsch und stand auf der Anmeldemaske, vier Fehlversuche später hielt er die
 * App für vergesslich. Der Header kommt durch beide nginx-Stufen (geprüft auf Dev und Prod).</p>
 */
export function isTokenRejection(err: unknown): boolean {
  if (!(err instanceof HttpErrorResponse) || err.status !== 401) return false;
  const challenge = err.headers?.get('WWW-Authenticate') ?? '';
  return /error="?invalid_token"?/i.test(challenge);
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  // Den Bearer NUR an unsere eigene API haengen. Alle App-Calls gehen relativ ueber
  // /api (nginx-Proxy); statische Assets/i18n brauchen ihn nicht. Verhindert zudem,
  // dass das JWT je an eine fremde Origin leakt, falls mal ein absoluter Drittanbieter-
  // URL ueber den HttpClient laeuft. Gleiche Gate-Logik wie der visitorInterceptor.
  const token = req.url.startsWith('/api') ? authService.token : null;

  const request = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(request).pipe(
    catchError(err => {
      if (isTokenRejection(err) && authService.isLoggedIn) {
        authService.logout();
      }
      return throwError(() => err);
    })
  );
};
