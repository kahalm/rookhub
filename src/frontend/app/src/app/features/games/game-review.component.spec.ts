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
  /** Die Seite fragt zweimal: schnell ohne Buchzüge (`?book=0`), danach einmal voll (0.664.0). Beide bekommen dieselbe Antwort. */
  function flushEvals(http: HttpTestingController, body: object | string, opts?: { status: number; statusText: string }): void {
    http.expectOne(`${url}?book=0`).flush(body, opts);
    http.match(url).forEach(r => r.flush(body, opts));
  }

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
    flushEvals(http, { status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null });
    fixture.detectChanges();

    expect(el.querySelector('.review')).toBeNull();
    expect(statuses).toEqual(['none']);
  });

  it('läuft die Analyse: Kurve soweit da, Fortschritt, und alle 10 s nachfragen — bis sie fertig ist', fakeAsync(() => {
    const { fixture, http, statuses, el } = setup();
    flushEvals(http, evals('running', false));
    fixture.detectChanges();

    expect(el.querySelector('app-eval-graph')).not.toBeNull();
    expect(el.querySelector('.progress')).not.toBeNull();
    expect(statuses).toEqual(['running']);

    tick(GameReviewComponent.PollMs - 1);
    http.expectNone(url);
    tick(1);
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    expect(statuses).toEqual(['running', 'done']);
    expect(el.querySelector('.progress')).toBeNull();

    // Fertig = Ruhe: kein weiterer Abruf.
    tick(GameReviewComponent.PollMs * 3);
    http.expectNone(url);
  }));

  it('ein Aussetzer beim Nachfragen bricht das Nachfragen nicht ab', fakeAsync(() => {
    const { http } = setup();
    flushEvals(http, evals('running', false));
    tick(GameReviewComponent.PollMs);
    flushEvals(http, 'boom', { status: 502, statusText: 'Bad Gateway' });
    tick(GameReviewComponent.PollMs);
    flushEvals(http, evals('done'));
    tick(GameReviewComponent.PollMs);
    http.expectNone(url);
  }));

  it('läuft die Analyse, steht neben dem Fortschritt die Restdauer — ohne Tempo keine', () => {
    const { fixture, http, el } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { gameAnalysis: { eta: 'about {{eta}} left' } });
    TestBed.inject(TranslateService).use('en');
    flushEvals(http, { ...evals('running', false), etaMinutes: 11 });
    fixture.detectChanges();
    expect(el.querySelector('.progress')!.textContent).toContain('about 11 min left');

    fixture.componentInstance.reload();
    flushEvals(http, { ...evals('running', false), etaMinutes: null });
    fixture.detectChanges();
    expect(el.querySelector('.progress')!.textContent).not.toContain('left');
  });

  it('fertig = keine Restdauer, auch wenn der Server noch eine mitschickte', () => {
    const { fixture, http } = setup();
    flushEvals(http, { ...evals('done'), etaMinutes: 3 });
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

    // 0.745.0 (Wunsch 2026-10-10): standardmäßig an; ein Klick schaltet aus, und das Aus bleibt gemerkt
    it('standardmäßig an: die Linien der Stellung auf dem Brett; aus per Klick, gemerkt je Gerät', () => {
      const { fixture, http, el } = setup();
      flushEvals(http, withCandidates());
      fixture.detectChanges();
      // currentIndex −1 = Startstellung → Zeile 0
      expect(Array.from(el.querySelectorAll('.lines .line-san')).map(e => e.textContent!.trim())).toEqual(['1. e4 e5', '1. d4']);

      (el.querySelector('button.lines-toggle') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(el.querySelector('.lines')).toBeNull();
      expect(localStorage.getItem(GameReviewComponent.LinesKey)).toBe('0');
      (el.querySelector('button.lines-toggle') as HTMLButtonElement).click();
      fixture.detectChanges();

      fixture.componentRef.setInput('currentIndex', 0);
      fixture.detectChanges();
      expect(el.querySelector('.lines .line-san')!.textContent!.trim()).toBe('1... e5 2. Nf3');
      expect(el.querySelectorAll('.lines li.played').length).toBe(1);   // c5 wurde gespielt
    });

    it('Pfeil: an → der beste Zug der Stellung geht an die Seite; wechselt mit dem Zug', () => {
      const { fixture, http, el } = setup();
      const arrows: unknown[] = [];
      fixture.componentInstance.arrowsChange.subscribe(a => arrows.push(a));
      flushEvals(http, withCandidates());
      fixture.detectChanges();
      expect(arrows[arrows.length - 1]).toEqual([{ from: 'e2', to: 'e4' }]);   // standardmäßig an (0.745.0)

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
      flushEvals(http, withCandidates());
      fixture.detectChanges();
      expect(el.querySelector('.lines')).not.toBeNull();

      fixture.componentRef.setInput('engineHidden', true);
      fixture.detectChanges();
      expect(el.querySelector('.lines')).toBeNull();
      expect(el.querySelector('button.lines-toggle')).toBeNull();
      expect(arrows[arrows.length - 1]).toEqual([]);
    });

    // Gewünscht 2026-10-06: „die vorberechneten stockfishlines … wenn die für den aktuellen zug existieren einblenden".
    it('neben der Live-Engine: Linien des Partiezugs bleiben, Pfeil und Pfeil-Schalter nicht; in einer Nebenvariante keine Linien', () => {
      localStorage.setItem(GameReviewComponent.LinesKey, '1');
      localStorage.setItem(GameReviewComponent.ArrowKey, '1');
      const { fixture, http, el } = setup();
      const arrows: unknown[] = [];
      fixture.componentInstance.arrowsChange.subscribe(a => arrows.push(a));
      flushEvals(http, withCandidates());
      fixture.componentRef.setInput('liveEngine', true);
      fixture.detectChanges();

      expect(Array.from(el.querySelectorAll('.lines .line-san')).map(e => e.textContent!.trim())).toEqual(['1. e4 e5', '1. d4']);
      expect(el.querySelector('button.lines-toggle')).not.toBeNull();
      expect(el.querySelector('button.arrow-toggle')).toBeNull();     // das Brett trägt den Pfeil der Live-Engine
      expect(arrows[arrows.length - 1]).toEqual([]);

      fixture.componentRef.setInput('offGame', true);                 // eigener Zug auf dem Brett
      fixture.detectChanges();
      expect(el.querySelector('.lines')).toBeNull();

      fixture.componentRef.setInput('offGame', false);                // zurück zur Partie
      fixture.detectChanges();
      expect(el.querySelector('.lines')).not.toBeNull();
    });
  });

  // Gewünscht 2026-09-27 (chess.com-Screenshot): die Klasse des aktuellen Zugs sitzt als Symbol an der Figur.
  it('Brett-Symbol: die Klasse des aktuellen Zugs geht mit dem Zielfeld an die Seite; Startstellung und Training ohne', () => {
    const { fixture, http } = setup({ moves: [{ from: 'e2', to: 'e4' }, { from: 'c7', to: 'c5' }] });
    const badges: ({ square: string; svg: string } | null)[] = [];
    fixture.componentInstance.badgeChange.subscribe(b => badges.push(b));
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    expect(badges.filter(b => b !== null)).toEqual([]);          // Startstellung: kein Zug, kein Symbol

    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();
    const b = badges[badges.length - 1]!;
    expect(b.square).toBe('c5');
    expect(b.svg).toContain('#ca3431');                          // c5 verliert fast drei Bauern: grober Fehler

    fixture.componentRef.setInput('engineHidden', true);         // Training: das Brett zeigt anderes
    fixture.detectChanges();
    expect(badges[badges.length - 1]).toBeNull();

    fixture.componentRef.setInput('engineHidden', false);
    fixture.componentRef.setInput('liveEngine', true);           // Live-Engine: ebenso kein Symbol
    fixture.detectChanges();
    expect(badges[badges.length - 1]).toBeNull();
  });

  // Gewünscht 2026-09-24: die Kurve standardmäßig eingeklappt, auf Wunsch aufklappen.
  it('die Kurve ist zu, bis man auf die Überschrift klickt — Zähler und Genauigkeit stehen trotzdem da', () => {
    const { fixture, http, el } = setup({ graphClosed: true });
    flushEvals(http, evals('done'));
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
    flushEvals(http, { ...evals('done'), bookPlies: [0, 1] });
    fixture.componentRef.setInput('currentIndex', 1);
    fixture.detectChanges();

    expect(el.querySelector('.current')!.className).toContain('book');
    expect(el.querySelector('.current .why')!.textContent).toContain('In your repertoire');
    expect(el.querySelector('.row-white .count.book')!.textContent!.trim()).toBe('1');
    expect(el.querySelector('.row-black .count.book')!.textContent!.trim()).toBe('1');
    expect(el.querySelectorAll('app-eval-graph .dot').length).toBe(0);
  });

  // 0.664.0: die Kurve kommt sofort, die Buchzüge später — und ein späterer schneller Takt löscht sie nicht wieder.
  it('Buchzüge: erst schnell ohne, dann einmal voll nachgetragen; spätere Takte behalten sie', fakeAsync(() => {
    const { fixture, http, el } = setup();
    http.expectOne(`${url}?book=0`).flush({ ...evals('running'), bookPlies: [] });
    fixture.detectChanges();
    expect(el.querySelector('.review')).not.toBeNull();   // die Auswertung steht, bevor die Buchzüge da sind

    http.expectOne(url).flush({ ...evals('running'), bookPlies: [0, 1] });   // die volle Antwort
    tick(GameReviewComponent.PollMs);
    http.expectOne(`${url}?book=0`).flush({ ...evals('done'), bookPlies: [] });
    http.expectNone(url);                                  // nur EINMAL je Adresse voll
    fixture.detectChanges();

    expect(fixture.componentInstance.evals()!.bookPlies).toEqual([0, 1]);
  }));

  // Zwei Durchgänge (0.523.0): nach dem schnellen ist die Analyse „done", die Vertiefung läuft im Hintergrund.
  it('Vertiefung: Text statt Knopf-Sperre, gemächlich alle 60 s nachfragen — und Ruhe, sobald sie fertig ist', fakeAsync(() => {
    const { fixture, http, el, statuses } = setup();
    TestBed.inject(TranslateService).setTranslation('en', { games: { review: { refining: 'Deeper {{done}}/{{total}}' } } });
    TestBed.inject(TranslateService).use('en');
    flushEvals(http, { ...evals('done'), refining: true, refined: 1 });
    fixture.detectChanges();

    expect(statuses).toEqual(['done']);                       // die Seite blendet ihren Knopf aus
    expect(el.querySelector('.progress')!.textContent).toContain('Deeper 1/2');

    tick(GameReviewComponent.PollMs);
    http.expectNone(url);                                     // nicht im 10-s-Takt
    tick(GameReviewComponent.RefinePollMs - GameReviewComponent.PollMs);
    flushEvals(http, { ...evals('done'), refining: false, refined: 2 });
    fixture.detectChanges();
    expect(el.querySelector('.progress')).toBeNull();

    tick(GameReviewComponent.RefinePollMs * 2);
    http.expectNone(url);
  }));

  it('geschlossen = kein Nachfragen mehr', fakeAsync(() => {
    const { fixture, http } = setup();
    flushEvals(http, evals('pending', false));
    fixture.destroy();
    tick(GameReviewComponent.PollMs * 2);
    http.expectNone(url);
  }));

  // Gemeldet 2026-10-07: am Laptop ein waagrechter Scrollbalken unter der Tabelle (mit Lc0-Spalte).
  it('die Tabelle passt ohne Scrollbalken in 380 px (Platz für die Lc0-Spalte bleibt)', () => {
    const { fixture, http, el } = setup();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', { games: { review: { accuracy: 'Genauigkeit', white: 'Weiß', black: 'Schwarz' } } }, true);
    t.use('de');
    el.style.display = 'block';
    el.style.width = '380px';
    document.body.appendChild(el);
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    const wrap = el.querySelector('.table-wrap') as HTMLElement;
    expect(wrap.scrollWidth).toBeLessThanOrEqual(wrap.clientWidth);
    el.remove();
  });

  it('Klick aufs Klassen-Symbol bzw. auf die Zahl springt zum nächsten Zug dieser Klasse (0.688.0)', () => {
    const { fixture, http, el } = setup();
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    const clicked: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(p => clicked.push(p));

    (el.querySelector('tr.row-black td.count.blunder button') as HTMLButtonElement).click();
    expect(clicked.length).toBe(1);
    const blunderPly = clicked[0];
    expect(fixture.componentInstance.review().moves[blunderPly]!.cls).toBe('blunder');
    expect(fixture.componentInstance.review().moves[blunderPly]!.white).toBeFalse();

    (el.querySelector('thead button.jump.blunder') as HTMLButtonElement).click();
    expect(clicked[1]).toBe(blunderPly);
    // Ohne Treffer kein Knopf
    expect(el.querySelector('tr.row-white td.count.blunder button')).toBeNull();
  });

  it('fertig: Zähler je Seite, Genauigkeit, und die Klasse des AKTUELLEN Zugs', () => {
    const { fixture, http, el } = setup();
    flushEvals(http, evals('done'));
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
    flushEvals(http, evals('done'));
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
    flushEvals(http, evals('done'));
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

  it('Fehler-Erklärungen: fremde Partie (kein Erzeugen) und ohne Modell — kein Knopf', () => {
    const { fixture, http, el } = setup();
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/games/4/explanations')
      .flush({ available: false, canGenerate: false, running: false, language: 'en', items: [] });
    fixture.detectChanges();
    expect(el.querySelector('.explain-btn')).toBeNull();
    expect(el.querySelector('.explaining')).toBeNull();
  });

  it('Fehler und grobe Fehler gehen als Punkte in die Kurve', () => {
    const { fixture, http, el } = setup();
    flushEvals(http, evals('done'));
    fixture.detectChanges();
    expect(el.querySelectorAll('app-eval-graph .dot.blunder').length).toBe(1);
  });

  it('ein Klick in die Kurve geht als Zug-Index nach außen', () => {
    const { fixture, http, el } = setup();
    flushEvals(http, evals('done'));
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
      flushEvals(http, sicilianEvals);
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
      flushEvals(http, sicilianEvals);
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
      flushEvals(http, {
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
      flushEvals(http, sicilianEvals);
      fixture.detectChanges();
      expect(count(el, 'white', 'great')).toBe('0');
      expect(count(el, 'white', 'miss')).toBe('0');
      expect(count(el, 'white', 'blunder')).toBe('1');
      expect(el.querySelector('app-eval-graph .dot.great')).toBeNull();
    });
  });

  it('reload() fragt sofort neu (nach „Partie analysieren")', () => {
    const { fixture, http, statuses } = setup();
    flushEvals(http, { status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [] });
    fixture.componentInstance.reload();
    flushEvals(http, evals('pending', false));
    expect(statuses).toEqual(['none', 'pending']);
  });

  // 0.682.0: Umschalter „Stockfish | Lc0 | beide" — gewünscht 2026-10-06, „auch beide gleichzeitig anzeigen".
  describe('zweite Analyse derselben Partie', () => {
    const moves = [{ from: 'e2', to: 'e4', san: 'e4' }, { from: 'c7', to: 'c5', san: 'c5' }];
    const lc0 = (): GameEvals => ({
      ...evals('done'),
      plies: [
        { ply: 0, cp: 11, depth: 8, bestUci: 'd2d4', playedUci: 'e2e4', playedCp: 5, candidates: [{ uci: 'd2d4', cp: 11, pv: ['d2d4'] }] },
        { ply: 1, cp: 20, depth: 8, bestUci: 'e7e5', playedUci: 'c7c5', playedCp: 250, candidates: [{ uci: 'e7e5', cp: 20, pv: ['e7e5'] }] },
      ],
    });
    const stockfish = (): GameEvals => ({
      ...evals('done'),
      plies: [
        { ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30, candidates: [{ uci: 'e2e4', cp: 30, pv: ['e2e4'] }] },
        { ply: 1, cp: 25, depth: 20, bestUci: 'e7e5', playedUci: 'c7c5', playedCp: 300, candidates: [{ uci: 'e7e5', cp: 25, pv: ['e7e5'] }] },
      ],
    });
    beforeEach(() => {
      localStorage.removeItem(GameReviewComponent.ViewKey);
      localStorage.setItem(GameReviewComponent.LinesKey, '1');
    });
    afterEach(() => {
      localStorage.removeItem(GameReviewComponent.ViewKey);
      localStorage.removeItem(GameReviewComponent.LinesKey);
    });

    function withAlternative() {
      const ctx = setup({ moves });
      ctx.fixture.componentRef.setInput('withAlternatives', true);
      ctx.fixture.detectChanges();
      flushEvals(ctx.http, stockfish());
      const same = ctx.http.expectOne('/api/game-analyses/same-game');
      expect(same.request.body).toEqual({ ucis: ['e2e4', 'c7c5'] });
      same.flush([
        { id: 7, title: 'x', engineId: null, engineName: null, targetNodes: null, targetDepth: 30, multiPv: 3, status: 'done', analyzedPlies: 2, plyCount: 2 },
        { id: 8, title: 'x (lc0)', engineId: 'rhe_lc0', engineName: 'RookHub Spark Lc0', targetNodes: 50000, targetDepth: 30, multiPv: 3, status: 'done', analyzedPlies: 2, plyCount: 2 },
      ]);
      ctx.http.expectOne('/api/game-analyses/8/evals').flush(lc0());   // die mit ausdrücklicher Engine, Lc0
      ctx.fixture.detectChanges();
      return ctx;
    }
    const lineSans = (el: HTMLElement, sel = '.lines:not(.alt) .line-san') =>
      Array.from(el.querySelectorAll(sel)).map(e => e.textContent!.trim());

    it('findet die Lc0-Analyse derselben Partie und bietet den Umschalter an — Vorgabe „Beide" (0.745.0)', () => {
      const { el, fixture } = withAlternative();
      const toggles = Array.from(el.querySelectorAll('.engine-view mat-button-toggle')).map(t => t.textContent!.trim());
      expect(toggles).toEqual(['Stockfish', 'Lc0', 'games.review.engineBoth']);
      expect(fixture.componentInstance.view()).toBe('both');
      expect(lineSans(el)).toEqual(['1. e4']);
      expect(el.querySelector('.lines.alt')).not.toBeNull();   // Lc0-Linien darunter
    });

    it('„Lc0": Linien und Kurve aus der zweiten Analyse; gemerkt je Gerät', () => {
      const { el, fixture } = withAlternative();
      fixture.componentInstance.setView('alt');
      fixture.detectChanges();
      expect(lineSans(el)).toEqual(['1. d4']);
      expect(el.querySelector('.lines.alt')).toBeNull();
      expect(localStorage.getItem(GameReviewComponent.ViewKey)).toBe('alt');
    });

    it('„Beide": zwei Linienblöcke mit Etikett und die zweite Kurve über der ersten', () => {
      const { el, fixture } = withAlternative();
      fixture.componentInstance.setView('both');
      fixture.detectChanges();
      expect(lineSans(el)).toEqual(['1. e4']);
      expect(lineSans(el, '.lines.alt .line-san')).toEqual(['1. d4']);
      expect(Array.from(el.querySelectorAll('.lines-label')).map(e => e.textContent!.trim())).toEqual(['Stockfish', 'Lc0']);
      expect(fixture.componentInstance.overlay()?.length).toBe(3);   // Start + zwei Züge
      expect(el.querySelector('.alt-nodes')).toBeNull();   // keine Knoten gemeldet
      expect(el.querySelectorAll('app-eval-graph polyline.overlay').length).toBeGreaterThan(0);
    });

    it('„Beide": Lc0-Genauigkeit als eigene Spalte; uneinige Züge anspringbar (0.683.0)', () => {
      const { el, fixture } = withAlternative();
      const cmp = fixture.componentInstance;
      cmp.setView('both');
      fixture.detectChanges();
      expect(el.querySelector('th.acc-h.alt')?.textContent?.trim()).toBe('Lc0');
      expect(el.querySelectorAll('td.acc.alt').length).toBe(2);
      // 1.e4: beide finden ihn in Ordnung — keine Meinungsverschiedenheit
      expect(cmp.disagreements()).toEqual([]);
      expect(el.querySelector('.disagree')).toBeNull();

      // Lc0 sieht nach 1.e4 Schwarz klar vorn → für Lc0 ein grober Fehler, für Stockfish der beste Zug
      const harsh = lc0();
      harsh.plies[1] = { ...harsh.plies[1], cp: -400, candidates: [{ uci: 'e7e5', cp: -400, pv: ['e7e5'] }] };
      cmp.altEvals.set(harsh);
      fixture.detectChanges();
      expect(cmp.disagreements().map(d => d.ply)).toEqual([0]);
      expect(el.querySelector('.dis-title')?.textContent).toContain('games.review.disagree');
      expect(el.querySelector('.dis-move')?.textContent?.trim()).toBe('1. e4');
      expect(cmp.marks().find(m => m.ply === 0)?.kind).toBe('disagree');

      const jumped: number[] = [];
      cmp.moveClicked.subscribe(i => jumped.push(i));
      (el.querySelector('.dis-chip') as HTMLButtonElement).click();
      cmp.stepDisagree(1);
      expect(jumped).toEqual([0, 0]);
    });

    it('zeigt je Stellung die erreichten Knoten der zweiten Analyse (0.684.0)', () => {
      const { el, fixture } = withAlternative();
      const cmp = fixture.componentInstance;
      const withNodes = lc0();
      withNodes.plies[1] = { ...withNodes.plies[1], nodes: 89133 };
      cmp.altEvals.set(withNodes);
      fixture.componentRef.setInput('currentIndex', 0);   // Brett nach 1.e4 = Stellung von Halbzug 1
      cmp.setView('primary');
      fixture.detectChanges();
      expect(el.querySelector('.alt-nodes')).toBeNull();   // Ansicht „Stockfish"
      cmp.setView('alt');
      fixture.detectChanges();
      expect(cmp.altNodes()).toBe(89133);
      expect(el.querySelector('.alt-nodes')?.textContent).toContain('Lc0');
    });

    it('Tiefe Analyse: ist sie tiefer als das Hinterlegte, stehen ihre Linien unter der Partie (0.690.0)', () => {
      const { el, fixture } = withAlternative();
      const cmp = fixture.componentInstance;
      const start = cmp.fens()[0];
      const deep = (depth: number) => ({ id: 77, fen: start, title: 'Tiefe Analyse · Stockfish', engineId: 'rhe_sf', targetDepth: 40,
        multiPv: 3, status: 'running' as const, reachedDepth: depth, secondsSpent: 0, lastError: null, createdAt: '', updatedAt: '',
        lastRunAt: null, finishedAt: null, resultJson: JSON.stringify({ depth, pvs: [{ moves: ['g1f3'], cp: 40, depth }] }) });
      cmp.deepJobs.set([deep(12)]);   // flacher als die hinterlegten 20
      fixture.detectChanges();
      expect(lineSans(el)).toEqual(['1. e4']);
      expect(el.querySelector('.lines-label.deep')).toBeNull();

      cmp.deepJobs.set([deep(25)]);
      fixture.detectChanges();
      expect(lineSans(el)[0]).toContain('Nf3');
      expect(el.querySelector('.lines-label.deep')?.textContent).toContain('games.deep.reviewDepth');
    });

    it('nur „Stockfish": keine Lc0-Spalte, keine uneinigen Züge', () => {
      const { el, fixture } = withAlternative();
      fixture.componentInstance.setView('primary');
      fixture.detectChanges();
      expect(el.querySelector('th.acc-h.alt')).toBeNull();
      expect(fixture.componentInstance.disagreements()).toEqual([]);
    });

    it('ohne Anmeldung (withAlternatives aus) wird gar nicht erst gesucht', () => {
      const { http, el } = setup({ moves });
      flushEvals(http, stockfish());
      http.expectNone('/api/game-analyses/same-game');
      expect(el.querySelector('.engine-view')).toBeNull();
    });

    it('nur Analysen ohne ausdrückliche Engine: kein Umschalter (dasselbe wie die Kurve der Seite)', () => {
      const ctx = setup({ moves });
      ctx.fixture.componentRef.setInput('withAlternatives', true);
      ctx.fixture.detectChanges();
      flushEvals(ctx.http, stockfish());
      ctx.http.expectOne('/api/game-analyses/same-game').flush([
        { id: 7, title: 'x', engineId: null, engineName: null, targetNodes: null, targetDepth: 30, multiPv: 3, status: 'done', analyzedPlies: 2, plyCount: 2 },
      ]);
      ctx.fixture.detectChanges();
      ctx.http.expectNone('/api/game-analyses/7/evals');
      expect(ctx.el.querySelector('.engine-view')).toBeNull();
    });
  });
});

