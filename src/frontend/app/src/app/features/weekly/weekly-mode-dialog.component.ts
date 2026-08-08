import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Die beiden Spielweisen eines Wochenposts. Die Zeichenketten sind der Vertrag zum Server
 *  (`WeeklyPostAttempt.Mode`) — nicht umbenennen ohne Migration. */
export type WeeklyMode = 'training' | 'easy';

/**
 * Auswahl beim Start eines Wochenposts: Training (Brett eingefroren, Züge werden nur getippt —
 * das bisherige Verhalten) oder Einfach (Figuren normal ziehen).
 *
 * Bewusst als BLOCKIERENDE Abfrage vor dem ersten Puzzle: die Wahl entscheidet, wie die Ergebnisse
 * gewertet werden, und lässt sich hinterher pro Puzzle nicht rückwirkend korrigieren. Wechseln geht
 * jederzeit über die Aktionszeile — ab dem nächsten Puzzle.
 */
@Component({
  selector: 'app-weekly-mode-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'weekly.mode.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="wm-intro">{{ 'weekly.mode.intro' | translate }}</p>

      <button type="button" class="wm-choice" (click)="pick('training')">
        <mat-icon>psychology</mat-icon>
        <span class="wm-text">
          <span class="wm-name">{{ 'weekly.mode.training' | translate }}</span>
          <span class="wm-desc">{{ 'weekly.mode.trainingDesc' | translate }}</span>
        </span>
      </button>

      <button type="button" class="wm-choice" (click)="pick('easy')">
        <mat-icon>pan_tool</mat-icon>
        <span class="wm-text">
          <span class="wm-name">{{ 'weekly.mode.easy' | translate }}</span>
          <span class="wm-desc">{{ 'weekly.mode.easyDesc' | translate }}</span>
        </span>
      </button>

      <p class="wm-hint">{{ 'weekly.mode.switchHint' | translate }}</p>
    </mat-dialog-content>
  `,
  styles: [`
    .wm-intro { margin: 0 0 12px; }
    .wm-choice {
      display: flex; align-items: center; gap: 12px; width: 100%;
      padding: 12px 14px; margin-bottom: 8px;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent);
      border-radius: 8px; background: none; color: inherit; cursor: pointer; text-align: left;
      font: inherit;
    }
    .wm-choice:hover, .wm-choice:focus-visible { background: color-mix(in srgb, currentColor 8%, transparent); }
    .wm-choice mat-icon { flex: 0 0 auto; }
    .wm-text { display: flex; flex-direction: column; gap: 2px; }
    .wm-name { font-weight: 600; }
    .wm-desc { font-size: .85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .wm-hint { margin: 4px 0 0; font-size: .82rem; color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class WeeklyModeDialogComponent {
  private readonly ref = inject(MatDialogRef<WeeklyModeDialogComponent, WeeklyMode>);

  pick(mode: WeeklyMode): void {
    this.ref.close(mode);
  }
}
