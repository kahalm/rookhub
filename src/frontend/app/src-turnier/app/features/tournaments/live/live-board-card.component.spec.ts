import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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

  it('Nur ein Zug (1.033.0): Chip vor der Uhr der Seite am Zug, Tooltip bester/nächstbester, kein Pfeil, kein Sprung bei 240 px', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { tournaments: { live: { onlyMove: 'Nur ein Zug', onlyMoveTip: 'bester {{best}}, nächstbester {{second}}' } } });
    translate.use('de');
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '240px';
    document.body.appendChild(host);
    const plain = { ...GAME, white: { ...GAME.white, name: 'Sehr langer Name, Wilhelmine-Kunigunde' },
      black: { ...GAME.black, name: 'Noch längerer Name, Bartholomäus-Maximilian' } };
    fixture.componentRef.setInput('game', plain);
    fixture.componentRef.setInput('board', 1);
    fixture.componentRef.setInput('showEval', true);
    fixture.detectChanges();
    const rows = () => Array.from(host.querySelectorAll('.lb-player')) as HTMLElement[];
    const heights = rows().map(r => r.getBoundingClientRect().height);
    expect(host.querySelector('.lb-only')).toBeNull();
    // Schwarz am Zug (oben, nicht gedreht); Weiß-Sicht −1.5 / 0.0 (Server hat entschieden).
    const marked = { ...plain, eval: { cp: -150, mate: null, source: 'engine' as const, depth: 24, best: 'c7c5', secondCp: 0, onlyMove: true } };
    fixture.componentRef.setInput('game', marked);
    fixture.detectChanges();
    const chip = rows()[0].querySelector('.lb-only') as HTMLElement;
    expect(chip.textContent!.trim()).toBe('Nur ein Zug');
    expect(chip.getAttribute('title')).toBe('bester −1.5, nächstbester 0.0');
    expect(rows()[1].querySelector('.lb-only')).toBeNull();
    expect(rows().map(r => r.getBoundingClientRect().height)).toEqual(heights);          // kein Sprung
    const card = host.querySelector('.lb-card')!.getBoundingClientRect();
    expect(rows()[0].querySelector('.lb-clock')!.getBoundingClientRect().right).toBeLessThanOrEqual(card.right);
    expect(chip.getBoundingClientRect().right).toBeLessThanOrEqual(rows()[0].querySelector('.lb-clock')!.getBoundingClientRect().left);
    expect(fixture.componentInstance.bestArrows()).toEqual([]);                          // verrät den Zug nicht
    fixture.componentRef.setInput('flipped', true);                                        // Schwarz unten → Chip unten
    fixture.detectChanges();
    expect(rows()[0].querySelector('.lb-only')).toBeNull();
    expect(rows()[1].querySelector('.lb-only')).not.toBeNull();
    fixture.componentRef.setInput('game', { ...marked, fen: 'rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2', plies: 2,
      whiteToMove: true, eval: null });                                                     // nächster Zug: alter Wert gehalten, Chip weg
    fixture.detectChanges();
    expect(host.querySelector('.lb-only')).toBeNull();
    fixture.componentRef.setInput('game', marked);
    fixture.componentRef.setInput('showEval', false);
    fixture.detectChanges();
    expect(host.querySelector('.lb-only')).toBeNull();                                    // ohne Balken nichts
    fixture.componentRef.setInput('showEval', true);
    fixture.componentRef.setInput('game', { ...marked, result: '0-1', finished: true });
    fixture.detectChanges();
    expect(host.querySelector('.lb-only')).toBeNull();                                    // fertig → nichts
    host.remove();
  });

  it('Elo unter dem Namen (27.11.): bei 240 px und langem Namen kürzt nur der Name, die Elo bleibt ganz sichtbar', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '240px';
    document.body.appendChild(host);
    fixture.componentRef.setInput('game', { ...GAME, white: { ...GAME.white, name: 'Sehr langer Name, Wilhelmine-Kunigunde Maria' },
      black: { ...GAME.black, name: 'Noch längerer Name, Bartholomäus-Maximilian Josef' } });
    fixture.componentRef.setInput('board', 1);
    fixture.detectChanges();
    const card = host.querySelector('.lb-card')!.getBoundingClientRect();
    for (const row of Array.from(host.querySelectorAll('.lb-player')) as HTMLElement[]) {
      const name = row.querySelector('.lb-name') as HTMLElement;
      const elo = row.querySelector('.lb-elo') as HTMLElement;
      expect(name.scrollWidth).toBeGreaterThan(name.clientWidth);                       // Name kürzt mit „…“
      expect(elo.getBoundingClientRect().top).toBeGreaterThanOrEqual(name.getBoundingClientRect().bottom - 1);   // eigene Zeile darunter
      expect(elo.scrollWidth).toBeLessThanOrEqual(elo.clientWidth + 1);                 // Elo ungekürzt
      expect(elo.getBoundingClientRect().right).toBeLessThanOrEqual(card.right);
      expect(row.querySelector('.lb-clock')!.getBoundingClientRect().right).toBeLessThanOrEqual(card.right);
    }
    expect(host.querySelectorAll('.lb-elo')[0].textContent!.trim()).toBe('2500');      // oben Schwarz
    host.remove();
  });

  it('Brett drehen: Knopf im Kopf meldet turn; bei 240 px mit Analyse und ✕ passt der Kopf, nichts abgeschnitten', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
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

  describe('Vollbild (1.036.0)', () => {
    const PGN = '[Event "BL"]\n\n1. e4 { [%clk 1:30:00] } *';

    async function open(width: number, height: number) {
      await TestBed.configureTestingModule({
        imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      localStorage.setItem('rookhub_live_fs', JSON.stringify({ engine: false, lines: 3 }));   // kein Stockfish-Worker im Test
      const fixture = TestBed.createComponent(LiveBoardCardComponent);
      const host = fixture.nativeElement as HTMLElement;
      host.style.width = '240px';
      document.body.appendChild(host);
      fixture.componentRef.setInput('game', GAME);
      fixture.componentRef.setInput('board', 1);
      fixture.componentRef.setInput('roundId', 'fnmEB1kH');
      fixture.detectChanges();
      const c = fixture.componentInstance;
      // Fenstermaße stellt der Test über die Ansicht nach (Element-Vollbild gibt es in Headless-Chrome nicht verlässlich)
      spyOn(window, 'matchMedia').and.callFake((q: string) => ({
        matches: q.includes('landscape') ? width > height && width >= 600 : false,
        addEventListener: () => {}, removeEventListener: () => {},
      }) as unknown as MediaQueryList);
      (host.querySelector('.board-fs-btn') as HTMLButtonElement).click();
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      const req = http.expectOne('/api/live-boards/pgn/fnmEB1kH/g1');
      req.flush(PGN);
      fixture.detectChanges();
      await fixture.whenStable();
      return { fixture, host, c, http };
    }

    afterEach(() => localStorage.removeItem('rookhub_live_fs'));

    it('Vollbild-Knopf des Bretts öffnet die große Ansicht mit Zugliste; ✕ und Esc schließen sie', async () => {
      const { fixture, host, c } = await open(390, 800);
      const card = host.querySelector('.lb-card') as HTMLElement;
      expect(card.classList).toContain('lb-fs');
      expect(getComputedStyle(card).position).toBe('fixed');
      expect(host.querySelector('app-live-board-study')).not.toBeNull();
      expect(host.querySelectorAll('app-live-board-study .move').length).toBe(1);   // Zugliste aus dem PGN
      expect(host.querySelector('app-chess-board app-board-fullscreen-button')).toBeNull();   // im Vollbild kein zweiter Knopf
      (host.querySelector('.lb-fs-close') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(c.fs()).toBeFalse();
      expect(host.querySelector('app-live-board-study')).toBeNull();
      (host.querySelector('.board-fs-btn') as HTMLButtonElement).click();
      fixture.detectChanges();
      TestBed.inject(HttpTestingController).expectOne('/api/live-boards/pgn/fnmEB1kH/g1').flush(PGN);
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      fixture.detectChanges();
      expect(c.fs()).toBeFalse();
      host.remove();
    });

    it('Handy hochformat: Brett volle Breite, Zugliste darunter; ein neuer Live-Zug kommt dazu und das Brett folgt', async () => {
      const { fixture, host, c } = await open(390, 800);
      const card = (host.querySelector('.lb-card') as HTMLElement).getBoundingClientRect();
      const board = (host.querySelector('.lb-cg') as HTMLElement).getBoundingClientRect();
      const side = (host.querySelector('app-live-board-study') as HTMLElement).getBoundingClientRect();
      expect(board.width).toBeGreaterThan(card.width - 60);
      expect(side.top).toBeGreaterThanOrEqual(board.bottom);
      const afterE5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
      fixture.componentRef.setInput('game', { ...GAME, fen: afterE5, lastMove: 'e7e5', plies: 2, whiteToMove: true });
      fixture.detectChanges();
      expect(host.querySelectorAll('app-live-board-study .move').length).toBe(2);
      expect(c.boardFen()).toBe(afterE5);
      // zurückblättern: Brett bleibt dort, auch wenn weitere Züge kommen; Live-Balkenwert gilt dann nicht
      (host.querySelector('.ls-first') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(c.boardFen()).toContain('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR');
      expect(c.barEval()).toBeNull();
      expect(host.querySelector('.ls-tolive')).not.toBeNull();
      (host.querySelector('.ls-tolive') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(c.boardFen()).toBe(afterE5);
      expect(host.querySelector('.ls-live')).not.toBeNull();
      c.closeFs();
      host.remove();
    });

    it('breit (quer): Zugliste und Engine neben dem Brett, Brett so hoch wie möglich', async () => {
      const { host, c } = await open(1280, 720);
      expect(c.wide()).toBeTrue();
      const card = host.querySelector('.lb-card') as HTMLElement;
      expect(card.classList).toContain('lb-wide');
      const board = (host.querySelector('.lb-cg') as HTMLElement).getBoundingClientRect();
      const side = (host.querySelector('app-live-board-study') as HTMLElement).getBoundingClientRect();
      expect(side.left).toBeGreaterThanOrEqual(board.right);
      expect(board.width).toBeGreaterThan(200);
      c.closeFs();
      host.remove();
    });

    it('Selbst ziehen macht das Brett spielbar; Engine-Schalter merkt sich die Wahl', async () => {
      const { fixture, host, c } = await open(390, 800);
      (host.querySelector('.ls-own') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(c.study()!.ownMoves()).toBeTrue();
      expect(host.querySelector('.ls-own')!.getAttribute('aria-pressed')).toBe('true');
      expect(host.querySelector('.ls-hint')).not.toBeNull();
      c.study()!.play({ from: 'e7', to: 'e5', san: 'e5', fen: 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2' });
      fixture.detectChanges();
      expect(host.querySelector('.ls-var-san')!.textContent).toContain('e5');
      expect(c.atLive()).toBeFalse();
      expect(c.game().fen).toBe(GAME.fen);       // die Live-Partie bleibt unverändert
      c.closeFs();
      host.remove();
    });
  });

  it('Aufblitzen (1.042.0): Klasse je Art an der Karte, ohne Wert keine', async () => {
    await TestBed.configureTestingModule({
      imports: [LiveBoardCardComponent], providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(LiveBoardCardComponent);
    fixture.componentRef.setInput('game', GAME);
    fixture.componentRef.setInput('board', 1);
    fixture.detectChanges();
    const card = (fixture.nativeElement as HTMLElement).querySelector('.lb-card')!;
    expect(card.classList.contains('lb-flash-result') || card.classList.contains('lb-flash-swing')).toBeFalse();
    fixture.componentRef.setInput('flash', 'result');
    fixture.detectChanges();
    expect(card.classList).toContain('lb-flash-result');
    fixture.componentRef.setInput('flash', 'swing');
    fixture.detectChanges();
    expect(card.classList).toContain('lb-flash-swing');
    expect(card.classList).not.toContain('lb-flash-result');
    fixture.componentRef.setInput('flash', null);
    fixture.detectChanges();
    expect(card.classList).not.toContain('lb-flash-swing');
  });
});
