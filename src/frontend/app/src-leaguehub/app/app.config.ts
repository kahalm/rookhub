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
import { provideFullscreenSafeOverlays } from '@rh/shared/fullscreen/fullscreen-overlay.service';
import { ClubContextService, leagueClubInterceptor } from './core/club-context.service';

registerLocaleData(localeDe);

/**
 * LeagueHub teilt sich mit RookHub die HTTP-Kette, die Anmeldung (Masken per `@rh/*`, dasselbe Konto)
 * und die Sprachdateien der Masken. Die Seite selbst ist deutsch (Tiroler Ligen). Kein Service Worker:
 * die Prognosen sollen immer frisch vom Server kommen.
 */
export const leaguehubConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Rechte live vom Server (eigene Rollen + Gruppenrollen), vor dem ersten Seitenaufbau und danach laufend (0.589.0).
    provideAppInitializer(() => inject(PermissionRefresher).start()),
    // Die Seite ist deutsch und registriert nur die deutschen Locale-Daten — eine aus RookHub geerbte Wahl
    // (hr, hu …) als LOCALE_ID ließe jede Datums-/Zahlen-Pipe mit „Missing locale data" scheitern.
    { provide: LOCALE_ID, useValue: 'de' },
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
    // Vereine als Mandanten (0.698.0): jeder LeagueHub-Aufruf trägt den gewählten Verein als `?club=`.
    provideRhHttpClient([leagueClubInterceptor]),
    provideAnimationsAsync(),
    // Rechtsseiten wie in RookHub (Impressum, Kontakt aus OPERATOR), die Datenschutzerklaerung dazu mit dem
    // LeagueHub-Abschnitt: Ligaspieler ohne Konto, Online-Konten, Prognosen, Teilen-Links (Codereview F7-006).
    // Konto loeschen geht nur in RookHub — die Loeschseite verweist dorthin (Codereview UX-023).
    // Seit 0.698.0 mit dem Verein, für den das Konto LeagueHub gerade nutzt (ClubContextService).
    { provide: LEGAL_SITE, useFactory: (): LegalSite => {
      const clubs = inject(ClubContextService);
      return { ...defaultLegalSite(), kind: 'leaguehub', accountHome: 'rookhub', leagueClub: () => clubs.club()?.name ?? null };
    } },
    // Rueckfragen (ConfirmService) mit deutschen Knoepfen — die Seite stellt keine Sprache ein, sonst kaeme „Cancel“.
    { provide: CONFIRM_LABELS, useValue: { confirm: 'OK', cancel: 'Abbrechen' } },
    // Klassische Overlays statt Popover (wie RookHub): nur so lässt sich eine Rückfrage in den modalen <dialog> der
    // Spielerkarte umhängen (ConfirmService, 0.659.1) — ein Popover am <body> bleibt hinter dem modalen Dialog inert.
    provideFullscreenSafeOverlays(),
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
  ],
};
