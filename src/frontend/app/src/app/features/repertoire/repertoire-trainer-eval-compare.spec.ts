import { fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { EvalCompare, EVAL_COMPARE_DEPTH, compareEvals } from './repertoire-trainer-eval-compare';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_A3 = 'rnbqkbnr/pppppppp/8/8/8/P7/1PPPPPPP/RNBQKBNR b KQkq - 0 1';

describe('compareEvals', () => {
  const cases: [string, string, 'w' | 'b', number, 'missed' | 'allowed' | null][] = [
    ['0.2', '0.7', 'w', -0.5, null],
    ['0.2', '0.7', 'b', 0.5, null],
    ['0.5', '#3', 'w', (50 - 99997) / 100, 'missed'],
    ['#-2', '0.1', 'w', (-99998 - 10) / 100, 'allowed'],
    ['#2', '#-1', 'b', (-99998 - 99999) / 100, 'allowed'],
    ['#4', '#2', 'w', (99996 - 99998) / 100, null],
  ];
  for (const [p, r, color, delta, note] of cases) {
    it(`${p} gegen ${r} als ${color} → ${delta} / ${note}`, () => {
      const res = compareEvals(p, r, color);
      expect(res.deltaPawns).toBeCloseTo(delta, 6);
      expect(res.mateNote).toBe(note);
    });
  }
});

describe('EvalCompare', () => {
  function make(evals: string[] | Error) {
    const getEval = jasmine.createSpy('getEval');
    if (evals instanceof Error) getEval.and.returnValue(Promise.reject(evals));
    else getEval.and.returnValues(...evals.map(e => Promise.resolve(e)));
    const changed = jasmine.createSpy('changed');
    return { cmp: new EvalCompare({ getEval }, changed), getEval, changed };
  }

  it('rechnet beide Stellungen in Tiefe 14 und hält den Unterschied', fakeAsync(() => {
    const { cmp, getEval, changed } = make(['0.2', '0.7']);
    cmp.start(START, AFTER_A3, 'e4', 'w');
    expect(cmp.loading).toBeTrue();
    expect(getEval.calls.argsFor(0)).toEqual([AFTER_A3, EVAL_COMPARE_DEPTH]);
    expect(getEval.calls.argsFor(1)[0]).toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    flushMicrotasks();
    expect(cmp.loading).toBeFalse();
    expect(cmp.deltaPawns).toBeCloseTo(-0.5, 6);
    expect(cmp.deltaAbsDisplay).toBe('0.5');
    expect(changed).toHaveBeenCalledTimes(1);
  }));

  it('leere Bewertung: kein Ergebnis, aber nicht mehr am Laden', fakeAsync(() => {
    const { cmp, changed } = make(['', '0.7']);
    cmp.start(START, AFTER_A3, 'e4', 'w');
    flushMicrotasks();
    expect(cmp.loading).toBeFalse();
    expect(cmp.deltaPawns).toBeNull();
    expect(changed).toHaveBeenCalled();
  }));

  it('nicht spielbarer Repertoirezug: sofort fertig, keine Engine', () => {
    const { cmp, getEval, changed } = make(['0.1', '0.1']);
    cmp.start(START, AFTER_A3, 'Ke5', 'w');
    expect(cmp.loading).toBeFalse();
    expect(getEval).not.toHaveBeenCalled();
    expect(changed).toHaveBeenCalledTimes(1);
  });

  it('reset verwirft ein spät eintreffendes Ergebnis', fakeAsync(() => {
    const { cmp, changed } = make(['0.2', '0.7']);
    cmp.start(START, AFTER_A3, 'e4', 'w');
    cmp.reset();
    flushMicrotasks();
    expect(cmp.deltaPawns).toBeNull();
    expect(cmp.loading).toBeFalse();
    expect(changed).not.toHaveBeenCalled();
    expect(cmp.deltaAbsDisplay).toBe('');
  }));

  it('Engine-Fehler je Stellung zählt als leere Bewertung', fakeAsync(() => {
    const { cmp } = make(new Error('worker'));
    cmp.start(START, AFTER_A3, 'e4', 'b');
    flushMicrotasks();
    expect(cmp.loading).toBeFalse();
    expect(cmp.deltaPawns).toBeNull();
  }));
});
