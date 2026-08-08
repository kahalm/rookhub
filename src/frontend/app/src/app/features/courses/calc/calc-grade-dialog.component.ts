import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { CALC_GRADE_OPTIONS, CalcGrade } from './calc-review.util';

/** Was der Dialog über die Stellung weiß, für die bewertet wird. */
export interface CalcGradeDialogData {
  /** Bisherige Stufe; `null` = noch nicht bewertet. */
  grade: CalcGrade | null;
  /** Der Zug, auf den sich der Nutzer festgelegt hatte (SAN) — falls es einen gibt. */
  chosenSan: string | null;
}

/**
 * Ergebnis: die gewählte Stufe oder `null` fürs Zurücknehmen. `undefined` (Dialog weggeklickt)
 * heißt „nichts ändern" — das ist etwas anderes als `null` („Bewertung entfernen").
 */
export type CalcGradeDialogResult = CalcGrade | null;

/**
 * Selbstbewertung EINER Stellung des Kalkulations-Modus.
 *
 * Die fünf Stufen standen früher als Schalterreihe direkt in der Seitenspalte. Sie sind hierher
 * gewandert, weil der Modus ein BRETT bleiben soll und kein Formular (UI-Dichte-Regel): draußen
 * steht nur noch ein Knopf — „Ergebnis", bzw. die gewählte Stufe, sobald bewertet wurde.
 *
 * Gebaut wie `shared/solve-mode/solve-mode-dialog.component.ts`: eine Liste großflächiger
 * Auswahl-Knöpfe mit Bedeutung statt eines Radio-Formulars. Anders als dort ist er NICHT
 * blockierend — Bewerten ist freiwillig und jederzeit nachholbar.
 */
@Component({
  selector: 'app-calc-grade-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'calc.review.dialog.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="cg-intro">{{ 'calc.review.selfRatingHint' | translate }}</p>

      <!-- Die Festlegung gehört hierher: bewertet wird, was aus ihr geworden ist. -->
      @if (data.chosenSan) {
        <p class="cg-choice">
          <mat-icon>star</mat-icon>
          <span>{{ 'calc.review.dialog.choice' | translate: { move: data.chosenSan } }}</span>
        </p>
      } @else {
        <p class="cg-choice cg-choice--none">
          <mat-icon>star_border</mat-icon>
          <span>{{ 'calc.review.dialog.noChoice' | translate }}</span>
        </p>
      }

      @for (option of options; track option.grade) {
        <button type="button" class="cg-option" [class.cg-option--on]="option.grade === data.grade"
                [attr.aria-pressed]="option.grade === data.grade" (click)="pick(option.grade)">
          <span class="cg-text">{{ option.labelKey | translate }}</span>
          <span class="cg-points">{{ option.points }}</span>
        </button>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.grade !== null) {
        <button mat-button class="cg-clear" (click)="clear()">
          {{ 'calc.review.dialog.clear' | translate }}
        </button>
      }
      <!-- Bewusst KEIN mat-dialog-close-Attribut: als statisches Attribut befüllt Angular dessen
           Input dialogResult mit dem leeren STRING, der Dialog schlösse also mit '' statt mit
           undefined. Das ist hier kein Schönheitsfehler — der Aufrufer liest null als
           „Bewertung entfernen", und ein leerer Fremdwert käme dort als Löschbefehl an. -->
      <button mat-button class="cg-cancel" (click)="cancel()">{{ 'common.cancel' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .cg-intro { margin: 0 0 10px; font-size: .88rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .cg-choice {
      display: flex; align-items: center; gap: 6px; margin: 0 0 12px;
      font-size: .9rem; font-weight: 600; color: #f57f17;
    }
    .cg-choice mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .cg-choice--none { color: color-mix(in srgb, currentColor 55%, transparent); font-weight: 400; }
    .cg-option {
      display: flex; align-items: center; gap: 10px; width: 100%;
      padding: 10px 12px; margin-bottom: 6px;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent);
      border-radius: 8px; background: none; color: inherit; cursor: pointer; text-align: left;
      font: inherit;
    }
    .cg-option:hover, .cg-option:focus-visible { background: color-mix(in srgb, currentColor 8%, transparent); }
    .cg-option--on { border-color: #f9a825; background: color-mix(in srgb, #f9a825 16%, transparent); font-weight: 600; }
    .cg-text { flex: 1; }
    .cg-points {
      flex: 0 0 auto; min-width: 1.4rem; text-align: center;
      font-variant-numeric: tabular-nums; opacity: .7;
    }
  `],
})
export class CalcGradeDialogComponent {
  private readonly ref = inject(MatDialogRef<CalcGradeDialogComponent, CalcGradeDialogResult>);
  readonly data: CalcGradeDialogData =
    inject<CalcGradeDialogData>(MAT_DIALOG_DATA, { optional: true }) || { grade: null, chosenSan: null };

  /** Dieselbe Quelle wie überall — kein zweiter Satz Stufen. */
  readonly options = CALC_GRADE_OPTIONS;

  pick(grade: CalcGrade): void {
    this.ref.close(grade);
  }

  /** Bewertung entfernen — ausdrücklich „noch nicht bewertet", nicht Stufe 0 („nicht gelöst"). */
  clear(): void {
    this.ref.close(null);
  }

  /**
   * Abbrechen = NICHTS ändern, und das heißt genau `undefined` (wie ESC/Backdrop) — niemals `null`
   * und auch kein leerer String: `null` ist hier der ausdrückliche Löschbefehl (siehe {@link clear}).
   */
  cancel(): void {
    this.ref.close(undefined);
  }
}
