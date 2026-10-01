import { Injector, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { SnackbarService } from './snackbar.service';

/**
 * Hinweis „Dieser Bereich ist für dein Konto nicht freigeschaltet“ für Guards, die einen ANGEMELDETEN Nutzer
 * umleiten (Codereview UX-026): ohne ihn stand man nach einem Link auf einen gesperrten Bereich stumm auf dem
 * Ausweichziel und hielt den Link für kaputt.
 *
 * Im Guard-Rumpf aufrufen (Injektionskontext); die Dienste werden erst beim Melden geholt — der Rückgabewert darf
 * also auch später in einem `map()` laufen, und ein Guard, der durchlässt, braucht keinen Snackbar.
 */
export function blockedNotice(): () => void {
  const injector = inject(Injector);
  return () => {
    injector.get(SnackbarService).info(injector.get(TranslateService).instant('app.blocked'));
  };
}
