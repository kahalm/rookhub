import { SecondsCountdown, SolverTimer } from './solver-timer';
import { ManualClock } from '../../testing/manual-clock';
import { VisibilityStopwatch } from './visibility-stopwatch';

class FakeStopwatch extends VisibilityStopwatch {
  seconds = 0;
  starts: number[] = [];
  override start(initial = 0): void { this.starts.push(initial); this.seconds = initial; }
  override get elapsedSeconds(): number { return this.seconds; }
  override stop(): number { return this.seconds; }
}

describe('SolverTimer', () => {
  it('meldet je Takt die Stoppuhr-Sekunden und ist beim Neustart idempotent', () => {
    const clock = new ManualClock();
    const sw = new FakeStopwatch();
    const ticks: number[] = [];
    const t = new SolverTimer(s => ticks.push(s), clock, sw);
    t.start(5);
    t.start(7);                       // zweiter Start ohne stop → kein Waisen-Intervall
    expect(clock.active.size).toBe(1);
    expect(sw.starts).toEqual([5, 7]);
    sw.seconds = 8;
    clock.advance();
    expect(ticks).toEqual([8]);
    sw.seconds = 9;
    expect(t.stop()).toBe(9);
    expect(clock.active.size).toBe(0);
    clock.advance();
    expect(ticks).toEqual([8]);
  });

  it('stop ohne Start liefert den Stand der Stoppuhr', () => {
    const sw = new FakeStopwatch();
    expect(new SolverTimer(() => {}, new ManualClock(), sw).stop()).toBe(0);
  });
});

describe('SecondsCountdown', () => {
  it('zählt herunter und ruft bei 0 genau einmal onElapsed', () => {
    const clock = new ManualClock();
    const c = new SecondsCountdown(clock);
    let done = 0;
    c.start(2, () => done++);
    expect(c.remaining).toBe(2);
    clock.advance();
    expect(c.remaining).toBe(1);
    clock.advance();
    expect(c.remaining).toBe(0);
    expect(done).toBe(1);
    expect(clock.active.size).toBe(0);
  });

  it('stop bricht ab und setzt auf 0, ohne onElapsed', () => {
    const clock = new ManualClock();
    const c = new SecondsCountdown(clock);
    let done = 0;
    c.start(3, () => done++);
    c.start(3, () => done++);       // Neustart räumt das alte Intervall ab
    expect(clock.active.size).toBe(1);
    c.stop();
    clock.advance(5);
    expect(c.remaining).toBe(0);
    expect(done).toBe(0);
  });
});
