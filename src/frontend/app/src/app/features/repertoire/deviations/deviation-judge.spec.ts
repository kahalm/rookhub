import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { Chess } from 'chess.js';
import { AFTER_TIMEOUT_MS, DeviationJudge, Verdict, moverCp } from './deviation-judge';

/** Engine-Attrappe: merkt sich die angefragten Stellungen; die Antworten schickt der Test über `emit`. */
function fakeEngine() {
  const analysis$ = new Subject<any>();
  const e = {
    analysis$, depthLimit: 22, asked: [] as string[], multi: [] as number[],
    analyze: (fen: string) => { e.asked.push(fen); return Promise.resolve(); },
    stop: jasmine.createSpy('stop'),
    setMultiPv: (n: number) => e.multi.push(n),
    setDepth: (d: number) => { e.depthLimit = d; },
    emit: (fen: string, depth: number, lines: { uci: string; cp?: number; mate?: number }[]) => analysis$.next({
      fen, depth, running: true, nodes: 0, nps: 0,
      lines: lines.map((l, i) => ({ multipv: i + 1, depth, scoreType: l.mate != null ? 'mate' : 'cp', score: l.mate ?? l.cp ?? 0,
        evalText: l.mate != null ? '#' + l.mate : (l.cp! / 100).toFixed(2), pvUci: [l.uci] })),
    }),
  };
  return e;
}

// Nach 1.e4 c5 (Weiß am Zug): Antworten aus Sicht von Weiß
const BEFORE = (() => { const c = new Chess(); c.move('e4'); c.move('c5'); return c.fen(); })();

describe('DeviationJudge (Engine-Urteil der Abweichungen, 0.892.0)', () => {
  it('Antwort unter den besten Linien: Verlust aus deren Werten; gut bis zur Schwelle, sonst nicht optimal mit den besten Zügen', fakeAsync(() => {
    const e = fakeEngine();
    const j = new DeviationJudge(e as any);
    j.prepare(BEFORE);
    expect(e.asked).toEqual([BEFORE]);
    expect(e.multi).toEqual([3]);
    e.emit(BEFORE, 16, [{ uci: 'g1f3', cp: 40 }, { uci: 'b1c3', cp: 25 }, { uci: 'c2c3', cp: 0 }]);
    let v: Verdict | null = null;
    j.judge(BEFORE, 'b1c3').then(x => v = x);
    flushMicrotasks();
    expect(v!).toEqual({ good: true, lossCp: 15, best: [
      { uci: 'g1f3', san: 'Nf3', evalText: '0.40' }, { uci: 'b1c3', san: 'Nc3', evalText: '0.25' }, { uci: 'c2c3', san: 'c3', evalText: '0.00' }] });
    j.judge(BEFORE, 'c2c3', 30).then(x => v = x);
    flushMicrotasks();
    expect(v!.good).toBeFalse();
    expect(v!.lossCp).toBe(40);
    expect(e.depthLimit).toBe(22);                              // Tiefe wieder wie vorher
  }));

  it('Antwort nicht unter den besten: Stellung danach kurz rechnen (Seite wechselt), schwarze Sicht richtig', fakeAsync(() => {
    const e = fakeEngine();
    const j = new DeviationJudge(e as any);
    j.prepare(BEFORE);
    e.emit(BEFORE, 16, [{ uci: 'g1f3', cp: 40 }]);
    let v: Verdict | null = null;
    j.judge(BEFORE, 'a2a3').then(x => v = x);
    flushMicrotasks();
    const after = e.asked[1];
    expect(after).toContain(' b ');
    expect(e.multi[1]).toBe(1);
    e.emit(after, 12, [{ uci: 'b8c6', cp: 10 }]);           // aus Sicht von Weiß +0.10 → für Weiß 0.30 schlechter
    flushMicrotasks();
    expect(v!).toEqual(jasmine.objectContaining({ good: true, lossCp: 30 }));
  }));

  it('Engine antwortet nicht: nach der Frist ohne Urteil als gut (kein falscher Fehler)', fakeAsync(() => {
    const e = fakeEngine();
    const j = new DeviationJudge(e as any);
    let v: Verdict | null = null;
    j.judge(BEFORE, 'g1f3').then(x => v = x);
    tick(5000);
    expect(v!).toEqual({ good: true, lossCp: 0, best: [] });
    tick(AFTER_TIMEOUT_MS);
  }));

  it('Matt zählt weit vor jedem Zentibauern-Wert, aus Sicht der Seite am Zug', () => {
    const line = (scoreType: 'cp' | 'mate', score: number) => ({ multipv: 1, depth: 1, scoreType, score, evalText: '', pvUci: [] });
    expect(moverCp(line('mate', 3), BEFORE)).toBeGreaterThan(90000);
    expect(moverCp(line('cp', 50), BEFORE.replace(' w ', ' b '))).toBe(-50);
  });
});
