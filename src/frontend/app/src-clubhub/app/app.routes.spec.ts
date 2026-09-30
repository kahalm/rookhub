import { authGuard } from '@rh/core/auth.guard';
import { routes } from './app.routes';

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
});
