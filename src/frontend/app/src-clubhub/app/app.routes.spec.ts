import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { authGuard } from '@rh/core/auth.guard';
import { AuthService } from '@rh/core/auth.service';
import { LoginComponent } from '@rh/features/auth/login.component';
import { routes } from './app.routes';
import { checkSharedPageLinks } from '@rh/testing/shared-page-links';
import { clubhubConfig } from './app.config';
import { LEGAL_SITE, LegalSite } from '@rh/features/legal/legal-site';
import { AUTH_INTRO, AuthIntro } from '@rh/features/auth/auth-intro';

describe('ClubHub-Routen', () => {
  it('Kartei, Karteiblatt, Gruppen und Anwesenheit gibt es nur angemeldet', () => {
    for (const path of ['', 'kind/neu', 'kind/:id', 'gruppen', 'gruppen/:id', 'gruppen/:id/anwesenheit', 'verknuepfen']) {
      expect(routes.find(r => r.path === path)?.canActivate).withContext(path).toContain(authGuard);
    }
  });

  it('„neu" steht vor „:id" — sonst läse das Karteiblatt „neu" als Nummer', () => {
    const paths = routes.map(r => r.path);
    expect(paths.indexOf('kind/neu')).toBeLessThan(paths.indexOf('kind/:id'));
  });

  it('kein Pfad, den der gemeinsame nginx an die Link-Vorschau schickt (/g, /t, /puzzles)', () => {
    for (const r of routes) {
      expect(/^(g|t|puzzles)(\/|$)/.test(r.path ?? '')).withContext(r.path ?? '').toBeFalse();
    }
  });

  it('jeder Link der geteilten Anmelde- und Rechtsseiten hat hier einen Weg (UX-003)', async () => {
    const report = await checkSharedPageLinks(routes, clubhubConfig);
    expect(report.mounted).toEqual(jasmine.arrayWithExactContents(['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion']));
    expect(report.links).not.toContain('/account-deletion → /profile');   // kein Profil hier (UX-023)
    expect(report.problems).toEqual([]);
  });

  it('Konto loeschen verweist auf RookHub — ClubHub hat kein Profil (UX-023)', () => {
    const legal = clubhubConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE) as
      { useFactory: () => LegalSite } | undefined;
    expect(legal?.useFactory().accountHome).toBe('rookhub');
  });

  /**
   * UX-027: Die Anmeldemaske kommt aus RookHub und sagte bei jeder Umleitung „Ein Konto ist kostenlos …“. Hier oeffnet
   * ein neues Konto die Kartei aber nicht (club.manage/club.trainer, sonst „Nicht freigeschaltet“) — ClubHub setzt
   * deshalb eine eigene Einleitung, die den Satz ersetzt.
   */
  describe('Anmeldemaske (UX-027)', () => {
    const intro = clubhubConfig.providers.find(p => (p as { provide?: unknown }).provide === AUTH_INTRO) as
      { provide: unknown; useValue: AuthIntro } | undefined;

    it('setzt eine eigene Einleitung fuer die Anmeldung', () => {
      expect(intro?.useValue).toEqual({ login: 'clubhub.authIntro.login' });
    });

    it('Gast auf der Kartei: Einleitung statt „kostenlos“ (mit dem Provider dieser Konfiguration gerendert)', async () => {
      const query = { returnUrl: '/gruppen', authRequired: '1' };   // was der authGuard aus /gruppen macht
      await TestBed.configureTestingModule({
        imports: [LoginComponent],
        providers: [
          provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
          { provide: AuthService, useValue: {} },
          { provide: ActivatedRoute, useValue: { snapshot: { queryParams: query }, queryParamMap: of(convertToParamMap(query)) } },
          intro!,
        ],
      }).compileComponents();
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('en', {
        auth: { login: { required: 'GENERIC', freeNote: 'FREE' } },
        clubhub: { authIntro: { login: 'CLUB INTRO' } },
      }, true);
      translate.use('en');
      const fixture = TestBed.createComponent(LoginComponent);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.auth-required')?.textContent).toContain('GENERIC');
      expect(el.querySelector('.auth-sub')).toBeNull();
      expect(el.textContent).not.toContain('FREE');
      expect(el.querySelector('.site-note')?.textContent).toContain('CLUB INTRO');
    });
  });
});
