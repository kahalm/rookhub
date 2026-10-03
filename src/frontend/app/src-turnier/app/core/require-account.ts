import { Router } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { loginRedirect } from '@rh/core/auth.guard';

/**
 * Speichern braucht ein Konto — Lesen nicht (seit 0.643.0 ist die Turnierseite ohne Anmeldung voll benutzbar).
 *
 * <p>Angemeldet: `true`, weiter wie bisher. Sonst geht es zur Anmeldemaske, mit dem Hinweis, dass dafür ein Konto
 * nötig ist (`authRequired`), und mit Rücksprung auf genau diese Seite — wer im Kalender „Merken" tippt, steht nach
 * der Anmeldung wieder beim Turnier, nicht auf der Startseite. EINE Regel für alle Knöpfe, die etwas speichern
 * (Suchprofil, Merken, Favorit, Beobachten, Ausblenden, Melden …).</p>
 */
export function requireAccount(auth: AuthService, router: Router): boolean {
  if (auth.isLoggedIn) return true;
  void router.navigateByUrl(loginRedirect(router, router.url));
  return false;
}
