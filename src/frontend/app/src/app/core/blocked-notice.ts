import { Injector, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { catchError, of, take } from 'rxjs';
import { SnackbarService } from './snackbar.service';

/**
 * Hinweis „Dieser Bereich ist für dein Konto nicht freigeschaltet“ für Guards, die einen ANGEMELDETEN Nutzer
 * umleiten (Codereview UX-026): ohne ihn stand man nach einem Link auf einen gesperrten Bereich stumm auf dem
 * Ausweichziel und hielt den Link für kaputt.
 *
 * Im Guard-Rumpf aufrufen (Injektionskontext); die Dienste werden erst beim Melden geholt — der Rückgabewert darf
 * also auch später in einem `map()` laufen, und ein Guard, der durchlässt, braucht keinen Snackbar.
 *
 * FALLE Kaltstart: `get()`, nicht `instant()`. Bei einem Deep-Link in einem neuen Tab läuft die erste Navigation
 * direkt nach `translate.use()` an, bevor `/i18n/<lang>.json` da ist — `instant()` lieferte dann den rohen Key
 * „app.blocked“ (adminGuard ist synchron, also sicher; menu-/coursePlayGuard im Wettlauf mit `/api/menu`).
 * `get()` wartet auf die laufende Sprachdatei. Scheitert deren Laden, bleibt `instant()` als Rückfall (Fallback-Sprache).
 */
export function blockedNotice(): () => void {
  const injector = inject(Injector);
  return () => {
    const translate = injector.get(TranslateService);
    translate.get('app.blocked').pipe(
      take(1),
      catchError(() => of(translate.instant('app.blocked'))),
    ).subscribe(msg => injector.get(SnackbarService).info(msg));
  };
}
