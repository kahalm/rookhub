import { VisibilityStopwatch } from '../../puzzles/visibility-stopwatch';
import { CalcTrainingTimer, CalcTrainingTimerOptions } from './calc-training-timer';
import { newSecondsToken } from './calc-review.util';

/** Was die Trainings-Sitzung von der Kalkulations-Seite braucht (alles live gelesen). */
export interface CalcTrainingSessionHost {
  /** Ist eine Stellung geladen (kein Ladefehler)? Ohne sie wird nicht gemessen (siehe {@link CalcTrainingSession.canTrain}). */
  positionLoaded(): boolean;
  /** Id der angezeigten Stellung, `null` ohne. */
  currentPositionId(): number | null;
  /** Gemessene Sekunden einer Stellung als Delta (mit eigener Marke) an den Review-Endpoint einreihen. */
  creditSeconds(bookPuzzleId: number, seconds: number, token: string): void;
  /** Warnung zeigen; `actionKey` = Knopf in der Snackbar, dessen Klick `onAction` meldet. */
  warn(textKey: string, action?: { actionKey: string; duration: number }): { onAction(): { subscribe(cb: () => void): unknown } };
}

/**
 * Kapitel-Training der Kalkulations-Seite (ohne Angular): die Kapitel-Uhr ({@link CalcTrainingTimer}), die
 * Rechenzeit der angezeigten STELLUNG (Stoppuhr, die nur bei laufendem Training misst und ihre Zeit als Delta an
 * den Review-Endpoint abgibt), die Startsperre ohne geladene Stellung und der einmalige Start-Hinweis.
 */
export class CalcTrainingSession {
  // ===== Kapitel-Training (Timer) ==========================================
  // Kumuliert die Rechenzeit JE KAPITEL — Uhr, Topf und Besitzer stehen in {@link CalcTrainingTimer}.
  // Solange nicht gestartet wurde, misst auch die Stellungs-Uhr nichts (siehe `beginWatch`)
  // und es geht kein `addSeconds` an den Server.
  readonly timer: CalcTrainingTimer;

  /** Sekunden der laufenden Sitzung an dieser Stellung (nur Anzeige; gesendet wird das Delta). */
  liveSeconds = 0;

  /** Misst die AKTIVE Zeit an der Stellung (pausiert, wenn der Tab weg ist). */
  readonly watch = new VisibilityStopwatch();
  /** Stellung, zu der die laufende Uhr gehört; null = keine Uhr. */
  private watchPositionId: number | null = null;
  private liveHandle?: ReturnType<typeof setInterval>;

  /** Wurde in diesem Besuch schon gefragt, ob das Training starten soll? Nur EINMAL fragen. */
  private timerAsked = false;

  constructor(timerOptions: CalcTrainingTimerOptions, private readonly host: CalcTrainingSessionHost) {
    this.timer = new CalcTrainingTimer(timerOptions);
  }

  get running(): boolean { return this.timer.running; }

  toggle(): void {
    if (this.running) this.stop(); else this.start();
  }

  /**
   * Darf JETZT gemessen werden? Nur mit geladener Stellung: der Kapitel-Topf
   * (`CalcTrainingTimer.hasChapter`) entsteht erst beim Anwenden der Stellung — ohne ihn speichert
   * die Uhr nichts und `beginWatch` hätte keine Stellung, der die Zeit gehört. Scheitert
   * das Laden (`loadError`), tickte die Anzeige also vor sich hin, ohne irgendetwas zu messen oder
   * zu sichern: eine Uhr, die lügt. Dann lieber gar nicht erst starten (und sagen, warum).
   */
  get canTrain(): boolean {
    return this.host.positionLoaded() && this.timer.hasChapter;
  }

  /** Stoppen muss immer gehen — gesperrt wird nur das STARTEN ohne geladene Stellung. */
  get disabled(): boolean {
    return !this.running && !this.canTrain;
  }

  /** Beschriftung/Erklärung des Trainings-Knopfes — inklusive des Grundes, wenn es nicht geht. */
  get actionKey(): string {
    if (this.running) return 'calc.timer.stop';
    return this.canTrain ? 'calc.timer.start' : 'calc.timer.unavailable';
  }

  /** Laufzustand in Worten; ohne ladbare Stellung steht dort der Grund statt „gestoppt". */
  get stateKey(): string {
    if (this.running) return 'calc.timer.running';
    return this.canTrain ? 'calc.timer.stopped' : 'calc.timer.unavailable';
  }

  /**
   * Ab jetzt wird gemessen — und zwar beides: die kumulierte KAPITEL-Zeit und (über die
   * VisibilityStopwatch) die Rechenzeit der angezeigten STELLUNG. Ohne diesen Start läuft nichts;
   * das bloße Ansehen einer Stellung ist kein Training.
   */
  start(): void {
    if (this.running) return;
    // Ohne geladene Stellung misst und speichert nichts (siehe {@link canTrain}) — statt still ins
    // Leere zu zählen, geht der Start gar nicht erst los und sagt es.
    if (!this.canTrain) {
      this.host.warn('calc.timer.unavailable');
      return;
    }
    this.timer.start();
    // Die Stellungs-Uhr wird erst hier aufgezogen (`beginWatch` misst nur bei laufendem Training).
    const id = this.host.currentPositionId();
    if (id !== null) this.beginWatch(id);
  }

  /**
   * Stoppen heißt auch ABRECHNEN: die bis hierher gemessene Stellungszeit geht noch raus
   * (`harvestWatch`), sonst wäre sie mit dem Stopp verloren.
   */
  stop(): void {
    if (!this.running) return;
    this.timer.stop();
    this.harvestWatch();
  }

  /**
   * Wer anfängt zu rechnen, während das Training nicht läuft, misst nichts — und merkt es oft
   * erst hinterher. Ein Hinweis mit Sofort-Schalter statt eines Dialogs: er hält beim Rechnen
   * niemanden auf. Gefragt wird EINMAL je Besuch; wer nicht will, soll nicht bei jedem Zug
   * erneut gefragt werden. `onStart` = was der Knopf auslöst.
   */
  offerStart(onStart: () => void): void {
    if (this.running || this.timerAsked || !this.canTrain) return;
    this.timerAsked = true;
    const ref = this.host.warn('calc.timer.notRunning', { actionKey: 'calc.timer.start', duration: 10000 });
    ref.onAction().subscribe(() => onStart());
  }

  // ===== Rechenzeit messen ==================================================

  /**
   * Uhr für die angezeigte Stellung neu aufziehen (vorherige Zeit vorher abschöpfen).
   *
   * NUR bei gestartetem Kapitel-Training: das bloße Ansehen einer Stellung ist kein Training, und
   * ohne laufende Uhr geht auch kein `addSeconds` an den Server.
   */
  beginWatch(bookPuzzleId: number): void {
    this.harvestWatch();
    if (!this.running) return;
    this.watchPositionId = bookPuzzleId;
    this.watch.start(0);
    this.liveSeconds = 0;
  }

  /** Gemessene Sekunden als DELTA einreihen und die Uhr anhalten (idempotent). */
  harvestWatch(): void {
    if (this.watchPositionId === null) return;
    const bookPuzzleId = this.watchPositionId;
    this.watchPositionId = null;
    this.liveSeconds = 0;
    const seconds = this.watch.stop();
    if (seconds <= 0) return;
    // Jedes gemessene Delta bekommt seine eigene Marke: der Server ADDIERT die Zeit, und ein
    // Wiederholversuch (Antwort verloren, Anfrage angekommen) darf sie nicht doppelt buchen.
    this.host.creditSeconds(bookPuzzleId, seconds, newSecondsToken());
  }

  private currentWatchSeconds(): number {
    return this.watchPositionId === null ? 0 : this.watch.elapsedSeconds;
  }

  /** Nur Anzeige: die Stoppuhr selbst zählt ohne Takt weiter (und pausiert bei verstecktem Tab). */
  startLiveTicker(): void {
    this.liveHandle = setInterval(() => { this.liveSeconds = this.currentWatchSeconds(); }, 1000);
  }

  stopLiveTicker(): void {
    if (this.liveHandle !== undefined) { clearInterval(this.liveHandle); this.liveHandle = undefined; }
  }
}
