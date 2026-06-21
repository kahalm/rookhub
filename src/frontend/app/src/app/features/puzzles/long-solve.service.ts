import { Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { LongSolveDialogComponent } from './long-solve-dialog.component';

/**
 * Plausibilisiert die gemessene Lösezeit eines Puzzles. Lösezeiten über dem Schwellwert sind
 * verdächtig (der Tab lag vermutlich offen, während der Nutzer weg war) und würden Statistiken
 * (Kurs-Zeit/Trefferquote, Tagespuzzle-Bestenliste, Endlos-Dauer) verfälschen. In dem Fall wird
 * nachgefragt; bei „war weg" wird die gewertete Zeit auf den Schwellwert gekappt (NICHT 0 — sonst
 * zählte ein Tagespuzzle fälschlich als blitzschnell gelöst).
 *
 * Einheitlich für Standard-, Endlos- und Buch-/Kurs-Puzzle.
 */
@Injectable({ providedIn: 'root' })
export class LongSolveService {
  /** Lösezeiten über diesem Wert (Sekunden) lösen die Nachfrage aus. */
  static readonly THRESHOLD_SECONDS = 300;

  constructor(private dialog: MatDialog) {}

  /**
   * Liefert die tatsächlich zu wertende Lösezeit (Sekunden). Bei ≤ Schwellwert sofort `elapsed`
   * (keine Nachfrage). Sonst modaler Dialog (blockiert „Weiter" dahinter): „ja so lange" → `elapsed`,
   * „war weg" → Schwellwert. Emittiert genau einmal.
   */
  resolve(elapsedSeconds: number): Observable<number> {
    if (elapsedSeconds <= LongSolveService.THRESHOLD_SECONDS) return of(elapsedSeconds);
    return this.dialog.open(LongSolveDialogComponent, {
      data: { seconds: elapsedSeconds },
      disableClose: true,
      width: '420px',
      maxWidth: '92vw',
    }).afterClosed().pipe(
      map((reallyTookThatLong: boolean | undefined) =>
        reallyTookThatLong === false ? LongSolveService.THRESHOLD_SECONDS : elapsedSeconds),
    );
  }
}
