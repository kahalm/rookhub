import { ApplicationConfig, isDevMode, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { routes } from './app.routes';
import { provideRhHttpClient } from '@rh/core/http-chain';
import { resolveStartupLocale } from '@rh/core/locale.service';
import { registerFormatLocaleData } from '@rh/core/locale-data';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';

registerFormatLocaleData();

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
    provideRhHttpClient(),
    provideAnimationsAsync(),
    // Kein Impressum auf der Kinderseite, eigene Adresse fuer Datenschutzfragen (Wunsch 2026-09-27); die
    // Datenschutzerklaerung in der Kinder-Fassung, der Ruecklink fuehrt zur Startseite (Codereview F7-003). Konto
    // loeschen geht nur in RookHub — die Loeschseite verweist dorthin (Codereview UX-023).
    { provide: LEGAL_SITE, useValue: { contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/', accountHome: 'rookhub' } },
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
