import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Nachfrage bei auffällig langer Lösezeit (> Schwellwert): vermutlich lag der Tab offen,
 * während der Nutzer weg war → die gemessene Zeit ist überhöht und würde Kurs-Zeit/Trefferquote
 * (und die Tagespuzzle-Bestenliste) verfälschen.
 * Ergebnis via afterClosed(): <c>true</c> = Zeit war echt (übernehmen), <c>false</c> = war weg (kappen).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-long-solve-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe],
  template: `
    <h2 class="ls-title"><mat-icon>schedule</mat-icon> {{ 'book.longSolve.title' | translate }}</h2>
    <p class="ls-text">{{ 'book.longSolve.question' | translate:{ time: formatted } }}</p>
    <div class="ls-actions">
      <button mat-stroked-button (click)="close(false)">{{ 'book.longSolve.wasAway' | translate }}</button>
      <button mat-raised-button color="primary" (click)="close(true)">{{ 'book.longSolve.reallyTookThatLong' | translate }}</button>
    </div>
  `,
  styles: [`
    :host { display: block; padding: 1.25rem; max-width: min(420px, 92vw); }
    .ls-title { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 0.75rem; font-size: 1.15rem; }
    .ls-text { margin: 0 0 1.25rem; line-height: 1.4; }
    .ls-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; justify-content: flex-end; }
  `],
})
export class LongSolveDialogComponent {
  constructor(
    @Inject(MAT_DIALOG_DATA) public data: { seconds: number },
    private ref: MatDialogRef<LongSolveDialogComponent, boolean>,
  ) {}

  get formatted(): string {
    const s = Math.max(0, Math.floor(this.data.seconds));
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
    const pad = (n: number) => String(n).padStart(2, '0');
    return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
  }

  close(reallyTookThatLong: boolean): void {
    this.ref.close(reallyTookThatLong);
  }
}
