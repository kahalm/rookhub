/**
 * Stoppuhr, die nur läuft, solange der Tab sichtbar ist (Page Visibility API).
 *
 * Hintergrund: Die Puzzle-Lösezeit wurde bisher als reine Wanduhr-Differenz
 * (`Date.now() - start`) gemessen — lag der Tab im Hintergrund, zählte die Zeit voll
 * weiter und verfälschte Statistiken (Tagespuzzle-Bestenliste, Kurs-/Endlos-Zeit).
 * Diese Stoppuhr summiert ausschließlich die sichtbaren Phasen: Wird der Tab versteckt,
 * pausiert sie; wird er wieder sichtbar, läuft sie weiter.
 *
 * Bewusst kein Angular-Service: jede Solver-Instanz hält ihre eigene(n) Stoppuhr(en)
 * (pro Puzzle + ggf. pro Endless-Session). `start()` ist mehrfach aufrufbar (re-armiert,
 * ohne Listener zu stapeln); `stop()` hängt den Listener wieder ab.
 *
 * ANHALTEN VON HAND ({@link hold}/{@link release}, 2026-11-18): liegt ein Fenster über der Aufgabe (Woodpecker-
 * Statistik), zählt die Zeit ebenfalls nicht — gezählt wird nur „sichtbar UND nicht angehalten“. Der Halt gehört der
 * Uhr, nicht dem Lauf: ein `start()` während des Halts (nächste Aufgabe lädt hinter dem Fenster) beginnt angehalten
 * und läuft erst mit `release()` los.
 */
export class VisibilityStopwatch {
  private accumulatedMs = 0;
  private resumedAt = 0;     // Zeitstempel des laufenden sichtbaren Abschnitts (nur gültig, wenn counting)
  private counting = false;  // zählt gerade (sichtbar)
  private running = false;   // zwischen start() und stop()
  private held = false;      // von Hand angehalten (hold() … release()), unabhängig von start()/stop()
  private readonly listener = () => this.onVisibilityChange();

  /**
   * Startet (bzw. setzt mit `initialSeconds` vorbelegt fort — für die Endless-Session,
   * die einen Reload/Resume überlebt). Re-armiert idempotent, ohne Listener zu doppeln.
   */
  start(initialSeconds = 0): void {
    this.detach();
    this.accumulatedMs = Math.max(0, initialSeconds) * 1000;
    this.running = true;
    this.counting = this.isVisible() && !this.held;
    this.resumedAt = this.counting ? this.now() : 0;
    this.attach();
  }

  /** Von Hand anhalten (idempotent): ab jetzt zählt nichts, bis {@link release} — auch über `start()` hinweg. */
  hold(): void {
    if (this.held) return;
    this.held = true;
    this.pause();
  }

  /** Halt aufheben (idempotent): läuft weiter, wenn die Uhr gestartet und der Tab sichtbar ist. */
  release(): void {
    if (!this.held) return;
    this.held = false;
    if (this.running && !this.counting && this.isVisible()) { this.counting = true; this.resumedAt = this.now(); }
  }

  /** Von Hand angehalten? */
  get isHeld(): boolean { return this.held; }

  /** Aktive (sichtbare) Sekunden seit `start()`. */
  get elapsedSeconds(): number {
    return Math.floor(this.elapsedMs() / 1000);
  }

  /** Hält an, hängt den Listener ab und liefert die finale aktive Sekundenzahl. */
  stop(): number {
    this.pause();
    this.running = false;
    this.detach();
    return Math.floor(this.accumulatedMs / 1000);
  }

  private elapsedMs(): number {
    const live = this.counting ? this.now() - this.resumedAt : 0;
    return this.accumulatedMs + live;
  }

  private onVisibilityChange(): void {
    if (!this.running) return;
    if (this.isVisible()) {
      if (!this.counting && !this.held) { this.counting = true; this.resumedAt = this.now(); }   // fortsetzen
    } else {
      this.pause();                                                                // anhalten
    }
  }

  private pause(): void {
    if (this.counting) {
      this.accumulatedMs += this.now() - this.resumedAt;
      this.counting = false;
    }
  }

  private attach(): void {
    if (typeof document !== 'undefined') document.addEventListener('visibilitychange', this.listener);
  }

  private detach(): void {
    if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', this.listener);
  }

  private isVisible(): boolean {
    return typeof document === 'undefined' || document.visibilityState !== 'hidden';
  }

  private now(): number {
    return Date.now();
  }
}
