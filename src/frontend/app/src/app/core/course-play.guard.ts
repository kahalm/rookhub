import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';
import { blockedNotice } from './blocked-notice';

/**
 * Zugriff auf das Durchspielen eines Kurses (`/courses/:bookId/...`).
 *
 * - **Anonym (nicht eingeloggt)**: durchlassen. Der Direkt-Link zu einem als öffentlich markierten
 *   Kurs muss ohne Login funktionieren; ob der Kurs wirklich öffentlich ist, erzwingt der Server
 *   (der `…/public`-Endpoint liefert 404 bei nicht-öffentlichen Kursen → die Komponente zeigt
 *   „nicht verfügbar"). Nicht-öffentliche Kurse sind anonym so nicht spielbar.
 * - **Eingeloggt**: wie {@link menuGuard}('courses') die admin-konfigurierte Menü-Sichtbarkeit
 *   respektieren; ausgeblendet → zurück aufs Dashboard, mit Hinweis (UX-026). Fail-open bei API-Fehlern.
 */
export const coursePlayGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  if (!auth.isLoggedIn) return true;   // anonym: Zugriff serverseitig (public) abgesichert

  const menu = inject(MenuService);
  const router = inject(Router);
  const blocked = blockedNotice();
  return menu.check('courses').pipe(
    map(ok => {
      if (ok) return true;
      blocked();
      return router.createUrlTree(['/dashboard']);
    }),
    catchError(() => of(true)),
  );
};
