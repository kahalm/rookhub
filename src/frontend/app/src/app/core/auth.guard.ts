import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';

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
  // Nicht eingeloggt → zur Login-Seite mit Rücksprungziel + Flag, damit dort der Hinweis
  // „bitte einloggen/registrieren" erscheint (z. B. beim Klick auf einen Wochenpost-Link).
  return loginRedirect(router, state.url);
};
