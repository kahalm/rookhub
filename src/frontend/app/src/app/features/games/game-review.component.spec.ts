import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { MatTooltip } from '@angular/material/tooltip';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { GameReviewComponent } from './game-review.component';
import { GameEvals, GameEvalsStatus } from './game-review.util';

describe('GameReviewComponent', () => {
  const url = '/api/games/4/evals';
  // 1.e4 c5 — Schwarz verliert mit c5 deutlich (nur für die Anzeige, die Rechnung prüft die util-Spec).
  const fens = [
    'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
    'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1',
    'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2',
  ];
  const evals = (status: GameEvalsStatus, withSecond = true): GameEvals => ({
    status, analyzed: withSecond ? 2 : 1, total: 2, targetDepth: 20, analysisId: 9,
    plies: [
      { ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30 },
      ...(withSecond ? [{ ply: 1, cp: 25, depth: 20, bestUci: 'e7e5', playedUci: 'c7c5', playedCp: 300 }] : []),
    ],
    final: withSecond ? { cp: 300 } : null,
  });

  function setup(game: { fens?: string[]; moves?: { from: string; to: string }[] } = {}) {
    TestBed.configureTestingModule({
      imports: [GameReviewComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    const fixture = TestBed.createComponent(GameReviewComponent);
    const statuses: GameEvalsStatus[] = [];
    fixture.componentInstance.statusChange.subscribe(s => statuses.push(s));
    fixture.componentRef.setInput('fens', game.fens ?? fens);
    if (game.moves) fixture.componentRef.setInput('moves', game.moves);
    fixture.componentRef.setInput('evalsUrl', url);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), statuses, el: fixture.nativeElement as HTMLElement };
  }

  it('lädt die Bewertungen und zeigt bei „none" NICHTS (die Seite zeigt ihren Knopf)', () => {
    const { fixture, http, statuses, el } = setup();
    http.expectOne(url).flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null });
    fixture.detectChanges();

    expect(el.querySelector('.review')).toBeNull();
    expect(statuses).toEqual(['none']);
  });

  it('läuft die Analyse: Kurve soweit da, Fortschritt, und alle 10 s nachfragen — bis sie fertig ist', fakeAsync(() => {
    const { fixture, http, statuses, el } = setup();
    http.expectOne(url).flush(evals('running', false));
    fixture.detectChanges();

    expect(el.querySelector('app-eval-graph')).not.toBeNull();
    expect(el.querySelector('.progress')).not.toBeNull();
    expect(statuses).toEqual(['running']);

    tick(GameReviewComponent.PollMs - 1);
    http.expectNone(url);
    tick(1);
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    expect(statuses).toEqual(['running', 'done']);
    expect(el.querySelector('.progress')).toBeNull();

    // Fertig = Ruhe: kein weiterer Abruf.
    tick(GameReviewComponent.PollMs * 3);
    http.expectNone(url);
  }));

  it('ein Aussetzer beim Nachfragen bricht das Nachfragen nicht ab', fakeAsync(() => {
    const { http } = setup();
    http.expectOne(url).flush(evals('running', false));
    tick(GameReviewComponent.PollMs);
    http.expectOne(url).flush('boom', { status: 502, statusText: 'Bad Gateway' });
    tick(GameReviewComponent.PollMs);
    http.expectOne(url).flush(evals('done'));
    tick(GameReviewComponent.PollMs);
    http.expectNone(url);
  }));

  it('läuft die Analyse, steht neben dem Fortschritt die Restdauer — ohne Tempo keine', () => {
    const { fixture, http, el } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { gameAnalysis: { eta: 'about {{eta}} left' } });
    TestBed.inject(TranslateService).use('en');
    http.expectOne(url).flush({ ...evals('running', false), etaMinutes: 11 });
    fixture.detectChanges();
    expect(el.querySelector('.progress')!.textContent).toContain('about 11 min left');

    fixture.componentInstance.reload();
    http.expectOne(url).flush({ ...evals('running', false), etaMinutes: null });
    fixture.detectChanges();
    expect(el.querySelector('.progress')!.textContent).not.toContain('left');
  });

  it('fertig = keine Restdauer, auch wenn der Server noch eine mitschickte', () => {
    const { fixture, http } = setup();
    http.expectOne(url).flush({ ...evals('done'), etaMinutes: 3 });
    fixture.detectChanges();

    expect(fixture.componentInstance.eta()).toBeNull();
  });

  it('geschlossen = kein Nachfragen mehr', fakeAsync(() => {
    const { fixture, http } = setup();
    http.expectOne(url).flush(evals('pending', false));
    fixture.destroy();
    tick(GameReviewComponent.PollMs * 2);
    http.expectNone(url);
  }));

  it('fertig: Zähler je Seite, Genauigkeit, und die Klasse des AKTUELLEN Zugs', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();

    expect(el.querySelector('tr.row-white td.count.best')!.textContent!.trim()).toBe('1');
    expect(el.querySelector('tr.row-black td.count.blunder')!.textContent!.trim()).toBe('1');
    expect(el.querySelector('tr.row-white td.acc')!.textContent).toContain('%');
    expect(el.querySelector('.current')).toBeNull();   // Startstellung: kein Zug

    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();
    const current = el.querySelector('.current') as HTMLElement;
    expect(current.classList).toContain('blunder');
    expect(current.textContent).toContain('+0.25');
    expect(current.textContent).toContain('+3.00');
  });

  it('Fehler und grobe Fehler gehen als Punkte in die Kurve', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    expect(el.querySelectorAll('app-eval-graph .dot.blunder').length).toBe(1);
  });

  it('ein Klick in die Kurve geht als Zug-Index nach außen', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    const clicked: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(i => clicked.push(i));

    const graph = el.querySelector('app-eval-graph .graph') as HTMLElement;
    const rect = graph.getBoundingClientRect();
    graph.dispatchEvent(new MouseEvent('click', { clientX: rect.right - 1, bubbles: true }));
    expect(clicked).toEqual([1]);
  });

  describe('Brilliant, Great, Miss', () => {
    // 1.e4 c5 2.Sf3 d6 3.d4 — 1…c5 grober Fehler, 2.Sf3 einziger guter Zug (great), 2…d6 Fehler, 3.d4 lässt
    // ihn liegen (miss). Die Zahlen prüft die util-Spec; hier geht es um Zähler, Kurve und Abzeichen.
    const sicilian = [
      'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
      'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2',
      'rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2',
      'rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3',
      'rnbqkbnr/pp2pppp/3p4/2p5/3PP3/5N2/PPP2PPP/RNBQKB1R b KQkq - 0 3',
    ];
    const sicilianMoves = [
      { from: 'e2', to: 'e4' }, { from: 'c7', to: 'c5' }, { from: 'g1', to: 'f3' }, { from: 'd7', to: 'd6' },
      { from: 'd2', to: 'd4' },
    ];
    const sicilianEvals: GameEvals = {
      status: 'done', analyzed: 5, total: 5, targetDepth: 20,
      plies: [
        { ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4' },
        { ply: 1, cp: 25, depth: 20, bestUci: 'e7e5', playedUci: 'c7c5' },
        { ply: 2, cp: 300, depth: 20, bestUci: 'g1f3', playedUci: 'g1f3', secondCp: 100 },
        { ply: 3, cp: 280, depth: 20, bestUci: 'b8c6', playedUci: 'd7d6' },
        { ply: 4, cp: 600, depth: 20, bestUci: 'c1g5', playedUci: 'd2d4' },
      ],
      final: { cp: 100 },
    };
    const count = (el: HTMLElement, side: string, cls: string) =>
      el.querySelector(`tr.row-${side} td.count.${cls}`)!.textContent!.trim();
    // Die Erklärung steht als TEXT neben dem Abzeichen (am Handy gibt es kein Hover), nicht als Tooltip.
    const badgeTooltip = (fixture: ReturnType<typeof setup>['fixture']) =>
      (fixture.nativeElement as HTMLElement).querySelector('.current .why')!.textContent!.trim();

    it('neun Spalten; Zähler je Seite und Punkte in der Kurve auch für Great und Miss, in der Farbe der Tabelle', () => {
      const { fixture, http, el } = setup({ fens: sicilian, moves: sicilianMoves });
      http.expectOne(url).flush(sicilianEvals);
      fixture.detectChanges();

      expect(el.querySelectorAll('thead .sym').length).toBe(9);
      expect(count(el, 'white', 'great')).toBe('1');
      expect(count(el, 'white', 'miss')).toBe('1');
      expect(count(el, 'white', 'blunder')).toBe('0');
      expect(count(el, 'black', 'blunder')).toBe('1');
      expect(count(el, 'black', 'mistake')).toBe('1');

      expect(el.querySelectorAll('app-eval-graph .dot.great').length).toBe(1);
      expect(el.querySelectorAll('app-eval-graph .dot.miss').length).toBe(1);
      expect((el.querySelector('app-eval-graph .dot.great') as HTMLElement).style.backgroundColor)
        .toBe('rgb(91, 143, 214)');   // #5b8fd6
      expect((el.querySelector('thead .sym.great') as HTMLElement).style.backgroundColor)
        .toBe('rgb(91, 143, 214)');
    });

    it('Great: das Abzeichen nennt den Abstand zum nächstbesten Zug; Miss sagt, was verpasst wurde', () => {
      const { fixture, http } = setup({ fens: sicilian, moves: sicilianMoves });
      TestBed.inject(TranslateService).setTranslation('en', { games: { review: {
        greatGap: 'next best {{gap}} pawns worse', missHint: 'did not punish',
      } } });
      TestBed.inject(TranslateService).use('en');
      http.expectOne(url).flush(sicilianEvals);
      fixture.componentRef.setInput('currentIndex', 2);
      fixture.detectChanges();
      expect(badgeTooltip(fixture)).toBe('next best 2.0 pawns worse');

      fixture.componentRef.setInput('currentIndex', 4);
      fixture.detectChanges();
      expect(badgeTooltip(fixture)).toBe('did not punish');
    });

    it('Brilliant: Punkt in der Kurve, Zähler, und das Abzeichen nennt die geopferte Figur und ihr Feld', () => {
      // 7.Lxh7+ (Griechisches Geschenk), Zweitbester +0,50.
      const { fixture, http, el } = setup({
        fens: [
          'rnbq1rk1/pppnbppp/4p3/3pP3/3P4/2NB1N2/PPP2PPP/R1BQK2R w KQ - 5 7',
          'rnbq1rk1/pppnbppB/4p3/3pP3/3P4/2N2N2/PPP2PPP/R1BQK2R b KQ - 0 7',
        ],
        moves: [{ from: 'd3', to: 'h7' }],
      });
      TestBed.inject(TranslateService).setTranslation('en', { games: { review: {
        sacrifice: 'Sacrifice: {{piece}} on {{square}}', piece: { b: 'bishop' },
      } } });
      TestBed.inject(TranslateService).use('en');
      http.expectOne(url).flush({
        status: 'done', analyzed: 1, total: 1, targetDepth: 20,
        plies: [{ ply: 0, cp: 150, depth: 20, bestUci: 'd3h7', playedUci: 'd3h7', secondCp: 50 }],
        final: { cp: 150 },
      });
      fixture.componentRef.setInput('currentIndex', 0);
      fixture.detectChanges();

      expect(count(el, 'white', 'brilliant')).toBe('1');
      expect(el.querySelectorAll('app-eval-graph .dot.brilliant').length).toBe(1);
      expect(el.querySelector('.current')!.classList).toContain('brilliant');
      expect(badgeTooltip(fixture)).toBe('Sacrifice: bishop on h7');
    });

    it('ohne Züge (die Seite reicht keine herein) bleibt es bei den Grundklassen', () => {
      const { fixture, http, el } = setup({ fens: sicilian });
      http.expectOne(url).flush(sicilianEvals);
      fixture.detectChanges();
      expect(count(el, 'white', 'great')).toBe('0');
      expect(count(el, 'white', 'miss')).toBe('0');
      expect(count(el, 'white', 'blunder')).toBe('1');
      expect(el.querySelector('app-eval-graph .dot.great')).toBeNull();
    });
  });

  it('reload() fragt sofort neu (nach „Partie analysieren")', () => {
    const { fixture, http, statuses } = setup();
    http.expectOne(url).flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [] });
    fixture.componentInstance.reload();
    http.expectOne(url).flush(evals('pending', false));
    expect(statuses).toEqual(['none', 'pending']);
  });
});
