import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { SnackbarService } from '../../core/snackbar.service';
import { MaiaEngineService } from '../analysis/maia/maia-engine.service';
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

  function make(d: EbookDiagram = D): ComponentFixture<EbookBoardComponent> {
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
        { provide: SnackbarService, useValue: { warn: () => {}, quick: () => {} } },
        { provide: MaiaEngineService, useValue: maia },
        { provide: StockfishService, useValue: stockfish },
      ],
    });
    const fixture = TestBed.createComponent(EbookBoardComponent);
    fixture.componentRef.setInput('diagram', d);
    fixture.componentRef.setInput('ebookId', 9);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => { try { localStorage.removeItem('rh.ebooks.calc.11'); } catch { /* egal */ } });

  it('startet im Ansehen-Modus, Schwarz am Zug dreht das Brett, Brett nicht spielbar', () => {
    const c = make().componentInstance;
    expect(c.mode()).toBe('view');
    expect(c.flipped()).toBeTrue();
    expect(c.playable()).toBeFalse();
    expect(c.analysisParams()).toEqual({ fen: D.fen, orientation: 'black', from: '/ebooks/9' });
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

  it('Lösen Training: der erste Fehler zählt, danach spielt das Brett die Lösung vor', fakeAsync(() => {
    const c = make().componentInstance;
    c.setMode('solve');
    c.setSolveStyle('training');
    c.onBoardMove({ from: 'f4', to: 'h3', san: 'Nh3', fen: WRONG });
    expect(c.solveState()).toBe('failed');
    expect(c.playable()).toBeFalse();
    tick(700 * 4);
    expect(c.played().map(m => m.san)).toEqual(['Rxh2+', 'Kxh2', 'Rh8#']);
    expect(c.solveState()).toBe('failed');
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
});
