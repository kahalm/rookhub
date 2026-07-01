import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';

export interface StopDialogData {
  /** Kompakter Timer-Titel („Coaching mit Alice"). */
  label: string;
  /** UTC-ISO — daraus wird die Default-Ende-Anzeige (jetzt) und das Min-Attribut abgeleitet. */
  startedAtIso: string;
}

export interface StopDialogResult {
  /** UTC-ISO oder null (= „jetzt"). */
  endedAtIso: string | null;
  note: string;
}

/**
 * Bestätigt das Stoppen eines laufenden Offline-Trainings-Timers. Zeigt die live-Dauer und lässt
 * das Endzeitpunkt zurückdatieren (falls der User das Ausschalten vergessen hat). Ein "Verwerfen"-
 * Ergebnis wird über einen separaten Discard-Knopf abgebildet — dieser Dialog ist nur zum Speichern.
 */
@Component({
  selector: 'app-activity-timer-stop-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatIconModule, TranslateModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'trainingGoals.timer.stopTitle' | translate }}</h2>
    <mat-dialog-content>
      <div class="dialog-form">
        <p class="label">{{ data.label }}</p>
        <p class="hint">{{ 'trainingGoals.timer.stopHint' | translate }}</p>

        <div class="row">
          <span class="row-label">{{ 'trainingGoals.timer.duration' | translate }}</span>
          <span class="row-value">{{ formatMinutes(computedMinutes) }}</span>
        </div>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'trainingGoals.timer.endedAt' | translate }}</mat-label>
          <input matInput type="datetime-local" [(ngModel)]="endedAtLocal"
                 [min]="startedAtLocal" [max]="nowLocal"
                 (ngModelChange)="recompute()">
          <mat-icon matSuffix>schedule</mat-icon>
        </mat-form-field>
        <p class="hint-small">{{ 'trainingGoals.timer.endedHint' | translate }}</p>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'trainingGoals.timer.noteLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="note" maxlength="180"
                 [placeholder]="'trainingGoals.timer.notePlaceholder' | translate">
        </mat-form-field>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="computedMinutes < 1" (click)="save()">
        <mat-icon>save</mat-icon>
        {{ 'trainingGoals.timer.saveEntry' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 0.6rem; min-width: min(360px, 82vw); }
    .label { margin: 0; font-weight: 600; font-size: 1rem; }
    .hint { margin: 0; color: color-mix(in srgb, currentColor 65%, transparent); font-size: 0.87rem; }
    .hint-small { margin: -2px 0 0; color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.78rem; }
    .row { display: flex; justify-content: space-between; align-items: center; padding: 6px 0;
      border-top: 1px solid color-mix(in srgb, currentColor 10%, transparent);
      border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); }
    .row-label { color: color-mix(in srgb, currentColor 70%, transparent); font-size: 0.85rem; }
    .row-value { font-variant-numeric: tabular-nums; font-weight: 600; font-size: 1rem; }
    mat-form-field { width: 100%; }
  `]
})
export class ActivityTimerStopDialogComponent {
  /** ISO-Local-String für `<input type="datetime-local">` (kein Z, keine ms). */
  endedAtLocal: string;
  readonly startedAtLocal: string;
  readonly nowLocal: string;
  note = '';
  computedMinutes: number;

  private readonly startedMs: number;

  constructor(
    public dialogRef: MatDialogRef<ActivityTimerStopDialogComponent, StopDialogResult>,
    @Inject(MAT_DIALOG_DATA) public data: StopDialogData,
  ) {
    this.startedMs = new Date(data.startedAtIso).getTime();
    this.startedAtLocal = toLocalInput(new Date(this.startedMs));
    this.nowLocal = toLocalInput(new Date());
    this.endedAtLocal = this.nowLocal;
    this.computedMinutes = this.minutesBetween(this.startedMs, Date.now());
  }

  recompute(): void {
    const end = this.endedAtLocal ? new Date(this.endedAtLocal).getTime() : Date.now();
    this.computedMinutes = this.minutesBetween(this.startedMs, end);
  }

  save(): void {
    // Wenn User das Feld leer lässt → als „jetzt" schicken (null).
    const endedIso = this.endedAtLocal ? new Date(this.endedAtLocal).toISOString() : null;
    this.dialogRef.close({ endedAtIso: endedIso, note: this.note.trim() });
  }

  formatMinutes(min: number): string {
    if (min < 60) return `${min} min`;
    const h = Math.floor(min / 60);
    const m = min - h * 60;
    return m === 0 ? `${h} h` : `${h} h ${m} min`;
  }

  private minutesBetween(startMs: number, endMs: number): number {
    const secs = Math.max(0, Math.round((endMs - startMs) / 1000));
    return Math.max(1, Math.min(600, Math.round(secs / 60)));
  }
}

/** JS-Date → String im Format `yyyy-MM-ddTHH:mm` (Lokalzeit) — passt auf `<input type="datetime-local">`. */
export function toLocalInput(d: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
