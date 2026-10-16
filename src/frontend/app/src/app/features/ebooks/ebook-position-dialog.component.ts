import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { PositionSetupComponent } from '../analysis/position-setup.component';

export interface EbookPositionDialogData { fen: string; flipped?: boolean; }

/**
 * „Stellung korrigieren“ (0.798.0, ⋮ des Bretts, nur Verwalter des Buchs): der Stellungs-Editor des Analysebretts
 * ({@link PositionSetupComponent}) im Dialog — Figuren setzen/entfernen/verschieben, Weiß/Schwarz am Zug. „Übernehmen“
 * schließt mit der vollen FEN, „Abbrechen“/Esc ohne Ergebnis. Verlangt nur je einen König (Buchdiagramme dürfen
 * Teilstellungen sein).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-position-dialog',
  standalone: true,
  imports: [MatDialogModule, TranslatePipe, PositionSetupComponent],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.board.correctTitle' | translate }}</h2>
    <mat-dialog-content>
      <p class="hint">{{ 'ebooks.board.correctHint' | translate }}</p>
      <app-position-setup [initialFen]="data.fen" [orientation]="data.flipped ? 'black' : 'white'" [requireLegal]="false"
                          (apply)="ref.close($event)" (cancel)="ref.close()" />
    </mat-dialog-content>
  `,
  styles: [`
    .hint { margin: 0 0 8px; opacity: .8; }
    app-position-setup { display: block; width: min(100%, 26rem); margin: 0 auto; }
  `],
})
export class EbookPositionDialogComponent {
  readonly data = inject<EbookPositionDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject<MatDialogRef<EbookPositionDialogComponent, string>>(MatDialogRef);
  @ViewChild(PositionSetupComponent) setup?: PositionSetupComponent;

  constructor() {
    // Erst nach der Öffnungsanimation hat das Brett seine echte Größe — vorher vermessen lagen die Figuren daneben (0.803.6).
    this.ref.afterOpened().subscribe(() => this.setup?.redraw());
  }
}
