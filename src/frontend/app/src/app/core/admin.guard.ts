import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { loginRedirect } from './auth.guard';

export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn && auth.isAdmin) {
    return true;
  }
  // Gast (abgelaufene Sitzung, Lesezeichen /admin): direkt zur Anmeldung mit Rücksprung (UX-024). Bisher ging es
  // erst aufs Dashboard, dessen authGuard dann „/dashboard“ statt „/admin“ als Ziel mitgab.
  if (!auth.isLoggedIn) return loginRedirect(router, state?.url);
  return router.createUrlTree(['/dashboard']);
};
