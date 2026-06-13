import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

/**
 * Sperrt den direkten URL-Aufruf einer Seite, deren Menüeintrag der Admin für den
 * aktuellen Nutzer ausgeblendet hat. Nicht berechtigt → Redirect (Login wenn anonym,
 * sonst Dashboard). Bei API-Fehler wird NICHT ausgesperrt (Server-Endpoints sichern
 * sich ohnehin selbst ab) — fail-open verhindert Lockouts bei Netzproblemen.
 */
export function menuGuard(key: string): CanActivateFn {
  return () => {
    const menu = inject(MenuService);
    const auth = inject(AuthService);
    const router = inject(Router);
    return menu.check(key).pipe(
      map(ok => ok ? true : router.createUrlTree([auth.isLoggedIn ? '/dashboard' : '/login'])),
      catchError(() => of(true)),
    );
  };
}
