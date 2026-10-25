import { GuessHistoryMove, GuessSession } from './guess.service';

/** Die Uhr der Abspielsteuerung — austauschbar, damit die Zeitfolge ohne Angular testbar ist. */
export interface PlaybackClock {
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
}

/** Die echte Uhr des Browsers (unter `fakeAsync` die gepatchte). */
export const browserClock: PlaybackClock = {
  setTimeout: (fn, ms) => setTimeout(fn, ms),
  clearTimeout: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/** Was die Abspielsteuerung an der Seite bewegt — Brett, Sperre, Kommentar. */
export interface GuessPlaybackHost {
  /** Einen Halbzug aufs Brett legen (Stellung, Markierung, Kommentar des Schritts). */
  showStep(step: GuessHistoryMove): void;
  /** Die Sitzung übernehmen, die nach dem letzten Schritt gilt (= die nächste Aufgabe). */
  apply(session: GuessSession): void;
  setBusy(busy: boolean): void;
  clearStepNote(): void;
  /** Der Partiezug des letzten Rateversuchs — `undefined`, solange es keinen gibt. */
  gameMoveUci(): string | undefined;
  /** Nach einem Zeitschritt: die Anzeige muss neu gezeichnet werden. */
  changed(): void;
}

/**
 * <b>Automatisch gespielte Zuege laufen EINZELN ueber das Brett.</b>
 *
 * Nach einem Rateversuch kommen mindestens zwei Halbzuege dazu (der Partiezug und die Antwort),
 * oft mehr: eine Stellung, in der die Engine den Partiezug nicht unter ihren Kandidaten fuehrt,
 * wird uebersprungen — sie waere nicht wertbar. Frueher sprang das Brett dann in einem Satz auf
 * die naechste Aufgabe; auf Dev gemeldet als „nach Bxe7 spielt er sofort 3 Zuege". Man sah nie,
 * WAS gespielt wurde, und die Kommentare dieser Zuege gingen verloren — bei einer Meisterpartie
 * genau das, wofuer man sie spielt.
 *
 * Jetzt wird jeder dieser Halbzuege gezeigt und gehalten:
 * <ul>
 *   <li>mit Kommentar: stehenbleiben, bis „Weiter" kommt;</li>
 *   <li>der Partiezug ohne Kommentar: eine Sekunde (er gehoert zur eigenen Antwort);</li>
 *   <li>alles Uebrige: eine halbe Sekunde — genug, um es zu sehen, ohne zu bremsen.</li>
 * </ul>
 *
 * <p>Reine Klasse ohne Angular (Zerlegung Z24): Zustand des Haltens und der Schrittfolge, die Uhr
 * kommt von aussen.</p>
 */
export class GuessPlayback {
  static readonly GameMoveDelayMs = 1000;
  static readonly StepDelayMs = 500;

  /**
   * Ein Zug, der NICHT der Partiezug war, bleibt auf dem Brett stehen — mit der Info darunter,
   * was die Partie gespielt hat. Erst „Weiter" rückt auf die nächste Aufgabe vor. Die Sitzung ist
   * serverseitig längst weitergerückt; zurückgehalten wird nur die ANZEIGE (`pending`).
   */
  private _holding = false;
  /** Wird gerade wegen eines Kommentars gehalten? Dann spielt „Weiter" den naechsten Schritt. */
  private _holdingNote = false;
  private pending: GuessSession | null = null;
  /** Ab welchem Halbzug die Schrittfolge laeuft, wenn nach dem eigenen Zug gehalten wurde. */
  private pendingFromPly = -1;

  private timer: unknown = undefined;
  /** Noch abzuspielende Halbzuege bis zur naechsten Aufgabe. */
  private steps: GuessHistoryMove[] = [];
  /** Die Sitzung, die nach dem letzten Schritt gilt. */
  private stepTarget: GuessSession | null = null;

  constructor(private readonly host: GuessPlaybackHost, private readonly clock: PlaybackClock = browserClock) {}

  get holding(): boolean { return this._holding; }
  get holdingNote(): boolean { return this._holdingNote; }
  /** Läuft gerade eine Wartezeit zwischen zwei Schritten? */
  get running(): boolean { return this.timer !== undefined; }

  /** Ein anderer Zug als der Partiezug bleibt stehen; `session` wartet hinter „Weiter". */
  hold(session: GuessSession, fromPly: number): void {
    this.pending = session;
    this._holding = true;
    this.pendingFromPly = fromPly;
  }

  /** Sitzung durch: kein Halt mehr, nichts wartet. */
  dropHold(): void {
    this._holding = false;
    this.pending = null;
  }

  /**
   * Die Halbzuege, die seit dem geratenen Zug dazugekommen sind — aus dem VERLAUF der neuen
   * Sitzung, samt Stellung und Kommentar. Die Stellungen von dort zu nehmen ist genauer als sie
   * selbst nachzuspielen: es sind dieselben, die der Server geliefert hat.
   */
  start(next: GuessSession, fromPly: number): void {
    this.steps = next.history.filter(h => h.ply >= fromPly);
    this.stepTarget = next;
    this.nextStep();
  }

  /**
   * „Weiter" — zwei Haltegruende, zwei Bedeutungen:
   * <ul>
   *   <li>gehalten wegen des EIGENEN (anderen) Zuges → jetzt den Partiezug zeigen;</li>
   *   <li>gehalten wegen des KOMMENTARS zum Partiezug → jetzt die Antwort des Gegners spielen.</li>
   * </ul>
   * @returns `false`, wenn schon eine Wartezeit läuft (dann geschieht nichts).
   */
  continue(): boolean {
    if (this.running) return false;
    const s = this.pending;
    this.pending = null;
    this._holding = false;

    if (this._holdingNote) {
      // Gehalten wegen eines Kommentars: weiter in der laufenden Schrittfolge.
      this._holdingNote = false;
      this.host.clearStepNote();
      this.nextStep();
    } else if (s) {
      // Gehalten wegen des EIGENEN Zuges: jetzt zeigt die Partie, was sie gespielt hat.
      this.start(s, this.pendingFromPly);
    }
    return true;
  }

  dispose(): void { this.clearTimer(); }

  private clearTimer(): void {
    if (this.timer !== undefined) {
      this.clock.clearTimeout(this.timer);
      this.timer = undefined;
    }
  }

  /** Einen Schritt zeigen; danach warten, halten oder fertig sein. */
  private nextStep(): void {
    const step = this.steps.shift();
    const target = this.stepTarget;
    if (!step || !target) {
      if (target) { this.stepTarget = null; this.host.apply(target); }
      this.host.setBusy(false);
      return;
    }

    this.host.showStep(step);

    // Der LETZTE Schritt braucht weder Halt noch Wartezeit: seine Stellung IST die naechste
    // Aufgabe. Ein „Weiter" waere dort ein Klick, der nichts tut, ausser den Nutzer aufzuhalten —
    // der Kommentar bleibt einfach stehen, und er zieht.
    if (this.steps.length === 0) {
      this.stepTarget = null;
      this.host.apply(target);
      this.host.setBusy(false);
      return;
    }

    if (step.comment) {
      // Kommentar mitten in der Folge: stehenbleiben, bis „Weiter" kommt. Sonst laeuft die Partie
      // weiter, waehrend man liest — und der Text ist der Grund, sie zu spielen.
      this._holding = true;
      this._holdingNote = true;
      this.host.setBusy(false);
      return;
    }

    this.host.setBusy(true);              // Brett gesperrt, solange die Partie von selbst laeuft
    this.clearTimer();
    const gameMove = this.host.gameMoveUci();
    const ms = gameMove !== undefined && step.uci === gameMove
      ? GuessPlayback.GameMoveDelayMs
      : GuessPlayback.StepDelayMs;
    this.timer = this.clock.setTimeout(() => {
      this.timer = undefined;
      this.nextStep();
      this.host.changed();
    }, ms);
  }
}
