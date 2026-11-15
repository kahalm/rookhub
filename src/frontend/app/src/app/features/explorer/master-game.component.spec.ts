import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PreferencesService } from '../../core/preferences.service';
import { AuthService } from '../../core/auth.service';
import { routes } from '../../app.routes';
import { MasterGameComponent, masterGameHeaders } from './master-game.component';

const PGN = `[Event "7. Sinquefield Cup 2019"]
[Site "Saint Louis USA"]
[Date "2019.08.19"]
[Round "?"]
[White "Carlsen, Magnus"]
[Black "Caruana, Fabiano"]
[Result "1/2-1/2"]
[WhiteElo "2882"]
[BlackElo "2818"]

1. d4 Nf6 2. c4 e6 3. Nf3 d5 1/2-1/2`;

/** „Meisterpartie“ (0.980.0): Linkziel der Beispielpartien aus dem Explorer, ohne Anmeldung. */
describe('MasterGameComponent', () => {
  function render(id: string, query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [MasterGameComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }), queryParamMap: convertToParamMap(query) } } },
        { provide: AuthService, useValue: { isLoggedIn: false, currentUser: null, currentUser$: { subscribe: () => ({ unsubscribe() { /* leer */ } }) } } },
        { provide: PreferencesService, useValue: { boardTheme: 'brown', pieceSet: 'cburnett' } },
      ],
    });
    const fixture = TestBed.createComponent(MasterGameComponent);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), el: fixture.nativeElement as HTMLElement };
  }

  it('zeigt Spieler, Elo, Ergebnis und Turnier aus dem PGN und das Brett zum Nachspielen', () => {
    const { fixture, http, el } = render('LZBtv84I');
    http.expectOne('/api/explorer/master-games/LZBtv84I').flush({ id: 'LZBtv84I', pgn: PGN });
    fixture.detectChanges();

    const head = el.querySelector('.head-card')!.textContent!.replace(/\s+/g, ' ');
    // Abstände zwischen Name, Elo und Ergebnis macht das CSS, nicht der Text.
    expect(head).toContain('Carlsen, Magnus(2882) – Caruana, Fabiano(2818)½–½');
    expect(head).toContain('games.masterGame.title · 7. Sinquefield Cup 2019 · Saint Louis USA · 2019');
    const board = el.querySelector('app-summary-game-board')!;
    expect(board.querySelectorAll('app-move-list .move, app-move-list button').length).toBeGreaterThan(0);
    expect(el.querySelector('.nav-forward')).not.toBeNull();
  });

  it('?ply= öffnet die Stellung nach so vielen Halbzügen; Unsinn wird ignoriert, zu viel endet am Schluss', () => {
    const open = (ply: string) => {
      TestBed.resetTestingModule();
      const { fixture, http } = render('LZBtv84I', { ply });
      http.expectOne('/api/explorer/master-games/LZBtv84I').flush({ id: 'LZBtv84I', pgn: PGN });
      fixture.detectChanges();
      return fixture.debugElement.children[0].query(d => d.name === 'app-summary-game-board').componentInstance.ply() as number;
    };
    expect(open('3')).toBe(3);
    expect(open('abc')).toBe(0);
    expect(open('-2')).toBe(0);
    expect(open('999')).toBe(6);
  });

  it('unbekannt (404) und abgelehnte Kennung (400): „gibt es nicht“ ohne Wiederholen', () => {
    for (const status of [404, 400]) {
      TestBed.resetTestingModule();
      const { fixture, http, el } = render('XXXXXXXX');
      http.expectOne('/api/explorer/master-games/XXXXXXXX').flush({}, { status, statusText: 'x' });
      fixture.detectChanges();
      expect(el.querySelector('.empty-state.not-found p')?.textContent).withContext(String(status)).toContain('games.masterGame.notFound');
      expect(el.querySelector('app-load-error')).toBeNull();
      expect(el.querySelector('a.to-home')?.getAttribute('href')).toBe('/');
    }
  });

  it('eine Kennung, die keine sein kann, fragt den Server gar nicht', () => {
    const { http, el } = render('../admin');
    http.expectNone(() => true);
    expect(el.querySelector('.empty-state.not-found')).not.toBeNull();
  });

  it('Explorer nicht erreichbar (503): Ladefehler mit Wiederholen, das erneut fragt', () => {
    const { fixture, http, el } = render('LZBtv84I');
    http.expectOne('/api/explorer/master-games/LZBtv84I').flush({}, { status: 503, statusText: 'x' });
    fixture.detectChanges();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('.empty-state.not-found')).toBeNull();

    fixture.componentInstance.load();
    http.expectOne('/api/explorer/master-games/LZBtv84I').flush({ id: 'LZBtv84I', pgn: PGN });
    fixture.detectChanges();
    expect(el.querySelector('app-summary-game-board')).not.toBeNull();
  });

  it('masterGameHeaders: Platzhalter fehlen, maskierte Anführungszeichen werden gelesen', () => {
    expect(masterGameHeaders(PGN)).toEqual({
      Event: '7. Sinquefield Cup 2019', Site: 'Saint Louis USA', Date: '2019.08.19', White: 'Carlsen, Magnus', Black: 'Caruana, Fabiano',
      Result: '1/2-1/2', WhiteElo: '2882', BlackElo: '2818',
    });
    expect(masterGameHeaders('[Event "Der \\"Große\\" Preis"]\n\n1. e4')).toEqual({ Event: 'Der "Große" Preis' });
  });

  it('die Route ist ohne Anmeldung erreichbar (kein Guard) — die Zusammenfassung wird auch über Teilen-Links gelesen', () => {
    const route = routes.find(r => r.path === 'explorer/game/masters/:id');
    expect(route).toBeDefined();
    expect(route!.canActivate).toBeUndefined();
  });
});
