import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
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
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'calc.timer.editTitle' | translate }}</h2>
    <mat-dialog-content>
      <p class="ct-where">{{ 'calc.timer.editFor' | translate: { chapter: data.chapter } }}</p>

      <!-- d8-rk-kalk-kapitelzeit-felder-nicht-outline / d8-rk-kapitelzeit-eingabefelder-klein: Outline-Felder wie in allen
           RookHub-Dialogen (DESIGN 5.8) — Schrift 16 px, Höhe und Rahmen kommen von Material. -->
      <div class="ct-fields">
        <mat-form-field appearance="outline" class="ct-field" subscriptSizing="dynamic">
          <mat-label>{{ 'calc.timer.hours' | translate }}</mat-label>
          <input matInput type="number" min="0" max="99" step="1" [(ngModel)]="hours" (ngModelChange)="clamp()" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="ct-field" subscriptSizing="dynamic">
          <mat-label>{{ 'calc.timer.minutes' | translate }}</mat-label>
          <input matInput type="number" min="0" max="59" step="1" [(ngModel)]="minutes" (ngModelChange)="clamp()" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="ct-field" subscriptSizing="dynamic">
          <mat-label>{{ 'calc.timer.seconds' | translate }}</mat-label>
          <input matInput type="number" min="0" max="59" step="1" [(ngModel)]="seconds" (ngModelChange)="clamp()" />
        </mat-form-field>
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
    /* Die drei Felder teilen sich die Breite. */
    .ct-field { flex: 1 1 0; min-width: 0; }
    .ct-field input { font-variant-numeric: tabular-nums; }
    .ct-note {
      display: flex; align-items: flex-start; gap: 6px; margin: 14px 0 0;
      font-size: .82rem; color: var(--mat-sys-on-surface-variant);
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
