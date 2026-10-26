import { EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RedirectFunction } from '@angular/router';
import { routes } from './app.routes';
import { KidsStarsStore } from './core/kids-stars.store';
import { kidhubConfig } from './app.config';
import { LEGAL_SITE, LegalSite } from '@rh/core/legal-site';
import { checkSharedPageLinks } from '@rh/testing/shared-page-links';
import { AUTH_INTRO, AuthIntro } from '@rh/features/auth/auth-intro';

describe('KidHub-Routen und Rechtsseiten', () => {
  it('Anmelden, Registrieren und Datenschutz gibt es, ein Impressum nicht', () => {
    const paths = routes.map(r => r.path);
    expect(paths).toContain('login');
    expect(paths).toContain('register');
    expect(paths).toContain('privacy');
    expect(paths).toContain('account-deletion');     // die Datenschutzerklaerung verlinkt sie
    expect(paths).not.toContain('impressum');
  });

  it('Datenschutzfragen gehen an kidhub@oberschm.id, Kinder-Fassung, Ruecklink zur Startseite, Konto in RookHub', () => {
    const legal = kidhubConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE) as
      { useValue: LegalSite } | undefined;
    // accountHome: KidHub hat kein Profil — die Loeschseite verweist auf RookHubs Profil (UX-023).
    expect(legal?.useValue).toEqual({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/', accountHome: 'rookhub' });
  });

  it('Anmelde- und Registriermaske sagen, wozu das Konto dient (UX-066)', () => {
    const intro = kidhubConfig.providers.find(p => (p as { provide?: unknown }).provide === AUTH_INTRO) as
      { useValue: AuthIntro } | undefined;
    expect(intro?.useValue).toEqual({ login: 'kids.authIntro.login', register: 'kids.authIntro.register' });
  });

  it('jeder Link der geteilten Anmelde- und Rechtsseiten hat hier einen Weg — mit KidHubs LEGAL_SITE (UX-003)', async () => {
    // Mit kidhubConfig: ohne Impressum (die Anmeldemaske zeigt den Link dann nicht), Ruecklink auf '/'.
    const report = await checkSharedPageLinks(routes, kidhubConfig);
    expect(report.mounted).toEqual(jasmine.arrayWithExactContents(['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'account-deletion']));
    expect(report.links).toContain('/privacy → /');
    expect(report.links).not.toContain('/account-deletion → /profile');   // kein Profil hier (UX-023)
    expect(report.problems).toEqual([]);
  });

  it('/stars/:stage/unlock schaltet die Stufe frei und spielt sie, Unsinn geht zur Übersicht', () => {
    localStorage.removeItem('rh-kids-stars-v1');
    const redirect = routes.find(r => r.path === 'stars/:stage/unlock')?.redirectTo as RedirectFunction;
    const run = (stage: string) => runInInjectionContext(TestBed.inject(EnvironmentInjector),
      () => redirect({ params: { stage } } as unknown as Parameters<RedirectFunction>[0]));
    expect(run('17')).toBe('/stars/17');
    expect(TestBed.inject(KidsStarsStore).isOpen(17)).toBeTrue();
    expect(run('99')).toBe('/stars');
    expect(run('x')).toBe('/stars');
    localStorage.removeItem('rh-kids-stars-v1');
  });
});
