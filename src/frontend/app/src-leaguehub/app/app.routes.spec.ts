import { authGuard } from '@rh/core/auth.guard';
import { routes } from './app.routes';

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

  it('kein Pfad, den der gemeinsame nginx an die Link-Vorschau schickt (/g, /t, /puzzles)', () => {
    for (const r of routes) {
      expect(/^(g|t|puzzles)(\/|$)/.test(r.path ?? '')).withContext(r.path ?? '').toBeFalse();
    }
  });
});
