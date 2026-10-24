import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { loginRedirect } from '../../core/auth.guard';
import { blockedNotice } from '../../core/blocked-notice';
import { adminTabIndex, AdminTabKey, visibleAdminTabs } from './admin-tabs';

/**
 * Guard der Admin-Seite `/admin` (F5-005): öffnet sie je RECHT, nicht nur fürs Admin-Flag. Durch kommt, wer mindestens
 * einen Admin-Tab sehen darf ({@link visibleAdminTabs}, Zuordnung in `ADMIN_TAB_PERMISSIONS`); Admins dürfen alles.
 * Ein Deep-Link `?tab=<key>` auf einen Tab ohne Recht landet MIT Hinweis auf dem ersten erlaubten Tab, statt stumm
 * abzuprallen — so funktionieren die Glockenlinks (`/admin?tab=messages`, `/admin?tab=uploads`) für Rechteinhaber.
 *
 * Wie adminGuard/permissionGuard: Gäste zur Anmeldung mit Rücksprung (UX-024), Angemeldete ganz ohne Admin-Recht
 * aufs Dashboard mit Hinweis (UX-026). `adminGuard` (nur Admin-Flag) bleibt für die Turnier-App.
 */
export const adminAreaGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isLoggedIn) return loginRedirect(router, state?.url);
  const tabs = visibleAdminTabs(auth);
  if (tabs.length === 0) {
    blockedNotice()();
    return router.createUrlTree(['/dashboard']);
  }
  const key = route?.queryParamMap?.get('tab');
  // Bekannter Tab ohne Recht → erster erlaubter Tab. Unbekannte Keys ignoriert die Seite ohnehin (erster Tab).
  if (key && adminTabIndex(key) >= 0 && !tabs.includes(key as AdminTabKey)) {
    blockedNotice()();
    return router.createUrlTree(['/admin'], { queryParams: { tab: tabs[0] } });
  }
  return true;
};
