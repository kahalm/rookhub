import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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

  function setup() {
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
    fixture.componentRef.setInput('fens', fens);
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

  it('reload() fragt sofort neu (nach „Partie analysieren")', () => {
    const { fixture, http, statuses } = setup();
    http.expectOne(url).flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [] });
    fixture.componentInstance.reload();
    http.expectOne(url).flush(evals('pending', false));
    expect(statuses).toEqual(['none', 'pending']);
  });
});
