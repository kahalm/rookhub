import { Key } from 'chessground/types';
import { clearVisualizationHide } from './board-theme.util';
import { realClock, SecondsCountdown, SolverClock } from './solver-timer';
import type { ThemeMode } from './board-theme.util';

/** Wann der Gegnerzug-Pfeil gezeigt wird: 'off' = gar nicht, 'timed' = 1 s (Viz-Modus), 'persist' = bis zum
 *  nächsten User-Zug (Crazy-Brett, dort ist der Gegnerzug schlecht erkennbar). */
export type OpponentArrowMode = 'off' | 'timed' | 'persist';

/** Crazy-Modus → bleibender Pfeil; sonst richtet er sich nach dem Viz-Modus. */
export function opponentArrowModeFor(themeMode: ThemeMode, visualizationMode: number): OpponentArrowMode {
  return themeMode === 'crazy' ? 'persist' : (visualizationMode > 0 ? 'timed' : 'off');
}

/** Sekunden, nach denen die Figuren ab Stufe 2 verdeckt werden. */
export const VIZ_HIDE_COUNTDOWN_SECONDS = 3;

/**
 * Visualisierungs-/Blindfold-Zustand eines Lösungsversuchs (Zerlegung Z23): Countdown bis zum Verdecken der
 * Figuren (Stufe 2–4), der „Anzeigen"-Knopf als Umschalter, und der Gegnerzug-Pfeil mit seinem Zeitgeber.
 * Die Stufe selbst, die eingefrorene Stellung und die SAN-Zugliste hält {@link BasePuzzleSolver}; das
 * Verdecken (CSS) läuft über `hide`, damit es die Stufe zum Zeitpunkt des Ablaufs liest.
 */
export class VizController {
  /** Stufe 2–4: Figuren verdeckt (nach dem Countdown). */
  piecesHidden = false;
  /** „Anzeigen" gedrückt (deckt auf — Umschalter, läuft NICHT automatisch ab). */
  showPressed = false;
  /** Wie oft in diesem Versuch aufgedeckt wurde. */
  showCount = 0;
  /** Letzter Gegnerzug als Pfeil (oder keiner). */
  opponentLastMove?: [Key, Key];

  private readonly countdown: SecondsCountdown;
  private arrowTimer: unknown;
  private arrowTiming = false;

  constructor(
    private readonly hide: () => void,
    private readonly clock: SolverClock = realClock,
    private readonly unhide: () => void = clearVisualizationHide,
  ) {
    this.countdown = new SecondsCountdown(clock);
  }

  /** Sekunden bis zum Verdecken (0 = kein Countdown). */
  get countdownSeconds(): number { return this.countdown.remaining; }

  /** Countdown bis zum Verdecken starten (Stufe 2–4, beim Lösungsbeginn). */
  startCountdown(): void {
    this.countdown.stop();
    this.piecesHidden = false;
    this.showPressed = false;
    this.countdown.start(VIZ_HIDE_COUNTDOWN_SECONDS, () => {
      this.piecesHidden = true;
      this.hide();
    });
  }

  /** Verdecken beenden (Endzustand, neues Puzzle, Abbruch): Countdown weg, Figuren und Knopf zurück. */
  endHide(): void {
    this.countdown.stop();
    this.piecesHidden = false;
    this.showPressed = false;
    this.unhide();
  }

  /** „Anzeigen" umschalten; nur das Aufdecken zählt. */
  toggleShow(): void {
    this.showPressed = !this.showPressed;
    if (this.showPressed) this.showCount++;
  }

  /** Gegnerzug-Pfeil je nach Modus zeigen; 'timed' respektiert die Pfeil-Einstellung (`timedEnabled`). */
  showOpponentArrow(mode: OpponentArrowMode, timedEnabled: boolean, move: [Key, Key] | undefined): void {
    if (mode === 'off') return;
    if (mode === 'timed' && !timedEnabled) return;
    this.stopArrowTimer();
    this.opponentLastMove = move;
    if (mode === 'timed') {
      this.arrowTiming = true;
      this.arrowTimer = this.clock.setTimeout(() => {
        this.opponentLastMove = undefined;
        this.arrowTiming = false;
        this.arrowTimer = undefined;
      }, 1000);
    }
    // 'persist': kein Timer → der Pfeil bleibt, bis der User zieht.
  }

  clearOpponentArrow(): void {
    this.stopArrowTimer();
    this.opponentLastMove = undefined;
  }

  private stopArrowTimer(): void {
    if (!this.arrowTiming) return;
    this.clock.clearTimeout(this.arrowTimer);
    this.arrowTiming = false;
    this.arrowTimer = undefined;
  }
}
