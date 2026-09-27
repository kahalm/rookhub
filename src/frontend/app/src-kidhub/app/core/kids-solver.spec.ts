import { KidsSolver, parseAltMoves, splitMoves } from './kids-solver';

/**
 * Die Loese-Logik von KidHub, an echten Lichess-Puzzles (CC0) aus der Kinder-Leiter und an einer
 * Kurs-Linie mit eigenem Startzug.
 */
describe('KidsSolver', () => {
  // Matt in 1: nach …g3 (Stellungszug) setzt Weiss mit Th8 matt.
  const mate1 = { fen: '1R6/8/8/8/6p1/8/r6k/5K2 b - - 3 73', moves: ['g4g3', 'b8h8'], startPly: 0 };
  // Gabel in zwei Zuegen: …Ta2, Sb4+ (Koenig und Turm), …Kd6, Sxa2.
  const fork = { fen: '8/8/2k5/8/8/r2NK1P1/5P2/8 b - - 1 63', moves: ['a3a2', 'd3b4', 'c6d6', 'b4a2'], startPly: 0 };
  // Umwandeln: nach …Sg8 schlaegt h7xg8 und wird Dame.
  const promote = { fen: '6R1/1b2n2P/kp2r3/8/8/8/3K4/8 b - - 28 64', moves: ['e7g8', 'h7g8q'], startPly: 0 };

  it('spielt den Stellungszug erst auf Anfrage und kennt die Farbe des Kindes', () => {
    const s = new KidsSolver(mate1);
    expect(s.hasPendingSetup()).toBeTrue();
    expect(s.solverColor).toBe('white');
    expect(s.isSolverTurn()).toBeFalse();
    expect(s.dests().size).toBe(0);

    expect(s.playSetup()).toEqual({ from: 'g4', to: 'g3' });
    expect(s.isSolverTurn()).toBeTrue();
    expect(s.dests().get('b8')).toContain('h8');
  });

  it('loest mit dem richtigen Zug', () => {
    const s = new KidsSolver(mate1);
    s.playSetup();
    expect(s.tryMove('b8', 'h8')).toBe('solved');
    expect(s.isFinished()).toBeTrue();
  });

  it('fenAfter zeigt die Stellung nach einem Zug, ohne ihn zu spielen', () => {
    const s = new KidsSolver(mate1);
    s.playSetup();
    const before = s.fen();
    expect(s.fenAfter('b8', 'b1')!.split(' ')[0]).toBe('8/8/8/8/8/6p1/r6k/1R3K2');
    expect(s.fen()).toBe(before);
    expect(s.fenAfter('b8', 'c7')).toBeNull();                 // Turm zieht nicht schraeg
  });

  it('fenAfter wandelt wie das Brett zur Dame um', () => {
    const s = new KidsSolver(promote);
    s.playSetup();
    expect(s.fenAfter('h7', 'h8')!.split(' ')[0]).toContain('Q');
  });

  it('nimmt einen falschen Zug zurueck', () => {
    const s = new KidsSolver(mate1);
    s.playSetup();
    const before = s.fen();
    expect(s.tryMove('b8', 'b1')).toBe('wrong');
    expect(s.fen()).toBe(before);
    expect(s.isSolverTurn()).toBeTrue();
  });

  it('ein unmoeglicher Zug ist „illegal", nichts veraendert sich', () => {
    const s = new KidsSolver(mate1);
    s.playSetup();
    const before = s.fen();
    expect(s.tryMove('b8', 'c6')).toBe('illegal');
    expect(s.fen()).toBe(before);
  });

  it('im letzten Zug zaehlt jedes Matt, nicht nur das gespeicherte', () => {
    // …Kh8 (Stellungszug); Ta8# ist gespeichert, Tb8# ist genauso matt.
    const s = new KidsSolver({ fen: '6k1/8/6K1/8/8/8/1R6/R7 b - - 0 1', moves: ['g8h8', 'a1a8'], startPly: 0 });
    s.playSetup();
    expect(s.tryMove('b2', 'b8')).toBe('solved');
  });

  it('zwei eigene Zuege: richtig → Antwort des Gegners → geloest', () => {
    const s = new KidsSolver(fork);
    s.playSetup();
    expect(s.tryMove('d3', 'b4')).toBe('continue');
    expect(s.isSolverTurn()).toBeFalse();
    expect(s.playReply()).toEqual({ from: 'c6', to: 'd6' });
    expect(s.hint()).toEqual({ from: 'b4', to: 'a2' });
    expect(s.tryMove('b4', 'a2')).toBe('solved');
  });

  it('ein anderer Schach-Zug im ersten Zug ist falsch, auch wenn er gut aussieht', () => {
    const s = new KidsSolver(fork);
    s.playSetup();
    expect(s.tryMove('d3', 'e5')).toBe('wrong');
  });

  it('wandelt ohne Rueckfrage in die Dame um', () => {
    const s = new KidsSolver(promote);
    s.playSetup();
    expect(s.tryMove('h7', 'g8')).toBe('solved');
    expect(s.fen().split(' ')[0]).toContain('Q');
  });

  it('startPly -1: kein Stellungszug, das Kind zieht sofort', () => {
    const s = new KidsSolver({ fen: '6k1/8/6K1/8/8/8/8/R7 w - - 0 1', moves: ['a1a8'], startPly: -1 });
    expect(s.hasPendingSetup()).toBeFalse();
    expect(s.playSetup()).toBeNull();
    expect(s.isSolverTurn()).toBeTrue();
    expect(s.tryMove('a1', 'a8')).toBe('solved');
  });

  it('Kurs-Linie mit Startzug mitten in der Partie: davor stumm vorgespult', () => {
    const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const s = new KidsSolver({ fen: start, moves: ['e2e4', 'e7e5', 'g1f3', 'b8c6'], startPly: 2 });
    expect(s.solverColor).toBe('black');
    expect(s.playSetup()).toEqual({ from: 'g1', to: 'f3' });
    expect(s.tryMove('b8', 'c6')).toBe('solved');
  });

  it('eine im Kurs geduldete Alternative ist kein Fehler, aber auch nicht die Loesung', () => {
    const s = new KidsSolver({ ...mate1, altMoves: { 1: ['b8a8'] } });
    s.playSetup();
    const before = s.fen();
    expect(s.tryMove('b8', 'a8')).toBe('alternative');
    expect(s.fen()).toBe(before);
    expect(s.tryMove('b8', 'h8')).toBe('solved');
  });

  it('eine Linie, die mit dem Gegnerzug endet, ist nach der Antwort geloest', () => {
    const start = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const s = new KidsSolver({ fen: start, moves: ['e2e4', 'e7e5'], startPly: -1 });
    expect(s.tryMove('e2', 'e4')).toBe('continue');
    s.playReply();
    expect(s.isFinished()).toBeTrue();
  });
});

describe('splitMoves / parseAltMoves', () => {
  it('zerlegt die Zugfolge und ignoriert Leerraum', () => {
    expect(splitMoves(' e2e4  e7e5 ')).toEqual(['e2e4', 'e7e5']);
    expect(splitMoves('')).toEqual([]);
  });

  it('liest AltMoves und verwirft Unbrauchbares', () => {
    expect(parseAltMoves('{"1":["b8a8"],"x":["a1a2"],"3":"kaputt"}')).toEqual({ 1: ['b8a8'] });
    expect(parseAltMoves(null)).toBeUndefined();
    expect(parseAltMoves('kein json')).toBeUndefined();
  });
});
