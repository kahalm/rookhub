import { authGuard } from '@rh/core/auth.guard';
import { LEGAL_SITE, LegalSite } from '@rh/features/legal/legal-site';
import { OPERATOR } from '../../src/environments/operator';
import { leaguehubConfig } from './app.config';
import { routes } from './app.routes';
import { checkSharedPageLinks } from '@rh/testing/shared-page-links';

describe('LeagueHub-Routen', () => {
  it('die Prognose-Seite braucht eine Anmeldung, der Teilen-Link nicht', () => {
    const home = routes.find(r => r.path === '')!;
    const share = routes.find(r => r.path === 's/:token')!;
    expect(home.canActivate).toContain(authGuard);
    expect(share.canActivate).toBeUndefined();
  });

  it('die Vereins-Datenbank braucht eine Anmeldung', () => {
    for (const path of ['verein', 'verein/neu', 'verein/formular/:id']) {
      expect(routes.find(r => r.path === path)?.canActivate).withContext(path).toContain(authGuard);
    }
  });

  it('Hochladen über einen Teilen-Link geht ohne Anmeldung', () => {
    for (const path of ['s/:token/hochladen', 's/:token/formular/:key']) {
      const r = routes.find(x => x.path === path);
      expect(r).withContext(path).toBeDefined();
      expect(r!.canActivate).withContext(path).toBeUndefined();
    }
  });

  it('Datenschutz mit LeagueHub-Abschnitt, Impressum und Kontakt wie RookHub (F7-006)', () => {
    expect(routes.map(r => r.path)).toContain('privacy');
    const legal = leaguehubConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE) as
      { useFactory: () => LegalSite } | undefined;
    expect(legal?.useFactory()).toEqual({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' });
  });

  it('kein Pfad, den der gemeinsame nginx an die Link-Vorschau schickt (/g, /t, /puzzles)', () => {
    for (const r of routes) {
      expect(/^(g|t|puzzles)(\/|$)/.test(r.path ?? '')).withContext(r.path ?? '').toBeFalse();
    }
  });

  it('jeder Link der geteilten Anmelde- und Rechtsseiten hat hier einen Weg (UX-003)', async () => {
    const report = await checkSharedPageLinks(routes, leaguehubConfig);
    expect(report.mounted).toEqual(jasmine.arrayWithExactContents(['login', 'register', 'forgot-password', 'reset-password', 'privacy', 'impressum', 'account-deletion']));
    expect(report.problems).toEqual([]);
  });
});
