import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { HandoffReady } from './handoff-ready';

/**
 * Gast → Anmeldemaske mit Rücksprungziel und dem Hinweis „bitte einloggen“ (`authRequired`). EINE Form für jeden
 * Guard, der Gäste abweist (UX-024): adminGuard und menuGuard leiteten auf das nackte `/login` bzw. erst aufs
 * Dashboard — ohne Hinweis, und nach der Anmeldung ging es aufs Dashboard statt zur angesteuerten Seite.
 */
export function loginRedirect(router: Router, returnUrl: string | undefined): UrlTree {
  return router.createUrlTree(['/login'], { queryParams: { returnUrl, authRequired: '1' } });
}

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isLoggedIn) {
    return true;
  }
  // Läuft gerade die Start-Übernahme (Einmal-Code oder geteilte Anmeldung, HandoffService.consumeIncoming), erst
  // deren Ende abwarten — höchstens HandoffReady.GuardWaitMs. Sonst landete ein Gast mit geteilter Anmeldung auf der
  // Maske, obwohl die Sitzung einen Moment später stand (Fund 2026-11-28, LeagueHub). Ohne Übernahme bleibt der
  // Guard synchron.
  // Nicht eingeloggt → zur Login-Seite mit Rücksprungziel + Flag, damit dort der Hinweis
  // „bitte einloggen/registrieren" erscheint (z. B. beim Klick auf einen Wochenpost-Link).
  return inject(HandoffReady).afterPending(() =>
    authService.isLoggedIn ? true : loginRedirect(router, state.url));
};
