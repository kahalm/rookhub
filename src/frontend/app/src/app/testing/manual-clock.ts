import { SolverClock } from '../features/puzzles/solver-timer';

/** Handbetriebene Uhr: `advance()` feuert jedes laufende Intervall einmal, `flushTimeouts()` alle Timeouts. */
export class ManualClock implements SolverClock {
  private next = 1;
  readonly active = new Map<number, () => void>();
  readonly timeouts = new Map<number, () => void>();
  setInterval(fn: () => void): unknown { const h = this.next++; this.active.set(h, fn); return h; }
  clearInterval(h: unknown): void { this.active.delete(h as number); }
  setTimeout(fn: () => void): unknown { const h = this.next++; this.timeouts.set(h, fn); return h; }
  clearTimeout(h: unknown): void { this.timeouts.delete(h as number); }
  advance(times = 1): void { for (let i = 0; i < times; i++) [...this.active.values()].forEach(f => f()); }
  flushTimeouts(): void { const fs = [...this.timeouts.values()]; this.timeouts.clear(); fs.forEach(f => f()); }
}
