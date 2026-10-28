import { ChangeDetectionStrategy, Component, ViewChild, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
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
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe, PositionSetupComponent],
  // Knöpfe in mat-dialog-actions und das Brett an der Fensterhöhe bemessen (d7-rk-stellung-korrigieren-dialog-abgeschnitten):
  // vorher lagen Hinweis, Brett UND „Übernehmen“/„Abbrechen“ im auf 65vh begrenzten, scrollenden mat-dialog-content und das
  // Brett war nur in der Breite begrenzt — bei 1920×1080 fehlten die Knöpfe, am Laptop war das Brett oben angeschnitten.
  // Höhe des Dialogs ≈ Brett × 4/3 (zwei Figurenleisten zu je ⅙) + Titel, Hinweis, Werkzeugzeile, Abstände und Knopfzeile
  // (zusammen ≈ 17rem, gemessen 262 px); Material begrenzt den Dialog auf 95vh. Nur Brett und Figurenleisten schrumpfen
  // (--ps-board-size), die Werkzeugzeile behält die Dialogbreite und bricht nicht um.
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.board.correctTitle' | translate }}</h2>
    <mat-dialog-content class="pos-content">
      <p class="hint">{{ 'ebooks.board.correctHint' | translate }}</p>
      <app-position-setup [initialFen]="data.fen" [orientation]="data.flipped ? 'black' : 'white'" [requireLegal]="false"
                          [hideActions]="true" (apply)="ref.close($event)" (cancel)="ref.close()" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close class="pos-cancel">{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" class="pos-apply" (click)="setup?.applyPosition()">
        <mat-icon>check</mat-icon> {{ 'analysis.setup.apply' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { margin: 0 0 8px; opacity: .8; }
    .pos-content { max-height: calc(100vh - 9rem); }
    app-position-setup { display: block; width: 100%;
      --ps-board-size: min(100%, 26rem, max(13rem, calc((95vh - 17rem) * .75))); }
    /* Handy: „Weiß/Schwarz am Zug“ bricht die Werkzeugzeile um (+48 px) und der Hinweis wird länger — mehr Höhe abziehen. */
    @media (max-width: 599px) {
      app-position-setup { --ps-board-size: min(100%, max(13rem, calc((95vh - 24rem) * .75))); }
    }
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
