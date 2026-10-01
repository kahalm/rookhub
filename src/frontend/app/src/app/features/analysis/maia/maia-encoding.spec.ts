import {
  boardTokens, encodePosition, mirrorPlacement, mirrorUci, moveIndex, pickMove, policyFromLogits,
} from './maia-encoding';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const START_PLACEMENT = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR';

// Literale Vektoren aus dem Plan (MAIA_SPARRING_PLAN_2026-10-01, Abschnitt 2) — gegen alle 4352 Einträge
// der Upstream-Tabelle geprüft. NICHT aus der Implementierung ableiten.
describe('maia-encoding moveIndex', () => {
  const vectors: [string, number][] = [
    ['a1a1', 0], ['a1b1', 1], ['e2e4', 796], ['d2d4', 731], ['h8h8', 4095],
    ['a7a8q', 4096], ['a7a8n', 4099], ['a7b8q', 4100], ['e7d8n', 4239], ['e7e8q', 4240], ['h7h8n', 4351],
  ];
  for (const [uci, index] of vectors) {
    it(`${uci} → ${index}`, () => expect(moveIndex(uci)).toBe(index));
  }
});

describe('maia-encoding mirror', () => {
  it('mirrors moves across the middle of the board', () => {
    expect(mirrorUci('e7e5')).toBe('e2e4');
    expect(mirrorUci('h2h1q')).toBe('h7h8q');
    expect(mirrorUci('e8g8')).toBe('e1g1');
  });

  it('mirrors the placement: ranks reversed, colours swapped', () => {
    expect(mirrorPlacement('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR'))
      .toBe('rnbqkbnr/pppp1ppp/8/4p3/8/8/PPPPPPPP/RNBQKBNR');
  });
});

describe('maia-encoding boardTokens', () => {
  it('sets exactly one channel per piece of the starting position', () => {
    const t = boardTokens(START_PLACEMENT);
    expect(t.length).toBe(768);
    expect(t.reduce((sum, v) => sum + v, 0)).toBe(32);
    expect(t[3]).toBe(1);     // a1 Turm (Feld 0, Kanal R = 3)
    expect(t[53]).toBe(1);    // e1 König (Feld 4 · 12 + K = 5)
    expect(t[731]).toBe(1);   // e8 schwarzer König (Feld 60 · 12 + k = 11)
    expect(t[96]).toBe(1);    // a2 Bauer (Feld 8 · 12 + P = 0)
    expect(t[0]).toBe(0);     // a1 ist kein Bauer
  });
});

describe('maia-encoding encodePosition', () => {
  it('lists the legal moves of the starting position with their index', () => {
    const enc = encodePosition(START);
    expect(enc.tokens.length).toBe(768);
    expect(enc.legal.length).toBe(20);
    expect(enc.indices.length).toBe(20);
    expect(enc.indices[enc.legal.indexOf('e2e4')]).toBe(796);
    expect(enc.indices[enc.legal.indexOf('g1f3')]).toBe(405);   // g1 = 6, f3 = 21
  });

  it('mirrors black to move: e7e5 after 1.e4 sits on the index of e2e4', () => {
    const enc = encodePosition('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    expect(enc.legal).toContain('e7e5');
    expect(enc.indices[enc.legal.indexOf('e7e5')]).toBe(796);
    // Die Tokens sind die GESPIEGELTE Stellung: der schwarze König steht für das Netz auf e1 (als weißer).
    expect(enc.tokens[53]).toBe(1);
  });

  it('mirrors a black promotion into the 7th→8th rank block', () => {
    const enc = encodePosition('8/6k1/8/8/8/8/K6p/8 b - - 0 1');
    expect(enc.legal).toContain('h2h1q');
    expect(enc.indices[enc.legal.indexOf('h2h1q')]).toBe(4348);
    expect(enc.indices[enc.legal.indexOf('h2h1n')]).toBe(4351);
  });

  it('writes castling as plain UCI (king two squares)', () => {
    const enc = encodePosition('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1');
    expect(enc.legal).toContain('e1g1');
    expect(enc.legal).toContain('e1c1');
  });

  it('throws on an unreadable FEN', () => {
    expect(() => encodePosition('not a fen')).toThrow();
  });

  it('has no legal moves in a mate', () => {
    // Narrenmatt: Weiß ist matt.
    const enc = encodePosition('rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3');
    expect(enc.legal).toEqual([]);
    expect(enc.indices).toEqual([]);
  });
});

describe('maia-encoding policyFromLogits', () => {
  it('softmaxes only over legal moves, sums to 1 and sorts descending', () => {
    const enc = encodePosition(START);
    const logits = new Float32Array(4352);
    logits[796] = 2;     // e2e4
    logits[731] = 1;     // d2d4
    logits[0] = 50;      // a1a1 — kein legaler Zug, darf nichts bewirken
    const policy = policyFromLogits(logits, enc);

    expect(policy.length).toBe(20);
    expect(policy[0].uci).toBe('e2e4');
    expect(policy[1].uci).toBe('d2d4');
    expect(policy[0].p).toBeCloseTo(0.26289, 4);   // e² / (e² + e¹ + 18)
    expect(policy[1].p).toBeCloseTo(0.09671, 4);
    expect(policy[2].p).toBeCloseTo(0.03558, 4);
    expect(policy.reduce((s, m) => s + m.p, 0)).toBeCloseTo(1, 6);
    for (let i = 1; i < policy.length; i++) expect(policy[i - 1].p).toBeGreaterThanOrEqual(policy[i].p);
  });

  it('is empty without legal moves', () => {
    const enc = encodePosition('rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3');
    expect(policyFromLogits(new Float32Array(4352), enc)).toEqual([]);
  });
});

describe('maia-encoding pickMove', () => {
  const dist = [
    { uci: 'a', p: 0.6 }, { uci: 'b', p: 0.3 }, { uci: 'c', p: 0.06 }, { uci: 'd', p: 0.04 },
  ];

  it('keeps the move that crosses topP but none after it', () => {
    // Summe davor: 0 → a, 0,6 → b, 0,9 < 0,95 → c gehört dazu, 0,96 ≥ 0,95 → d nicht.
    expect(pickMove(dist, () => 0, 0.95)).toBe('a');
    expect(pickMove(dist, () => 0.65, 0.95)).toBe('b');     // 0,65 · 0,96 = 0,624 → zweiter
    expect(pickMove(dist, () => 0.999, 0.95)).toBe('c');    // nie der vierte
  });

  it('never picks the cut-off move, whatever the dice say', () => {
    for (let r = 0; r < 1; r += 0.001) expect(pickMove(dist, () => r, 0.95)).not.toBe('d');
  });

  it('always keeps the first move, even when it alone crosses topP', () => {
    expect(pickMove([{ uci: 'x', p: 0.97 }, { uci: 'y', p: 0.03 }], () => 0.999, 0.95)).toBe('x');
  });

  it('walks the moves in descending order even if they come unsorted', () => {
    const unsorted = [dist[3], dist[1], dist[0], dist[2]];
    expect(pickMove(unsorted, () => 0, 0.95)).toBe('a');
    expect(pickMove(unsorted, () => 0.999, 0.95)).toBe('c');
  });

  it('uses topP 0.95 by default', () => {
    expect(pickMove(dist, () => 0.999)).toBe('c');
  });

  it('returns null for an empty distribution', () => {
    expect(pickMove([], () => 0)).toBeNull();
  });
});
