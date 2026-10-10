import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatCheckboxModule } from '@angular/material/checkbox';
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
  /** „Für alle Puzzle-Bereiche übernehmen" anbieten (setzt der `SolveModeService` für die
   *  Bereiche ohne Id; Kurse fragen weiter einzeln). */
  offerApplyAll?: boolean;
}

/** Antwort des Dialogs: die Wahl und ob sie als geräteweite Grundwahl gelten soll. */
export interface SolveModeDialogResult {
  mode: 'training' | 'easy';
  applyAll: boolean;
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
  imports: [MatDialogModule, MatCheckboxModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title class="sm-title">{{ (data.scopeLabel ? 'solveMode.titleFor' : 'solveMode.title') | translate:{ scope: data.scopeLabel } }}</h2>
    <mat-dialog-content>
      <p class="sm-intro">{{ data.intro || ('solveMode.intro' | translate) }}</p>

      <div class="sm-choices">
        <button type="button" class="sm-choice" (click)="pick('training')">
          <span class="sm-icon"><mat-icon>psychology</mat-icon></span>
          <span class="sm-name">{{ 'solveMode.training' | translate }}</span>
          <span class="sm-desc">{{ data.trainingDesc || ('solveMode.trainingDesc' | translate) }}</span>
        </button>

        <button type="button" class="sm-choice" (click)="pick('easy')">
          <span class="sm-icon"><mat-icon>pan_tool</mat-icon></span>
          <span class="sm-name">{{ 'solveMode.easy' | translate }}</span>
          <span class="sm-desc">{{ 'solveMode.easyDesc' | translate }}</span>
        </button>
      </div>

      @if (data.offerApplyAll) {
        <mat-checkbox class="sm-all" [checked]="applyAll" (change)="applyAll = $event.checked">
          {{ 'solveMode.applyAll' | translate }}
        </mat-checkbox>
      }

      <p class="sm-hint">{{ 'solveMode.switchHint' | translate }}</p>
    </mat-dialog-content>
  `,
  styles: [`
    /* Zwei gleich große Kacheln nebeneinander (am Handy untereinander), Symbol im getönten Kreis. Texte unverändert. */
    .sm-intro { margin: 0 0 14px; color: color-mix(in srgb, currentColor 75%, transparent); }
    .sm-choices { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 12px; }
    .sm-choice {
      display: flex; flex-direction: column; align-items: flex-start; gap: 6px;
      padding: 16px; min-height: 44px; box-sizing: border-box;
      border: 1px solid color-mix(in srgb, currentColor 18%, transparent);
      border-radius: 14px; background: color-mix(in srgb, currentColor 4%, transparent);
      color: inherit; cursor: pointer; text-align: left; font: inherit;
      transition: border-color .15s, background .15s;
    }
    .sm-choice:hover, .sm-choice:focus-visible {
      border-color: var(--rh-accent); background: color-mix(in srgb, var(--rh-accent) 10%, transparent); outline: none;
    }
    .sm-icon {
      display: grid; place-items: center; width: 40px; height: 40px; border-radius: 50%;
      background: color-mix(in srgb, var(--rh-accent) 16%, transparent); color: var(--rh-accent);
    }
    .sm-name { font-weight: 600; font-size: 1.02rem; }
    .sm-desc { font-size: .85rem; line-height: 1.4; color: color-mix(in srgb, currentColor 65%, transparent); }
    .sm-all { display: block; margin: 0 0 4px; }
    .sm-hint { margin: 4px 0 0; font-size: .82rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    @media (max-width: 600px) {
      .sm-choices { grid-template-columns: 1fr; }
      .sm-choice { flex-direction: row; flex-wrap: wrap; align-items: center; column-gap: 12px; }
      .sm-desc { flex-basis: 100%; }
    }
  `],
})
export class SolveModeDialogComponent {
  private readonly ref = inject(MatDialogRef<SolveModeDialogComponent, SolveModeDialogResult>);
  readonly data: SolveModeDialogData = inject(MAT_DIALOG_DATA, { optional: true }) || {};
  /** Voreingestellt an: die meisten wollen überall gleich spielen und nicht je Bereich gefragt werden. */
  applyAll = true;

  pick(mode: 'training' | 'easy'): void {
    this.ref.close({ mode, applyAll: !!this.data.offerApplyAll && this.applyAll });
  }
}
