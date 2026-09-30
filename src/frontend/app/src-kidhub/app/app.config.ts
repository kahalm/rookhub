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
import { LEGAL_SITE } from '@rh/features/legal/legal-site';

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
    // Kein Impressum auf der Kinderseite, eigene Adresse fuer Datenschutzfragen (Wunsch 2026-09-27).
    { provide: LEGAL_SITE, useValue: { contactEmail: 'kidhub@oberschm.id', imprint: false } },
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
    // Nur im Prod-Build (`ngsw-config.kidhub.json`): App-Shell und Sprachdateien offline — ein Tablet
    // im Schachkurs hat nicht immer Netz. Die Aufgaben kommen weiter vom Server; Stufen und Kurse
    // (dataGroup „kids-catalog", freshness: Netz zuerst, nach 5 s oder ohne Netz der zuletzt geholte
    // Stand) lassen sich aber auch ohne Netz wieder oeffnen, wenn sie schon einmal geladen waren.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
