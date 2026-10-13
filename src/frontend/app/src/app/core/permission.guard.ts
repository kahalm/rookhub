import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { loginRedirect } from './auth.guard';
import { blockedNotice } from './blocked-notice';

/**
 * Generischer RBAC-Guard: lässt nur eingeloggte Nutzer durch, die die geforderte Permission besitzen
 * (Admin erfüllt jede Permission). Ergänzt/ersetzt `adminGuard` für permission-basierte Routen.
 * Nutzung in den Routen: `canActivate: [permissionGuard('users.manage')]`.
 *
 * Wie adminGuard/menuGuard (a1-rk-prep-ohne-recht-stumm): Gäste gehen zur Anmeldung mit Rücksprung (UX-024),
 * Angemeldete ohne Recht landen auf dem Dashboard MIT Hinweis „nicht freigeschaltet" (UX-026) — vorher stumm,
 * der Link wirkte kaputt.
 */
export function permissionGuard(permission: string): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (auth.isLoggedIn && auth.has(permission)) {
      return true;
    }
    if (!auth.isLoggedIn) return loginRedirect(router, state?.url);
    blockedNotice()();
    return router.createUrlTree(['/dashboard']);
  };
}
