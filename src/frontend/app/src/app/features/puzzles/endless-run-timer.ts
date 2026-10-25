import { realClock, SolverClock } from './solver-timer';
import { VisibilityStopwatch } from './visibility-stopwatch';

/** Uhr des Endlos-Laufs: Sekunden-Takt (wie {@link SolverClock}) plus Wanduhr für Start-/Endzeiten. */
export interface EndlessRunClock extends SolverClock {
  now(): number;
}

/** Vorgabe: globale Funktionen erst beim Aufruf (Jasmine-/fakeAsync-Uhren greifen wie zuvor). */
export const realRunClock: EndlessRunClock = { ...realClock, now: () => Date.now() };

/**
 * Zeitmessung eines Endlos-Laufs (Zerlegung A4): Session-Stoppuhr mit 1-s-Takt und Puzzle-Stoppuhr; beide zählen nur
 * sichtbare Tab-Zeit. Reine Klasse ohne Angular; der Takt meldet `(sessionSekunden)` an den Besitzer.
 */
export class EndlessRunTimer {
  /** Wanduhr-Start des aktuellen Puzzles (startedAt der Session-Aufzeichnung); 0 = noch nicht begonnen. */
  puzzleStartTime = 0;
  /** Beim Resume ermittelte fortzusetzende Puzzle-Zeit (Refresh mitten im selben Puzzle);
   *  wird vom nächsten {@link beginPuzzle} genau einmal konsumiert. */
  resumePuzzleSeconds = 0;
  readonly puzzleStopwatch = new VisibilityStopwatch();
  private readonly sessionStopwatch = new VisibilityStopwatch();
  private handle: unknown;
  private ticking = false;

  constructor(
    private readonly onTick: (sessionSeconds: number) => void,
    readonly clock: EndlessRunClock = realRunClock,
  ) {}

  /** Session-Takt läuft (= ein Lauf ist im Gange). */
  get running(): boolean { return this.ticking; }

  /** `initialSeconds` > 0 setzt einen fortgesetzten Lauf (Resume) zeitlich korrekt fort. */
  startSession(initialSeconds = 0): void {
    this.sessionStopwatch.start(initialSeconds);
    this.handle = this.clock.setInterval(() => this.onTick(this.sessionStopwatch.elapsedSeconds), 1000);
    this.ticking = true;
  }

  /** Session-Takt und beide Stoppuhren anhalten. */
  stop(): void {
    if (this.ticking) {
      this.clock.clearInterval(this.handle);
      this.handle = undefined;
      this.ticking = false;
    }
    this.sessionStopwatch.stop();
    this.puzzleStopwatch.stop();
  }

  /** Puzzle beginnt (Setup-Zug gespielt): Wanduhr-Start merken, Stoppuhr starten — nach einem Resume DESSELBEN
   *  Puzzles (Refresh mitten im Lösen) mit der persistierten Zwischenzeit. Liefert die Start-Sekunden. */
  beginPuzzle(): number {
    this.puzzleStartTime = this.clock.now();   // Wanduhr-Timestamp (für startedAt der Session-Aufzeichnung)
    const seconds = this.resumePuzzleSeconds;
    this.puzzleStopwatch.start(seconds);
    this.resumePuzzleSeconds = 0;
    return seconds;
  }

  /** Gemessene (aktive) Sekunden am aktuellen Puzzle; 0, solange es nicht begonnen hat. */
  puzzleSeconds(): number {
    return this.puzzleStartTime > 0 ? this.puzzleStopwatch.elapsedSeconds : 0;
  }
}
