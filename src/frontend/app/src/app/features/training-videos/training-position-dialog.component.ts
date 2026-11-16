import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { PositionSetupComponent } from '../analysis/position-setup.component';

export interface TrainingPositionDialogData { fen: string; }

/**
 * „Brett aufbauen“ der Trainerstunden-Suche: der Stellungs-Editor des Analysebretts im Dialog (Muster
 * `EbookPositionDialogComponent`). „Suchen“ schließt mit der vollen FEN, „Abbrechen“/Esc ohne Ergebnis.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-position-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe, PositionSetupComponent],
  template: `
    <h2 mat-dialog-title>{{ 'trainingVideos.search.setupTitle' | translate }}</h2>
    <mat-dialog-content class="pos-content">
      <app-position-setup [initialFen]="data.fen" [requireLegal]="false" [hideActions]="true"
                          (apply)="ref.close($event)" (cancel)="ref.close()" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close class="pos-cancel">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" class="pos-apply" (click)="setup()?.applyPosition()">
        <mat-icon>search</mat-icon> {{ 'common.search' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .pos-content { max-height: calc(100vh - 9rem); }
    app-position-setup { display: block; width: 100%;
      --ps-board-size: min(100%, 26rem, max(13rem, calc((95vh - 15rem) * .75))); }
    @media (max-width: 599px) {
      app-position-setup { --ps-board-size: min(100%, max(13rem, calc((95vh - 20rem) * .75))); }
    }
  `],
})
export class TrainingPositionDialogComponent {
  readonly data = inject<TrainingPositionDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<TrainingPositionDialogComponent, string>>(MatDialogRef);
  readonly setup = viewChild(PositionSetupComponent);

  constructor() {
    this.ref.afterOpened().subscribe(() => this.setup()?.redraw());
  }
}
