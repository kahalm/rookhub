import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { sanitizeReturnUrl } from './return-url.util';

/**
 * Gegenstück zum `authGuard` für Anmelde- und Registrierungsseite: wer schon angemeldet ist, hat
 * dort nichts zu tun und landet auf seinem Ziel (`returnUrl`, sonst der Startseite der App).
 *
 * <p><b>Warum:</b> Am 2026-09-09 kam der Login eines Nutzers von einem Browser, der noch ein
 * gültiges Token trug — die Maske war über Lesezeichen, Adressleisten-Vorschlag oder einen
 * wiederhergestellten Tab geöffnet worden und zeigte sich Angemeldeten wie Fremden. Der Nutzer
 * tippte sein Passwort umsonst und hielt die App für vergesslich („muss mich immer wieder
 * einloggen"). Ein Tippfehler dabei hätte ihn über den 401 sogar wirklich ausgeloggt.</p>
 *
 * <p>Fallback ist `/`, nicht `/dashboard`: der Guard läuft auch in der Turnierseite, deren
 * Startziel der Kalender ist — die Wurzel-Route jeder App weiß selbst, wohin.</p>
 *
 * <p><b>Ausnahme `?switch=1`:</b> Wer WILLENTLICH das Konto wechseln will (Test- und Support-Konten,
 * geteiltes Gerät), kommt damit an die Maske, ohne sich vorher abzumelden. Ohne diese Tür wäre der
 * einzige Weg „abmelden, dann anmelden" — und Abmelden räumt die geräte-lokalen Offline-Inhalte.
 * Sicherheitlich belanglos: die Zugangsdaten braucht man ohnehin.</p>
 */
export const guestGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const auth = inject(AuthService);
  if (!auth.isLoggedIn || route.queryParams['switch'] === '1') return true;
  return inject(Router).parseUrl(sanitizeReturnUrl(route.queryParams['returnUrl'], '/'));
};
