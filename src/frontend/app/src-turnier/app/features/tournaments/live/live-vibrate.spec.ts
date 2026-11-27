import { LiveGame } from './live-boards.models';
import { LIVE_EVAL_CAP, LiveAlertTracker, evalPawns } from './live-vibrate';

/** Partie mit Bewertung `cp` (Centipawns, Weiß-Sicht) bzw. Matt nach `plies` Halbzügen. */
function g(plies: number, cp: number | null, opts: { mate?: number; result?: string; id?: string } = {}): LiveGame {
  const result = opts.result ?? '*';
  return {
    id: opts.id ?? 'abcdefgh', boardTag: null, white: {} as LiveGame['white'], black: {} as LiveGame['black'], fen: '',
    lastMove: null, plies, result, finished: result !== '*', whiteToMove: plies % 2 === 0,
    eval: cp === null && opts.mate === undefined ? null : { cp, mate: opts.mate ?? null, source: 'engine', depth: 20 },
  };
}

describe('live-vibrate', () => {
  it('evalPawns: Bauern, Matt und große Werte auf ±10 gekappt', () => {
    expect(evalPawns({ cp: 150, mate: null, source: 'cloud', depth: 1 })).toBe(1.5);
    expect(evalPawns({ cp: -2500, mate: null, source: 'cloud', depth: 1 })).toBe(-LIVE_EVAL_CAP);
    expect(evalPawns({ cp: null, mate: 3, source: 'cloud', depth: 1 })).toBe(LIVE_EVAL_CAP);
    expect(evalPawns({ cp: null, mate: -1, source: 'cloud', depth: 1 })).toBe(-LIVE_EVAL_CAP);
    expect(evalPawns(null)).toBeNull();
  });

  it('Anfangsstand meldet nie, auch wenn schon entschieden oder schief', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, 0));
    t.seed(g(40, null, { result: '1-0', id: 'zzzzzzzz' }));
    expect(t.observe(g(40, null, { result: '1-0', id: 'zzzzzzzz' }))).toBeNull();
  });

  it('Ergebnis kommt herein → result, genau einmal', () => {
    const t = new LiveAlertTracker();
    t.seed(g(30, 20));
    expect(t.observe(g(31, 30))).toBeNull();
    expect(t.observe(g(31, null, { result: '1/2-1/2' }))).toBe('result');
    expect(t.observe(g(31, null, { result: '1/2-1/2' }))).toBeNull();
  });

  it('Unbekannte Partie, die schon fertig ankommt, meldet nicht', () => {
    const t = new LiveAlertTracker();
    expect(t.observe(g(50, null, { result: '0-1' }))).toBeNull();
  });

  it('Sprung von Zug zu Zug ≥ 1,0 in beide Richtungen', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, 30));
    expect(t.observe(g(21, 80))).toBeNull();
    expect(t.observe(g(22, -40))).toBe('swing');   // 0,8 → −0,4 = 1,2 nach unten
    expect(t.observe(g(23, 70))).toBe('swing');    // −0,4 → 0,7 = 1,1 nach oben
  });

  it('Schleichend über das Fenster: Min/Max der letzten 8 Halbzüge', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, 0));
    expect(t.observe(g(21, 30))).toBeNull();
    expect(t.observe(g(22, 60))).toBeNull();
    expect(t.observe(g(23, 80))).toBeNull();
    expect(t.observe(g(24, 100))).toBe('swing');  // 0,0 (vor 4 Halbzügen) → 1,0
  });

  it('Langsamer als das Fenster meldet nicht', () => {
    const t = new LiveAlertTracker();
    t.seed(g(10, 0));
    // 0,12 je Halbzug: nach 8 Halbzügen 0,96 — darunter; danach fällt der Anfang aus dem Fenster.
    for (let i = 1; i <= 12; i++) expect(t.observe(g(10 + i, 12 * i))).withContext(`Halbzug ${10 + i}`).toBeNull();
  });

  it('Nach einer Meldung beginnt das Fenster neu (kein Dauervibrieren)', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, 0));
    expect(t.observe(g(21, 150))).toBe('swing');
    expect(t.observe(g(22, 200))).toBeNull();      // gegen 1,5 nur +0,5
    expect(t.observe(g(23, 230))).toBeNull();
    expect(t.observe(g(24, 260))).toBe('swing');   // 1,5 → 2,6
  });

  it('Matt nach großem Vorteil ist kein neuer Sprung (Kappe)', () => {
    const t = new LiveAlertTracker();
    t.seed(g(60, 1200));
    expect(t.observe(g(61, null, { mate: 4 }))).toBeNull();
    expect(t.observe(g(62, null, { mate: -2 }))).toBe('swing');   // +10 → −10
  });

  it('Neue Bewertung derselben Stellung ersetzt die alte und zählt gegen frühere Halbzüge', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, 0));
    expect(t.observe(g(21, 50))).toBeNull();
    expect(t.observe(g(21, 120))).toBe('swing');   // tiefer gerechnet: 0,0 → 1,2
  });

  it('Ohne Bewertung (kein Balken) gibt es keinen Sprung', () => {
    const t = new LiveAlertTracker();
    t.seed(g(20, null));
    expect(t.observe(g(21, null))).toBeNull();
    expect(t.observe(g(22, 300))).toBeNull();   // erster Wert: nichts zum Vergleichen
  });
});
