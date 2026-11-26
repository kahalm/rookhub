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
    expect(move()).toBeNull();                                                 // fertig: nur das Ergebnis (26.11.) …
    expect(el.querySelector('.lb-result')!.getAttribute('title')).toBe('Zug 41');    // … die Zugzahl im Tooltip
    fixture.componentRef.setInput('game', { ...GAME, plies: 0 });
    fixture.detectChanges();
    expect(move()).toBeNull();
  });
  // 1.030.0 „Besten Zug zeigen“: Pfeil + SAN des besten Zugs — nur zur Bewertung der AKTUELLEN Stellung
  it('Besten Zug zeigen: Pfeil und SAN aus der Bewertung, kein Pfeil zum gehaltenen alten Wert, kein illegaler', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    const withBest = { ...GAME, eval: { cp: -20, mate: null, source: 'cloud' as const, depth: 40, best: 'c7c5' } };
    fixture.componentRef.setInput('game', withBest);
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('showEval', true);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const el = fixture.nativeElement as HTMLElement;
    expect(c.bestArrows()).toEqual([]);                                           // Schalter aus
    fixture.componentRef.setInput('showBest', true);
    fixture.detectChanges();
    expect(c.bestArrows()).toEqual([{ from: 'c7', to: 'c5', brush: 'blue' }]);
    expect(el.querySelector('.lb-best')!.textContent!.trim()).toBe('c5');
    // Nach dem nächsten Zug: Balken hält den alten Wert, der Pfeil gehörte zur Stellung davor → weg.
    fixture.componentRef.setInput('game', { ...GAME, fen: 'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', plies: 2, eval: null });
    fixture.detectChanges();
    expect(c.shownEval()?.cp).toBe(-20);
    expect(c.bestArrows()).toEqual([]);
    expect(el.querySelector('.lb-best')).toBeNull();
    fixture.componentRef.setInput('game', { ...withBest, eval: { ...withBest.eval, best: 'e2e4' } });   // Weiß-Zug, Schwarz am Zug
    fixture.detectChanges();
    expect(c.bestArrows()).toEqual([]);
    fixture.componentRef.setInput('showEval', false);
    fixture.componentRef.setInput('game', withBest);
    fixture.detectChanges();
    expect(c.bestArrows()).toEqual([]);                                           // ohne Balken keine Bewertung → kein Zug
  });

  it('Brett drehen: Knopf im Kopf meldet turn; bei 240 px mit Analyse und ✕ passt der Kopf, nichts abgeschnitten', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { tournaments: { live: { moveN: 'Zug {{move}}', boardN: 'Brett {{board}}' } } });
    translate.use('de');
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '240px';
    document.body.appendChild(host);
    fixture.componentRef.setInput('game', { ...GAME, plies: 66, whiteToMove: true });
    fixture.componentRef.setInput('board', 6);
    fixture.componentRef.setInput('caption', 'Pinggau-Friedberg – Schachklub Schwaz');
    fixture.componentRef.setInput('analysisUrl', 'https://rookhub.oberschmid.homes/analysis?live=fnmEB1kH.RD8sbYw1');
    fixture.componentRef.setInput('removable', true);
    fixture.detectChanges();
    let turns = 0;
    fixture.componentInstance.turn.subscribe(() => turns++);
    const btn = host.querySelector('.lb-turn') as HTMLButtonElement;
    expect(btn.getAttribute('aria-label')).toBe('tournaments.live.turnBoard');
    btn.click();
    expect(turns).toBe(1);
    // Die Symbolknöpfe ragen bewusst 4 px ins Polster (Glyphe bündig) — entscheidend: alles innerhalb der Karte.
    const card = host.querySelector('.lb-card')!.getBoundingClientRect();
    for (const sel of ['.lb-turn', '.lb-analyse', '.lb-remove', '.lb-move']) {
      const r = host.querySelector(sel)!.getBoundingClientRect();
      expect(r.right).toBeLessThanOrEqual(card.right - 1);
      expect(r.width).toBeGreaterThan(0);
    }
    expect((host.querySelector('.lb-board') as HTMLElement).getBoundingClientRect().width).toBeGreaterThanOrEqual(45);   // „Brett 6“ bleibt lesbar (mind. 3,5 em)
    host.remove();
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
  it('fertige Partie: Ergebnis einzeilig in schmaler Karte, keine Zugzahl daneben (26.11.: „½–½“ brach um)', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '250px';
    document.body.appendChild(host);
    fixture.componentRef.setInput('game', { ...GAME, plies: 27, result: '1/2-1/2', finished: true });
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('caption', 'FIDE World Chess Championship 2026 · Game 3');
    fixture.componentRef.setInput('analysisUrl', 'https://rookhub.oberschmid.homes/analysis?live=a.b');
    fixture.componentRef.setInput('removable', true);
    fixture.detectChanges();
    const res = host.querySelector('.lb-result') as HTMLElement;
    expect(res.textContent!.trim()).toBe('½–½');
    const lh = parseFloat(getComputedStyle(res).lineHeight);
    expect(res.getBoundingClientRect().height).toBeLessThan(lh * 1.6 + 2);   // eine Zeile (+ Rahmen)
    expect(host.querySelector('.lb-move')).toBeNull();
    host.remove();
  });
});
