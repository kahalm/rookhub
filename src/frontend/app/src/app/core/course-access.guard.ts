import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Zugriff auf die „Kurse"-Seite: jeder eingeloggte Nutzer. Die Seite ist auch dann erreichbar,
 * wenn (noch) kein Kurs sichtbar ist — dort kann jeder Nutzer ein eigenes PGN als persönlichen
 * Kurs hochladen. Die einzelnen Lese-/Schreib-Endpoints sichern den Zugriff je Buch ohnehin
 * serverseitig ab (kein Zugriff → 404). Die Navbar zeigt „Kurse" weiterhin nur, wenn tatsächlich
 * mindestens ein Kurs zugänglich ist (content-gated) — der Einstieg für den ersten Upload läuft
 * über die Dashboard-Kachel.
 */
export const courseAccessGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isLoggedIn ? true : router.createUrlTree(['/login']);
};
