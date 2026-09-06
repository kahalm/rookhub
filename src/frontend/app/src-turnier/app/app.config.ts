import { ApplicationConfig, isDevMode, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideServiceWorker } from '@angular/service-worker';
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
 * <p>Bewusst NICHT uebernommen: der `visitorInterceptor` — die anonyme Sitzungs-Id zaehlt geloeste
 * Puzzles, wofuer es hier kein Gegenstueck gibt.</p>
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
    // Service Worker (nur im Prod-Build aktiv, `ngsw-config.turnier.json`) — App-Shell, i18n und
    // die Kartenkacheln aus dem eigenen /tiles-Proxy, damit der Kalender auch unterwegs mit
    // wackeligem Netz noch etwas anzeigt.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
