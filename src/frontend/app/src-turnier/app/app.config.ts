import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import localeHr from '@angular/common/locales/hr';

import { routes } from './app.routes';
import { authInterceptor } from '@rh/core/auth.interceptor';
import { connectivityInterceptor } from '@rh/core/connectivity.interceptor';
import { retryInterceptor } from '@rh/core/retry.interceptor';
import { resolveStartupLocale } from '@rh/core/locale.service';

registerLocaleData(localeDe);
registerLocaleData(localeHr);

/**
 * Die Turnierseite teilt sich Auth, Sprache und die HTTP-Kette mit RookHub (Import ueber `@rh/*`,
 * keine Kopie) — sie ist dieselbe Anwendung fuer dasselbe Konto, nur mit einem anderen Ausschnitt.
 *
 * <p>Bewusst NICHT uebernommen: der `visitorInterceptor` (anonyme Sitzungs-Id fuer Puzzle-Zaehlung,
 * hier ohne Gegenstueck) und der Service Worker — der kommt mit dem Ausrollen dazu, sonst zeigt
 * eine frische Seite waehrend der Entwicklung dauerhaft alte Staende.</p>
 */
export const turnierConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    { provide: LOCALE_ID, useFactory: resolveStartupLocale },
    provideRouter(routes),
    provideHttpClient(withInterceptors([connectivityInterceptor, retryInterceptor, authInterceptor])),
    provideAnimationsAsync(),
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
  ],
};
