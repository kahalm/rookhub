import { Component, Inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry } from './tournament-directory.model';

export interface TournamentCardDialogData {
  entry: DirectoryEntry;
}

/**
 * Die Kurzansicht als kleines Fenster — fuer den KALENDER.
 *
 * <p>Im Monatsraster ist ein Tag ein paar Zeilen hoch; die Kurzansicht passt dort nicht hinein.
 * Ein Klick auf einen Turniernamen fuehrte deshalb bisher direkt auf die Detailseite und damit
 * aus dem Kalender heraus — wer vergleicht, verliert dabei den Monat. Ein Fenster mit derselben
 * Kurzansicht wie in Liste und Karte laesst merken, uebertragen, ausblenden und melden, ohne den
 * Kalender zu verlassen; erst der Klick auf den Namen fuehrt weiter.</p>
 *
 * <p>Ein Dialog und kein schwebendes Feld: auf einem Handy ist er die bessere Bedienung, und er
 * braucht keine Positionslogik gegen den Rand des Monatsrasters.</p>
 */
@Component({
  selector: 'app-tournament-card-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, TranslatePipe, TournamentCardComponent],
  template: `
    <mat-dialog-content>
      <app-tournament-card [entry]="data.entry" [overview]="true"
                           (selected)="dialogRef.close($event)"
                           (ignoredChanged)="ignored = true" />
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { padding-top: 0.75rem; min-width: 260px; }
  `],
})
export class TournamentCardDialogComponent {
  /**
   * Wurde hier etwas aus- oder eingeblendet? Der Kalender laedt dann neu — anders als die Liste
   * kann er eine einzelne Zeile nicht herausnehmen, ein Turnier steht an mehreren Tagen.
   */
  ignored = false;

  constructor(
    public dialogRef: MatDialogRef<TournamentCardDialogComponent, DirectoryEntry | null>,
    @Inject(MAT_DIALOG_DATA) public data: TournamentCardDialogData,
  ) {}
}
