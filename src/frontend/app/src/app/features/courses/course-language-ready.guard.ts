import { Injector, inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { from, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';

/**
 * Kursseiten erst öffnen, wenn die Sprachwahl des Kontos aus dem Profil da ist (`CourseLanguageService.ready`).
 * Sonst lüde eine Kursseite direkt nach dem Anmelden (oder nach dem Wechsel zwischen den Hubs) erst in der
 * Oberflächensprache — die lokale Wahl wird beim Abmelden geräumt und kommt erst mit dem Profil zurück.
 *
 * Lässt IMMER durch (Gast, Fehler und Zeitüberschreitung eingeschlossen) — er hält nur kurz an. Der Dienst kommt per
 * dynamischem Import, damit er nicht ins Start-Bündel wandert.
 */
export const courseLanguageReadyGuard: CanActivateFn = () => {
  const injector = inject(Injector);
  return from(import('./course-language.service')).pipe(
    switchMap(m => injector.get(m.CourseLanguageService).ready()),
    map(() => true),
    catchError(() => of(true)),
  );
};
