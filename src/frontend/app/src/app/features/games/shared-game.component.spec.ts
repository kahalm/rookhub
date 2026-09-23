import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { SharedGameComponent } from './shared-game.component';

describe('SharedGameComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    return { fixture: TestBed.createComponent(SharedGameComponent), http: TestBed.inject(HttpTestingController) };
  }

  const sharedGame = (ownerSide: 'white' | 'black' | null) => ({
    source: 'lichess', white: 'a', black: 'b', result: '0-1',
    pgn: '[White "a"]\n[Black "b"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z', ownerSide,
  });

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const { fixture } = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Teilender spielte Schwarz → Brett startet aus seiner Sicht gedreht (Flip-Knopf bleibt nutzbar).
  it('starts flipped when the sharer played black (ownerSide=black)', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges(); // ngOnInit → GET /api/games/shared/…
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('black'));
    expect(fixture.componentInstance.flipped).toBeTrue();
  });

  it('starts unflipped for ownerSide=white or unknown', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame(null));
    expect(fixture.componentInstance.flipped).toBeFalse();
  });

  // Gemeldet 2026-09-23: am PC stand ein 400-px-Brett auf einer 900-px-Karte, die Zugliste füllte den Rest mit
  // einer Handbreit Luft zwischen den Spalten, „Partie im Original öffnen" lag als kartenbreite Zeile darunter.
  // Jetzt wächst das Brett mit dem Fenster, die Zugliste hat eine feste Breite neben ihm, und der Knopf steht
  // in der Kopfzeile. Der Karma-Browser ist breit genug für die Desktop-Regeln (> 768 px).
  it('uses the desktop space: board grows past 400px, move list keeps a fixed width beside it, link sits in the header', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/'))
      .flush({ ...sharedGame('white'), sourceUrl: 'https://lichess.org/abc' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const board = el.querySelector('.board-wrap') as HTMLElement;
    const moves = el.querySelector('.moves-section') as HTMLElement;
    // Brett = clamp(360, min(100vh − 300, 100vw − 440), 640) — hängt am Fenster, nicht mehr fest 400 px.
    const expected = Math.min(640, Math.max(360, Math.min(window.innerHeight - 300, window.innerWidth - 440)));
    expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(expected));
    expect(Math.round(moves.getBoundingClientRect().width)).toBe(300);
    // Zugliste NEBEN dem Brett (gleiche Oberkante), nicht darunter.
    expect(Math.abs(moves.getBoundingClientRect().top - board.getBoundingClientRect().top)).toBeLessThan(2);
    expect(el.querySelector('.header .original')).not.toBeNull();
  });
});
