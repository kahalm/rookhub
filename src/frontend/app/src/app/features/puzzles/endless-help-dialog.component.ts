import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Phasengrenzen der Hilfe (1-basierte Puzzle-Nummern) — Platzhalter der Texte endless.help.fasttrackPhase1–3. */
export interface EndlessHelpPhases {
  p1End: number;
  p2Start: number;
  p2End: number;
  p3Start: number;
  flatStep: number;
}

/** Hilfe zu Endless (Zerlegung Z19, vorher ng-template in endless-puzzle.component.html); geöffnet über MatDialog
 *  mit den Phasengrenzen als Dialog-Daten. */
@Component({
  selector: 'app-endless-help-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  templateUrl: './endless-help-dialog.component.html',
  styleUrls: ['./endless-help-dialog.component.scss'],
})
export class EndlessHelpDialogComponent {
  readonly phase = inject<EndlessHelpPhases>(MAT_DIALOG_DATA);
}
