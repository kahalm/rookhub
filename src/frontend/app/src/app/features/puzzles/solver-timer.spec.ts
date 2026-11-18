import { SecondsCountdown, SolverTimer } from './solver-timer';
import { ManualClock } from '../../testing/manual-clock';
import { VisibilityStopwatch } from './visibility-stopwatch';

class FakeStopwatch extends VisibilityStopwatch {
  seconds = 0;
  starts: number[] = [];
  override start(initial = 0): void { this.starts.push(initial); this.seconds = initial; }
  override get elapsedSeconds(): number { return this.seconds; }
  override stop(): number { return this.seconds; }
  holds = 0;
  releases = 0;
  override hold(): void { this.holds++; }
  override release(): void { this.releases++; }
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

  it('pause/resume halten die Stoppuhr an, der Anzeige-Takt läuft weiter und meldet den stehenden Stand', () => {
    const clock = new ManualClock();
    const sw = new FakeStopwatch();
    const ticks: number[] = [];
    const t = new SolverTimer(s => ticks.push(s), clock, sw);
    t.start(3);
    t.pause();
    expect(sw.holds).toBe(1);
    clock.advance();
    clock.advance();
    expect(ticks).toEqual([3, 3]);     // die Uhr steht
    expect(clock.active.size).toBe(1);
    t.resume();
    expect(sw.releases).toBe(1);
    sw.seconds = 4;
    clock.advance();
    expect(ticks).toEqual([3, 3, 4]);
    t.stop();
  });

  it('mit echter Stoppuhr: die Zeit im Halt fehlt im Endstand', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(0));
    try {
      const t = new SolverTimer(() => {}, new ManualClock());
      t.start();
      jasmine.clock().tick(10_000);
      t.pause();
      jasmine.clock().tick(120_000);
      t.resume();
      jasmine.clock().tick(5_000);
      expect(t.stop()).toBe(15);
    } finally {
      jasmine.clock().uninstall();
    }
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

  it('paused: der Takt zählt nicht herunter — auch nicht bei einem währenddessen gestarteten Countdown', () => {
    const clock = new ManualClock();
    const c = new SecondsCountdown(clock);
    let done = 0;
    c.start(2, () => done++);
    clock.advance();
    expect(c.remaining).toBe(1);
    c.paused = true;
    clock.advance(5);
    expect(c.remaining).toBe(1);
    expect(done).toBe(0);
    c.start(3, () => done++);       // z. B. nächste Aufgabe hinter dem Fenster
    clock.advance(4);
    expect(c.remaining).toBe(3);
    c.paused = false;
    clock.advance(3);
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
