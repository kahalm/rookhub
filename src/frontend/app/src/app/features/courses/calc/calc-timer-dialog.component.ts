import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

export interface CalcTimerDialogData {
  /** Aktuelle Kapitelzeit in Sekunden. */
  seconds: number;
  /** Name des Kapitels — man soll sehen, WESSEN Zeit man ändert. */
  chapter: string;
}

/** Neue Kapitelzeit in Sekunden; `undefined` = weggeklickt. */
export type CalcTimerDialogResult = number;

/**
 * Kapitelzeit nachtragen.
 *
 * Wer das Training zu spät startet, hat gerechnet, ohne dass die Uhr lief — die Zahl ist dann
 * falsch, und eine falsche Zahl ist schlimmer als keine. Hier lässt sie sich korrigieren.
 *
 * WICHTIG und in der Ansicht auch gesagt: das ist die KAPITEL-Uhr. Sie liegt auf diesem Gerät
 * (localStorage) und ist eine Merkhilfe fürs Durcharbeiten. Die je STELLUNG gemessene Rechenzeit
 * — die, die in Auswertungen und Kapitelsummen steht — bleibt unangetastet: sie entsteht aus
 * tatsächlich gemessenen Abschnitten, und ein von Hand gesetzter Wert wäre dort keine Messung
 * mehr, sondern eine Behauptung.
 */
@Component({
  selector: 'app-calc-timer-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'calc.timer.editTitle' | translate }}</h2>
    <mat-dialog-content>
      <p class="ct-where">{{ 'calc.timer.editFor' | translate: { chapter: data.chapter } }}</p>

      <div class="ct-fields">
        <label class="ct-field">
          <span>{{ 'calc.timer.hours' | translate }}</span>
          <input type="number" min="0" max="99" step="1" [(ngModel)]="hours" (ngModelChange)="clamp()" />
        </label>
        <label class="ct-field">
          <span>{{ 'calc.timer.minutes' | translate }}</span>
          <input type="number" min="0" max="59" step="1" [(ngModel)]="minutes" (ngModelChange)="clamp()" />
        </label>
        <label class="ct-field">
          <span>{{ 'calc.timer.seconds' | translate }}</span>
          <input type="number" min="0" max="59" step="1" [(ngModel)]="seconds" (ngModelChange)="clamp()" />
        </label>
      </div>

      <p class="ct-note">
        <mat-icon>info_outline</mat-icon>
        <span>{{ 'calc.timer.editNote' | translate }}</span>
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <!-- Bewusst KEIN mat-dialog-close-Attribut: Angular befüllt dessen Input als statisches
           Attribut mit dem leeren STRING, der Dialog schlösse also mit '' statt undefined — und
           der Aufrufer nähme das für eine gesetzte Zeit. Derselbe Fehler war im Ergebnis-Dialog. -->
      <button mat-button (click)="cancel()">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="apply()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .ct-where { margin: 0 0 12px; font-weight: 600; }
    .ct-fields { display: flex; gap: 10px; }
    .ct-field { display: flex; flex-direction: column; gap: 4px; font-size: .8rem; }
    .ct-field input {
      width: 5rem; padding: 6px 8px; border-radius: 4px; font: inherit;
      font-variant-numeric: tabular-nums;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent);
      background: transparent; color: inherit;
    }
    .ct-note {
      display: flex; align-items: flex-start; gap: 6px; margin: 14px 0 0;
      font-size: .82rem; color: color-mix(in srgb, currentColor 65%, transparent);
    }
    .ct-note mat-icon { font-size: 18px; width: 18px; height: 18px; flex: 0 0 auto; }
  `],
})
export class CalcTimerDialogComponent {
  private ref = inject<MatDialogRef<CalcTimerDialogComponent, CalcTimerDialogResult | undefined>>(MatDialogRef);
  data = inject<CalcTimerDialogData>(MAT_DIALOG_DATA, { optional: true })
    ?? { seconds: 0, chapter: '' };

  hours = Math.floor(this.data.seconds / 3600);
  minutes = Math.floor((this.data.seconds % 3600) / 60);
  seconds = this.data.seconds % 60;

  /** Eingaben in den gültigen Bereich zwingen — ein negativer Wert wäre keine Zeit. */
  clamp(): void {
    this.hours = this.limit(this.hours, 99);
    this.minutes = this.limit(this.minutes, 59);
    this.seconds = this.limit(this.seconds, 59);
  }

  private limit(value: number, max: number): number {
    const n = Math.floor(Number(value));
    if (!Number.isFinite(n) || n < 0) return 0;
    return Math.min(n, max);
  }

  apply(): void {
    this.clamp();
    this.ref.close(this.hours * 3600 + this.minutes * 60 + this.seconds);
  }

  cancel(): void { this.ref.close(undefined); }
}
