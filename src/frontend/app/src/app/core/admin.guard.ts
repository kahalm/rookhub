import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { loginRedirect } from './auth.guard';
import { blockedNotice } from './blocked-notice';
import { HandoffReady } from './handoff-ready';

/**
 * Nur fürs Admin-Flag (Turnier-App `/admin`). Die Admin-Seite der Haupt-App öffnet sich je Recht — dort gilt
 * `adminAreaGuard` (features/admin/admin-area.guard.ts, F5-005).
 */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const notice = blockedNotice();          // im Injektionskontext holen — decideAdmin läuft ggf. erst nach dem Warten
  // Wie authGuard: eine laufende Start-Übernahme der Anmeldung erst abwarten (steht neben authGuard auf /admin —
  // entschiede dieser Guard sofort, schickte er den Gast trotzdem auf die Maske).
  if (!auth.isLoggedIn) return inject(HandoffReady).afterPending(() => decideAdmin(auth, router, notice, state?.url));
  return decideAdmin(auth, router, notice, state?.url);
};

function decideAdmin(auth: AuthService, router: Router, notice: () => void, url: string | undefined): boolean | UrlTree {
  if (auth.isLoggedIn && auth.isAdmin) {
    return true;
  }
  // Gast (abgelaufene Sitzung, Lesezeichen /admin): direkt zur Anmeldung mit Rücksprung (UX-024). Bisher ging es
  // erst aufs Dashboard, dessen authGuard dann „/dashboard“ statt „/admin“ als Ziel mitgab.
  if (!auth.isLoggedIn) return loginRedirect(router, url);
  // Angemeldet, aber kein Admin: Grund nennen statt stumm aufs Dashboard (UX-026).
  notice();
  return router.createUrlTree(['/dashboard']);
}
