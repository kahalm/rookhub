import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isLoggedIn) {
    return true;
  }
  // Nicht eingeloggt → zur Login-Seite mit Rücksprungziel + Flag, damit dort der Hinweis
  // „bitte einloggen/registrieren" erscheint (z. B. beim Klick auf einen Wochenpost-Link).
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url, authRequired: '1' } });
};
