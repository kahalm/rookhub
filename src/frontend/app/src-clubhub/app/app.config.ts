import { ApplicationConfig, inject, provideAppInitializer, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { PermissionRefresher } from '@rh/core/permission-refresher.service';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';

import { routes } from './app.routes';
import { provideRhHttpClient } from '@rh/core/http-chain';
import { LEGAL_SITE, LegalSite, defaultLegalSite } from '@rh/features/legal/legal-site';
import { CONFIRM_LABELS } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { AUTH_INTRO } from '@rh/features/auth/auth-intro';

registerLocaleData(localeDe);

/**
 * ClubHub teilt sich mit RookHub die HTTP-Kette, die Anmeldung (Masken per `@rh/*`, dasselbe Konto) und die Sprachdateien
 * der Masken. Die Seite selbst ist deutsch. Kein Service Worker: die Kartei soll nie aus einem Zwischenspeicher kommen —
 * weder ein veralteter Stand noch Daten von Kindern, die auf einem geteilten Gerät liegen bleiben.
 */
export const clubhubConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Rechte live vom Server (eigene Rollen + Gruppenrollen), vor dem ersten Seitenaufbau und danach laufend.
    provideAppInitializer(() => inject(PermissionRefresher).start()),
    // Nur die deutschen Locale-Daten sind registriert — eine aus RookHub geerbte Wahl (hr, hu …) als LOCALE_ID ließe
    // jede Datums-/Zahlen-Pipe mit „Missing locale data" scheitern.
    { provide: LOCALE_ID, useValue: 'de' },
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    provideRhHttpClient(),
    provideAnimationsAsync(),
    // Rechtsseiten wie in RookHub; Konto loeschen geht aber nur dort — die Loeschseite verweist dorthin (Codereview UX-023).
    { provide: LEGAL_SITE, useFactory: (): LegalSite => ({ ...defaultLegalSite(), accountHome: 'rookhub' }) },
    // Anmeldemaske: für wen die Kartei ist und dass der Verein freischaltet — statt RookHubs „Ein Konto ist kostenlos“,
    // nach dem ein neues Konto hier nur „Nicht freigeschaltet“ sähe. Den Code des Trainers (/verknuepfen) löst dagegen
    // jedes Konto ein; auch das sagt der Satz (Codereview UX-027).
    { provide: AUTH_INTRO, useValue: { login: 'clubhub.authIntro.login' } },
    // Rueckfragen (ConfirmService) mit deutschen Knoepfen — die Seite stellt keine Sprache ein, sonst kaeme „Cancel“.
    { provide: CONFIRM_LABELS, useValue: { confirm: 'OK', cancel: 'Abbrechen' } },
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
  ],
};
