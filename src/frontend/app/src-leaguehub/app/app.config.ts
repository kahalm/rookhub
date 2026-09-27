import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
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

registerLocaleData(localeDe);

/**
 * LeagueHub teilt sich mit RookHub die HTTP-Kette, die Anmeldung (Masken per `@rh/*`, dasselbe Konto)
 * und die Sprachdateien der Masken. Die Seite selbst ist deutsch (Tiroler Ligen). Kein Service Worker:
 * die Prognosen sollen immer frisch vom Server kommen.
 */
export const leaguehubConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Die Seite ist deutsch und registriert nur die deutschen Locale-Daten — eine aus RookHub geerbte Wahl
    // (hr, hu …) als LOCALE_ID ließe jede Datums-/Zahlen-Pipe mit „Missing locale data" scheitern.
    { provide: LOCALE_ID, useValue: 'de' },
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([connectivityInterceptor, retryInterceptor, authInterceptor, renderAfterHttpInterceptor])),
    provideAnimationsAsync(),
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
  ],
};
