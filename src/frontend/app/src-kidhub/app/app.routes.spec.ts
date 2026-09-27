import { routes } from './app.routes';
import { kidhubConfig } from './app.config';
import { LEGAL_SITE } from '@rh/features/legal/legal-site';

describe('KidHub-Routen und Rechtsseiten', () => {
  it('Anmelden, Registrieren und Datenschutz gibt es, ein Impressum nicht', () => {
    const paths = routes.map(r => r.path);
    expect(paths).toContain('login');
    expect(paths).toContain('register');
    expect(paths).toContain('privacy');
    expect(paths).toContain('account-deletion');     // die Datenschutzerklaerung verlinkt sie
    expect(paths).not.toContain('impressum');
  });

  it('Datenschutzfragen gehen an kidhub@oberschm.id', () => {
    const legal = kidhubConfig.providers.find(p => (p as { provide?: unknown }).provide === LEGAL_SITE) as
      { useValue: { contactEmail: string; imprint: boolean } } | undefined;
    expect(legal?.useValue).toEqual({ contactEmail: 'kidhub@oberschm.id', imprint: false });
  });
});
