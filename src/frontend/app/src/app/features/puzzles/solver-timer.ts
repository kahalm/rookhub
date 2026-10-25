import { VisibilityStopwatch } from './visibility-stopwatch';

/**
 * Sekunden-Takt des Lös-Automaten, austauschbar für Tests. Die Vorgabe {@link realClock} ruft die globalen
 * Funktionen erst beim Aufruf auf — `fakeAsync` greift damit wie zuvor.
 */
export interface SolverClock {
  setInterval(fn: () => void, ms: number): unknown;
  clearInterval(handle: unknown): void;
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

export const realClock: SolverClock = {
  setInterval: (fn, ms) => setInterval(fn, ms),
  clearInterval: handle => clearInterval(handle as ReturnType<typeof setInterval>),
  setTimeout: (fn, ms) => setTimeout(fn, ms),
  clearTimeout: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/**
 * Lösezeit-Uhr (Zerlegung Z23): {@link VisibilityStopwatch} (zählt nur aktive Tab-Zeit) plus ein 1-s-Anzeige-Tick,
 * der die aktuelle Sekundenzahl meldet. Von Standard-, Buch- und Woodpecker-Löser über die Basis genutzt; Endless
 * hat eigene Doppel-Stoppuhren.
 */
export class SolverTimer {
  private handle: unknown;
  private ticking = false;

  constructor(
    private readonly onTick: (seconds: number) => void,
    private readonly clock: SolverClock = realClock,
    private readonly stopwatch = new VisibilityStopwatch(),
  ) {}

  /** Stoppuhr ab `initialSeconds` (Fortsetzen einer gemerkten Zwischenzeit) starten.
   *  IDEMPOTENT: mehrere Wege setzen ein neues Puzzle auf, ohne vorher {@link stop} zu rufen („Überspringen" ist im
   *  Kurs/Wochenpost MITTEN im Lösen klickbar, dazu Zurücksetzen, Retry, Kapitel-/Wochen-Navigation). Ohne das
   *  Abräumen blieb je Aufruf ein Waisen-Intervall zurück: es überlebte ngOnDestroy und schrieb sekündlich weiter
   *  die Zwischenzeit — es belebte damit genau den Eintrag wieder, den die Versuchs-Erfassung gelöscht hatte. */
  start(initialSeconds = 0): void {
    if (this.ticking) this.clock.clearInterval(this.handle);
    this.stopwatch.start(initialSeconds);
    this.handle = this.clock.setInterval(() => this.onTick(this.stopwatch.elapsedSeconds), 1000);
    this.ticking = true;
  }

  /** Anhalten; liefert den finalen aktiven Stand in Sekunden. */
  stop(): number {
    if (this.ticking) {
      this.clock.clearInterval(this.handle);
      this.ticking = false;
      this.handle = undefined;
    }
    const seconds = this.stopwatch.elapsedSeconds;
    this.stopwatch.stop();
    return seconds;
  }
}

/**
 * Sichtbarer Sekunden-Countdown (Zerlegung Z23): zählt `remaining` je Sekunde herunter und ruft bei 0 einmal
 * `onElapsed` (nach dem Anhalten). Genutzt für „nächstes Puzzle in N s" und den Viz-Countdown bis zum Verdecken.
 */
export class SecondsCountdown {
  remaining = 0;
  private handle: unknown;
  private ticking = false;

  constructor(private readonly clock: SolverClock = realClock) {}

  start(seconds: number, onElapsed: () => void): void {
    this.stop();
    this.remaining = seconds;
    this.handle = this.clock.setInterval(() => {
      this.remaining--;
      if (this.remaining <= 0) {
        this.stop();
        onElapsed();
      }
    }, 1000);
    this.ticking = true;
  }

  stop(): void {
    if (this.ticking) {
      this.clock.clearInterval(this.handle);
      this.ticking = false;
      this.handle = undefined;
    }
    this.remaining = 0;
  }
}
