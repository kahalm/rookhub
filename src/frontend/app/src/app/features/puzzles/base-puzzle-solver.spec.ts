import { fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { Key } from 'chessground/types';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { StockfishService } from './stockfish.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

class TestSolver extends BasePuzzleSolver {
  evalRefreshCount = 0;
  protected handleSolved(): void { this.state = 'SOLVED'; }
  protected handleFailed(): void { this.state = 'FAILED'; }
  override get reviewTotal(): number { return this.solutionMoves.length; }
  protected override reviewGoTo(index: number): void { this.reviewIndex = index; }
  protected override refreshEvalIfShown(): void { this.evalRefreshCount++; }
  setup(fen: string, moves: string): void { this.setupSolver(fen, moves, 0); }
  get fen(): string { return (this as unknown as { chess: { fen(): string } }).chess.fen(); }
}

describe('BasePuzzleSolver mouseslip after Stockfish error', () => {
  it('undoes only the user move (keeps a valid solution move) when Stockfish failed', fakeAsync(() => {
    // Stockfish lehnt ab -> Fehlerpfad in opponentRespond: state wird PLAYING,
    // aber es wird KEIN Gegnerzug gespielt.
    const stockfish = { getBestMove: () => Promise.reject('err') } as unknown as StockfishService;
    const solver = new TestSolver(stockfish);

    // Setup: 1.e4 wird automatisch gespielt; User (Schwarz) loest ab e7e5.
    solver.setup(START, 'e2e4 e7e5 g1f3 b8c6');
    tick(600);
    const afterSetup = solver.fen;

    // Falscher (aber legaler) schwarzer Zug -> off-path -> opponentRespond -> Stockfish-Fehler.
    solver.onMoveMade({ orig: 'a7' as Key, dest: 'a6' as Key });
    tick();
    expect(solver.state).toBe('PLAYING');

    solver.mouseslip();
    // Fix: nur a6 zurueck (1 Ply); der Setup-Zug e4 bleibt erhalten.
    // Vor dem Fix wurden 2 Plies zurueckgenommen -> auch e4 weg.
    expect(solver.fen).toBe(afterSetup);
  }));
});

describe('BasePuzzleSolver Level-1 „Anzeigen" (Toggle, läuft nicht ab)', () => {
  it('deckt beim Drücken die tatsächliche Stellung auf und verbirgt sie erst beim erneuten Drücken', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const solver = new TestSolver(stockfish);
    solver.visualizationMode = 1;                 // Blindspiel: Brett bleibt eingefroren
    solver.setup(START, 'e2e4 e7e5 g1f3 b8c6');
    tick(600);                                     // Setup-Zug e4, Lösen beginnt → frozenFen = nach e4

    const frozen = (solver as unknown as { frozenFen: string }).frozenFen;
    expect(solver.boardFen).toBe(frozen);

    // Korrekter User-Zug e7e5 → Brett bleibt trotzdem eingefroren (blind), actualFen weicht ab.
    solver.onMoveMade({ orig: 'e7' as Key, dest: 'e5' as Key });
    expect(solver.boardFen).toBe(frozen);
    expect(solver.actualFen).not.toBe(frozen);

    // „Anzeigen": aktuelle Stellung wird sofort eingeblendet.
    const current = solver.actualFen;
    solver.onVizShow();
    expect(solver.vizShowPressed).toBeTrue();
    expect(solver.vizShowCount).toBe(1);
    expect(solver.boardFen).toBe(current);

    // Kernverhalten: auch nach 3s (alter Auto-Ablauf-Zeitpunkt) bleibt es aufgedeckt.
    tick(3500);
    expect(solver.vizShowPressed).toBeTrue();

    // Erneutes Drücken verbirgt wieder → Brett zurück auf eingefroren; Verbergen zählt NICHT mit.
    solver.onVizShow();
    expect(solver.vizShowPressed).toBeFalse();
    expect(solver.vizShowCount).toBe(1);
    expect(solver.boardFen).toBe(frozen);

    discardPeriodicTasks();
  }));
});

describe('BasePuzzleSolver Eval-Nachziehen (Lösungspfad + Mouseslip)', () => {
  it('zieht die Eval im Lösungspfad nach der Solver-Antwort und nach Mouseslip nach', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const solver = new TestSolver(stockfish);
    solver.setup(START, 'e2e4 e7e5 g1f3 b8c6');   // Setup e4; User=Schwarz löst e7e5, g1f3 ist Solver-Antwort
    tick(600);

    solver.evalRefreshCount = 0;

    // Korrekter Lösungszug e7e5 → THINKING; Solver antwortet g1f3 nach 400ms → AWAITING.
    solver.onMoveMade({ orig: 'e7' as Key, dest: 'e5' as Key });
    expect(solver.evalRefreshCount).toBe(0);       // während THINKING noch nicht
    tick(400);
    expect(solver.evalRefreshCount).toBe(1);       // nach der Solver-Antwort: Eval auf neue Stellung nachgezogen

    // Mouseslip (Lösungspfad) → Eval erneut nachziehen.
    solver.mouseslip();
    expect(solver.evalRefreshCount).toBe(2);

    discardPeriodicTasks();
  }));
});

class WarnSolver extends TestSolver {
  warnThreshold = 0;
  warned = 0;
  protected override get offPathWarnThreshold(): number { return this.warnThreshold; }
  protected override onOffPathWarning(): void { this.warned++; }
  // Test-Helfer für die interne Logik.
  setEval(e: string): void { (this as any).currentEval = e; }
  setOffPath(plies: number): void { (this as any).offPathUserPlies = plies; (this as any).onSolutionPath = false; }
  callWarn(): void { (this as any).maybeWarnOffPath(); }
}

describe('BasePuzzleSolver Off-Path-Warnung', () => {
  const noStock = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;

  it('warnt einmalig ab dem Schwellwert-Zug, wenn Eval < +2 (Spieler Weiß)', () => {
    const s = new WarnSolver(noStock);
    s.warnThreshold = 3; s.orientation = 'white'; s.setEval('+0.5'); s.setOffPath(3);
    s.callWarn();
    expect(s.warned).toBe(1);
    s.callWarn();               // idempotent je Episode
    expect(s.warned).toBe(1);
  });

  it('warnt NICHT vor dem Schwellwert', () => {
    const s = new WarnSolver(noStock);
    s.warnThreshold = 3; s.orientation = 'white'; s.setEval('-1.0'); s.setOffPath(2);
    s.callWarn();
    expect(s.warned).toBe(0);
  });

  it('warnt NICHT, wenn der Spieler noch klar (>= +2) steht', () => {
    const s = new WarnSolver(noStock);
    s.warnThreshold = 3; s.orientation = 'white'; s.setEval('+2.5'); s.setOffPath(4);
    s.callWarn();
    expect(s.warned).toBe(0);
  });

  it('Schwelle 0 = nie warnen', () => {
    const s = new WarnSolver(noStock);
    s.warnThreshold = 0; s.orientation = 'white'; s.setEval('-5.0'); s.setOffPath(9);
    s.callWarn();
    expect(s.warned).toBe(0);
  });

  it('rechnet die Eval aus Spieler-Sicht (Schwarz) + Matt-Sonderfälle', () => {
    const black = new WarnSolver(noStock);
    black.warnThreshold = 1; black.orientation = 'black'; black.setOffPath(1);
    black.setEval('-3.0');            // Weiß -3 → Schwarz +3 → klar → keine Warnung
    black.callWarn();
    expect(black.warned).toBe(0);

    const mate = new WarnSolver(noStock);
    mate.warnThreshold = 1; mate.orientation = 'white'; mate.setOffPath(1);
    mate.setEval('#-2');             // Schwarz mattt → Weiß-Sicht -100 → Warnung
    mate.callWarn();
    expect(mate.warned).toBe(1);
  });

  it('zählt off-path-Züge im echten Zugfluss und warnt (Integration)', fakeAsync(() => {
    const stock = { getBestMove: () => Promise.resolve({ move: 'g1f3', eval: '+0.1' }) } as unknown as StockfishService;
    const s = new WarnSolver(stock);
    s.warnThreshold = 1; s.orientation = 'black';
    s.setup(START, 'e2e4 e7e5 g1f3 b8c6');   // e4 Setup; Schwarz am Zug
    tick(600);
    s.onMoveMade({ orig: 'a7' as Key, dest: 'a6' as Key });   // off-path #1 (statt e7e5)
    tick();                                                    // opponentRespond → Eval → maybeWarnOffPath
    expect(s.warned).toBe(1);
    discardPeriodicTasks();
  }));
});

class HintSolver extends TestSolver {
  override get availableHints(): string[] { return ['normal-1', 'normal-2']; }
  protected override get epForcedHints(): string[] { return ['ep-1', 'ep-2', 'ep-3']; }
}

describe('BasePuzzleSolver Anarchy-Tipps (e.p. forciert)', () => {
  const noStock = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;

  it('zeigt normalerweise die echten Tipps', () => {
    const s = new HintSolver(noStock);
    s.enPassantForced = false;
    s.hintLevel = 2;
    expect(s.hasHints).toBeTrue();
    expect(s.shownHints).toEqual(['normal-1', 'normal-2']);
    expect(s.canShowMoreHints).toBeFalse();
  });

  it('ersetzt im e.p.-Zwang alle Tipps durch die 3 Anarchy-Hinweise', () => {
    const s = new HintSolver(noStock);
    s.enPassantForced = true;
    expect(s.canShowMoreHints).toBeTrue();     // 3 verfügbar
    s.hintLevel = 3;
    expect(s.shownHints).toEqual(['ep-1', 'ep-2', 'ep-3']);
    expect(s.canShowMoreHints).toBeFalse();
  });
});

describe('BasePuzzleSolver maxHintLevel (Tipp-Nutzung über alle Züge)', () => {
  const noStock = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;

  it('showNextHint hebt maxHintLevel, das einen hintLevel-Reset (Zug-Wechsel) überlebt', () => {
    const s = new HintSolver(noStock);
    s.showNextHint();            // hintLevel 1
    s.showNextHint();            // hintLevel 2 (Maximum von 2 verfügbaren)
    expect(s.hintLevel).toBe(2);
    expect((s as any).maxHintLevel).toBe(2);
    s.hintLevel = 0;             // advanceAfterCorrectMove setzt hintLevel pro Zug zurück
    s.showNextHint();            // wieder 1
    expect(s.hintLevel).toBe(1);
    expect((s as any).maxHintLevel).toBe(2);   // Höchstwert übers ganze Solve bleibt
  });
});

class ArrowSolver extends TestSolver {
  protected override get opponentArrowMode(): 'off' | 'timed' | 'persist' { return 'persist'; }
}

describe('BasePuzzleSolver Gegnerzug-Pfeil (Crazy = bleibend)', () => {
  it('lässt den Gegnerzug-Pfeil im persist-Modus stehen, bis der User zieht', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const s = new ArrowSolver(stockfish);
    s.setup(START, 'e2e4 e7e5 g1f3 b8c6');   // Setup e4; Schwarz am Zug
    tick(600);

    s.onMoveMade({ orig: 'e7' as Key, dest: 'e5' as Key });   // korrekt → THINKING
    tick(400);                                                 // Solver spielt g1f3 → Pfeil gesetzt
    expect(s.vizOpponentLastMove).toBeDefined();
    tick(3000);                                                // persist: kein Auto-Ausblenden
    expect(s.vizOpponentLastMove).toBeDefined();

    s.onMoveMade({ orig: 'b8' as Key, dest: 'c6' as Key });   // User zieht → Pfeil weg
    expect(s.vizOpponentLastMove).toBeUndefined();
    discardPeriodicTasks();
  }));

  it('markiert schon den ersten (automatischen) Gegnerzug nach dem Setup', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const s = new ArrowSolver(stockfish);
    s.setup(START, 'e2e4 e7e5 g1f3 b8c6');   // Setup e2e4 → soll direkt einen Pfeil setzen
    tick(600);
    expect(s.vizOpponentLastMove).toEqual(['e2' as Key, 'e4' as Key]);
    discardPeriodicTasks();
  }));

  it('setzt im off-Modus (kein Viz/Crazy) keinen Pfeil', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const s = new TestSolver(stockfish);   // opponentArrowMode = 'off' (visualizationMode 0)
    s.setup(START, 'e2e4 e7e5 g1f3 b8c6');
    tick(600);
    s.onMoveMade({ orig: 'e7' as Key, dest: 'e5' as Key });
    tick(400);
    expect(s.vizOpponentLastMove).toBeUndefined();
    discardPeriodicTasks();
  }));
});

describe('BasePuzzleSolver playSolutionFromStart (geteiltes Aufgeben)', () => {
  it('spielt die Lösung ab Zug 0 selbsttätig durch und stoppt am Ende', fakeAsync(() => {
    const stockfish = { getBestMove: () => Promise.reject('x') } as unknown as StockfishService;
    const solver = new TestSolver(stockfish);
    solver.setup(START, 'e2e4 e7e5 g1f3 b8c6');   // 4 Lösungszüge → reviewTotal = 4
    tick(600);

    (solver as unknown as { playSolutionFromStart(): void }).playSolutionFromStart();
    expect(solver.reviewMode).toBeTrue();
    expect(solver.reviewIndex).toBe(0);

    tick(900); expect(solver.reviewIndex).toBe(1);
    tick(900 * 3); expect(solver.reviewIndex).toBe(4);   // bis ans Ende
    tick(900); expect(solver.reviewIndex).toBe(4);       // gestoppt, läuft nicht weiter
  }));
});
