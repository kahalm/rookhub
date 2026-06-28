import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, catchError, of } from 'rxjs';
import { AuthService } from './auth.service';
import { CourseService } from '../features/courses/course.service';

/**
 * Zugriff auf die „Kurse": eingeloggt UND (Admin ODER mind. ein Kurs per Gruppe freigegeben).
 * Admins werden ohne Server-Roundtrip durchgelassen; sonst entscheidet /api/courses/access.
 */
export const courseAccessGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const courses = inject(CourseService);

  if (!auth.isLoggedIn) return router.createUrlTree(['/login']);
  if (auth.isAdmin) return true;

  // Offline: Zugriff nicht prüfbar → durchlassen, damit offline gespeicherte Kurse erreichbar
  // bleiben (die Lese-/Schreib-Endpoints sichern sich serverseitig ohnehin selbst ab).
  if (typeof navigator !== 'undefined' && !navigator.onLine) return true;

  return courses.checkAccess().pipe(
    map(res => res.hasAccess ? true : router.createUrlTree(['/dashboard'])),
    // Netzfehler → fail-open (kein Lockout bei Netzproblemen; konsistent mit menuGuard).
    catchError(() => of(true))
  );
};
