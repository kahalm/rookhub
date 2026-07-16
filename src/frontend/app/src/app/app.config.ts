import { ApplicationConfig, isDevMode, provideZoneChangeDetection, LOCALE_ID } from '@angular/core';
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
import { authInterceptor } from './core/auth.interceptor';
import { connectivityInterceptor } from './core/connectivity.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { visitorInterceptor } from './core/visitor.interceptor';
import { resolveStartupLocale } from './core/locale.service';

// Locale-Daten für die übersetzten Sprachen registrieren (en ist eingebaut), damit
// DatePipe/DecimalPipe/PercentPipe entsprechend der gewählten Sprache formatieren
// statt immer en-US. Die effektive Start-Locale steckt im LOCALE_ID-Provider unten.
registerLocaleData(localeDe);
registerLocaleData(localeHr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Aktive Locale (en/de/hr) für Angular-Pipes; aus gespeicherter Sprache beim Start.
    { provide: LOCALE_ID, useFactory: resolveStartupLocale },
    provideRouter(routes),
    // connectivity zuerst (äußerster) — sieht Erfolge/finale Fehler NACH den Retries.
    provideHttpClient(withInterceptors([connectivityInterceptor, retryInterceptor, visitorInterceptor, authInterceptor])),
    provideAnimationsAsync(),
    // i18n (ngx-translate): JSON aus public/i18n/*.json, Fallback Englisch.
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' })
    }),
    // Service Worker (nur im Prod-Build aktiv) — cacht App-Shell + Lazy-Chunks + i18n,
    // damit Puzzle-/Endless-Modus auch offline geladen & gestartet werden können.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000'
    })
  ]
};
