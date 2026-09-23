import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { SharedGameComponent } from './shared-game.component';

describe('SharedGameComponent', () => {
  async function setup(loggedIn = false) {
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
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
  // in der Kopfzeile. Welche Regeln gelten, entscheidet der VIEWPORT des Karma-Browsers (Media-Query bei
  // 768 px): der Launcher stellt 1400 × 900 ein (karma.conf.js); ohne die Angabe war der CI-Browser schmaler
  // und dieser Spec dort rot — deshalb prüft er in beiden Fällen das jeweils richtige Layout, statt eines
  // davon vorauszusetzen.
  it('uses the desktop space: board grows past 400px, move list keeps a fixed width beside it, link sits in the header', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/'))
      .flush({ ...sharedGame('white'), sourceUrl: 'https://lichess.org/abc' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const board = el.querySelector('.board-wrap') as HTMLElement;
    const moves = el.querySelector('.moves-section') as HTMLElement;
    expect(el.querySelector('.header .original')).not.toBeNull();

    if (window.innerWidth > 768) {
      // Brett = clamp(360, min(100vh − 300, 100vw − 440), 640) — hängt am Fenster, nicht mehr fest 400 px.
      const expected = Math.min(640, Math.max(360, Math.min(window.innerHeight - 300, window.innerWidth - 440)));
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(expected));
      expect(Math.round(moves.getBoundingClientRect().width)).toBe(300);
      // Zugliste NEBEN dem Brett (gleiche Oberkante), nicht darunter.
      expect(Math.abs(moves.getBoundingClientRect().top - board.getBoundingClientRect().top)).toBeLessThan(2);
    } else {
      // Handy-Regeln: Brett volle Breite, Zugliste darunter.
      const section = el.querySelector('.board-section') as HTMLElement;
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(section.getBoundingClientRect().width));
      expect(moves.getBoundingClientRect().top).toBeGreaterThanOrEqual(board.getBoundingClientRect().bottom);
    }
  });

  // ----- „Partie analysieren" = derselbe Einwurf wie auf der Punktepartie-Seite -----

  it('analyze without login: no request, goes to the login page and comes back here afterwards', async () => {
    const { fixture, http } = await setup(false);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    http.expectNone(req => req.url.startsWith('/api/game-analyses'));
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: router.url } });
  });

  it('analyze when logged in: posts the PGN to the guess upload and goes to the points-game page', async () => {
    const { fixture, http } = await setup(true);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    // Angemeldet fragt die Seite vorab, ob eine Engine da ist (wie die Punktepartie-Seite).
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    const post = http.expectOne({ method: 'POST', url: '/api/game-analyses/guess' });
    expect(post.request.body.pgn).toBe(sharedGame('white').pgn);
    expect(post.request.body.title).toBe('a – b');
    post.flush({ id: 7 });
    expect(navigate).toHaveBeenCalledWith(['/guess']);
  });

  it('analyze without an engine: the button is disabled instead of failing on click', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: false, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).disabled).toBeTrue();
  });
});
