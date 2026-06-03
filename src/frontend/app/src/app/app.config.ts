import { ApplicationConfig, isDevMode, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';
import { retryInterceptor } from './core/retry.interceptor';
import { visitorInterceptor } from './core/visitor.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptors([retryInterceptor, visitorInterceptor, authInterceptor])),
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
