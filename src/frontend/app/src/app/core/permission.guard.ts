import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Generischer RBAC-Guard: lässt nur eingeloggte Nutzer durch, die die geforderte Permission besitzen
 * (Admin erfüllt jede Permission). Ergänzt/ersetzt `adminGuard` für permission-basierte Routen.
 * Nutzung in den Routen: `canActivate: [permissionGuard('users.manage')]`.
 */
export function permissionGuard(permission: string): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (auth.isLoggedIn && auth.has(permission)) {
      return true;
    }
    return router.createUrlTree(['/dashboard']);
  };
}
