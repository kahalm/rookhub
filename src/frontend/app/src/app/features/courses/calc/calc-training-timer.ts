import { localStore, readJson, writeJson } from '../../../core/local-json-store';
import { formatSecondsClock } from '../../../shared/clock-format.util';

/** Takt der Kapitel-Uhr — austauschbar, damit Specs ohne echte Zeit auskommen. */
export interface CalcTimerClock {
  setInterval(tick: () => void, ms: number): unknown;
  clearInterval(handle: unknown): void;
}

/** Die echte Uhr; greift erst beim Aufruf auf die globalen Funktionen zu (jasmine.clock() bleibt wirksam). */
export const SYSTEM_TIMER_CLOCK: CalcTimerClock = {
  setInterval: (tick, ms) => setInterval(tick, ms),
  clearInterval: handle => clearInterval(handle as ReturnType<typeof setInterval>),
};

export interface CalcTrainingTimerOptions {
  /** Schlüssel im localStorage (`rookhub_calc_timer_<bookId>`); erst beim Lesen/Schreiben abgefragt. */
  storageKey: () => string;
  /** Angemeldete Nutzer-Id oder null (anonym) — wem der geladene Stand gehört. */
  userId: () => number | null;
  /** Der Schreibversuch ist gerade (erstmals nach Erfolg) gescheitert. */
  onPersistFailed: () => void;
  clock?: CalcTimerClock;
}

/**
 * Kapitel-Uhr des Kalkulations-Modus (Zerlegung Z20, aus `CalculationComponent`): kumuliert die Rechenzeit JE
 * KAPITEL (nicht je Stellung). Beim Stellungswechsel innerhalb desselben Kapitels läuft derselbe Zähler weiter, beim
 * Kapitelwechsel wird der Topf des neuen Kapitels geladen. Die AKKUMULIERTE Zeit liegt je Gerät im localStorage (Map
 * Kapitelname→Sekunden) — bewusst ohne Server-Anteil: ein Trainingswerkzeug, kein Wertungsbestandteil.
 *
 * Der LAUFZUSTAND lebt nur hier im Speicher und wird nirgends persistiert: ein Zustand, der ein Neuladen überlebt,
 * fängt sich irgendwann eine vergessene Stunde ein.
 */
export class CalcTrainingTimer {
  running = false;
  seconds = 0;
  /** Kapitel-Schlüssel des laufenden Zählers ('' = „ohne Kapitel"); null = noch nicht geladen. */
  private chapterKey: string | null = null;
  /** Ist der LETZTE Schreibversuch gescheitert? Gemeldet wird nur der Übergang in den Fehlschlag. */
  private persistFailed = false;
  /** Wessen Speicherstand in {@link seconds} steht (Nutzer-Id beim Laden des Topfs, null = anonym). */
  private owner: number | null = null;
  private handle: unknown;
  private readonly clock: CalcTimerClock;

  constructor(private readonly options: CalcTrainingTimerOptions) {
    this.clock = options.clock ?? SYSTEM_TIMER_CLOCK;
  }

  /** Ist ein Kapitel-Topf geladen? Ohne ihn speichert {@link persist} nichts. */
  get hasChapter(): boolean { return this.chapterKey !== null; }

  /** Angezeigte kumulierte Kapitel-Zeit (m:ss bzw. h:mm:ss). */
  get display(): string { return formatSecondsClock(this.seconds); }

  /** Ab jetzt zählen; jede Sekunde wird persistiert (übersteht Tab-Schließen/Navigieren ohne eigenen Flush-Pfad). */
  start(): void {
    if (this.running) return;
    this.running = true;
    this.handle = this.clock.setInterval(() => {
      this.seconds++;
      this.persist();
    }, 1000);
  }

  /** Anhalten und den Stand sichern. */
  stop(): void {
    if (!this.running) return;
    this.running = false;
    if (this.handle !== undefined) { this.clock.clearInterval(this.handle); this.handle = undefined; }
    this.persist();
  }

  /** Von Hand nachgetragene Kapitelzeit übernehmen und sichern. */
  setSeconds(seconds: number): void {
    this.seconds = seconds;
    this.persist();
  }

  /**
   * Beim Stellungswechsel den Topf des Kapitels nachziehen: gleiches Kapitel → weiterzählen (die Zeit AKKUMULIERT über
   * mehrere Durchgänge), anderes Kapitel → alten Stand sichern und den des neuen Kapitels laden.
   */
  syncChapter(chapter: string | null): void {
    const key = chapter ?? '';
    if (key === this.chapterKey) return;
    if (this.chapterKey !== null) this.persist();
    this.chapterKey = key;
    // Besitzer VOR dem Lesen festhalten: die Abfrage kann einen Sitzungsablauf auslösen, der den
    // Speicher räumt — gelesen wird dann schon der geräumte Stand.
    this.owner = this.options.userId();
    this.seconds = this.readStore()[key] ?? 0;
  }

  /**
   * Speicher voll/gesperrt wird GEMELDET ({@link CalcTrainingTimerOptions.onPersistFailed}) statt still verschluckt —
   * aber nur der ÜBERGANG in den Fehlschlag: die Uhr schreibt jede Sekunde, eine weggeklickte Warnung stünde sonst
   * sekündlich wieder da.
   *
   * Die Zeit gehört dem, aus dessen Speicherstand sie geladen wurde. Abmelden auf offener Seite räumt den Speicher und
   * navigiert ERST DANACH — ein späteres Stoppen schrieb sonst die Kapitelzeit des Vorgängers zurück, und der nächste
   * Nutzer am Gerät zählte darauf weiter. Ebenso Sekundentakt nach Sitzungsablauf und Ab-/Ummelden in einem anderen Tab.
   * Die eigene Zeit geht damit beim Abmelden verloren — gewollt (F3-019).
   */
  persist(): void {
    if (this.chapterKey === null) return;
    if (this.options.userId() !== this.owner) return;
    const store = this.readStore();
    store[this.chapterKey] = this.seconds;
    const ok = writeJson(localStore(), this.options.storageKey(), store);
    if (!ok && !this.persistFailed) this.options.onPersistFailed();
    this.persistFailed = !ok;
  }

  private readStore(): Record<string, number> {
    const parsed = readJson<Record<string, number>>(localStore(), this.options.storageKey());
    return parsed && typeof parsed === 'object' ? parsed : {};
  }
}
