import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { BOARD_THEMES, PIECE_SETS } from '../../puzzles/board-theme.util';
import { PreferencesService } from '../../../core/preferences.service';

/**
 * Einstellungen des Bretts, erreichbar über das Zahnrad in der schmalen Leiste neben dem Brett.
 *
 * Enthält bewusst NUR, was in diesem Modus etwas bewirkt: Brett- und Figurenart. Die
 * Visualisierungsstufe gehört ausdrücklich NICHT dazu — im Kalkulations-Modus ist das Brett
 * eingefroren und steht immer auf Stufe 1; ein Regler, der nichts tut, ist schlimmer als keiner.
 * Andere Modi können denselben Dialog später um ihre eigenen Felder erweitern.
 *
 * Änderungen wirken SOFORT (und werden über den PreferencesService auch serverseitig gesichert,
 * sobald jemand angemeldet ist) — deshalb gibt es nur „Schließen" und kein „Speichern": es gäbe
 * nichts zu verwerfen.
 */
@Component({
  selector: 'app-calc-settings-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'calc.settings.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="cs-label">{{ 'puzzles.theme.boardTheme' | translate }}</div>
      <div class="cs-chips">
        @for (t of boardThemes; track t.key) {
          <button type="button" class="cs-chip" [class.cs-chip--on]="prefs.boardTheme === t.key"
                  [attr.aria-pressed]="prefs.boardTheme === t.key" (click)="pickBoard(t.key)">
            @if (t.img) {
              <span class="cs-img" [style.backgroundImage]="'url(' + t.img + ')'"></span>
            } @else {
              <span class="cs-preview">
                <span [style.background]="t.light"></span><span [style.background]="t.dark"></span>
              </span>
            }
            <span class="cs-name">{{ t.name }}</span>
          </button>
        }
      </div>

      <div class="cs-label cs-label--spaced">{{ 'puzzles.theme.pieces' | translate }}</div>
      <div class="cs-chips">
        @for (p of pieceSets; track p.key) {
          <button type="button" class="cs-chip" [class.cs-chip--on]="prefs.pieceSet === p.key"
                  [attr.aria-pressed]="prefs.pieceSet === p.key" (click)="pickPieces(p.key)">
            <span class="cs-piece" [style.backgroundImage]="'url(' + p.preview + ')'"></span>
            <span class="cs-name">{{ p.name }}</span>
          </button>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <!-- Kein „Speichern": die Wahl wirkt sofort, es gäbe nichts zu verwerfen. Und bewusst kein
           statisches mat-dialog-close-Attribut (es liefert den leeren String statt undefined). -->
      <button mat-button (click)="close()">{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .cs-label { font-size: .78rem; font-weight: 600; text-transform: uppercase; letter-spacing: .06em;
                color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px; }
    .cs-label--spaced { margin-top: 16px; }
    .cs-chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .cs-chip {
      display: flex; align-items: center; gap: 6px;
      padding: 4px 10px 4px 4px; border-radius: 999px; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent);
      background: transparent; color: inherit; font: inherit; font-size: .82rem;
    }
    .cs-chip:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .cs-chip--on { border-color: #1976d2; background: color-mix(in srgb, #1976d2 14%, transparent); font-weight: 600; }
    .cs-preview { display: flex; width: 22px; height: 22px; border-radius: 4px; overflow: hidden; }
    .cs-preview span { flex: 1; }
    .cs-img, .cs-piece { width: 22px; height: 22px; border-radius: 4px; background-size: cover; background-position: center; }
    .cs-piece { background-size: contain; background-repeat: no-repeat; }
  `],
})
export class CalcSettingsDialogComponent {
  private ref = inject<MatDialogRef<CalcSettingsDialogComponent>>(MatDialogRef);
  prefs = inject(PreferencesService);

  readonly boardThemes = BOARD_THEMES;
  readonly pieceSets = PIECE_SETS;

  pickBoard(key: string): void { this.prefs.setBoardTheme(key); }
  pickPieces(key: string): void { this.prefs.setPieceSet(key); }

  close(): void { this.ref.close(); }
}
