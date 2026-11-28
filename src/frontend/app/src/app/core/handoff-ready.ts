import { Injectable } from '@angular/core';

/**
 * Laeuft gerade die Start-Uebernahme einer Anmeldung (Einmal-Code oder geteilte Anmeldung, siehe
 * `HandoffService.consumeIncoming`)? Die Guards fragen das, bevor sie einen Gast zur Anmeldemaske schicken.
 *
 * <p>Ein eigener, abhaengigkeitsfreier Dienst statt eines Feldes am `HandoffService`: der zieht HttpClient und
 * Router nach, und jeder Guard-Spec muesste beides bereitstellen, nur damit der Guard nachsehen kann.</p>
 *
 * <p>Fund 2026-11-28 (Prod v1.036.0, ES-Logs): LeagueHub ohne eigene Sitzung holte sich die geteilte Anmeldung
 * (`rh-session` → 200), zeigte danach aber die Anmeldemaske — die erste Navigation lief parallel zur Uebernahme,
 * der authGuard sah „nicht angemeldet“ und leitete auf `/login`, dessen Chunk noch lud.</p>
 */
@Injectable({ providedIn: 'root' })
export class HandoffReady {
  /** So lange wartet ein Guard hoechstens auf die laufende Uebernahme — ein haengender Aufruf soll die Seite
   *  nicht blockieren. Der rh-session-Tausch braucht gewoehnlich unter 300 ms; 5 s decken auch die Wiederholungen
   *  des retryInterceptors (500 ms + 1 s + 2 s) ab. */
  static readonly GuardWaitMs = 5_000;

  private pending: Promise<unknown> | null = null;

  /** Merkt sich eine laufende Uebernahme, bis sie (erfolgreich oder nicht) fertig ist. */
  track(run: Promise<unknown>): void {
    this.pending = run;
    const clear = () => { if (this.pending === run) this.pending = null; };
    run.then(clear, clear);
  }

  /** Laeuft gerade eine Uebernahme? */
  get inFlight(): boolean { return this.pending !== null; }

  /** Erfuellt, sobald die laufende Uebernahme fertig ist — spaetestens nach `maxMs` (`Infinity`: ohne Limit). Sofort, wenn keine laeuft.
   *  Wird nie verworfen. */
  settled(maxMs = HandoffReady.GuardWaitMs): Promise<void> {
    const run = this.pending;
    if (!run) return Promise.resolve();
    return new Promise<void>(resolve => {
      // Ohne Zeitlimit (`Infinity`) KEIN Timer — setTimeout macht aus Infinity sofort (0 ms).
      const timer = Number.isFinite(maxMs) ? setTimeout(resolve, maxMs) : undefined;
      const done = () => { clearTimeout(timer); resolve(); };
      run.then(done, done);
    });
  }

  /**
   * Wertet `decide` aus — laeuft eine Uebernahme, erst nach deren Ende (oder dem Zeitlimit), sonst sofort und
   * synchron (ein Guard bleibt dann so schnell wie vorher).
   */
  afterPending<T>(decide: () => T): T | Promise<T> {
    return this.inFlight ? this.settled().then(decide) : decide();
  }
}
