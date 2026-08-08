import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Was der Dialog über den Bereich weiß, in dem gefragt wird. */
export interface SolveModeDialogData {
  /** Klartext-Name des Bereichs für die Überschrift („Tagespuzzle", Kursname …). */
  scopeLabel?: string;
  /** Erklärt, wie oft die Wahl gilt. Default: „einmalig, jederzeit umschaltbar". */
  intro?: string;
  /** Was der Trainingsmodus hier konkret bedeutet — hängt an der eingestellten Stufe. */
  trainingDesc?: string;
}

/**
 * Erstabfrage der Spielweise: Training (Brett eingefroren bzw. die eingestellte
 * Visualisierungsstufe) oder Einfach (Figuren normal ziehbar).
 *
 * Bewusst blockierend (`disableClose`) und bewusst ohne Abbrechen-Knopf: die Wahl bestimmt,
 * wie das Brett reagiert und wie der Versuch gewertet wird. Sie kommt aber nur EINMAL je
 * Bereich — danach entscheidet der `SolveModeService` wortlos.
 */
@Component({
  selector: 'app-solve-mode-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>
      {{ (data.scopeLabel ? 'solveMode.titleFor' : 'solveMode.title') | translate:{ scope: data.scopeLabel } }}
    </h2>
    <mat-dialog-content>
      <p class="sm-intro">{{ data.intro || ('solveMode.intro' | translate) }}</p>

      <button type="button" class="sm-choice" (click)="pick('training')">
        <mat-icon>psychology</mat-icon>
        <span class="sm-text">
          <span class="sm-name">{{ 'solveMode.training' | translate }}</span>
          <span class="sm-desc">{{ data.trainingDesc || ('solveMode.trainingDesc' | translate) }}</span>
        </span>
      </button>

      <button type="button" class="sm-choice" (click)="pick('easy')">
        <mat-icon>pan_tool</mat-icon>
        <span class="sm-text">
          <span class="sm-name">{{ 'solveMode.easy' | translate }}</span>
          <span class="sm-desc">{{ 'solveMode.easyDesc' | translate }}</span>
        </span>
      </button>

      <p class="sm-hint">{{ 'solveMode.switchHint' | translate }}</p>
    </mat-dialog-content>
  `,
  styles: [`
    .sm-intro { margin: 0 0 12px; }
    .sm-choice {
      display: flex; align-items: center; gap: 12px; width: 100%;
      padding: 12px 14px; margin-bottom: 8px;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent);
      border-radius: 8px; background: none; color: inherit; cursor: pointer; text-align: left;
      font: inherit;
    }
    .sm-choice:hover, .sm-choice:focus-visible { background: color-mix(in srgb, currentColor 8%, transparent); }
    .sm-choice mat-icon { flex: 0 0 auto; }
    .sm-text { display: flex; flex-direction: column; gap: 2px; }
    .sm-name { font-weight: 600; }
    .sm-desc { font-size: .85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .sm-hint { margin: 4px 0 0; font-size: .82rem; color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class SolveModeDialogComponent {
  private readonly ref = inject(MatDialogRef<SolveModeDialogComponent, 'training' | 'easy'>);
  readonly data: SolveModeDialogData = inject(MAT_DIALOG_DATA, { optional: true }) || {};

  pick(mode: 'training' | 'easy'): void {
    this.ref.close(mode);
  }
}
