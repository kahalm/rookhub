import { CalcTimerClock, CalcTrainingTimer } from './calc-training-timer';

const KEY = 'rookhub_calc_timer_spec';

/** Uhr zum Weiterdrehen von Hand. */
function fakeClock() {
  let tick: (() => void) | null = null;
  const clock: CalcTimerClock & { advance(n: number): void; active(): boolean } = {
    setInterval: fn => { tick = fn; return 1; },
    clearInterval: () => { tick = null; },
    advance: n => { for (let i = 0; i < n; i++) tick?.(); },
    active: () => tick !== null,
  };
  return clock;
}

describe('CalcTrainingTimer', () => {
  let user: number | null;
  let failures: number;

  beforeEach(() => { localStorage.removeItem(KEY); user = 1; failures = 0; });
  afterEach(() => localStorage.removeItem(KEY));

  function make() {
    const clock = fakeClock();
    const timer = new CalcTrainingTimer({
      storageKey: () => KEY, userId: () => user, onPersistFailed: () => failures++, clock,
    });
    return { timer, clock };
  }

  it('zählt nur zwischen start und stop und persistiert je Sekunde in den Kapitel-Topf', () => {
    const { timer, clock } = make();
    timer.syncChapter('A');
    clock.advance(3);
    expect(timer.seconds).toBe(0);
    timer.start();
    clock.advance(3);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ A: 3 });
    timer.stop();
    expect(clock.active()).toBeFalse();
    expect(timer.running).toBeFalse();
    expect(timer.display).toBe('0:03');
  });

  it('führt je Kapitel einen Topf und lädt ihn beim Wechsel zurück', () => {
    const { timer, clock } = make();
    timer.syncChapter('A'); timer.start(); clock.advance(4); timer.stop();
    timer.syncChapter('B'); expect(timer.seconds).toBe(0);
    timer.start(); clock.advance(2); timer.stop();
    timer.syncChapter('A'); expect(timer.seconds).toBe(4);
    timer.syncChapter(null); expect(timer.seconds).toBe(0);
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ A: 4, B: 2 });
  });

  it('ohne geladenen Topf wird nichts geschrieben', () => {
    const { timer } = make();
    expect(timer.hasChapter).toBeFalse();
    timer.setSeconds(10);
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('schreibt nach Ab- oder Ummelden nicht mehr (F3-019)', () => {
    const { timer, clock } = make();
    timer.syncChapter('A'); timer.start(); clock.advance(2);
    user = null;
    clock.advance(5);
    timer.stop();
    expect(JSON.parse(localStorage.getItem(KEY)!)).toEqual({ A: 2 });
  });

  it('meldet einen Fehlschlag nur beim Übergang', () => {
    const { timer, clock } = make();
    timer.syncChapter('A');
    const spy = spyOn(Storage.prototype, 'setItem').and.throwError('quota');
    timer.start(); clock.advance(3);
    expect(failures).toBe(1);
    spy.and.callThrough();
    clock.advance(1);
    spy.and.throwError('quota');
    clock.advance(1);
    expect(failures).toBe(2);
    timer.stop();
  });
});
