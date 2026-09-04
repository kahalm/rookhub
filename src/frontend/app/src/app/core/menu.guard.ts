import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';

/**
 * Sperrt den direkten URL-Aufruf einer Seite, deren Menüeintrag der Admin für den
 * aktuellen Nutzer ausgeblendet hat. Nicht berechtigt → Redirect (Login wenn anonym,
 * sonst Dashboard). Bei unerreichbarem Menü-Endpoint wird NICHT ausgesperrt — das
 * „fail-open" steckt in `MenuService.check()` (Server-Endpoints sichern sich selbst ab).
 *
 * FALLE Umleitungsziel: `/dashboard` trägt selbst `menuGuard('dashboard')`, und „dashboard" ist ein
 * admin-konfigurierbarer Eintrag. Ist er für den Nutzer gesperrt, schickte der Guard ihn auf eine
 * Route, die derselbe Guard wieder ablehnt — die Navigation drehte endlos und der Nutzer landete auf
 * KEINER Seite (auch `''` und `**` zeigen aufs Dashboard). Deshalb wird das Ziel mitgeprüft und
 * ansonsten auf die guard-freie Hilfeseite ausgewichen.
 */
export function menuGuard(key: string): CanActivateFn {
  return () => {
    const menu = inject(MenuService);
    const auth = inject(AuthService);
    const router = inject(Router);
    return menu.check(key).pipe(
      map(ok => {
        if (ok) return true;
        if (!auth.isLoggedIn) return router.createUrlTree(['/login']);
        // Erste Route wählen, die der Nutzer WIRKLICH sehen darf. `check()` hat den Snapshot gerade
        // aufgefrischt, `isVisible` ist also aktuell und kostet keinen weiteren Request.
        const fallback = ['dashboard', 'help', 'install'].find(k => k !== key && menu.isVisible(k));
        // Bleibt gar nichts: `/login` ist die einzige guard-freie Seite und bietet einen Ausweg
        // (abmelden/neu anmelden) — besser als eine Endlos-Umleitung oder eine weiße Seite.
        return router.createUrlTree([fallback ? '/' + fallback : '/login']);
      }),
      catchError(() => of(true)),
    );
  };
}
