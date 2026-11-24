import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { SnackbarService } from '../../core/snackbar.service';
import { MaiaEngineService } from '../analysis/maia/maia-engine.service';
import { LiveEngineSession } from '../games/live-engine-session';
import { StockfishService } from '../puzzles/stockfish.service';
import { By } from '@angular/platform-browser';
import { Chess } from 'chess.js';
import { PuzzleBoardComponent } from '../puzzles/puzzle-board.component';
import { EbookBoardComponent, EbookBoardMode, ebookResultLabel } from './ebook-board.component';
import { EbookDiagram } from './ebook.service';
import { replayGame, sameResult, scoreCp } from './ebook-board.util';
import { PreferencesService } from '../../core/preferences.service';
import { BoardFsActionsComponent } from '../puzzles/board-fs-actions.component';

/** Woodpecker Nr. 1 (Hamppe – Steinitz): Schwarz am Zug, Lösung 17… Txh2+ 18. Kxh2 Th8+. */
const D: EbookDiagram = {
  id: 11, page: 33, x: 0, y: 0, w: 0.3, h: 0.3, fen: 'r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b - - 0 17', label: '1',
  caption: null, sideKnown: true, reportedByMe: false, reportCount: 0,
  solutionUci: 'h8h2 h1h2 a8h8', solutionSan: 'Rxh2+ Kxh2 Rh8+', solutionText: '17...Rxh2+! 18.Kxh2 Rh8+', solutionPage: 230, solutionSource: 'book',
};
const AFTER_RXH2 = 'r7/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5r/4RR1K w - - 0 18';
const AFTER_KXH2 = 'r7/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5K/4RR2 b - - 0 18';
const WRONG = 'r6r/1pp3k1/1b6/p2P1p2/P1N1p3/2P2PPn/BP5P/4RR1K w - - 1 18';

describe('EbookBoardComponent', () => {
  let maiaStatus: ReturnType<typeof signal<string>>;
  let maia: { status: () => string; progress: () => number; prepare: jasmine.Spy; download: jasmine.Spy; chooseMove: jasmine.Spy; release: jasmine.Spy };
  let stockfish: jasmine.SpyObj<StockfishService>;

  function make(d: EbookDiagram = D, defaultMode?: EbookBoardMode): ComponentFixture<EbookBoardComponent> {
    maiaStatus = signal('ready');
    maia = {
      status: () => maiaStatus(), progress: () => 0,
      prepare: jasmine.createSpy('prepare').and.resolveTo(true),
      download: jasmine.createSpy('download').and.resolveTo(true),
      chooseMove: jasmine.createSpy('chooseMove').and.resolveTo('h1h2'),
      release: jasmine.createSpy('release'),
    };
    stockfish = jasmine.createSpyObj<StockfishService>('StockfishService', ['getBestMove']);
    TestBed.configureTestingModule({
      imports: [EbookBoardComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting(),
        { provide: SnackbarService, useValue: { warn: jasmine.createSpy('warn'), quick: jasmine.createSpy('quick') } },
        { provide: MaiaEngineService, useValue: maia },
        { provide: StockfishService, useValue: stockfish },
      ],
    });
    const fixture = TestBed.createComponent(EbookBoardComponent);
    fixture.componentRef.setInput('diagram', d);
    fixture.componentRef.setInput('ebookId', 9);
    if (defaultMode) fixture.componentRef.setInput('defaultMode', defaultMode);
    fixture.detectChanges();
    return fixture;
  }

  /** Versuch beenden (wie am Ende der Linie). */
  const finish = (c: EbookBoardComponent, r: 'solved' | 'failed'): void => (c as unknown as { finish(r: string): void }).finish(r);

  beforeEach(() => { try { localStorage.removeItem('rh.ebooks.calc.11'); localStorage.removeItem(EbookBoardComponent.SOLVE_STYLE_KEY); } catch { /* egal */ } });
  afterEach(() => { try { localStorage.removeItem(EbookBoardComponent.SOLVE_STYLE_KEY); } catch { /* egal */ } });

  it('Lösen: Standard ist „Training“; die eigene Wahl wird gemerkt und gewinnt beim nächsten Brett', () => {
    const c = make().componentInstance;
    expect(c.solveStyle()).toBe('training');
    c.setSolveStyle('easy');
    expect(localStorage.getItem(EbookBoardComponent.SOLVE_STYLE_KEY)).toBe('easy');
    TestBed.resetTestingModule();
    expect(make().componentInstance.solveStyle()).toBe('easy');
    localStorage.setItem(EbookBoardComponent.SOLVE_STYLE_KEY, 'quatsch');
    expect(EbookBoardComponent.loadSolveStyle()).toBe('training');
  });

  it('„Automatisch weiter“: Standard an; nach dem Lösen sichtbarer Countdown am Knopf „Nächstes Diagramm“, dann solvedNext; aus wird auf dem Gerät gemerkt', fakeAsync(() => {
    try { localStorage.removeItem(EbookBoardComponent.AUTO_NEXT_KEY); } catch { /* egal */ }
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    const next = jasmine.createSpy('next');
    c.solvedNext.subscribe(next);
    expect(c.autoNext()).toBeTrue();
    expect(EbookBoardComponent.AUTO_NEXT_SECONDS).toBe(3);
    finish(c, 'solved');
    fixture.detectChanges();
    expect(el.querySelector('.eb-next')!.textContent).toContain('ebooks.reader.nextDiagram (3)');
    tick(1000);
    fixture.detectChanges();
    expect(el.querySelector('.eb-next')!.textContent).toContain('(2)');
    tick(1900);
    expect(next).not.toHaveBeenCalled();             // die eigene Lösung bleibt kurz sichtbar
    tick(200);
    expect(next).toHaveBeenCalledTimes(1);

    c.toggleAutoNext();
    expect(localStorage.getItem(EbookBoardComponent.AUTO_NEXT_KEY)).toBe('0');
    finish(c, 'solved');
    fixture.detectChanges();
    expect(el.querySelector('.eb-next')!.textContent!.trim()).toBe('ebooks.reader.nextDiagram');   // ohne Countdown
    tick(4000);
    expect(next).toHaveBeenCalledTimes(1);
    el.querySelector<HTMLButtonElement>('.eb-next')!.click();                                       // Weiter von Hand
    expect(next).toHaveBeenCalledTimes(2);
    try { localStorage.removeItem(EbookBoardComponent.AUTO_NEXT_KEY); } catch { /* egal */ }
  }));

  it('Weiter mit Leertaste/Enter nach dem Ende (wie im Puzzle-Löser) — nicht auf einem fokussierten Knopf, nicht während des Lösens, Pfeile bleiben beim Leser', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const next = jasmine.createSpy('next');
    c.solvedNext.subscribe(next);
    const key = (k: string, target: EventTarget = document.body): KeyboardEvent => {
      const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
      target.dispatchEvent(e);
      return e;
    };
    expect(key(' ').defaultPrevented).toBeFalse();   // läuft noch
    finish(c, 'solved');
    expect(key('ArrowRight').defaultPrevented).toBeFalse();
    const btn = document.createElement('button');
    document.body.appendChild(btn);
    key('Enter', btn);
    btn.remove();
    expect(next).not.toHaveBeenCalled();
    expect(key(' ').defaultPrevented).toBeTrue();
    expect(next).toHaveBeenCalledTimes(1);
    // Letztes Diagramm: kein Weiter, kein Countdown.
    fixture.componentRef.setInput('hasNext', false);
    finish(c, 'solved');
    fixture.detectChanges();
    expect(c.autoNextCountdown).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('.eb-next')).toBeNull();
    expect(key('Enter').defaultPrevented).toBeFalse();
    tick(4000);
    expect(next).toHaveBeenCalledTimes(1);
  }));

  it('kompakte Brett-Leiste: fünf Modi als Symbolknöpfe (Name im aria-label, gewählter mit aria-pressed), ⚙ nur bei Lösen/Kalkulieren/Maia', () => {
    const fixture = make();
    const el: HTMLElement = fixture.nativeElement;
    const modes = Array.from(el.querySelectorAll<HTMLButtonElement>('.eb-bar .eb-mode'));
    expect(modes.length).toBe(5);
    expect(modes.every(b => b.textContent!.trim() === b.querySelector('mat-icon')!.textContent!.trim())).toBeTrue();   // nur Symbol
    expect(modes[3].getAttribute('aria-label')).toContain('ebooks.board.mode.solve');
    // d7-rk-ebook-brett-tooltip-jargon: Name und Erklärung mit Halbgeviertstrich, keine Doppelpunkt-Kette.
    expect(modes[3].getAttribute('aria-label')).toBe('ebooks.board.mode.solve – ebooks.board.modeHint.solve');
    expect(modes[3].getAttribute('aria-pressed')).toBe('true');
    expect(modes[3].classList).toContain('eb-mode--on');
    expect(el.querySelector('.eb-bar .eb-settings')).not.toBeNull();
    expect(el.querySelector('.eb-options')).toBeNull();          // keine Umschaltgruppen mehr unter dem Brett
    expect(el.querySelector('.eb-actions')).toBeNull();          // Analysieren/Drehen/FEN im ⋮
    expect(el.querySelector('.eb-bar .eb-more')).not.toBeNull();
    fixture.componentInstance.setMode('free');
    fixture.detectChanges();
    expect(el.querySelector('.eb-bar .eb-settings')).toBeNull();
    fixture.componentInstance.setMode('calc');      // Kalkulieren: Art (einfach/Training) wählbar, 0.825.0
    fixture.detectChanges();
    expect(el.querySelector('.eb-bar .eb-settings')).not.toBeNull();
    fixture.componentInstance.setMode('maia');
    fixture.detectChanges();
    expect(el.querySelector('.eb-bar .eb-settings')).not.toBeNull();
    // Maia: Stärke steht in der Zugzeile.
    expect(el.querySelector('.eb-maia-state')!.textContent).toContain('ebooks.board.maiaElo');
  });

  it('gibt Maia beim Verlassen frei, wenn das Brett sie geholt hat — sonst nicht (F2-011)', () => {
    const used = make();
    used.detectChanges();
    used.componentInstance.setMode('maia');
    used.destroy();
    expect(maia.release).toHaveBeenCalledTimes(1);
    TestBed.resetTestingModule();
    const unused = make();
    unused.detectChanges();
    unused.destroy();
    expect(maia.release).not.toHaveBeenCalled();
  });

  it('⚙ beim Lösen: Art und Quelle als Auswahl im Menü; ⋮ meldet das Diagramm (nur „reportable“)', async () => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    el.querySelector<HTMLButtonElement>('.eb-settings')!.click();
    fixture.detectChanges(); await fixture.whenStable();
    const items = Array.from(document.querySelectorAll<HTMLButtonElement>('.cdk-overlay-container [role="menuitemradio"]'));
    expect(items.length).toBe(4);
    expect(items[1].getAttribute('aria-checked')).toBe('true');   // Training
    items[0].click();
    expect(c.solveStyle()).toBe('easy');
    fixture.detectChanges(); await fixture.whenStable();
    fixture.componentRef.setInput('reportable', true);
    fixture.detectChanges();
    const spy = jasmine.createSpy('report');
    c.report.subscribe(spy);
    el.querySelector<HTMLButtonElement>('.eb-more')!.click();
    fixture.detectChanges(); await fixture.whenStable();
    document.querySelector<HTMLButtonElement>('.cdk-overlay-container .eb-report-item')!.click();
    expect(spy).toHaveBeenCalled();
  });

  it('⋮ „Stellung korrigieren“ nur mit „correctable“; eine korrigierte Stellung (gleiche Id) zeigt das Brett sofort und beginnt von vorn', async () => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    el.querySelector<HTMLButtonElement>('.eb-more')!.click();
    fixture.detectChanges(); await fixture.whenStable();
    expect(document.querySelector('.cdk-overlay-container .eb-correct-item')).toBeNull();
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement | null)?.click();
    fixture.detectChanges(); await fixture.whenStable();

    fixture.componentRef.setInput('correctable', true);
    fixture.detectChanges();
    const spy = jasmine.createSpy('correct');
    c.correct.subscribe(spy);
    el.querySelector<HTMLButtonElement>('.eb-more')!.click();
    fixture.detectChanges(); await fixture.whenStable();
    document.querySelector<HTMLButtonElement>('.cdk-overlay-container .eb-correct-item')!.click();
    expect(spy).toHaveBeenCalled();

    c.setMode('free');
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 } as never);
    expect(c.played().length).toBe(1);
    const corrected = '4k3/8/8/8/8/8/4P3/4K3 w - - 0 1';
    fixture.componentRef.setInput('diagram', { ...D, fen: corrected });
    fixture.detectChanges();
    expect(c.boardFen()).toBe(corrected);
    expect(c.played()).toEqual([]);
  });

  it('Statuszeile beim Lösen: EINE Zeile mit Zustand und Lösung-zeigen-Knopf, Hinweis als Tooltip', () => {
    const fixture = make();
    const el: HTMLElement = fixture.nativeElement;
    const row = el.querySelector<HTMLElement>('.eb-solve')!;
    expect(row.getAttribute('title')).toBe('ebooks.board.anyMoveHint');
    const toggle = row.querySelector<HTMLButtonElement>('.eb-sol-toggle')!;
    expect(toggle).not.toBeNull();
    toggle.click();
    fixture.detectChanges();
    expect(el.querySelector('.eb-solution')).not.toBeNull();
    expect(el.querySelectorAll('.eb-note').length).toBe(0);   // kein eigener Hinweis-Absatz mehr
  });

  it('Training: Auge zeigt die aktuelle Stellung (nur ansehen), die Lösung hat die Glühbirne (Nutzerwunsch 25.10.2026)', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.eb-sol-toggle mat-icon')!.textContent!.trim()).toBe('lightbulb');
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    tick(EbookBoardComponent.REPLY_DELAY_MS);
    fixture.detectChanges();
    const eye = el.querySelector<HTMLButtonElement>('.eb-solve .eb-peek')!;
    expect(eye.querySelector('mat-icon')!.textContent!.trim()).toBe('visibility');
    eye.click();
    fixture.detectChanges();
    expect(el.querySelector('app-puzzle-board.eb-frozen')).toBeNull();
    const board = fixture.debugElement.query(By.directive(PuzzleBoardComponent)).componentInstance as PuzzleBoardComponent;
    expect(board.fen).toBe(AFTER_KXH2);         // aktuelle Stellung
    expect(board.viewOnly).toBeTrue();          // nur ansehen
    expect(el.querySelector('.eb-solution')).toBeNull();   // Lösung bleibt verdeckt
    eye.click();
    fixture.detectChanges();
    expect(el.querySelector('app-puzzle-board.eb-frozen')).not.toBeNull();
    // Neu anfangen deckt wieder zu.
    eye.click();
    c.reset();
    expect(c.peek()).toBeFalse();
  }));

  it('startet standardmäßig im Lösen-Modus; Schwarz am Zug dreht das Brett', () => {
    const c = make().componentInstance;
    expect(c.mode()).toBe('solve');
    expect(c.solveState()).toBe('running');
    expect(c.flipped()).toBeTrue();
    expect(c.analysisParams()).toEqual({ fen: D.fen, orientation: 'black', from: '/ebooks/9' });
  });

  it('der Startmodus des Buchs gilt beim ersten Diagramm, danach bleibt die eigene Wahl', () => {
    TestBed.resetTestingModule();
    const fixture = make(D, 'view');
    const c = fixture.componentInstance;
    expect(c.mode()).toBe('view');
    expect(c.playable()).toBeFalse();
    c.setMode('free');
    fixture.componentRef.setInput('diagram', { ...D, id: 12 });
    fixture.detectChanges();
    expect(c.mode()).toBe('free');
  });

  it('Frei ziehen: Züge mitgeschrieben, zurücknehmen, Buchstellung; neues Diagramm leert', () => {
    const fixture = make();
    const c = fixture.componentInstance;
    c.setMode('free');
    expect(c.playable()).toBeTrue();
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    c.onBoardMove({ from: 'h1', to: 'h2', san: 'Kxh2', fen: AFTER_KXH2 });
    expect(c.movesText).toBe('17… Rxh2+ 18. Kxh2');
    expect(c.analysisParams()['fen']).toBe(AFTER_KXH2);
    c.undo();
    expect(c.boardFen()).toBe(AFTER_RXH2);
    c.reset();
    expect(c.boardFen()).toBe(D.fen);
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    fixture.componentRef.setInput('diagram', { ...D, id: 12, fen: '8/8/8/8/8/8/8/K6k w - - 0 1' });
    fixture.detectChanges();
    expect(c.played().length).toBe(0);
    expect(c.flipped()).toBeFalse();
    expect(c.mode()).toBe('free');   // Modus bleibt beim Blättern
  });

  it('regelwidrige Buchstellung: nur Ansehen', () => {
    const c = make({ ...D, fen: '8/8/8/8/8/8/8/8 w - - 0 1' }).componentInstance;
    expect(c.legal()).toBeFalse();
    c.setMode('free');
    expect(c.mode()).toBe('view');
  });

  it('Lösen einfach (1.011.0): ein falscher Zug zählt auch hier als Fehler — kein „falsch“, Mausrutscher einmal, Buchzug angenommen, Ende „mehr oder weniger“', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    c.setSolveStyle('easy');
    c.setMode('solve');
    expect(c.solveState()).toBe('running');
    expect(c.solutionText).toBe('17… Rxh2+ 18. Kxh2 Rh8+');
    stockfish.getBestMove.and.rejectWith(new Error('weg'));
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.statusKey).toBe('solver.status.stockfishThinking');
    flushMicrotasks(); tick(700); flushMicrotasks();
    expect(c.played().length).toBe(1);   // Engine weg: keine Antwort
    expect(c.wrongAt()).toBe(0);
    expect(c.mistake()).toBeTrue();      // auch „Einfach“ merkt sich den Fehler
    expect(TestBed.inject(SnackbarService).quick).not.toHaveBeenCalled();   // kein „Falsch — such weiter“
    expect(c.statusKey).toBe('book.status.yourMoveVsStockfish');
    fixture.detectChanges();
    expect(el.querySelector('.eb-solve .eb-solve-text')!.textContent).toContain('book.status.yourMoveVsStockfish');
    expect(el.querySelector('.eb-solve mat-icon')!.textContent!.trim()).not.toBe('close');
    expect(c.canMouseslip).toBeTrue();
    c.mouseslip();
    expect(c.played().length).toBe(0);
    expect(c.wrongAt()).toBeNull();
    expect(c.canMouseslip).toBeFalse();  // einmal je Versuch
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(EbookBoardComponent.REPLY_DELAY_MS);
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+', 'Kxh2']);
    expect(c.playable()).toBeTrue();
    c.mouseslip();                       // verbraucht: bleibt stehen
    expect(c.played().length).toBe(2);
    c.onBoardMove({ from: 'a8', to: 'h8', san: 'Rh8+', fen: '7r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5K/4RR2 w - - 1 19' });
    expect(c.solveState()).toBe('solved');
    expect(c.resultKey).toBe('solver.status.correctKindOf');
    c.toggleAutoNext();
    c.reset();                           // „Nochmal“ nach dem Ende: neuer Versuch
    expect(c.solveState()).toBe('running');
    expect(c.mistake()).toBeFalse();
    expect(c.mouseslipUsed()).toBeFalse();
    c.toggleAutoNext();
  }));

  it('Lösen: Zurücksetzen mitten im Lösen wie im Puzzle-Löser — der Fehler bleibt gezählt, der Mausrutscher kommt zurück', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    fixture.detectChanges();
    expect(el.querySelector('.eb-reset')).toBeNull();   // noch nichts zurückzusetzen
    wrongThenReplies(['e1e2']);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    flushMicrotasks();
    c.mouseslip();
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    fixture.detectChanges();
    const reset = el.querySelector<HTMLButtonElement>('.eb-reset')!;
    expect(reset.textContent).toContain('solver.actions.reset');
    reset.click();
    expect(c.played()).toEqual([]);
    expect(c.solveState()).toBe('running');
    expect(c.mistake()).toBeTrue();
    expect(c.mouseslipUsed()).toBeFalse();
    tick(1000);
  }));

  it('Lösen: ein gleichwertiger Zug ist „auch gut“, wird aber zurückgenommen — man sucht den Buchzug weiter (kein Fehler)', fakeAsync(() => {
    const c = make().componentInstance;
    c.setMode('solve');
    c.setSolveStyle('training');
    let n = 0;
    stockfish.getBestMove.and.callFake(() => Promise.resolve({ move: 'e1e2', eval: '-5', score: { cp: n++ === 0 ? -500 : -520 } } as never));
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    flushMicrotasks();
    expect(TestBed.inject(SnackbarService).quick).toHaveBeenCalledWith('ebooks.board.alternativeFindBook');
    expect(c.deviated()).toBeFalse();
    expect(c.mistake()).toBeFalse();
    tick(700);
    expect(c.played().length).toBe(0);
    expect(c.solveState()).toBe('running');
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(500);
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+', 'Kxh2']);
  }));

  it('Lösen: ist der Buchzug als fraglich geprüft, zählt ein gleichwertiger Zug — die Antworten kommen dann von Stockfish', fakeAsync(() => {
    const c = make({ ...D, solutionCheck: 'doubtful' }).componentInstance;
    c.setMode('solve');
    // Stockfish: nach Sh3 wie nach Txh2+ beides klar für Schwarz (Weiß-Sicht −500 bzw. −520), dann Antwort „h1h2“.
    let n = 0;
    stockfish.getBestMove.and.callFake(() => Promise.resolve(
      (n++ < 2 ? { move: 'e1e2', eval: '-5', score: { cp: n === 1 ? -500 : -520 } } : { move: 'h1g2', eval: '0', score: { cp: 0 } }) as never));
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.checking()).toBeTrue();
    expect(c.playable()).toBeFalse();
    flushMicrotasks();
    expect(c.checking()).toBeFalse();
    expect(c.deviated()).toBeTrue();
    expect(c.played().length).toBe(1);   // Sh3 bleibt stehen
    tick(500); flushMicrotasks();
    expect(stockfish.getBestMove.calls.count()).toBe(3);
    expect(c.solveState()).toBe('running');
  }));

  it('gleiches Ergebnis: Remis bleibt Remis, Gewinn bleibt Gewinn, sonst höchstens 50 cp schlechter', () => {
    expect(sameResult(0, -60)).toBeTrue();     // beides Remis
    expect(sameResult(0, -120)).toBeFalse();   // Remis verspielt
    expect(sameResult(900, 350)).toBeTrue();   // gewinnt weiter
    expect(sameResult(900, 150)).toBeFalse();
    expect(sameResult(150, 110)).toBeTrue();   // 40 cp schlechter
    expect(scoreCp({ mate: 2 }, D.fen)).toBe(9998);
    expect(scoreCp({ mate: -3 }, D.fen)).toBe(-9997);
  });

  /** Stockfish-Attrappe: die ersten zwei Aufrufe (Prüfung) bewerten Sh3 deutlich schlechter als Txh2+ (→ falsch); die
   *  beste Antwort der ersten Suche ist die Widerlegung `replies[0]`, danach antwortet sie mit den übrigen der Reihe nach. */
  function wrongThenReplies(replies: string[]): void {
    let n = 0;
    stockfish.getBestMove.and.callFake(() => {
      const k = n++;
      return Promise.resolve((k < 2 ? { move: k === 0 ? replies[0] : 'e1e2', eval: '0', score: { cp: k === 0 ? 0 : -900 } }
        : { move: replies[k - 1] ?? '', eval: '0', score: { cp: 0 } }) as never);
    });
  }
  const fenAfter = (fen: string, ...ucis: string[]) => {
    const ch = new Chess(fen);
    for (const u of ucis) ch.move({ from: u.slice(0, 2), to: u.slice(2, 4) });
    return ch.fen();
  };

  it('Lösen Training (1.011.0, wie im Puzzle-Löser): ein falscher Zug bleibt stehen, Stockfish antwortet sofort (Tiefe aus den Einstellungen), kein „falsch“; Mausrutscher nimmt beide Halbzüge zurück; Ende „mehr oder weniger“', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    expect(c.solveStyle()).toBe('training');
    TestBed.inject(PreferencesService).bookStockfishDepth = 12;
    wrongThenReplies(['e1e2']);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.playable()).toBeFalse();                       // Stockfish rechnet
    flushMicrotasks();
    expect(c.wrongAt()).toBe(0);
    expect(c.mistake()).toBeTrue();
    expect(TestBed.inject(SnackbarService).quick).not.toHaveBeenCalled();
    expect(stockfish.getBestMove.calls.count()).toBe(2);   // Prüfung — deren beste Antwort ist die Widerlegung
    expect(stockfish.getBestMove.calls.first().args).toEqual([WRONG, 12]);
    expect(c.played().map(m => m.uci)).toEqual(['f4h3', 'e1e2']);   // Gegenzug gespielt
    expect(c.solveState()).toBe('running');                        // keine Lösung vorgespielt
    fixture.detectChanges();
    expect(el.querySelector('.eb-solve .eb-solve-text')!.textContent).toContain('book.status.yourMoveVsStockfish');
    expect(el.querySelector('.eb-solve--failed')).toBeNull();
    const slip = el.querySelector<HTMLButtonElement>('.eb-mouseslip')!;
    expect(slip.textContent).toContain('solver.actions.mouseslip');
    slip.click();
    expect(c.played()).toEqual([]);
    expect(c.wrongAt()).toBeNull();
    fixture.detectChanges();
    expect(el.querySelector('.eb-mouseslip')).toBeNull();          // einmal je Versuch
    // Jetzt die Lösung: gilt, aber „mehr oder weniger“.
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(EbookBoardComponent.REPLY_DELAY_MS);
    c.onBoardMove({ from: 'a8', to: 'h8', san: 'Rh8+', fen: fenAfter(AFTER_KXH2, 'a8h8') });
    expect(c.solveState()).toBe('solved');
    expect(c.mistake()).toBeTrue();
    fixture.detectChanges();
    expect(el.querySelector('.eb-solve--done .eb-solve-text')!.textContent).toContain('solver.status.correctKindOf');
    expect(el.querySelector('.eb-again')!.textContent).toContain('common.retry');
    tick(EbookBoardComponent.AUTO_NEXT_SECONDS * 1000);
  }));

  it('Lösen nach Fehlzug: abseits der Lösung weiterspielen (Stockfish antwortet); der Mausrutscher nimmt nur das letzte Paar zurück, der Fehlzug bleibt', fakeAsync(() => {
    const c = make().componentInstance;
    wrongThenReplies(['e1e2', 'e2e1']);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    flushMicrotasks();
    const off = fenAfter(WRONG, 'e1e2', 'a8a7');
    c.onBoardMove({ from: 'a8', to: 'a7', san: 'Ra7', fen: off });
    expect(c.played().length).toBe(3);
    flushMicrotasks();
    expect(c.played().map(m => m.uci)).toEqual(['f4h3', 'e1e2', 'a8a7', 'e2e1']);
    expect(c.solveState()).toBe('running');
    expect(c.shownHints).toEqual([]);
    c.showNextHint();                                    // abseits: die „falsch abgebogen“-Tipps
    expect(c.shownHints).toEqual(['puzzles.offPath.h1']);
    c.mouseslip();
    expect(c.played().map(m => m.uci)).toEqual(['f4h3', 'e1e2']);
    expect(c.wrongAt()).toBe(0);    // Fehlzug steht noch
    c.mouseslip();
    expect(c.played().length).toBe(2);
    expect(c.hintLadder.maxLevel).toBe(0);              // Tipps abseits zählen nicht
  }));

  it('Mausrutscher, während Stockfish noch rechnet: nimmt nur den Zug zurück, die späte Antwort wird verworfen', fakeAsync(() => {
    const c = make().componentInstance;
    let release!: (v: unknown) => void;
    stockfish.getBestMove.and.callFake(() => new Promise(r => { release = r; }) as never);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.canMouseslip).toBeTrue();
    c.mouseslip();
    expect(c.played()).toEqual([]);
    expect(c.checking()).toBeFalse();
    release({ move: 'e1e2', eval: '0', score: { cp: 0 } });
    flushMicrotasks();
    expect(c.played()).toEqual([]);
    expect(c.playable()).toBeTrue();
  }));

  it('Lösen nach Fehlzug: Diagrammwechsel, während Stockfish rechnet — die Antwort landet nicht auf dem neuen Diagramm', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const offPath = fenAfter(WRONG, 'e1e2', 'a8a7');
    let release!: (v: unknown) => void;
    let n = 0;
    stockfish.getBestMove.and.callFake(() => n++ < 2
      ? Promise.resolve({ move: 'e1e2', eval: '0', score: { cp: n === 1 ? 0 : -900 } } as never)
      : new Promise(r => { release = r; }) as never);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    flushMicrotasks();
    c.onBoardMove({ from: 'a8', to: 'a7', san: 'Ra7', fen: offPath });
    fixture.componentRef.setInput('diagram', { ...D, id: 12 });
    fixture.detectChanges();
    expect(c.wrongAt()).toBeNull();
    expect(c.mistake()).toBeFalse();
    release({ move: 'e2e1', eval: '0' });
    flushMicrotasks();
    expect(c.played()).toEqual([]);
    expect(c.solveState()).toBe('running');
  }));

  it('Lösen: ein abweichender Zug, der selbst mattsetzt, ist eine eigene Lösung — auch ohne Engine („Schachmatt!“, kein Auto-Weiter)', fakeAsync(() => {
    const Q: EbookDiagram = { ...D, id: 13, fen: '7k/8/6K1/8/8/8/8/Q7 w - - 0 1', solutionUci: 'a1a8', solutionSan: 'Qa8#', solutionText: '1.Qa8#' };
    const fixture = make(Q);
    const c = fixture.componentInstance;
    stockfish.getBestMove.and.rejectWith(new Error('weg'));
    c.onBoardMove({ from: 'a1', to: 'g7', san: 'Qg7#', fen: fenAfter(Q.fen, 'a1g7') });
    flushMicrotasks();
    expect(c.solveState()).toBe('solved');
    expect(c.alternative()).toBeTrue();
    expect(c.wrongAt()).toBeNull();
    expect(c.mistake()).toBeFalse();
    expect(c.resultKey).toBe('solver.status.checkmate');
    expect(c.autoNextCountdown).toBe(0);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.eb-solve-sub')!.textContent).toContain('solver.status.altSolution');
  }));

  describe('Matt abseits der Lösung (1.011.0, wie im Puzzle-Löser)', () => {
    /** Stockfish-Attrappe: die Prüfung bewertet den Zug als falsch (0 gegen +900 aus Sicht von Weiß), ihre beste Antwort
     *  ist `reply`; danach antwortet sie mit `more` der Reihe nach. */
    function wrongFor(reply: string, more: string[] = []): void {
      let n = 0;
      stockfish.getBestMove.and.callFake(() => {
        const k = n++;
        return Promise.resolve((k === 0 ? { move: reply, eval: '0', score: { cp: 0 } }
          : k === 1 ? { move: 'a1a2', eval: '0', score: { cp: 900 } } : { move: more[k - 2] ?? '', eval: '0', score: { cp: 0 } }) as never);
      });
    }

    it('nach einem Fehlzug selbst mattgesetzt → gelöst als eigene Lösung, wegen des Fehlers „mehr oder weniger“, kein Auto-Weiter', fakeAsync(() => {
      // Weiß Kg6, Tb1, Bh2 — Schwarz Kh8. Buchlinie 1.h3 Kg8 2.Tb8#.
      const W: EbookDiagram = { ...D, id: 15, fen: '7k/8/6K1/8/8/8/7P/1R6 w - - 0 1', solutionUci: 'h2h3 h8g8 b1b8', solutionSan: 'h3 Kg8 Rb8#', solutionText: '' };
      const fixture = make(W);
      const c = fixture.componentInstance;
      wrongFor('h8g8');
      c.onBoardMove({ from: 'h2', to: 'h4', san: 'h4', fen: fenAfter(W.fen, 'h2h4') });
      flushMicrotasks();
      expect(c.played().map(m => m.uci)).toEqual(['h2h4', 'h8g8']);
      expect(c.mistake()).toBeTrue();
      c.onBoardMove({ from: 'b1', to: 'b8', san: 'Rb8#', fen: fenAfter(W.fen, 'h2h4', 'h8g8', 'b1b8') });
      expect(c.solveState()).toBe('solved');
      expect(c.alternative()).toBeTrue();
      expect(c.resultKey).toBe('solver.status.correctKindOf');
      expect(c.autoNextCountdown).toBe(0);
      expect(c.boardFen()).toBe(fenAfter(W.fen, 'h2h4', 'h8g8', 'b1b8'));
    }));

    it('nach einem Fehlzug selbst mattgesetzt worden → nicht gelöst, das Matt bleibt auf dem Brett', fakeAsync(() => {
      // Grundreihe: Weiß Kg1, Ta4, Bf2/g2/h2 — Schwarz Kg8, Te8, Bf7/g7/h7. Buchlinie 1.h3 Kf8 2.Ta8.
      const B: EbookDiagram = { ...D, id: 16, fen: '4r1k1/5ppp/8/8/R7/8/5PPP/6K1 w - - 0 1', solutionUci: 'h2h3 g8f8 a4a8', solutionSan: 'h3 Kf8 Ra8', solutionText: '' };
      const fixture = make(B);
      const c = fixture.componentInstance;
      wrongFor('e8e1');
      c.onBoardMove({ from: 'a4', to: 'b4', san: 'Rb4', fen: fenAfter(B.fen, 'a4b4') });
      flushMicrotasks();
      const mated = fenAfter(B.fen, 'a4b4', 'e8e1');
      expect(new Chess(mated).isCheckmate()).toBeTrue();
      expect(c.solveState()).toBe('failed');
      expect(c.userMated).toBeTrue();
      expect(c.boardFen()).toBe(mated);                 // das Matt bleibt stehen
      expect(c.resultKey).toBe('solver.status.incorrect');
      fixture.detectChanges();
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelector('.eb-solve--failed .eb-solve-sub')!.textContent).toContain('puzzles.result.matedHint');
      expect(el.querySelector('.eb-again')).not.toBeNull();
      expect(el.querySelector('.eb-mouseslip')).toBeNull();
    }));
  });

  it('Tipps wie im Puzzle-Löser: drei Stufen je Zug (Zugart → Figur → Zug), neu für jeden Zug; alle drei geholt → „mehr oder weniger“, kein Auto-Weiter', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    fixture.detectChanges();
    const hintBtn = (): HTMLButtonElement | null => el.querySelector<HTMLButtonElement>('.eb-hint');
    expect(hintBtn()!.textContent).toContain('solver.hints.show');
    hintBtn()!.click();
    fixture.detectChanges();
    expect(c.shownHints).toEqual(['puzzles.hints.t1Check']);
    expect(hintBtn()!.textContent).toContain('solver.hints.next');
    expect(el.querySelectorAll('.eb-hints li').length).toBe(1);
    c.showNextHint();
    c.showNextHint();
    expect(c.shownHints).toEqual(['puzzles.hints.t1Check', 'puzzles.hints.t2Piece', 'puzzles.hints.t3Move']);
    fixture.detectChanges();
    expect(hintBtn()).toBeNull();                                    // nichts mehr aufzudecken
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    tick(EbookBoardComponent.REPLY_DELAY_MS);
    expect(c.hintLadder.level).toBe(0);                              // neuer Zug, Tipps wieder verdeckt
    expect(c.hintTexts.length).toBe(3);
    c.onFrozenMove({ orig: 'a8' as never, dest: 'h8' as never });
    expect(c.solveState()).toBe('solved');
    expect(c.hintLadder.failsSolve).toBeTrue();
    expect(c.resultKey).toBe('solver.status.correctKindOf');
    expect(c.autoNextCountdown).toBe(0);
  }));

  it('Zeiten wie im Puzzle-Löser: Antwort aus der Linie nach 400 ms, vorgespielter Gegnerzug nach 600 ms', fakeAsync(() => {
    expect(EbookBoardComponent.REPLY_DELAY_MS).toBe(400);
    expect(EbookBoardComponent.LEAD_DELAY_MS).toBe(600);
    const c = make().componentInstance;
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(399);
    expect(c.played().length).toBe(1);
    expect(c.premovable()).toBeTrue();                               // vorziehen, solange die Antwort aussteht
    tick(1);
    expect(c.played().length).toBe(2);
    expect(c.premovable()).toBeFalse();
  }));

  it('Einfach: Brett des Puzzle-Lösers — Thema/Figuren aus den Einstellungen, letzter Zug, Zugziele nur am Zug, gedreht zieht weiter die Farbe des Lösers; Vollbild mit Aktionsleiste ohne Aufgeben', fakeAsync(() => {
    try { localStorage.setItem(EbookBoardComponent.SOLVE_STYLE_KEY, 'easy'); } catch { /* egal */ }
    const fixture = make();
    const c = fixture.componentInstance;
    const prefs = TestBed.inject(PreferencesService);
    prefs.boardTheme = 'blue';
    prefs.pieceSet = 'merida';
    fixture.detectChanges();
    const board = (): PuzzleBoardComponent => fixture.debugElement.query(By.css('app-puzzle-board.eb-solve-board')).componentInstance as PuzzleBoardComponent;
    expect(board().boardTheme).toBe('blue');
    expect(board().pieceSet).toBe('merida');
    expect(board().allowFullscreen).toBeTrue();
    expect(board().orientation).toBe('black');
    expect(board().moveColor()).toBe('black');
    expect(board().dests.size).toBeGreaterThan(0);
    expect(board().lastMove).toBeUndefined();
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    fixture.detectChanges();
    expect(board().lastMove).toEqual(['h8' as never, 'h2' as never]);
    expect(board().dests.size).toBe(0);
    expect(board().premovable).toBeTrue();
    tick(EbookBoardComponent.REPLY_DELAY_MS);
    fixture.detectChanges();
    expect(board().lastMove).toEqual(['h1' as never, 'h2' as never]);
    c.flipped.set(false);                                            // gedreht: Weiß unten, gezogen wird weiter Schwarz
    fixture.detectChanges();
    expect(board().orientation).toBe('white');
    expect(board().moveColor()).toBe('black');
    const fs = fixture.debugElement.query(By.directive(BoardFsActionsComponent));
    expect(fs).not.toBeNull();
    expect(board().fen).toBe(AFTER_KXH2);
    const actions = fs.componentInstance as BoardFsActionsComponent;
    expect(actions.showGiveUp()).toBeFalse();
    expect(actions.showMouseslip).toBeTrue();
    expect(actions.state).toBe('AWAITING_USER_MOVE');
    try { localStorage.removeItem(EbookBoardComponent.SOLVE_STYLE_KEY); } catch { /* egal */ }
    prefs.boardTheme = 'brown';
    prefs.pieceSet = 'cburnett';
  }));

  it('Training: das stehende Brett hat jetzt Vollbild mit der Aktionsleiste des Puzzle-Lösers', () => {
    const fixture = make();
    const frozen = fixture.debugElement.query(By.css('app-puzzle-board.eb-frozen'));
    expect((frozen.componentInstance as PuzzleBoardComponent).allowFullscreen).toBeTrue();
    expect(frozen.query(By.directive(BoardFsActionsComponent))).not.toBeNull();
  });

  it('Lösen: ein Fehlzug, der die Partie beendet (Patt), ist das Ende — nicht gelöst (wie im Puzzle-Löser)', fakeAsync(() => {
    const P: EbookDiagram = { ...D, id: 14, fen: '7k/8/5K2/8/8/8/8/6Q1 w - - 0 1', solutionUci: 'g1g7', solutionSan: 'Qg7#', solutionText: '1.Qg7#' };
    const fixture = make(P);
    const c = fixture.componentInstance;
    let n = 0;
    stockfish.getBestMove.and.callFake(() => Promise.resolve({ move: '', eval: '0', score: n++ === 0 ? { cp: 0 } : { mate: 1 } } as never));
    const stale = fenAfter(P.fen, 'g1g6');
    expect(new Chess(stale).isStalemate()).toBeTrue();
    c.onBoardMove({ from: 'g1', to: 'g6', san: 'Qg6', fen: stale });
    flushMicrotasks(); tick(500); flushMicrotasks();
    expect(c.wrongAt()).toBe(0);
    expect(stockfish.getBestMove.calls.count()).toBe(2);   // nur die Prüfung, keine Antwort
    expect(c.solveState()).toBe('failed');
    expect(c.resultKey).toBe('solver.status.incorrect');
    expect(c.userMated).toBeFalse();
    expect(c.canMouseslip).toBeFalse();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.eb-solve--failed .eb-solve-text')!.textContent).toContain('solver.status.incorrect');
  }));

  it('Training (0.825.0, „wie beim Kurs“): beim Lösen und Kalkulieren bleibt die Buchstellung stehen, Züge gehen über das Viz-Brett', fakeAsync(() => {
    const fixture = make();
    const c = fixture.componentInstance;
    const el: HTMLElement = fixture.nativeElement;
    expect(c.solveStyle()).toBe('training');
    expect(c.frozen()).toBeTrue();
    expect(el.querySelector('app-puzzle-board.eb-frozen')).not.toBeNull();
    expect(el.querySelector('app-chess-board')).toBeNull();
    // Txh2+ auf dem eingefrorenen Brett: angenommen, Weiß antwortet — angezeigt bleibt die Buchstellung.
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+']);
    tick(500);
    expect(c.played().map(m => m.fen)).toEqual([AFTER_RXH2, AFTER_KXH2]);
    fixture.detectChanges();
    expect(c.opponentArrow()).toEqual(['h1' as never, 'h2' as never]);
    const board = fixture.debugElement.query(By.directive(PuzzleBoardComponent)).componentInstance as PuzzleBoardComponent;
    expect(board.fen).toBe(D.fen);              // Brett steht
    expect(board.actualFen).toBe(AFTER_KXH2);   // geprüft wird gegen die echte Stellung
    // Während der Antwort (Gegner am Zug) nimmt das Viz-Brett nichts an.
    c.reset();
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    c.onFrozenMove({ orig: 'h1' as never, dest: 'h2' as never });
    expect(c.played().length).toBe(1);
    tick(500);
    // Kalkulieren: ebenfalls eingefroren, Züge beider Seiten werden Linie.
    c.setMode('calc');
    fixture.detectChanges();
    expect(el.querySelector('app-puzzle-board.eb-frozen')).not.toBeNull();
    c.onFrozenMove({ orig: 'h8' as never, dest: 'h2' as never });
    c.onFrozenMove({ orig: 'h1' as never, dest: 'h2' as never });
    expect(c.boardFen()).toBe(AFTER_KXH2);
    // Einfach: normales Brett, Figuren ziehen mit.
    c.setSolveStyle('easy');
    fixture.detectChanges();
    expect(el.querySelector('app-puzzle-board')).toBeNull();
    expect(el.querySelector('app-chess-board')).not.toBeNull();
  }));

  it('Lösen gegen Stockfish: Linie wird im Browser gerechnet, dann gelöst wie die Buchlinie', fakeAsync(() => {
    const c = make().componentInstance;
    const answers = ['h8h2', 'h1h2', 'a8h8'];   // danach kein Zug mehr → Linie endet
    let k = 0;
    stockfish.getBestMove.and.callFake(() => Promise.resolve({ move: answers[k++] ?? '', eval: 0 } as never));
    c.setMode('solve');
    c.setSolveSource('engine');
    flushMicrotasks();
    expect(stockfish.getBestMove).toHaveBeenCalled();
    expect(c.engineLine()).toEqual(answers);
    expect(c.solveState()).toBe('running');
    expect(c.solutionText).toBe('17… Rxh2+ 18. Kxh2 Rh8#');
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(500);
    expect(c.played().length).toBe(2);
  }));

  describe('Wer löst (0.975.0, Server-Felder solvable/opponentFirst aus BookSolverLine.ForReader)', () => {
    /** „Alekhine: Move by Move“, S. 154: Schwarz am Zug, die Buchlinie endet mit dem Matt von WEISS (30…Ta8 31.Tb6 Txa7 32.Tb8#). */
    const A: EbookDiagram = {
      id: 31, page: 154, x: 0, y: 0, w: 0.3, h: 0.3, fen: '3r1k2/P4pbp/2R3p1/8/5P2/6P1/Pn3P1P/4R1K1 b - - 0 30', label: null,
      caption: null, sideKnown: true, reportedByMe: false, reportCount: 0,
      solutionUci: 'd8a8 c6b6 a8a7 b6b8', solutionSan: 'Ra8 Rb6 Rxa7 Rb8#', solutionText: '30...Ra8 31.Rb6 Rxa7 32.Rb8#', solutionSource: 'book',
      solutionCheck: 'confirmed', engineEval: -9998, solvable: true, opponentFirst: true,
    };
    /** Stellung nach einer UCI-Folge ab der Alekhine-Stellung und der letzte Zug als Brettzug. */
    function after(...ucis: string[]): { fen: string; move: { from: string; to: string; san: string; fen: string } } {
      const chess = new Chess(A.fen);
      let san = '';
      for (const u of ucis) san = chess.move({ from: u.slice(0, 2), to: u.slice(2, 4) }).san;
      const last = ucis[ucis.length - 1];
      return { fen: chess.fen(), move: { from: last.slice(0, 2), to: last.slice(2, 4), san, fen: chess.fen() } };
    }
    const statusText = (f: ComponentFixture<EbookBoardComponent>): string => {
      f.detectChanges();
      return (f.nativeElement as HTMLElement).querySelector('.eb-solve .eb-solve-text')!.firstChild!.textContent!.trim();
    };

    it('Gegner zuerst: der Gegnerzug wird vorgespielt, das Brett steht aus Sicht des Lösers, dann löst Weiß bis zum Matt', fakeAsync(() => {
      try { localStorage.setItem(EbookBoardComponent.SOLVE_STYLE_KEY, 'easy'); } catch { /* egal */ }
      const fixture = make(A);
      const c = fixture.componentInstance;
      expect(c.mode()).toBe('solve');
      expect(c.solveState()).toBe('running');
      expect(c.userColor).toBe('w');               // Schwarz ist am Zug, der Löser hat Weiß
      expect(c.flipped()).toBeFalse();             // Weiß unten
      expect(c.playable()).toBeFalse();            // erst zieht der Gegner
      expect(c.played().length).toBe(0);
      expect(statusText(fixture)).toBe('solver.status.watchOpponent');
      // Ein Zug vor dem Gegnerzug zählt nicht.
      c.onBoardMove(after('d8a8', 'c6b6').move as never);
      expect(c.played().length).toBe(0);

      tick(EbookBoardComponent.LEAD_DELAY_MS);
      expect(c.played().map(m => m.san)).toEqual(['Ra8']);
      expect(c.boardFen()).toBe(after('d8a8').fen);
      fixture.detectChanges();
      const board = fixture.debugElement.query(By.directive(PuzzleBoardComponent));
      expect((board.componentInstance as PuzzleBoardComponent).fen).toBe(after('d8a8').fen);   // der Zug steht auf dem Brett
      expect((board.componentInstance as PuzzleBoardComponent).orientation).toBe('white');
      expect((board.componentInstance as PuzzleBoardComponent).lastMove).toEqual(['d8' as never, 'a8' as never]);
      expect(c.playable()).toBeTrue();
      expect(statusText(fixture)).toBe('ebooks.board.yourMoveWhite');   // der Leser sagt, welche Farbe man spielt
      expect(c.movesText).toBe('30… Ra8');

      c.onBoardMove(after('d8a8', 'c6b6').move as never);
      expect(c.played().length).toBe(2);
      expect(c.solveState()).toBe('running');
      tick(500);
      expect(c.played().map(m => m.san)).toEqual(['Ra8', 'Rb6', 'Rxa7']);
      c.onBoardMove(after('d8a8', 'c6b6', 'a8a7', 'b6b8').move as never);
      expect(c.solveState()).toBe('solved');
      expect(c.mistake()).toBeFalse();
      expect(c.movesText).toBe('30… Ra8 31. Rb6 Rxa7 32. Rb8#');        // Zugnummern verrutschen nicht
      expect(c.solutionText).toBe('30… Ra8 31. Rb6 Rxa7 32. Rb8#');

      // „Nochmal“: wieder von vorn, wieder mit dem Gegnerzug.
      c.toggleAutoNext();
      c.reset();
      expect(c.played().length).toBe(0);
      tick(EbookBoardComponent.LEAD_DELAY_MS);
      expect(c.played().map(m => m.san)).toEqual(['Ra8']);
      c.toggleAutoNext();
      tick(EbookBoardComponent.AUTO_NEXT_SECONDS * 1000);
    }));

    it('Gegner zuerst: der Mausrutscher nimmt einen falschen Löserzug zurück, der Gegnerzug bleibt stehen; der Fehler zählt', fakeAsync(() => {
      const fixture = make(A);
      const c = fixture.componentInstance;
      expect(c.solveStyle()).toBe('training');
      tick(EbookBoardComponent.LEAD_DELAY_MS);
      stockfish.getBestMove.and.callFake((fen: string) => Promise.resolve({ move: 'a1a2', eval: '0', score: { cp: fen === after('d8a8', 'c6b6').fen ? 9000 : 0 } } as never));
      const wrong = after('d8a8', 'c6c7').move;
      c.onBoardMove(wrong as never);
      flushMicrotasks();
      tick(700); flushMicrotasks();
      // Die Widerlegung „a1a2“ passt nicht zur Stellung → keine Antwort; der Fehlzug steht, bis man ihn zurücknimmt.
      expect(c.played().map(m => m.san)).toEqual(['Ra8', 'Rc7']);
      expect(c.mistake()).toBeTrue();
      c.mouseslip();
      expect(c.played().map(m => m.san)).toEqual(['Ra8']);   // der vorgespielte Gegnerzug bleibt
      c.mouseslip();
      expect(c.played().map(m => m.san)).toEqual(['Ra8']);
      expect(c.playable()).toBeTrue();
    }));

    it('Gegner zuerst im Training: das stehende Brett zeigt die Stellung nach dem Gegnerzug, der Zug selbst ist der Pfeil', fakeAsync(() => {
      const fixture = make(A);
      const c = fixture.componentInstance;
      fixture.detectChanges();
      const board = (): PuzzleBoardComponent => fixture.debugElement.query(By.directive(PuzzleBoardComponent)).componentInstance as PuzzleBoardComponent;
      expect(board().fen).toBe(A.fen);
      expect(board().orientation).toBe('white');
      expect(c.opponentArrow()).toBeUndefined();
      tick(EbookBoardComponent.LEAD_DELAY_MS);
      fixture.detectChanges();
      expect(board().fen).toBe(after('d8a8').fen);
      expect(board().turnColor).toBe('white');
      expect(c.opponentArrow()).toEqual(['d8' as never, 'a8' as never]);
      c.onFrozenMove({ orig: 'c6' as never, dest: 'b6' as never });
      expect(c.played().map(m => m.san)).toEqual(['Ra8', 'Rb6']);
      expect(c.opponentArrow()).toBeUndefined();
      tick(500);
      fixture.detectChanges();
      expect(board().fen).toBe(after('d8a8').fen);                     // bleibt stehen
      expect(c.opponentArrow()).toEqual(['a8' as never, 'a7' as never]);
    }));

    it('Gegner zuerst gegen Stockfish: gerechnet wird ab der Stellung nach dem Gegnerzug, gelöst mit derselben Farbe', fakeAsync(() => {
      const c = make(A).componentInstance;
      const fens: string[] = [];
      const answers = ['c6b6', 'a8a7', 'b6b8'];
      let k = 0;
      stockfish.getBestMove.and.callFake((fen: string) => { fens.push(fen); return Promise.resolve({ move: answers[k++] ?? '', eval: 0 } as never); });
      c.setSolveSource('engine');
      flushMicrotasks();
      expect(fens[0]).toBe(after('d8a8').fen);
      expect(c.solutionLine).toEqual(['d8a8', 'c6b6', 'a8a7', 'b6b8']);
      expect(c.solutionText).toBe('30… Ra8 31. Rb6 Rxa7 32. Rb8#');
      expect(c.userColor).toBe('w');
      tick(EbookBoardComponent.LEAD_DELAY_MS);
      expect(c.played().map(m => m.san)).toEqual(['Ra8']);
      expect(c.playable()).toBeTrue();
    }));

    it('Gegner zuerst: außerhalb von „Lösen“ steht das Brett wie im Buch (Seite am Zug unten), Maia spielt man mit der Seite am Zug', fakeAsync(() => {
      const c = make(A).componentInstance;
      tick(EbookBoardComponent.LEAD_DELAY_MS);
      c.setMode('free');
      expect(c.flipped()).toBeTrue();
      expect(c.played().length).toBe(0);
      c.setMode('maia');
      expect(c.userColor).toBe('b');
      c.setMode('solve');
      expect(c.flipped()).toBeFalse();
      tick(EbookBoardComponent.LEAD_DELAY_MS);
    }));

    it('nicht lösbar: kein Modus „Lösen“, das Diagramm steht auf „Ansehen“ mit Grund und aufklappbarer Buchlinie; das nächste lösbare wird wieder gelöst', () => {
      const fixture = make({ ...D, id: 41, solvable: false, engineEval: -600 });
      const c = fixture.componentInstance;
      const el: HTMLElement = fixture.nativeElement;
      expect(c.mode()).toBe('view');
      expect(c.visibleModes).not.toContain('solve');
      expect(el.querySelectorAll('.eb-bar .eb-mode').length).toBe(4);                 // Knopf weg, nicht ausgegraut
      expect(Array.from(el.querySelectorAll<HTMLButtonElement>('.eb-bar .eb-mode')).every(b => !b.disabled)).toBeTrue();
      expect(el.querySelector('.eb-unsolvable .eb-solve-text')!.textContent).toContain('ebooks.board.notSolvable');
      c.setMode('solve');
      expect(c.mode()).toBe('view');
      // Lösung zeigen bleibt.
      el.querySelector<HTMLButtonElement>('.eb-unsolvable .eb-sol-toggle')!.click();
      fixture.detectChanges();
      expect(el.querySelector('.eb-solution .eb-solution-line')!.textContent).toBe('17… Rxh2+ 18. Kxh2 Rh8+');
      expect(el.querySelector('.eb-solution .eb-solution-text')!.textContent).toContain('17...Rxh2+!');
      expect(c.analysisParams()['fen']).toBe(D.fen);                                  // Analysebrett bleibt

      // Blättern: ein lösbares Diagramm steht wieder im Lösen-Modus (die Wahl „Lösen“ blieb), ohne Hinweis.
      fixture.componentRef.setInput('diagram', { ...D, id: 42, solvable: true });
      fixture.detectChanges();
      expect(c.mode()).toBe('solve');
      expect(c.solveState()).toBe('running');
      expect(el.querySelector('.eb-unsolvable')).toBeNull();
      expect(el.querySelectorAll('.eb-bar .eb-mode').length).toBe(5);
      // Wer auf einem nicht lösbaren Diagramm selbst einen Modus wählt, bleibt dabei.
      fixture.componentRef.setInput('diagram', { ...D, id: 43, solvable: false });
      fixture.detectChanges();
      c.setMode('free');                                                              // Frei ziehen bleibt
      expect(c.playable()).toBeTrue();
      fixture.componentRef.setInput('diagram', { ...D, id: 44, solvable: true });
      fixture.detectChanges();
      expect(c.mode()).toBe('free');
    });

    it('normale Aufgabe unverändert: Seite am Zug löst sofort, kein vorgespielter Zug, gewohnter Satz — auch bei alten Antworten ohne die Felder', fakeAsync(() => {
      for (const d of [D, { ...D, solvable: true, opponentFirst: false }]) {
        TestBed.resetTestingModule();
        const fixture = make(d);
        const c = fixture.componentInstance;
        expect(c.mode()).toBe('solve');
        expect(c.userColor).toBe('b');
        expect(c.flipped()).toBeTrue();
        expect(c.leadPending).toBeFalse();
        expect(c.playable()).toBeTrue();
        expect(statusText(fixture)).toBe('ebooks.reader.yourMove');
        tick(1000);
        expect(c.played().length).toBe(0);
        expect((fixture.nativeElement as HTMLElement).querySelector('.eb-unsolvable')).toBeNull();
      }
    }));
  });

  it('Engine an: die Stellung geht EINMAL an die Engine — kein Effekt-Kreislauf über die Signale der Sitzung (0.801.0)', () => {
    // Echtes sync liest und schreibt baseIndex; der Ersatz tut dasselbe, rechnet aber nicht (kein Worker im Test).
    const sync = spyOn(LiveEngineSession.prototype, 'sync').and.callFake(function (this: LiveEngineSession, i: number) {
      if (i !== this.baseIndex()) this.baseIndex.set(i);
    });
    spyOn(LiveEngineSession.prototype, 'destroy');
    const f = make();
    f.componentInstance.toggleEngine();
    f.detectChanges();
    f.detectChanges();
    expect(sync).toHaveBeenCalledTimes(1);
    expect(sync.calls.mostRecent().args[1]).toBe(D.fen);
  });

  it('ohne gespeicherte Lösung steht die Quelle auf Stockfish', () => {
    const c = make({ ...D, solutionUci: null, solutionSan: null }).componentInstance;
    expect(c.hasStoredSolution).toBeFalse();
    expect(c.solveSource()).toBe('engine');
  });

  it('Kalkulieren: Züge werden Linien, auf dem Gerät gemerkt, und eine Linie öffnet das Analysebrett', () => {
    const fixture = make();
    const c = fixture.componentInstance;
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    c.setMode('calc');
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    c.onBoardMove({ from: 'h1', to: 'h2', san: 'Kxh2', fen: AFTER_KXH2 });
    expect(c.boardFen()).toBe(AFTER_KXH2);
    c.calcNewLine();
    expect(c.boardFen()).toBe(D.fen);
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.calcLineCount).toBe(2);
    // Neu geladen: der Baum ist noch da.
    c.setMode('free');
    c.setMode('calc');
    expect(c.calcLineCount).toBe(2);
    const leaf = c.calcTree!.nodes.find(n => n.san === 'Kxh2')!.id;
    c.calcAnalyze(leaf);
    expect(router.navigate).toHaveBeenCalledWith(['/analysis'], jasmine.objectContaining({
      queryParams: jasmine.objectContaining({ fen: D.fen, moves: 'h8h2,h1h2' }),
    }));
  });

  it('Gegen Maia: Maia antwortet auf den eigenen Zug, Zurücknehmen nimmt beide Züge', fakeAsync(() => {
    const c = make().componentInstance;
    c.setMode('maia');
    expect(maia.prepare).toHaveBeenCalled();
    expect(c.userColor).toBe('b');
    expect(c.playable()).toBeTrue();
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    flushMicrotasks();
    expect(maia.chooseMove).toHaveBeenCalledWith(AFTER_RXH2, 1600);
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+', 'Kxh2']);
    c.undo();
    expect(c.played().length).toBe(0);
  }));

  it('Gegen Maia ohne Modell: Laden-Knopf, Brett gesperrt', () => {
    const fixture = make();
    const c = fixture.componentInstance;
    maiaStatus.set('missing');
    c.setMode('maia');
    fixture.detectChanges();
    expect(c.playable()).toBeFalse();
    const btn = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.eb-maia-load button')!;
    btn.click();
    expect(maia.download).toHaveBeenCalled();
  });
  it('c2-eb-brett-maia-leere-zugzeile: vor dem Laden keine leere Zugzeile, mit bereitem Maia schon', () => {
    const fixture = make();
    const c = fixture.componentInstance;
    maiaStatus.set('missing');
    c.setMode('maia');
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.eb-moves')).toBeNull();
    maiaStatus.set('ready');
    fixture.detectChanges();
    expect(el.querySelector('.eb-moves')).not.toBeNull();
  });

  const css = () => (((EbookBoardComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');

  it('c2-eb-board-leiste-laptop-falz: Brett ab 900 px höchstens Fensterhöhe minus Kopf und Bedienzeile, mind. 280 px', () => {
    const c = css();
    const base = c.slice(0, c.indexOf('@media'));
    // 0.785.x: EINE Leiste oben + kompakte Brett-Leiste — 220 px statt 64 + 330 px über/unter dem Brett.
    expect(base).toMatch(/\.eb-board(\[[^\]]*\])?\{width:clamp\(min\(280px,100%\),(?:calc\()?100dvh-250px\)?,100%\);margin-inline:auto/);
    expect(CSS.supports('width', 'clamp(min(280px, 100%), calc(100dvh - 250px), 100%)')).toBeTrue();
    // Unter 900 px (Vollbild am Handy) bleibt die eigene Regel.
    expect(c.slice(c.indexOf('@media(max-width:899.98px)'))).toMatch(/\.eb-board(\[[^\]]*\])?\{width:min\(100%,62dvh\)/);
  });

  it('c2-eb-board-modus-leiste-maia-allein, 0.785.x: am Handy alle Symbole der Leiste in EINER Zeile mit 44-px-Tippzielen', () => {
    const c = css();
    const at = c.indexOf('@media(max-width:599.98px)');
    expect(at).toBeGreaterThan(-1);
    const mobile = c.slice(at, c.indexOf('@media(max-width:899.98px)'));
    expect(mobile).toMatch(/\.eb-bar(\[[^\]]*\])?\.mat-mdc-icon-button(\[[^\]]*\])?\{[^}]*width:44px;height:44px/);
    expect(c.slice(0, at)).toMatch(/\.eb-bar(\[[^\]]*\])?\{display:flex;align-items:center/);
    expect(c.slice(0, at)).not.toContain('flex-wrap:wrap');   // eine Zeile, kein Umbruch
  });
  // d8-rk-hinweiszeile-auge-versetzt: im Kalkulieren steht das Auge nicht im Fließtext (Grundlinie, abgeblasst), sondern
  // wie beim Lösen in einer mittig ausgerichteten Flex-Zeile rechts neben dem Hinweis.
  it('d8: Kalkulieren — Auge sitzt mittig neben dem Hinweis, nicht im abgeblassten Absatz', () => {
    const fixture = make(D, 'calc');
    const c = fixture.componentInstance;
    c.setMode('calc');
    fixture.detectChanges();
    expect(c.frozen()).toBeTrue();
    const el: HTMLElement = fixture.nativeElement;
    document.body.appendChild(el);
    try {
      const row = el.querySelector<HTMLElement>('.eb-note-row')!;
      expect(getComputedStyle(row).display).toBe('flex');
      expect(getComputedStyle(row).alignItems).toBe('center');
      const eye = row.querySelector<HTMLElement>(':scope > .eb-peek')!;
      expect(eye).not.toBeNull();
      expect(el.querySelector('.eb-note .eb-peek')).toBeNull();
      expect(getComputedStyle(eye).opacity).toBe('1');
      const note = row.querySelector<HTMLElement>('.eb-note')!.getBoundingClientRect();
      const r = eye.getBoundingClientRect();
      expect(Math.abs((r.top + r.bottom) / 2 - (note.top + note.bottom) / 2)).toBeLessThanOrEqual(2);
      expect(r.left).toBeGreaterThanOrEqual(note.right - 1);
    } finally { el.remove(); }
  });

  // d4-rk-kalk-hilfetext-lang: unter dem Brett ein Satz, der Rest hinter dem ?-Symbol (DESIGN 2 Nr. 2).
  it('Kalkulieren: ein Satz Erklärung, Einzelheiten hinter ?', async () => {
    const fixture = make(D, 'calc');
    fixture.componentInstance.setMode('calc');
    fixture.detectChanges();
    const note = fixture.nativeElement.querySelector('.eb-note') as HTMLElement;
    expect(note.textContent).toContain('ebooks.board.calcHint');
    const help = note.querySelector('app-help-hint button') as HTMLElement;
    expect(help).not.toBeNull();
    expect(help.getAttribute('aria-label')).toBe('ebooks.board.calcHelp');
    for (const code of ['de', 'en', 'hr', 'hu']) {
      let json: any = null;
      for (const url of [`/i18n/${code}.json`, `/base/i18n/${code}.json`]) {
        const res = await fetch(url);
        if (res.ok) { json = await res.json(); break; }
      }
      const hint: string = json.ebooks.board.calcHint;
      expect(hint.trim().split(/[.!?](\s|$)/).filter(x => x && x.trim()).length).withContext(`${code}: ${hint}`).toBe(1);
      expect(hint).not.toContain('(');
      expect(json.ebooks.board.calcHelp).withContext(code).toBeTruthy();
    }
  });
  // ---------------------------------------------------------------- Partie (0.831.0)

  /** Spanisch nach 3… a6; das Diagramm zeigt die Stellung nach 3. Lb5 (Halbzug 5). */
  const GAME = { id: 5, white: 'Spassky, Boris', black: 'Tal, Mikhail', event: 'Riga', date: '1958', result: '1/2-1/2',
    moves: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'a6'], ply: 5 };
  const G_DIAGRAM: EbookDiagram = { ...D, id: 21, fen: 'r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3', hasGame: true,
    solutionUci: null, solutionSan: null, solutionText: null, solutionPage: null, solutionSource: null };

  function makeGame(d: EbookDiagram = G_DIAGRAM): { fixture: ComponentFixture<EbookBoardComponent>; http: HttpTestingController } {
    const fixture = make(d, 'game');
    const http = TestBed.inject(HttpTestingController);
    return { fixture, http };
  }

  it('d10-rk-ebook-partiekopf-pgn-rohwerte: PGN-Datum im Locale-Format, offenes „*“ weder im Kopf noch unter der Zugliste', () => {
    const { fixture, http } = makeGame();
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush({ ...GAME, white: 'Muster, Weiß', black: 'Probe, Schwarz', event: 'ui-test',
      date: '2026.10.29', result: '*' });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const head = el.querySelector('.eb-game-head.rh-dots')!;
    const spans = Array.from(head.querySelectorAll(':scope > span')).map(x => x.textContent!.trim());
    expect(spans.length).toBe(3);
    expect(spans[2]).not.toContain('2026.10.29');
    expect(spans[2]).toContain('2026');
    expect(head.textContent).not.toContain('*');
    expect(el.querySelector('.eb-gm-result')).toBeNull();
    expect(ebookResultLabel('1-0')).toBe('1–0');
    expect(ebookResultLabel('1/2-1/2')).toBe('½–½');
  });

  it('Partie: lädt die Partie, steht auf dem Diagrammzug und klickt sich durch die Hauptlinie', () => {
    const { fixture, http } = makeGame();
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush(GAME);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.gameState()).toBe('ready');
    expect(c.gamePly()).toBe(5);
    expect(c.boardFen()).toBe(G_DIAGRAM.fen);
    expect(c.gameHead).toEqual(['Spassky, Boris – Tal, Mikhail', 'Riga', '1958', '½–½']);
    c.gameStep(1);
    expect(c.boardFen()).toBe('r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4');
    c.gameGo(0);
    expect(c.boardFen()).toBe('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1');
    fixture.detectChanges();
    const moves = fixture.nativeElement.querySelectorAll('.eb-gm');
    expect(moves.length).toBe(6);
    (moves[2] as HTMLButtonElement).click();
    expect(c.gamePly()).toBe(3);
    http.verify();
  });

  // UI-Sweep 2026-11-10 (d10-rk-sanpipe-dialoge-ebook-zugliste): Zugliste und Nebenlinie in deutschen Figurenbuchstaben.
  it('Partie: Zugliste und Nebenlinie laufen durch die SanPipe (de: Sf3, Lb5, Sf6)', () => {
    const { fixture, http } = makeGame();
    TestBed.inject(TranslateService).use('de');
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush({ ...GAME, white: 'Muster, Weiß', black: 'Probe, Schwarz', event: 'ui-test' });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const moves = Array.from(el.querySelectorAll('.eb-gm')).map(m => m.textContent!.trim());
    expect(moves).toEqual(['e4', 'e5', 'Sf3', 'Sc6', 'Lb5', 'a6']);
    const c = fixture.componentInstance;
    c.onBoardMove({ from: 'g8', to: 'f6', san: 'Nf6', fen: 'r1bqkb1r/pppp1ppp/2n2n2/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4' });
    fixture.detectChanges();
    expect(el.querySelector('.eb-game-side .eb-moves-text')!.textContent).toContain('Sf6');
    expect(el.querySelector('.eb-game-side .eb-moves-text')!.textContent).not.toContain('Nf6');
    http.verify();
  });

  it('Partie: der nächste Partiezug bleibt in der Partie, ein anderer Zug beginnt eine Nebenlinie; „Zurück“ führt zurück', () => {
    const { fixture, http } = makeGame();
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush(GAME);
    const c = fixture.componentInstance;
    c.onBoardMove({ from: 'a7', to: 'a6', san: 'a6', fen: 'r1bqkbnr/1ppp1ppp/p1n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4' });
    expect(c.gamePly()).toBe(6);
    expect(c.played().length).toBe(0);
    c.gameGo(5);
    c.onBoardMove({ from: 'g8', to: 'f6', san: 'Nf6', fen: 'r1bqkb1r/pppp1ppp/2n2n2/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4' });
    expect(c.played().length).toBe(1);
    expect(c.sideLineText).toBe('3… Nf6');
    c.reset();
    expect(c.played().length).toBe(0);
    expect(c.boardFen()).toBe(G_DIAGRAM.fen);
  });

  it('Partie: Diagramm ohne Partie zeigt den Hinweis und fragt nicht nach', () => {
    const { fixture, http } = makeGame({ ...G_DIAGRAM, hasGame: false });
    fixture.detectChanges();
    http.expectNone('/api/ebooks/9/diagrams/21/game');
    expect(fixture.componentInstance.gameState()).toBe('missing');
    expect(fixture.componentInstance.playable()).toBeFalse();
  });

  it('Partie: der Modus-Knopf erscheint nur bei Diagrammen mit Partie', () => {
    const plain = make();
    expect(plain.nativeElement.querySelectorAll('.eb-mode').length).toBe(5);
    TestBed.resetTestingModule();
    const { fixture, http } = makeGame();
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush(GAME);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.eb-mode').length).toBe(6);
  });

  it('d9-rk-diagramm-knopf-gesperrt-ohne-grund: auf der Diagrammstellung eine Marke statt gesperrtem Knopf, sonst „Zum Diagramm“', () => {
    const { fixture, http } = makeGame();
    http.expectOne('/api/ebooks/9/diagrams/21/game').flush(GAME);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const c = fixture.componentInstance;
    expect(c.atGameDiagram()).toBeTrue();
    expect(el.querySelector('.eb-game-nav button.eb-game-diagram')).toBeNull();
    expect(el.querySelector('.eb-game-nav button:not([disabled]).mat-mdc-button:not(.mat-mdc-icon-button)')).toBeNull();
    expect(el.querySelector('.eb-game-at-diagram')?.textContent).toContain('ebooks.board.gameAtDiagram');
    c.gameStep(1);
    fixture.detectChanges();
    const btn = el.querySelector('.eb-game-nav button.eb-game-diagram') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(btn.disabled).toBeFalse();
    expect(btn.textContent).toContain('ebooks.board.gameToDiagram');
    btn.click();
    fixture.detectChanges();
    expect(c.gamePly()).toBe(5);
    expect(el.querySelector('.eb-game-at-diagram')).not.toBeNull();
  });

  it('Partie: eine unspielbare Zugfolge wird beim ersten falschen Zug abgeschnitten', () => {
    const line = replayGame({ ...GAME, moves: ['e4', 'e5', 'Qxf7', 'Nc6'] });
    expect(line.sans).toEqual(['e4', 'e5']);
    expect(line.fens.length).toBe(3);
  });

  // ---------------------------------------------------------------- Baum (0.881.0)

  describe('Baum', () => {
    // 1.e4 e5 2.Nf3 (2.d4) Nc6
    const TREE: [number, string][] = [[0, 'e4'], [1, 'e5'], [2, 'Nf3'], [2, 'd4'], [3, 'Nc6']];
    const AFTER_E5 = 'rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';
    const TD: EbookDiagram = { ...D, id: 31, fen: AFTER_E5.replace(' w KQkq - 0 2', ' b - - 0 1'), solutionUci: null, solutionSan: null, solutionText: null, solutionPage: null };

    beforeEach(() => { (EbookBoardComponent as unknown as { treeCache: unknown }).treeCache = null; });

    function makeTree(d: EbookDiagram = TD): { fixture: ComponentFixture<EbookBoardComponent>; http: HttpTestingController } {
      const fixture = make(d);
      fixture.componentRef.setInput('hasTree', true);
      fixture.componentRef.setInput('bookTitle', 'Danish Dynamite');
      fixture.detectChanges();
      const http = TestBed.inject(HttpTestingController);
      fixture.componentInstance.setMode('tree');
      http.expectOne('/api/ebooks/9/tree').flush({ nodes: TREE });
      fixture.detectChanges();
      return { fixture, http };
    }

    it('der Modus-Knopf erscheint nur bei Büchern mit Baum; „Baum“ braucht keine regelgerechte Diagrammstellung', () => {
      const fixture = make();
      expect(fixture.componentInstance.visibleModes).not.toContain('tree');
      fixture.componentRef.setInput('hasTree', true);
      expect(fixture.componentInstance.visibleModes).toContain('tree');
      expect(fixture.componentInstance.needsLegal('tree')).toBeFalse();
      expect(fixture.componentInstance.needsLegal('free')).toBeTrue();
    });

    it('findet die Diagrammstellung im Baum (Zugrecht egal) und zeigt Weg und Fortsetzungen', () => {
      const { fixture } = makeTree();
      const c = fixture.componentInstance;
      expect(c.treeCursor()).toBe(2);
      expect(c.treeNotInTree()).toBeFalse();
      expect(c.boardFen()).toBe(AFTER_E5);
      const el: HTMLElement = fixture.nativeElement;
      expect(el.querySelectorAll('app-ebook-tree-panel .etp-step').length).toBe(2);
      expect(el.querySelectorAll('app-ebook-tree-panel .etp-child').length).toBe(2);
    });

    it('nicht im Baum → Hinweis, Start bei der Grundstellung; der Baum wird je Buch nur einmal geholt', () => {
      const { fixture, http } = makeTree(D);
      const c = fixture.componentInstance;
      expect(c.treeNotInTree()).toBeTrue();
      expect(c.treeCursor()).toBe(0);
      expect(fixture.nativeElement.querySelector('.etp-not-in-tree')).not.toBeNull();
      fixture.componentRef.setInput('diagram', TD);
      fixture.detectChanges();
      http.expectNone('/api/ebooks/9/tree');
      expect(c.treeCursor()).toBe(2);
      expect(c.treeNotInTree()).toBeFalse();
    });

    it('Brettzug im Buch geht weiter, ein anderer wird mit „nicht im Buch“ zurückgenommen', fakeAsync(() => {
      const { fixture } = makeTree();
      const c = fixture.componentInstance;
      const snack = TestBed.inject(SnackbarService) as unknown as { quick: jasmine.Spy };
      c.onBoardMove({ from: 'd2', to: 'd4', san: 'd4', fen: 'x' });
      expect(c.treeCursor()).toBe(4);
      c.treeGo(2);
      c.onBoardMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: 'off-fen' });
      expect(c.treeCursor()).toBe(2);
      expect(c.boardFen()).toBe('off-fen');
      expect(c.playable()).toBeFalse();
      expect(snack.quick).toHaveBeenCalledWith('ebooks.tree.notInBook');
      tick(600);
      expect(c.boardFen()).toBe(AFTER_E5);
    }));

    it('← / → im Brett-Bereich: Elternknoten bzw. Hauptfortsetzung, Taste gilt als verarbeitet', () => {
      const { fixture } = makeTree();
      const c = fixture.componentInstance;
      const key = (k: string) => { const e = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }); fixture.nativeElement.dispatchEvent(e); return e; };
      expect(key('ArrowRight').defaultPrevented).toBeTrue();
      expect(c.treeCursor()).toBe(3);
      key('ArrowRight');
      expect(c.treeCursor()).toBe(5);
      key('ArrowLeft'); key('ArrowLeft');
      expect(c.treeCursor()).toBe(2);
      c.setMode('view');
      expect(key('ArrowLeft').defaultPrevented).toBeFalse();
    });

    it('Baum lässt sich nicht laden → Hinweis', () => {
      const fixture = make(TD);
      fixture.componentRef.setInput('hasTree', true);
      fixture.componentInstance.setMode('tree');
      TestBed.inject(HttpTestingController).expectOne('/api/ebooks/9/tree').flush({}, { status: 404, statusText: 'nf' });
      fixture.detectChanges();
      expect(fixture.componentInstance.treeState()).toBe('missing');
      expect(fixture.nativeElement.textContent).toContain('ebooks.tree.missing');
    });

    it('Startmodus „Baum“ ohne Baum fällt auf „Lösen“ zurück', () => {
      const fixture = make(TD, 'tree');
      expect(fixture.componentInstance.mode()).toBe('solve');
    });
  });

});

describe('Maia-Hinweis hält „etwa 46 MB“ zusammen (c2-eb-maia-46mb-umbruch)', () => {
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(`${lang}: Ungefähr-Wort, Zahl und Einheit mit U+00A0`, async () => {
      const d = await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json() as
        { ebooks: { board: { maiaMissing: string } }; analysis: { maia: { downloadInfo: string; downloadInfoNoStore: string } } };
      expect(d.ebooks.board.maiaMissing).toMatch(/\S 46 MB/);
      expect(d.analysis.maia.downloadInfo).toContain('46 MB');
      expect(d.analysis.maia.downloadInfoNoStore).toContain('46 MB');
    });
  }
});

describe('Tooltip „Lösen“ als Handlungssatz (d7-rk-ebook-brett-tooltip-jargon)', () => {
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(`${lang}: ohne Doppelpunkt und ohne interne Quellnamen`, async () => {
      const d = await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json() as
        { ebooks: { board: { modeHint: { solve: string } } } };
      const t = d.ebooks.board.modeHint.solve;
      expect(t).not.toContain(':');
      expect(t).not.toMatch(/Stockfish|engine|motor/i);
    });
  }
});
