import { ApplicationConfig, inject, provideAppInitializer, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { PermissionRefresher } from '@rh/core/permission-refresher.service';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';

import { routes } from './app.routes';
import { renderAfterHttpInterceptor } from '@rh/core/render-after-http.interceptor';
import { connectivityInterceptor } from '@rh/core/connectivity.interceptor';
import { retryInterceptor } from '@rh/core/retry.interceptor';
import { authInterceptor } from '@rh/core/auth.interceptor';
import { LEGAL_SITE, LegalSite, defaultLegalSite } from '@rh/features/legal/legal-site';
import { CONFIRM_LABELS } from '@rh/shared/confirm-dialog/confirm-dialog.component';

registerLocaleData(localeDe);

/**
 * LeagueHub teilt sich mit RookHub die HTTP-Kette, die Anmeldung (Masken per `@rh/*`, dasselbe Konto)
 * und die Sprachdateien der Masken. Die Seite selbst ist deutsch (Tiroler Ligen). Kein Service Worker:
 * die Prognosen sollen immer frisch vom Server kommen.
 */
export const leaguehubConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Rechte live vom Server (eigene Rollen + Gruppenrollen), vor dem ersten Seitenaufbau und danach laufend (0.589.0).
    provideAppInitializer(() => inject(PermissionRefresher).start()),
    // Die Seite ist deutsch und registriert nur die deutschen Locale-Daten — eine aus RookHub geerbte Wahl
    // (hr, hu …) als LOCALE_ID ließe jede Datums-/Zahlen-Pipe mit „Missing locale data" scheitern.
    { provide: LOCALE_ID, useValue: 'de' },
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([connectivityInterceptor, retryInterceptor, authInterceptor, renderAfterHttpInterceptor])),
    provideAnimationsAsync(),
    // Rechtsseiten wie in RookHub (Impressum, Kontakt aus OPERATOR), die Datenschutzerklaerung dazu mit dem
    // LeagueHub-Abschnitt: Ligaspieler ohne Konto, Online-Konten, Prognosen, Teilen-Links (Codereview F7-006).
    // Konto loeschen geht nur in RookHub — die Loeschseite verweist dorthin (Codereview UX-023).
    { provide: LEGAL_SITE, useFactory: (): LegalSite => ({ ...defaultLegalSite(), kind: 'leaguehub', accountHome: 'rookhub' }) },
    // Rueckfragen (ConfirmService) mit deutschen Knoepfen — die Seite stellt keine Sprache ein, sonst kaeme „Cancel“.
    { provide: CONFIRM_LABELS, useValue: { confirm: 'OK', cancel: 'Abbrechen' } },
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
  ],
};
