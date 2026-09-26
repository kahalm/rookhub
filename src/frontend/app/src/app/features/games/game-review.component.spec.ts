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

  /** `graphClosed`: die Kurve bleibt, wie sie startet — zu. Sonst wird sie für die Kurven-Tests aufgeklappt. */
  function setup(game: { fens?: string[]; moves?: { from: string; to: string }[]; graphClosed?: boolean } = {}) {
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
    if (!game.graphClosed) fixture.componentInstance.graphOpen.set(true);
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

  describe('Computer-Linien + Pfeil für den besten Zug', () => {
    beforeEach(() => {
      localStorage.removeItem(GameReviewComponent.LinesKey);
      localStorage.removeItem(GameReviewComponent.ArrowKey);
    });
    afterEach(() => {
      localStorage.removeItem(GameReviewComponent.LinesKey);
      localStorage.removeItem(GameReviewComponent.ArrowKey);
    });

    const withCandidates = (): GameEvals => ({
      ...evals('done'),
      plies: [
        { ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30, candidates: [
          { uci: 'e2e4', cp: 30, pv: ['e2e4', 'e7e5'] }, { uci: 'd2d4', cp: 25 },
        ] },
        { ply: 1, cp: 25, depth: 20, bestUci: 'e7e5', playedUci: 'c7c5', playedCp: 300, candidates: [
          { uci: 'e7e5', cp: 25, pv: ['e7e5', 'g1f3'] }, { uci: 'c7c5', cp: 300 },
        ] },
      ],
    });

    it('aus bis zum Klick; an: die Linien der Stellung auf dem Brett, gemerkt je Gerät', () => {
      const { fixture, http, el } = setup();
      http.expectOne(url).flush(withCandidates());
      fixture.detectChanges();
      expect(el.querySelector('.lines')).toBeNull();

      (el.querySelector('button.lines-toggle') as HTMLButtonElement).click();
      fixture.detectChanges();
      // currentIndex −1 = Startstellung → Zeile 0
      expect(Array.from(el.querySelectorAll('.lines .line-san')).map(e => e.textContent!.trim())).toEqual(['1. e4 e5', '1. d4']);
      expect(localStorage.getItem(GameReviewComponent.LinesKey)).toBe('1');

      fixture.componentRef.setInput('currentIndex', 0);
      fixture.detectChanges();
      expect(el.querySelector('.lines .line-san')!.textContent!.trim()).toBe('1... e5 2. Nf3');
      expect(el.querySelectorAll('.lines li.played').length).toBe(1);   // c5 wurde gespielt
    });

    it('Pfeil: an → der beste Zug der Stellung geht an die Seite; wechselt mit dem Zug', () => {
      const { fixture, http, el } = setup();
      const arrows: unknown[] = [];
      fixture.componentInstance.arrowsChange.subscribe(a => arrows.push(a));
      http.expectOne(url).flush(withCandidates());
      fixture.detectChanges();

      (el.querySelector('button.arrow-toggle') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(arrows[arrows.length - 1]).toEqual([{ from: 'e2', to: 'e4' }]);

      fixture.componentRef.setInput('currentIndex', 0);
      fixture.detectChanges();
      expect(arrows[arrows.length - 1]).toEqual([{ from: 'e7', to: 'e5' }]);
    });

    it('im Fehler-Training: keine Schalter, keine Linien, kein Pfeil — auch wenn sie eingeschaltet sind', () => {
      localStorage.setItem(GameReviewComponent.LinesKey, '1');
      localStorage.setItem(GameReviewComponent.ArrowKey, '1');
      const { fixture, http, el } = setup();
      const arrows: unknown[] = [];
      fixture.componentInstance.arrowsChange.subscribe(a => arrows.push(a));
      http.expectOne(url).flush(withCandidates());
      fixture.detectChanges();
      expect(el.querySelector('.lines')).not.toBeNull();

      fixture.componentRef.setInput('engineHidden', true);
      fixture.detectChanges();
      expect(el.querySelector('.lines')).toBeNull();
      expect(el.querySelector('button.lines-toggle')).toBeNull();
      expect(arrows[arrows.length - 1]).toEqual([]);
    });
  });

  // Gewünscht 2026-09-24: die Kurve standardmäßig eingeklappt, auf Wunsch aufklappen.
  it('die Kurve ist zu, bis man auf die Überschrift klickt — Zähler und Genauigkeit stehen trotzdem da', () => {
    const { fixture, http, el } = setup({ graphClosed: true });
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    expect(el.querySelector('app-eval-graph')).toBeNull();
    expect(el.querySelector('table.summary')).not.toBeNull();
    const title = el.querySelector('button.title') as HTMLButtonElement;
    expect(title.getAttribute('aria-expanded')).toBe('false');

    title.click();
    fixture.detectChanges();
    expect(el.querySelector('app-eval-graph')).not.toBeNull();
    expect(title.getAttribute('aria-expanded')).toBe('true');

    title.click();
    fixture.detectChanges();
    expect(el.querySelector('app-eval-graph')).toBeNull();
  });

  it('Buchzüge (vom Server, aus dem eigenen Repertoire): Etikett „Buch" mit Erklärung, eigene Spalte, kein Punkt in der Kurve', () => {
    const { fixture, http, el } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { games: { review: { bookHint: 'In your repertoire' } } });
    TestBed.inject(TranslateService).use('en');
    // Zug 1 (…c5) ist ein Patzer — steht er im Repertoire, heißt er trotzdem „Buch".
    http.expectOne(url).flush({ ...evals('done'), bookPlies: [0, 1] });
    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();

    expect(el.querySelector('.current')!.className).toContain('book');
    expect(el.querySelector('.current .why')!.textContent).toContain('In your repertoire');
    expect(el.querySelector('.row-white .count.book')!.textContent!.trim()).toBe('1');
    expect(el.querySelector('.row-black .count.book')!.textContent!.trim()).toBe('1');
    expect(el.querySelectorAll('app-eval-graph .dot').length).toBe(0);
  });

  // Zwei Durchgänge (0.523.0): nach dem schnellen ist die Analyse „done", die Vertiefung läuft im Hintergrund.
  it('Vertiefung: Text statt Knopf-Sperre, gemächlich alle 60 s nachfragen — und Ruhe, sobald sie fertig ist', fakeAsync(() => {
    const { fixture, http, el, statuses } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { games: { review: { refining: 'Deeper {{done}}/{{total}}' } } });
    TestBed.inject(TranslateService).use('en');
    http.expectOne(url).flush({ ...evals('done'), refining: true, refined: 1 });
    fixture.detectChanges();

    expect(statuses).toEqual(['done']);                       // die Seite blendet ihren Knopf aus
    expect(el.querySelector('.progress')!.textContent).toContain('Deeper 1/2');

    tick(GameReviewComponent.PollMs);
    http.expectNone(url);                                     // nicht im 10-s-Takt
    tick(GameReviewComponent.RefinePollMs - GameReviewComponent.PollMs);
    http.expectOne(url).flush({ ...evals('done'), refining: false, refined: 2 });
    fixture.detectChanges();
    expect(el.querySelector('.progress')).toBeNull();

    tick(GameReviewComponent.RefinePollMs * 2);
    http.expectNone(url);
  }));

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

  it('Fehler-Erklärungen (0.534.0): Knopf beim Besitzer, danach nachfragen, Text beim aktuellen Zug, im Training aus', fakeAsync(() => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    const ex = '/api/games/4/explanations';
    http.expectOne(r => r.url === ex && r.method === 'GET' && r.params.get('lang') === 'en')
      .flush({ available: true, canGenerate: true, running: false, language: 'en', items: [] });
    fixture.detectChanges();

    const btn = el.querySelector('.explain-btn') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    btn.click();
    http.expectOne(r => r.url === ex && r.method === 'POST')
      .flush({ available: true, canGenerate: false, running: true, language: 'en', items: [] });
    fixture.detectChanges();
    expect(el.querySelector('.explain-btn')).toBeNull();
    expect(el.querySelector('.explaining')).not.toBeNull();

    tick(5000);
    http.expectOne(r => r.url === ex && r.method === 'GET').flush({
      available: true, canGenerate: false, running: false, language: 'en',
      items: [{ ply: 1, class: 'blunder', text: 'c5 hands White the centre.' }],
    });
    fixture.detectChanges();
    expect(el.querySelector('.explaining')).toBeNull();
    expect(el.querySelector('.explain')).toBeNull();   // Startstellung: kein Zug

    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();
    expect(el.querySelector('.explain')!.textContent).toContain('c5 hands White the centre.');

    // Im Fehler-Training verriete der Text den besseren Zug.
    fixture.componentRef.setInput('engineHidden', true);
    fixture.detectChanges();
    expect(el.querySelector('.explain')).toBeNull();
  }));

  it('Meisterkommentar (0.542.0): Quelle unter der Erklärung, Wortlaut zum Aufklappen; ohne Kommentar keine Zeile', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/games/4/explanations').flush({
      available: true, canGenerate: false, running: false, language: 'en',
      items: [
        {
          ply: 1, class: 'blunder', text: 'c5 hands White the centre.',
          master: { libraryGameId: 7, white: 'Anderssen', black: 'Kieseritzky', event: 'London', year: 1851, annotator: 'Steinitz',
                    text: '1...c5: Too early — White takes the centre at once.' },
        },
      ],
    });
    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();

    const master = el.querySelector('details.explain-master') as HTMLElement;
    expect(master.querySelector('summary')!.textContent).toContain('games.review.master');
    expect(fixture.componentInstance.masterGame({ libraryGameId: 7, white: 'Anderssen', black: 'Kieseritzky', event: 'London', year: 1851, text: '' }))
      .toBe('Anderssen – Kieseritzky, London 1851');
    expect(master.querySelector('q')!.textContent).toContain('Too early');

    // Ohne Meisterkommentar: nur die Erklärung.
    fixture.componentInstance.explanations.set({
      available: true, canGenerate: false, running: false, language: 'en',
      items: [{ ply: 1, class: 'blunder', text: 'Without a source.' }],
    });
    fixture.detectChanges();
    expect(el.querySelector('.explain')!.textContent).toContain('Without a source.');
    expect(el.querySelector('details.explain-master')).toBeNull();
  });

  it('Fehler-Erklärungen in der Sperrzeit der Spark (0.546.0): Hinweis mit Uhrzeit statt Knopf', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/games/4/explanations').flush({
      available: true, canGenerate: false, running: false, quietUntil: '2026-09-25T15:00:00Z', language: 'en', items: [],
    });
    fixture.detectChanges();
    expect(el.querySelector('.explain-btn')).toBeNull();
    const quiet = el.querySelector('.explain-quiet') as HTMLElement;
    expect(quiet).not.toBeNull();
    expect(quiet.textContent).toContain('games.review.quietHours');
    expect(fixture.componentInstance.explainQuietTime()).not.toBe('');

    // Liegen schon Erklärungen vor, braucht es keinen Hinweis.
    fixture.componentInstance.explanations.set({
      available: true, canGenerate: false, running: false, quietUntil: '2026-09-25T15:00:00Z', language: 'en',
      items: [{ ply: 1, class: 'blunder', text: 'Already there.' }],
    });
    fixture.detectChanges();
    expect(el.querySelector('.explain-quiet')).toBeNull();
  });

  it('Fehler-Erklärungen: fremde Partie (kein Erzeugen) und ohne Modell — kein Knopf', () => {
    const { fixture, http, el } = setup();
    http.expectOne(url).flush(evals('done'));
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/games/4/explanations')
      .flush({ available: false, canGenerate: false, running: false, language: 'en', items: [] });
    fixture.detectChanges();
    expect(el.querySelector('.explain-btn')).toBeNull();
    expect(el.querySelector('.explaining')).toBeNull();
    expect(el.querySelector('.explain-quiet')).toBeNull();
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

    it('zehn Spalten (mit Buch); Zähler je Seite und Punkte in der Kurve auch für Great und Miss, in der Farbe der Tabelle', () => {
      const { fixture, http, el } = setup({ fens: sicilian, moves: sicilianMoves });
      http.expectOne(url).flush(sicilianEvals);
      fixture.detectChanges();

      expect(el.querySelectorAll('thead .sym').length).toBe(10);
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
