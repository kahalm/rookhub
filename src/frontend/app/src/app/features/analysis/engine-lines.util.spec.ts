import { formatCount, formatElapsed, formatKiloNodes, formatKiloNps, mapBrokerLine, toDisplayLines, uciLineToSan } from './engine-lines.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('engine-lines.util', () => {
  it('mapBrokerLine maps cp/mate from white POV and caps at multiPv', () => {
    const lines = mapBrokerLine(START, {
      time: 100, depth: 20, nodes: 5000,
      pvs: [{ depth: 20, cp: 35, moves: ['e2e4', 'e7e5'] }, { depth: 20, mate: -3, moves: ['d2d4'] }, { depth: 19, cp: -12, moves: ['c2c4'] }],
    }, 2);
    expect(lines.length).toBe(2);
    expect(lines[0]).toEqual(jasmine.objectContaining({ multipv: 1, depth: 20, scoreType: 'cp', score: 35, evalText: '+0.35', pvUci: ['e2e4', 'e7e5'] }));
    expect(lines[1]).toEqual(jasmine.objectContaining({ multipv: 2, scoreType: 'mate', score: -3, evalText: '#-3' }));
  });

  it('mapBrokerLine rewrites broker castling (king takes rook) into standard UCI', () => {
    const fen = 'r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1';
    const lines = mapBrokerLine(fen, { time: 1, depth: 5, nodes: 1, pvs: [{ cp: 0, moves: ['e1h1'] }] }, 1);
    expect(lines[0].pvUci).toEqual(['e1g1']);
  });

  it('uciLineToSan numbers moves from the position and stops at the first illegal move', () => {
    expect(uciLineToSan(START, ['e2e4', 'e7e5', 'g1f3'], 12)).toBe('1. e4 e5 2. Nf3');
    const blackToMove = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
    expect(uciLineToSan(blackToMove, ['e7e5', 'g1f3'], 12)).toBe('1... e5 2. Nf3');
    expect(uciLineToSan(START, ['e2e4', 'e2e4'], 12)).toBe('1. e4');   // zweiter Zug illegal → Abbruch
    expect(uciLineToSan('kaputt', ['e2e4'], 12)).toBe('');
  });

  it('toDisplayLines derives sign from cp resp. mate', () => {
    const d = toDisplayLines(START, [
      { multipv: 1, depth: 10, scoreType: 'cp', score: 0, evalText: '0.00', pvUci: ['e2e4'] },
      { multipv: 2, depth: 10, scoreType: 'mate', score: -2, evalText: '#-2', pvUci: ['d2d4'] },
    ]);
    expect(d[0].positive).toBeTrue();
    expect(d[0].san).toBe('1. e4');
    expect(d[1].positive).toBeFalse();
  });

  it('formatElapsed renders m:ss and h:mm:ss', () => {
    expect(formatElapsed(5)).toBe('0:05');
    expect(formatElapsed(65)).toBe('1:05');
    expect(formatElapsed(3600 + 125)).toBe('1:02:05');
    expect(formatElapsed(-3)).toBe('0:00');
  });
});

describe('Zahlformate (Kilo-Knoten, Tausenderpunkt, ohne Nachkomma)', () => {
  it('formatCount trennt Tausender und rundet auf ganze Zahlen', () => {
    expect(formatCount(8390.6)).toBe('8.391');
    expect(formatCount(999)).toBe('999');
    expect(formatCount(1234567)).toBe('1.234.567');
    expect(formatCount(0)).toBe('0');
  });

  it('formatKiloNps/formatKiloNodes nutzen ÜBERALL kN — kein Umschalten auf MN oder N', () => {
    expect(formatKiloNps(8390000)).toBe('8.390 kN/s');
    expect(formatKiloNps(1500)).toBe('2 kN/s');       // auch kleine Werte bleiben kN
    expect(formatKiloNps(0)).toBe('0 kN/s');
    expect(formatKiloNodes(45231000)).toBe('45.231 kN');
  });

  it('unbekannte Locale wirft nicht (Template-Getter)', () => {
    expect(formatCount(1234, 'xx-nonsense')).toMatch(/1.?234/);
  });
});
