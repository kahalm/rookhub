import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { GameReviewComponent } from '../../features/games/game-review.component';
import { PgnViewerComponent } from './pgn-viewer.component';

describe('PgnViewerComponent', () => {
  async function setup(data: object = {}) {
    await TestBed.configureTestingModule({
      imports: [PgnViewerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();
    return TestBed.createComponent(PgnViewerComponent);
  }

  it('creates (template AOT-compiles + DI resolves)', async () => {
    const fixture = await setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Gemeldet 2026-09-23: im Dialog blieb das Brett am PC bei 400 px in einer 900-px-Kiste. Jetzt wächst es mit
  // dem Fenster, die Zugliste hat eine feste Breite daneben. Welche Regeln gelten, entscheidet der Viewport des
  // Karma-Browsers (Media-Query bei 768 px; der Launcher stellt 1400 × 900 ein) — der Spec prüft beide.
  it('uses the window: board follows the viewport formula, move list keeps a fixed width beside it', async () => {
    const fixture = await setup({ pgn: '[White "a"]\n[Black "b"]\n\n1. e4 e5 2. Nf3 Nc6 *' });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const board = el.querySelector('.board-wrap') as HTMLElement;
    const moves = el.querySelector('.moves-section') as HTMLElement;
    if (window.innerWidth > 768) {
      const expected = Math.min(720, Math.max(360, Math.min(window.innerHeight - 280, window.innerWidth - 480)));
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(expected));
      expect(Math.round(moves.getBoundingClientRect().width)).toBe(320);
      expect(Math.abs(moves.getBoundingClientRect().top - board.getBoundingClientRect().top)).toBeLessThan(2);
    } else {
      const section = el.querySelector('.board-section') as HTMLElement;
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(section.getBoundingClientRect().width));
      expect(moves.getBoundingClientRect().top).toBeGreaterThanOrEqual(board.getBoundingClientRect().bottom);
    }
  });

  // ----- Bewertungskurve + „Partie analysieren" (eigene Partie aus /games, seit 0.512.0) -----

  const pgn = '[White "a"]\n[Black "b"]\n\n1. e4 e5 2. Nf3 Nc6 *';
  const running = {
    status: 'running', analyzed: 1, total: 4, targetDepth: 20,
    plies: [{ ply: 0, cp: 30, depth: 20, bestUci: 'e2e4', playedUci: 'e2e4', playedCp: 30 }], final: null,
  };

  it('without analyzeUrl/evalsUrl: no button, no graph, no requests', async () => {
    const fixture = await setup({ pgn });
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone(() => true);
    expect(fixture.nativeElement.querySelector('button.analyze')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-game-review')).toBeNull();
  });

  it('analyzeUrl: the header offers „Analyse game", the click posts there and the graph under the board reloads', async () => {
    const fixture = await setup({ pgn, evalsUrl: '/api/games/4/evals', analyzeUrl: '/api/games/4/analyze' });
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/game-analyses/guess/status').flush({ engineAvailable: true, ownEngine: false, openGames: 0, maxGames: 5 });
    http.expectOne('/api/games/4/evals').flush({ status: 'none', analyzed: 0, total: 0, targetDepth: 0, plies: [], final: null });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.viewer-header button.analyze') as HTMLButtonElement;
    expect(button).not.toBeNull();
    button.click();
    http.expectOne({ method: 'POST', url: '/api/games/4/analyze' }).flush({ analysis: { id: 3 }, reused: false });
    http.expectOne('/api/games/4/evals').flush(running);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.board-section app-game-review .review')).not.toBeNull();
    // Läuft die Analyse, ist der Knopf gesperrt (gleiche Regel wie auf /g/).
    expect((fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('hands the moves to the review as UCI — the basis for Brilliant, Great and Miss', async () => {
    const fixture = await setup({ pgn, evalsUrl: '/api/games/4/evals' });
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/games/4/evals').flush(running);
    fixture.detectChanges();
    const review = fixture.debugElement.query(By.directive(GameReviewComponent)).componentInstance as GameReviewComponent;
    expect(review.ucis()).toEqual(['e2e4', 'e7e5', 'g1f3', 'b8c6']);
  });

  it('without an engine the button is disabled instead of failing on click', async () => {
    const fixture = await setup({ pgn, analyzeUrl: '/api/games/4/analyze' });
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/game-analyses/guess/status')
      .flush({ engineAvailable: false, ownEngine: false, openGames: 0, maxGames: 5 });
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('button.analyze') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('with a graph the board makes room for it, so the column still fits the 90-vh dialog', async () => {
    const fixture = await setup({ pgn, evalsUrl: '/api/games/4/evals' });
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/games/4/evals').flush(running);
    fixture.detectChanges();

    const body = fixture.nativeElement.querySelector('.viewer-body') as HTMLElement;
    expect(body.classList).toContain('with-review');
    if (window.innerWidth > 768) {
      const board = fixture.nativeElement.querySelector('.board-wrap') as HTMLElement;
      const expected = Math.min(720, Math.max(300, Math.min(window.innerHeight - 520, window.innerWidth - 480)));
      expect(Math.round(board.getBoundingClientRect().width)).toBe(Math.round(expected));
    }
  });
});
