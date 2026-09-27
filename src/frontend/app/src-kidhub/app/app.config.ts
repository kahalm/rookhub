import { ApplicationConfig, isDevMode, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import localeHr from '@angular/common/locales/hr';
import localeHu from '@angular/common/locales/hu';

import { routes } from './app.routes';
import { renderAfterHttpInterceptor } from '@rh/core/render-after-http.interceptor';
import { connectivityInterceptor } from '@rh/core/connectivity.interceptor';
import { retryInterceptor } from '@rh/core/retry.interceptor';
import { authInterceptor } from '@rh/core/auth.interceptor';
import { resolveStartupLocale } from '@rh/core/locale.service';

registerLocaleData(localeDe);
registerLocaleData(localeHr);
registerLocaleData(localeHu);

/**
 * Die Kinderseite teilt sich mit RookHub die HTTP-Kette, die Sprachdateien, das Brett und die
 * Anmeldung (Import ueber `@rh/*`). Spielen geht weiter OHNE Konto — der Fortschritt bleibt auf dem
 * Geraet; Anmelden/Registrieren bietet die Startseite an, es ist dasselbe Konto wie in RookHub.
 * Bewusst NICHT dabei: die anonyme Sitzungs-Id (`visitorInterceptor`).
 */
export const kidhubConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    { provide: LOCALE_ID, useFactory: resolveStartupLocale },
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideHttpClient(withInterceptors([connectivityInterceptor, retryInterceptor, authInterceptor, renderAfterHttpInterceptor])),
    provideAnimationsAsync(),
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
    // Nur im Prod-Build (`ngsw-config.kidhub.json`): App-Shell und Sprachdateien offline — ein Tablet
    // im Schachkurs hat nicht immer Netz. Die Aufgaben selbst kommen weiter vom Server.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
