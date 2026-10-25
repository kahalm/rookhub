import { formatUtcDate, shiftDailyDate } from './book-nav.util';
import { getDailyOffline, saveDailyOffline } from './book-offline.util';
import { BookModeHost } from './book-mode-host';

/** Tag eines Tagespuzzles („YYYYMMDD“ bzw. „YYYY-MM-DD“) als Datum (UTC-Mitternacht) für die DatePipe. */
export function dailyDateValue(date: string | null | undefined): Date | null {
  const m = /^(\d{4})-?(\d{2})-?(\d{2})$/.exec(date ?? '');
  return m ? new Date(Date.UTC(+m[1], +m[2] - 1, +m[3])) : null;
}

/**
 * Tagespuzzle-Modus des Buch-Lösers (/puzzles/daily/:date, Zerlegung Z18): Datums-Navigation (zurück/vor) statt
 * Buch-Navigation, kein Auto-Weiter. Gemeldet wird wie beim einzelnen Buch-Puzzle (`BookStandaloneMode`).
 */
export class BookDailyMode {
  /** Datum „YYYYMMDD“ des angezeigten Tagespuzzles; null = kein Tagespuzzle. */
  date: string | null = null;

  constructor(private readonly h: BookModeHost) {}

  /** Tag des Tagespuzzles als Datum (UTC-Mitternacht) für die DatePipe; `date` ist „YYYYMMDD“. */
  get dateValue(): Date | null { return dailyDateValue(this.date); }

  /** „Vor" nur erlaubt, solange das angezeigte Datum vor heute (UTC) liegt — keine Zukunft. */
  get canGoForward(): boolean { return !!this.date && this.date < this.todayUtc(); }

  prev(): void { this.go(-1); }
  next(): void { if (this.canGoForward) this.go(1); }

  private go(delta: number): void {
    if (!this.date) return;
    this.h.router.navigate(['/puzzles/daily', shiftDailyDate(this.date, delta)]);
  }

  private todayUtc(): string { return formatUtcDate(new Date()); }

  /** Tagespuzzle eines Datums laden (Route /puzzles/daily/:date) — danach wie ein Buch-Puzzle. */
  load(dateParam: string): void {
    const h = this.h;
    const date = dateParam === 'today' ? this.todayUtc() : dateParam;
    this.date = date;
    h.loadError = false;
    h.retryFn = () => this.load(dateParam);
    h.state = 'LOADING';
    h.resetForLoad();
    const epoch = ++h.loadEpoch;

    // Offline → aus dem auto-gecachten Tagespuzzle bedienen (kein endloser Spinner).
    if (!navigator.onLine) {
      const cached = getDailyOffline(date);
      if (cached) { h.puzzle = cached; h.setupPuzzle(cached); return; }
    }

    h.puzzleService.getDailyPuzzle(date).subscribe({
      next: puzzle => {
        if (epoch !== h.loadEpoch) return;
        h.puzzle = puzzle;
        saveDailyOffline(date, puzzle);   // automatisch für Offline vorhalten
        h.setupPuzzle(puzzle);
      },
      error: () => {
        if (epoch !== h.loadEpoch) return;
        // Netzfehler trotz onLine → letzter Versuch aus dem Offline-Cache.
        const cached = getDailyOffline(date);
        if (cached) { h.puzzle = cached; h.setupPuzzle(cached); return; }
        h.state = 'LOADING';
        h.puzzle = null;
        h.loadError = true;
      }
    });
  }
}
