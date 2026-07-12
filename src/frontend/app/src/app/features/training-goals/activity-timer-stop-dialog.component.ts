import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { ActivityTheme, ACTIVITY_THEMES } from './training-goals.service';

export interface StopDialogData {
  /** Kompakter Timer-Titel („Coaching mit Alice"). */
  label: string;
  /** UTC-ISO — daraus wird die Default-Start-Anzeige abgeleitet (Server-Startzeitpunkt). */
  startedAtIso: string;
  /** Vom Preset/Timer geerbtes Thema (Default für den Select), null = kein Preset-Thema. */
  theme: ActivityTheme | null;
}

export interface StopDialogResult {
  /** UTC-ISO Startzeit — wird ans Backend geschickt, damit es dieselbe Rechnung sieht. */
  startedAtIso: string;
  /** UTC-ISO oder null (= „jetzt"). */
  endedAtIso: string | null;
  note: string;
  theme: ActivityTheme | null;
}

/**
 * Bestätigt das Stoppen eines laufenden Offline-Trainings-Timers. Zeigt die live-Dauer und lässt
 * <b>alle drei</b> Größen — Start, Ende und Dauer — bearbeiten. Sie werden konsistent gehalten:
 * <ul>
 *   <li>Ändert der User Start → Ende bleibt, Dauer wird nachgerechnet.</li>
 *   <li>Ändert der User Ende → Start bleibt, Dauer wird nachgerechnet.</li>
 *   <li>Ändert der User Dauer → Ende = Start + Dauer; würde das in der Zukunft liegen, wird
 *       stattdessen Start = jetzt − Dauer nach vorn geschoben und Ende = jetzt gesetzt.</li>
 * </ul>
 * Der User kann optional ein Thema wählen (Eröffnung/Mittelspiel/Endspiel/Taktik/Sonstiges); Default
 * ist das vom Preset geerbte Thema.
 */
@Component({
  selector: 'app-activity-timer-stop-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatIconModule, MatSelectModule, TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'trainingGoals.timer.stopTitle' | translate }}</h2>
    <mat-dialog-content>
      <div class="dialog-form">
        <p class="label">{{ data.label }}</p>
        <p class="hint">{{ 'trainingGoals.timer.stopHint' | translate }}</p>

        <div class="fields">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'trainingGoals.timer.startedAt' | translate }}</mat-label>
            <input matInput type="datetime-local" [(ngModel)]="startLocal"
                   [max]="nowLocal" (ngModelChange)="onStartChanged()">
            <mat-icon matSuffix>play_circle</mat-icon>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'trainingGoals.timer.duration' | translate }}</mat-label>
            <input matInput type="number" min="1" max="600" step="1" [(ngModel)]="durationMin"
                   (ngModelChange)="onDurationChanged()">
            <span matSuffix class="unit">{{ 'trainingGoals.min' | translate }}</span>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'trainingGoals.timer.endedAt' | translate }}</mat-label>
            <input matInput type="datetime-local" [(ngModel)]="endLocal"
                   [max]="nowLocal" (ngModelChange)="onEndChanged()">
            <mat-icon matSuffix>schedule</mat-icon>
          </mat-form-field>
        </div>
        <p class="hint-small">{{ 'trainingGoals.timer.startEndHint' | translate }}</p>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'trainingGoals.presets.kindLabel' | translate }}</mat-label>
          <mat-select [(ngModel)]="theme">
            <mat-option [value]="null">{{ 'trainingGoals.theme.unset' | translate }}</mat-option>
            @for (t of themes; track t) {
              <mat-option [value]="t">{{ ('trainingGoals.theme.' + t) | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'trainingGoals.timer.noteLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="note" maxlength="180"
                 [placeholder]="'trainingGoals.timer.notePlaceholder' | translate">
        </mat-form-field>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" [disabled]="durationMin < 1" (click)="save()">
        <mat-icon>save</mat-icon>
        {{ 'trainingGoals.timer.saveEntry' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-form { display: flex; flex-direction: column; gap: 0.6rem; min-width: min(420px, 92vw); }
    .label { margin: 0; font-weight: 600; font-size: 1rem; }
    .hint { margin: 0; color: color-mix(in srgb, currentColor 65%, transparent); font-size: 0.87rem; }
    .hint-small { margin: -2px 0 0; color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.78rem; }
    .fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0.4rem 0.6rem; }
    .fields mat-form-field:nth-child(2) { grid-column: 2; }
    .fields mat-form-field:nth-child(3) { grid-column: 1 / -1; }
    @media (max-width: 520px) { .fields { grid-template-columns: 1fr; } .fields mat-form-field:nth-child(2), .fields mat-form-field:nth-child(3) { grid-column: 1; } }
    .unit { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.85rem; }
    mat-form-field { width: 100%; }
  `]
})
export class ActivityTimerStopDialogComponent {
  /** Lokal-formattierte ISO-Strings für `<input type="datetime-local">` (keine Sekunden, kein Z). */
  startLocal: string;
  endLocal: string;
  /** Dauer in Minuten — separates Feld, hält Start/Ende zusammen. Min 1, Max 600. */
  durationMin: number;
  readonly nowLocal: string;
  readonly themes = ACTIVITY_THEMES;

  theme: ActivityTheme | null;
  note = '';

  constructor(
    public dialogRef: MatDialogRef<ActivityTimerStopDialogComponent, StopDialogResult>,
    @Inject(MAT_DIALOG_DATA) public data: StopDialogData,
  ) {
    const startMs = new Date(data.startedAtIso).getTime();
    const nowMs = Date.now();
    this.nowLocal = toLocalInput(new Date(nowMs));
    this.startLocal = toLocalInput(new Date(startMs));
    this.endLocal = this.nowLocal;
    this.durationMin = this.clampDuration(minutesBetween(startMs, nowMs));
    this.theme = data.theme ?? null;
  }

  /** User änderte Start → Dauer neu rechnen (Ende bleibt). Bei End < neuem Start → Ende = Start. */
  onStartChanged(): void {
    const startMs = fromLocalInput(this.startLocal);
    let endMs = fromLocalInput(this.endLocal);
    if (endMs < startMs) { endMs = startMs; this.endLocal = toLocalInput(new Date(endMs)); }
    this.durationMin = this.clampDuration(minutesBetween(startMs, endMs));
  }

  /** User änderte Ende → Dauer neu rechnen (Start bleibt). Kein zukünftiges Ende erlaubt. */
  onEndChanged(): void {
    const nowMs = Date.now();
    let endMs = fromLocalInput(this.endLocal);
    if (endMs > nowMs) { endMs = nowMs; this.endLocal = toLocalInput(new Date(endMs)); }
    const startMs = fromLocalInput(this.startLocal);
    this.durationMin = this.clampDuration(minutesBetween(startMs, endMs));
  }

  /** User änderte Dauer → Ende neu rechnen. Wenn Start + Dauer in Zukunft läge, schiebt der Regel-Text
   *  aus der Aufgabenbeschreibung Start nach vorn (Start = jetzt − Dauer, Ende = jetzt). */
  onDurationChanged(): void {
    this.durationMin = this.clampDuration(this.durationMin);
    const nowMs = Date.now();
    const startMs = fromLocalInput(this.startLocal);
    const durMs = this.durationMin * 60_000;
    const projectedEnd = startMs + durMs;
    if (projectedEnd > nowMs) {
      // Start nach vorn schieben, Ende = jetzt.
      const newStart = nowMs - durMs;
      this.startLocal = toLocalInput(new Date(newStart));
      this.endLocal = toLocalInput(new Date(nowMs));
    } else {
      // Ende = Start + Dauer.
      this.endLocal = toLocalInput(new Date(projectedEnd));
    }
  }

  save(): void {
    // Endwert leer? → als „jetzt" schicken (null → Server nimmt jetzt).
    const startIso = new Date(fromLocalInput(this.startLocal)).toISOString();
    const endIso = this.endLocal ? new Date(fromLocalInput(this.endLocal)).toISOString() : null;
    this.dialogRef.close({
      startedAtIso: startIso,
      endedAtIso: endIso,
      note: this.note.trim(),
      theme: this.theme,
    });
  }

  private clampDuration(min: number): number {
    if (!Number.isFinite(min)) return 1;
    return Math.max(1, Math.min(600, Math.round(min)));
  }
}

/** JS-Date → String im Format `yyyy-MM-ddTHH:mm` (Lokalzeit) — passt auf `<input type="datetime-local">`. */
export function toLocalInput(d: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Umkehr von {@link toLocalInput}: interpretiert einen `datetime-local`-String als Lokalzeit. */
export function fromLocalInput(s: string): number {
  return s ? new Date(s).getTime() : Date.now();
}

export function minutesBetween(startMs: number, endMs: number): number {
  const secs = Math.max(0, Math.round((endMs - startMs) / 1000));
  return Math.max(1, Math.round(secs / 60));
}
