import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { SnackbarService } from '../../core/snackbar.service';
import { MaiaEngineService } from '../analysis/maia/maia-engine.service';
import { LiveEngineSession } from '../games/live-engine-session';
import { StockfishService } from '../puzzles/stockfish.service';
import { EbookBoardComponent } from './ebook-board.component';
import { EbookDiagram } from './ebook.service';

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
  let maia: { status: () => string; progress: () => number; prepare: jasmine.Spy; download: jasmine.Spy; chooseMove: jasmine.Spy };
  let stockfish: jasmine.SpyObj<StockfishService>;

  function make(d: EbookDiagram = D, defaultMode?: 'view' | 'free' | 'calc' | 'solve' | 'maia'): ComponentFixture<EbookBoardComponent> {
    maiaStatus = signal('ready');
    maia = {
      status: () => maiaStatus(), progress: () => 0,
      prepare: jasmine.createSpy('prepare').and.resolveTo(true),
      download: jasmine.createSpy('download').and.resolveTo(true),
      chooseMove: jasmine.createSpy('chooseMove').and.resolveTo('h1h2'),
    };
    stockfish = jasmine.createSpyObj<StockfishService>('StockfishService', ['getBestMove']);
    TestBed.configureTestingModule({
      imports: [EbookBoardComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
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

  it('„Automatisch weiter“: Standard an; nach dem Lösen 3 s warten, dann solvedNext; aus wird auf dem Gerät gemerkt', fakeAsync(() => {
    try { localStorage.removeItem(EbookBoardComponent.AUTO_NEXT_KEY); } catch { /* egal */ }
    const c = make().componentInstance;
    const next = jasmine.createSpy('next');
    c.solvedNext.subscribe(next);
    expect(c.autoNext()).toBeTrue();
    expect(EbookBoardComponent.AUTO_NEXT_DELAY_MS).toBe(3000);
    (c as unknown as { markSolved(): void }).markSolved();
    tick(EbookBoardComponent.AUTO_NEXT_DELAY_MS - 100);
    expect(next).not.toHaveBeenCalled();             // die eigene Lösung bleibt kurz sichtbar
    tick(200);
    expect(next).toHaveBeenCalledTimes(1);

    c.toggleAutoNext();
    expect(localStorage.getItem(EbookBoardComponent.AUTO_NEXT_KEY)).toBe('0');
    (c as unknown as { markSolved(): void }).markSolved();
    tick(EbookBoardComponent.AUTO_NEXT_DELAY_MS + 50);
    expect(next).toHaveBeenCalledTimes(1);
    try { localStorage.removeItem(EbookBoardComponent.AUTO_NEXT_KEY); } catch { /* egal */ }
  }));

  it('kompakte Brett-Leiste: fünf Modi als Symbolknöpfe (Name im aria-label, gewählter mit aria-pressed), ⚙ nur bei Lösen/Maia', () => {
    const fixture = make();
    const el: HTMLElement = fixture.nativeElement;
    const modes = Array.from(el.querySelectorAll<HTMLButtonElement>('.eb-bar .eb-mode'));
    expect(modes.length).toBe(5);
    expect(modes.every(b => b.textContent!.trim() === b.querySelector('mat-icon')!.textContent!.trim())).toBeTrue();   // nur Symbol
    expect(modes[3].getAttribute('aria-label')).toContain('ebooks.board.mode.solve');
    expect(modes[3].getAttribute('aria-pressed')).toBe('true');
    expect(modes[3].classList).toContain('eb-mode--on');
    expect(el.querySelector('.eb-bar .eb-settings')).not.toBeNull();
    expect(el.querySelector('.eb-options')).toBeNull();          // keine Umschaltgruppen mehr unter dem Brett
    expect(el.querySelector('.eb-actions')).toBeNull();          // Analysieren/Drehen/FEN im ⋮
    expect(el.querySelector('.eb-bar .eb-more')).not.toBeNull();
    fixture.componentInstance.setMode('free');
    fixture.detectChanges();
    expect(el.querySelector('.eb-bar .eb-settings')).toBeNull();
    fixture.componentInstance.setMode('maia');
    fixture.detectChanges();
    expect(el.querySelector('.eb-bar .eb-settings')).not.toBeNull();
    // Maia: Stärke steht in der Zugzeile.
    expect(el.querySelector('.eb-maia-state')!.textContent).toContain('ebooks.board.maiaElo');
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

  it('Lösen einfach: falscher Zug wird zurückgenommen, Buchzug angenommen, Antwort kommt von selbst', fakeAsync(() => {
    const c = make().componentInstance;
    c.setSolveStyle('easy');
    c.setMode('solve');
    expect(c.solveState()).toBe('running');
    expect(c.solutionText).toBe('17… Rxh2+ 18. Kxh2 Rh8+');
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.played().length).toBe(1);
    tick(700);
    expect(c.played().length).toBe(0);
    c.onBoardMove({ from: 'h8', to: 'h2', san: 'Rxh2+', fen: AFTER_RXH2 });
    tick(500);
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+', 'Kxh2']);
    expect(c.playable()).toBeTrue();
    c.onBoardMove({ from: 'a8', to: 'h8', san: 'Rh8+', fen: '7r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5K/4RR2 w - - 1 19' });
    expect(c.solveState()).toBe('solved');
    c.reset();
    expect(c.solveState()).toBe('running');
  }));

  it('Lösen: ein abweichender Zug, der das Ergebnis hält, zählt auch — die Antworten kommen dann von Stockfish', fakeAsync(() => {
    const c = make().componentInstance;
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
    expect(EbookBoardComponent.sameResult(0, -60)).toBeTrue();     // beides Remis
    expect(EbookBoardComponent.sameResult(0, -120)).toBeFalse();   // Remis verspielt
    expect(EbookBoardComponent.sameResult(900, 350)).toBeTrue();   // gewinnt weiter
    expect(EbookBoardComponent.sameResult(900, 150)).toBeFalse();
    expect(EbookBoardComponent.sameResult(150, 110)).toBeTrue();   // 40 cp schlechter
    expect(EbookBoardComponent.scoreCp({ mate: 2 }, D.fen)).toBe(9998);
    expect(EbookBoardComponent.scoreCp({ mate: -3 }, D.fen)).toBe(-9997);
  });

  it('Lösen Training: ein falscher Zug sagt nur „falsch“, wird zurückgenommen, man sucht weiter; gelöst „mit Fehler“', fakeAsync(() => {
    const c = make().componentInstance;
    c.setMode('solve');
    c.setSolveStyle('training');
    // Stockfish hält Sh3 für deutlich schlechter als Txh2+ → falsch.
    let n = 0;
    stockfish.getBestMove.and.callFake(() => Promise.resolve({ move: 'a1a2', eval: '0', score: { cp: n++ === 0 ? 0 : -900 } } as never));
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    flushMicrotasks();
    tick(700);
    expect(c.solveState()).toBe('running');           // keine Lösung vorgespielt
    expect(c.played()).toEqual([]);                    // falscher Zug zurückgenommen
    expect(c.mistake()).toBeTrue();
    expect(TestBed.inject(SnackbarService).quick).toHaveBeenCalledWith('ebooks.board.wrongMove');
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
