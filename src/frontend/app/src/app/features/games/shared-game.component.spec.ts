import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { By } from '@angular/platform-browser';
import { GameReviewComponent } from './game-review.component';
import { SharedGameComponent } from './shared-game.component';

describe('SharedGameComponent', () => {
  async function setup(loggedIn = false, own = false) {
    const snapshot = own
      ? { paramMap: convertToParamMap({ id: '4' }), data: { mode: 'own' } }
      : { paramMap: convertToParamMap({ token: 'tok' }), data: {} };
    await TestBed.configureTestingModule({
      imports: [SharedGameComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
        { provide: ActivatedRoute, useValue: { snapshot } },
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

  // Gemeldet 2026-09-23: schnelles Doppeltippen auf den Brettrand markierte Text (und Safari zoomte). Die
  // Tippzonen sind nackte divs; die Regel dafür steht EINMAL in styles.scss (vier Komponenten teilen sie) —
  // Karma lädt das globale Stylesheet, also ist sie hier am gerenderten Element prüfbar.
  it('tap zones beside the board are excluded from text selection and double-tap zoom', async () => {
    const { fixture, http } = await setup();
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    fixture.detectChanges();

    const zones = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.board-tap')) as HTMLElement[];
    expect(zones.length).toBe(2);
    for (const z of zones) {
      const cs = getComputedStyle(z);
      expect(cs.userSelect).toBe('none');
      expect(cs.touchAction).toBe('manipulation');
    }
  });

  it('analyze without login: no request, goes to the login page and comes back here afterwards', async () => {
    const { fixture, http } = await setup(false);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    http.expectNone(req => req.url.startsWith('/api/game-analyses') || req.url.endsWith('/analyze'));
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: router.url } });
  });

  const noEvals = { status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null };
  const runningEvals = {
    status: 'running', analyzed: 1, total: 2, targetDepth: 20,
    plies: [{ ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30 }], final: null,
  };

  // Seit 0.512.0: der Server kennt das PGN selbst (Token), und man BLEIBT auf der Seite — die Kurve
  // erscheint hier. Vorher ging das PGN an den Punktepartie-Einwurf und der Nutzer auf /guess, wo eine
  // solche Analyse gar nicht mehr steht.
  it('analyze when logged in: posts to the shared analyze endpoint, stays on the page and reloads the graph', async () => {
    const { fixture, http } = await setup(true);
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok').flush(sharedGame('white'));
    // Angemeldet fragt die Seite vorab, ob eine Engine da ist (wie die Punktepartie-Seite).
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok/evals').flush(noEvals);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).click();

    const post = http.expectOne({ method: 'POST', url: '/api/games/shared/tok/analyze' });
    expect(post.request.body).toEqual({});
    post.flush({ analysis: { id: 7 }, reused: false });
    expect(navigate).not.toHaveBeenCalled();
    http.expectOne('/api/games/shared/tok/evals').flush(runningEvals);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-game-review .review')).not.toBeNull();
  });

  it('a second click while the first call runs sends nothing', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok').flush(sharedGame('white'));
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok/evals').flush(noEvals);
    fixture.detectChanges();

    fixture.componentInstance.analyze();
    fixture.componentInstance.analyze();

    expect(http.match({ method: 'POST', url: '/api/games/shared/tok/analyze' }).length).toBe(1);
  });

  it('shows the graph without login when the sharer analysed the game — and hides the button once it is done', async () => {
    const { fixture, http } = await setup(false);
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok').flush(sharedGame('white'));
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok/evals').flush({ ...runningEvals, status: 'done' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-game-review app-eval-graph')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('button.analyze')).toBeNull();
  });

  it('hands the moves to the review as UCI — the basis for Brilliant, Great and Miss', async () => {
    const { fixture, http } = await setup(false);
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok').flush(sharedGame('white'));
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok/evals').flush({ ...runningEvals, status: 'done' });
    fixture.detectChanges();
    const review = fixture.debugElement.query(By.directive(GameReviewComponent)).componentInstance as GameReviewComponent;
    expect(review.ucis()).toEqual(['e2e4', 'c7c5']);
  });

  it('while the analysis runs, the button is disabled instead of queueing a second one', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok').flush(sharedGame('white'));
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    http.expectOne('/api/games/shared/tok/evals').flush(runningEvals);
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('analyze without an engine: the button is disabled instead of failing on click', async () => {
    const { fixture, http } = await setup(true);
    fixture.detectChanges();
    http.expectOne(req => req.url.startsWith('/api/games/shared/')).flush(sharedGame('white'));
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: false, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).disabled).toBeTrue();
  });
  // ----- Eigene Partie als Seite (/games/:id, seit 0.513.0 — vorher ein Dialog) -----

  it('own mode: loads the game by id, uses the own analyze/evals urls, offers back and share', async () => {
    const { fixture, http } = await setup(true, true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush({
      id: 4, source: 'lichess', white: 'a', black: 'b', result: '0-1', shareToken: 'tok4', moveCount: 2,
      pgn: '[White "a"]\n[Black "b"]\n\n1. e4 c5 0-1', createdAt: '2026-07-16T00:00:00Z', ownerSide: 'black',
    });
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();   // erst jetzt entsteht die Kurven-Komponente und fragt die Bewertungen ab
    http.expectOne('/api/games/4/evals').flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.flipped).toBeTrue();
    expect(el.querySelector('a.back')?.getAttribute('href')).toBe('/games');
    expect(el.querySelector('button.share')).not.toBeNull();
    expect(el.querySelector('.header .analyze')).not.toBeNull();

    (el.querySelector('button.analyze') as HTMLButtonElement).click();
    http.expectOne({ method: 'POST', url: '/api/games/4/analyze' }).flush({ analysis: { id: 9 }, reused: false });
    http.expectOne('/api/games/4/evals').flush({ status: 'pending', analyzed: 0, total: 2, targetDepth: 20, plies: [], final: null });
  });

  it('own mode: a missing game shows the load error, not the „shared link" text', async () => {
    const { fixture, http } = await setup(true, true);
    fixture.detectChanges();
    http.expectOne('/api/games/4').flush('nope', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(fixture.componentInstance.notFound).toBeTrue();
    expect(fixture.componentInstance.notFoundKey).toBe('games.loadError');
  });
});
