import { ApplicationConfig, inject, provideAppInitializer, isDevMode, provideZoneChangeDetection, LOCALE_ID } from '@angular/core';
import { PermissionRefresher } from './core/permission-refresher.service';
import { provideRouter } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { routes } from './app.routes';
import { provideRhHttpClient } from './core/http-chain';
import { visitorInterceptor } from './core/visitor.interceptor';
import { resolveStartupLocale } from './core/locale.service';
import { registerFormatLocaleData } from './core/locale-data';
import { provideFullscreenSafeOverlays } from './shared/fullscreen/fullscreen-overlay.service';

// Locale-Daten für die übersetzten Sprachen registrieren (en ist eingebaut), damit
// DatePipe/DecimalPipe/PercentPipe entsprechend der gewählten Sprache formatieren
// statt immer en-US. Die effektive Start-Locale steckt im LOCALE_ID-Provider unten.
registerFormatLocaleData();

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Rechte live vom Server (eigene Rollen + Gruppenrollen), vor dem ersten Seitenaufbau und danach laufend (0.589.0).
    provideAppInitializer(() => inject(PermissionRefresher).start()),
    // Aktive Locale (en/de/hr) für Angular-Pipes; aus gespeicherter Sprache beim Start.
    { provide: LOCALE_ID, useFactory: resolveStartupLocale },
    provideRouter(routes),
    // Gemeinsame Kette (connectivity zuerst — sieht Erfolge/finale Fehler NACH den Retries); nur hier mit visitorInterceptor.
    provideRhHttpClient([visitorInterceptor]),
    provideAnimationsAsync(),
    // Dialoge/Menüs/Snackbars NICHT als Popover in der obersten Browser-Ebene — dort verschwanden
    // sie beim Wechsel der Vollbild-Arten (Begründung und Messung am Provider).
    provideFullscreenSafeOverlays(),
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
