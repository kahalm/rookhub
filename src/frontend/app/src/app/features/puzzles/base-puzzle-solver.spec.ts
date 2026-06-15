import { fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { Key } from 'chessground/types';
import { BasePuzzleSolver } from './base-puzzle-solver';
import { StockfishService } from './stockfish.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

class TestSolver extends BasePuzzleSolver {
  protected handleSolved(): void { this.state = 'SOLVED'; }
  protected handleFailed(): void { this.state = 'FAILED'; }
  override get reviewTotal(): number { return this.solutionMoves.length; }
  protected override reviewGoTo(index: number): void { this.reviewIndex = index; }
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

describe('BasePuzzleSolver Level-1 „Anzeigen" (aktuelle Stellung 3s)', () => {
  it('zeigt beim Drücken kurz die tatsächliche Stellung und kehrt nach 3s zur eingefrorenen zurück', fakeAsync(() => {
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

    // „Anzeigen": aktuelle Stellung wird eingeblendet.
    const current = solver.actualFen;
    solver.onVizShow();
    expect(solver.vizShowPressed).toBeTrue();
    expect(solver.boardFen).toBe(current);

    // Nach 3s wieder eingefroren.
    tick(3000);
    expect(solver.vizShowPressed).toBeFalse();
    expect(solver.boardFen).toBe(frozen);

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
