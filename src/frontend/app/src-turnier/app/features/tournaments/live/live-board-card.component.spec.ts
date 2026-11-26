import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { LiveBoardCardComponent } from './live-board-card.component';
import { LiveGame } from './live-boards.models';

const GAME: LiveGame = {
  id: 'g1', boardTag: '1.1', fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', lastMove: 'e2e4', plies: 1,
  result: '*', finished: false, whiteToMove: false, eval: { cp: 42, mate: null, source: 'engine', depth: 18 },
  white: { name: 'Weiß, Wilma', title: 'GM', elo: 2600, fideId: null, team: null, clockSeconds: 5400 },
  black: { name: 'Schwarz, Sepp', title: null, elo: 2500, fideId: null, team: null, clockSeconds: 5300 },
};

describe('LiveBoardCardComponent', () => {
  it('kennzeichnet die Quelle der Bewertung dezent (Tooltip) und zeigt die Tiefe', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    fixture.componentRef.setInput('game', GAME);
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('showEval', true);
    fixture.detectChanges();
    const line = (fixture.nativeElement as HTMLElement).querySelector('.lb-evaltext')!;
    expect(line.getAttribute('title')).toBe('tournaments.live.evalSource.engine');
    expect(line.textContent).toContain('+0.4');
    expect(line.textContent).toContain('tournaments.live.depth');
  });

  it('Analyse-Knopf (1.026.0): Link in neuem Tab, nur mit URL', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    fixture.componentRef.setInput('game', GAME);
    fixture.componentRef.setInput('board', 1);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.lb-analyse')).toBeNull();
    fixture.componentRef.setInput('analysisUrl', 'https://rookhub.oberschmid.homes/analysis?live=fnmEB1kH.RD8sbYw1');
    fixture.detectChanges();
    const a = el.querySelector('.lb-analyse') as HTMLAnchorElement;
    expect(a.href).toBe('https://rookhub.oberschmid.homes/analysis?live=fnmEB1kH.RD8sbYw1');
    expect(a.target).toBe('_blank');
    expect(a.getAttribute('aria-label')).toBe('tournaments.live.openAnalysis');
  });
  it('hält nach einem Zug den letzten Balkenwert, bis die neue Stellung bewertet ist; neue Partie beginnt leer', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    fixture.componentRef.setInput('game', GAME);
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('showEval', true);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.shownEval()?.cp).toBe(42);
    fixture.componentRef.setInput('game', { ...GAME, plies: 2, eval: null });
    fixture.detectChanges();
    expect(c.shownEval()?.cp).toBe(42);
    fixture.componentRef.setInput('game', { ...GAME, plies: 2, eval: { cp: -10, mate: null, source: 'engine', depth: 22 } });
    fixture.detectChanges();
    expect(c.shownEval()?.cp).toBe(-10);
    fixture.componentRef.setInput('game', { ...GAME, id: 'g2', eval: null });
    fixture.detectChanges();
    expect(c.shownEval()).toBeNull();
  });
  it('Zuganzahl im Kartenkopf: laufend der Zug am Brett, fertig der letzte, vor dem ersten Zug nichts', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { tournaments: { live: { moveN: 'Zug {{move}}', boardN: 'Brett {{board}}' } } });
    translate.use('de');
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    fixture.componentRef.setInput('game', { ...GAME, plies: 66, whiteToMove: true });
    fixture.componentRef.setInput('board', 3);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const move = () => el.querySelector('.lb-move')?.textContent?.trim() ?? null;
    expect(move()).toBe('Zug 34');                                             // 33 Züge gespielt, Weiß spielt den 34.
    expect(el.querySelector('.lb-move')!.getAttribute('title')).toBe('common.whiteToMove');
    fixture.componentRef.setInput('game', { ...GAME, plies: 67, whiteToMove: false });
    fixture.detectChanges();
    expect(move()).toBe('Zug 34');                                             // Schwarz am Zug im 34.
    fixture.componentRef.setInput('game', { ...GAME, plies: 81, finished: true, result: '1-0' });
    fixture.detectChanges();
    expect(move()).toBe('Zug 41');                                             // fertig: letzter gespielter Zug
    fixture.componentRef.setInput('game', { ...GAME, plies: 0 });
    fixture.detectChanges();
    expect(move()).toBeNull();
  });
  it('Balken genau so hoch wie das gezeichnete Brett, Mitte = Mitte des Bretts (0.0 zwischen 4. und 5. Reihe)', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    (fixture.nativeElement as HTMLElement).style.width = '303px';     // kein Vielfaches von acht: Chessground rastet ein
    document.body.appendChild(fixture.nativeElement);
    fixture.componentRef.setInput('game', GAME);
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('showEval', true);
    fixture.autoDetectChanges();
    const el = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 40 && !el.querySelector('.lb-boardrow.measured'); i++) {
      await new Promise(r => setTimeout(r, 25));
      fixture.componentInstance.measureBoard();
      await fixture.whenStable();
    }
    const bar = el.querySelector('.lb-evalbar')!.getBoundingClientRect();
    const board = el.querySelector('cg-board')!.getBoundingClientRect();
    expect(Math.abs(bar.height - board.height)).toBeLessThanOrEqual(1);
    expect(Math.abs((bar.top + bar.height / 2) - (board.top + board.height / 2))).toBeLessThanOrEqual(1);
    el.remove();
  });
});
